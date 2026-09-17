#!/usr/bin/env bash
# LabelFrame Server Linux 一键安装脚本（迭代 71，Issue #91；契约：docs/DESIGN.md §6.12 / 决策 #133）
#
# 用法（root / sudo）：
#   在线安装（官方稳定通道，默认含管理界面）：  sudo bash install-server-linux.sh
#   指定版本 / 清单：                          sudo bash install-server-linux.sh --manifest <install-manifest.json 的 URL>
#   离线布局目录（衔接迭代 70，零外网请求）：    sudo bash install-server-linux.sh --manifest /path/to/布局目录
#   离线清单文件（清单所在目录即布局目录）：      sudo bash install-server-linux.sh --manifest /path/to/install-manifest.json
#   不装管理界面：                              sudo bash install-server-linux.sh --no-webui
#
# 行为：消费 install-manifest.json（sha256 逐源强制校验，urls 多源回退）→ 拉取 Linux 归档 →
#   解包部署 /opt/labelframe/server → 安装 / 启用 / 重启 systemd 服务 labelframe-server →
#   管理界面 zip 解压到 /var/lib/labelframe/server/plugins/web-ui → 输出服务状态与管理界面地址。
# 幂等：重跑 = 覆盖升级——appsettings.json 保留用户版本，/var/lib/labelframe 数据与日志目录不动。
# 归档形态：Release 归档自迭代 71 起默认 self-contained（免 .NET 前置）；framework-dependent 归档
#   需目标机已装 .NET 10 ASP.NET Core Runtime，缺失时明确报错并给出官方直链，不自动安装（决议 3a）。
set -euo pipefail

MANIFEST_STABLE_URL="https://github.com/marci-labs/LabelFrame/releases/latest/download/install-manifest.json"
DOTNET_DOWNLOAD_URL="https://dotnet.microsoft.com/download/dotnet/10.0"
APP_DIR=/opt/labelframe/server
DATA_DIR=/var/lib/labelframe/server
LOGS_DIR=/var/lib/labelframe/logs
SERVICE=labelframe-server
PORT=53961

MANIFEST_ARG=""
INSTALL_WEBUI=1

die() {
  echo "[LabelFrame 安装] 错误：$*" >&2
  exit 1
}
info() {
  echo "[LabelFrame 安装] $*"
}
usage() {
  cat <<'USAGE'
LabelFrame Server Linux 一键安装脚本（docs/DEPLOY.md §5 / DESIGN §6.12）
用法（root / sudo）：
  sudo bash install-server-linux.sh                                        在线安装（官方稳定通道，默认含管理界面）
  sudo bash install-server-linux.sh --manifest <清单 URL>                  在线安装指定版本
  sudo bash install-server-linux.sh --manifest <布局目录>                  离线安装（零外网请求，衔接迭代 70 布局目录）
  sudo bash install-server-linux.sh --manifest <install-manifest.json>     离线安装（清单所在目录即布局目录）
  sudo bash install-server-linux.sh --no-webui                             不装管理界面
USAGE
  exit 0
}

# ---- 参数 ----
while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) [ "$#" -ge 2 ] || die "--manifest 需要一个参数（本地文件 / 布局目录 / URL）"; MANIFEST_ARG="$2"; shift 2 ;;
    --no-webui) INSTALL_WEBUI=0; shift ;;
    -h|--help) usage ;;
    *) die "未知参数：$1（--help 查看用法）" ;;
  esac
done

# ---- 前置检查 ----
[ "$(id -u)" -eq 0 ] || die "请用 root 或 sudo 运行。"

command -v systemctl >/dev/null 2>&1 || die "未找到 systemctl——本脚本面向 systemd 主机（Ubuntu / Debian 系）。"
[ -d /run/systemd/system ] || die "systemd 未运行（容器内需以 systemd 为 init 启动，或改用 Docker 形态部署，见 docs/DEPLOY.md §4）。"

DOWNLOADER=""
if command -v curl >/dev/null 2>&1; then
  DOWNLOADER=curl
elif command -v wget >/dev/null 2>&1; then
  DOWNLOADER=wget
else
  die "未找到 curl 或 wget——请先安装（Ubuntu / Debian：apt-get install -y curl）。"
fi
command -v tar >/dev/null 2>&1 || die "未找到 tar——请先安装（Ubuntu / Debian：apt-get install -y tar）。"
command -v sha256sum >/dev/null 2>&1 || die "未找到 sha256sum——请先安装 coreutils（Ubuntu / Debian：apt-get install -y coreutils）。"
if [ "$INSTALL_WEBUI" -eq 1 ]; then
  command -v unzip >/dev/null 2>&1 || die "未找到 unzip（安装管理界面需要）——请先安装（Ubuntu / Debian：apt-get install -y unzip），或用 --no-webui 跳过管理界面。"
fi

download() {
  # download <URL> <目标文件>：curl / wget 双支持（-f：HTTP 错误码视为失败；-L：跟随 Release 重定向）
  local url="$1" dest="$2"
  if [ "$DOWNLOADER" = curl ]; then
    curl -fsSL --connect-timeout 10 --retry 2 -o "$dest" "$url"
  else
    wget -q -t 2 -T 60 -O "$dest" "$url"
  fi
}

file_sha256() {
  sha256sum "$1" | awk '{print $1}'
}

WORK="$(mktemp -d /tmp/labelframe-install.XXXXXX)"
trap 'rm -rf "$WORK"' EXIT

# ---- 获取清单 ----
MANIFEST="$WORK/install-manifest.json"
LAYOUT_DIR=""
if [ -z "$MANIFEST_ARG" ]; then
  info "获取安装清单（官方稳定通道）：$MANIFEST_STABLE_URL"
  download "$MANIFEST_STABLE_URL" "$MANIFEST" || die "清单下载失败——检查网络 / 代理，或用 --manifest <本地路径> 离线安装。"
elif [ -f "$MANIFEST_ARG" ]; then
  MANIFEST="$(cd "$(dirname "$MANIFEST_ARG")" && pwd)/$(basename "$MANIFEST_ARG")"
  LAYOUT_DIR="$(dirname "$MANIFEST")"
  info "使用本地清单：$MANIFEST（布局目录：$LAYOUT_DIR，本地文件优先）"
elif [ -d "$MANIFEST_ARG" ]; then
  LAYOUT_DIR="$(cd "$MANIFEST_ARG" && pwd)"
  MANIFEST="$LAYOUT_DIR/install-manifest.json"
  [ -f "$MANIFEST" ] || die "布局目录缺少 install-manifest.json：$MANIFEST"
  info "使用布局目录清单：$MANIFEST（本地文件优先）"
else
  case "$MANIFEST_ARG" in
    http://*|https://*)
      info "获取安装清单（指定 URL）：$MANIFEST_ARG"
      download "$MANIFEST_ARG" "$MANIFEST" || die "清单下载失败：$MANIFEST_ARG"
      ;;
    *) die "--manifest 指定的文件或目录不存在：$MANIFEST_ARG" ;;
  esac
fi
[ -s "$MANIFEST" ] || die "清单文件为空或不可读：$MANIFEST"

# ---- 解析清单（行级解析 CI 生成的 schemaVersion=1 形态；字段缺失 / 非法一律拒绝，无 jq 依赖）----
SCHEMA_VERSION="$(awk 'match($0, /"schemaVersion": *[0-9]+/) { v = $0; sub(/.*"schemaVersion": */, "", v); print v; exit }' "$MANIFEST")"
[ "$SCHEMA_VERSION" = "1" ] || die "清单 schemaVersion=${SCHEMA_VERSION:-<缺失>} 不受本脚本支持（当前支持 1）——请从仓库更新本脚本。"

# parse_component <组件 id>：逐行输出 key<TAB>value（version / url / sha256 / sizeBytes）
parse_component() {
  awk -v want="$1" '
    /"id": *"/ {
      id = $0; sub(/.*"id": *"/, "", id); sub(/".*/, "", id)
      inb = (id == want)
    }
    inb && /"version": *"/ { v = $0; sub(/.*"version": *"/, "", v); sub(/".*/, "", v); print "version\t" v }
    inb && /"urls": *\[/ {
      u = $0; sub(/.*"urls": *\[/, "", u); sub(/\].*/, "", u)
      n = split(u, arr, /", */)
      for (i = 1; i <= n; i++) { gsub(/^[ \t"]+|[ \t"]+$/, "", arr[i]); if (arr[i] != "") print "url\t" arr[i] }
    }
    inb && /"sha256": *"/ { s = $0; sub(/.*"sha256": *"/, "", s); sub(/".*/, "", s); print "sha256\t" s }
    inb && /"sizeBytes": *[0-9]/ { b = $0; sub(/.*"sizeBytes": */, "", b); sub(/[^0-9].*/, "", b); print "sizeBytes\t" b }
  ' "$MANIFEST"
}

load_component() {
  # load_component <组件 id>：解析进 COMP_NAME / COMP_VERSION / COMP_SHA256 / COMP_SIZE / COMP_URLS
  local id="$1" key value
  COMP_NAME=""; COMP_VERSION=""; COMP_SHA256=""; COMP_SIZE=""; COMP_URLS=()
  while IFS=$'\t' read -r key value; do
    case "$key" in
      version) COMP_VERSION="$value" ;;
      sha256) COMP_SHA256="$value" ;;
      sizeBytes) COMP_SIZE="$value" ;;
      url) COMP_URLS+=("$value") ;;
    esac
  done < <(parse_component "$id")
  [ -n "$COMP_SHA256" ] || die "清单缺少组件条目：$id（manifest 应收录当版全部产物，见 DESIGN §6.2）"
  [ "${#COMP_URLS[@]}" -ge 1 ] || die "组件 $id 的 urls 为空——清单非法。"
  echo "$COMP_SHA256" | grep -Eq '^[0-9a-f]{64}$' || die "组件 $id 的 sha256 非小写 64 位 hex：$COMP_SHA256"
  COMP_NAME="$(basename "${COMP_URLS[0]}")"
  [ -n "$COMP_NAME" ] || die "组件 $id 的 urls[0] 缺文件名：${COMP_URLS[0]}"
}

acquire_component() {
  # acquire_component <组件 id>：布局目录本地文件优先（哈希同样强制、不符换源），urls 逐源回退
  local id="$1" dest="$WORK/$COMP_NAME" src i=1 total="${#COMP_URLS[@]}"
  [ -n "$COMP_SIZE" ] || die "组件 $id 缺 sizeBytes——清单非法。"
  if [ -n "$LAYOUT_DIR" ] && [ -f "$LAYOUT_DIR/$COMP_NAME" ]; then
    info "[$id] 命中布局目录本地文件：$LAYOUT_DIR/$COMP_NAME（sha256 校验强制）"
    if [ "$(file_sha256 "$LAYOUT_DIR/$COMP_NAME")" = "$COMP_SHA256" ]; then
      cp "$LAYOUT_DIR/$COMP_NAME" "$dest"
      info "[$id] 本地文件校验通过（$COMP_NAME，$COMP_SIZE 字节）。"
      return 0
    fi
    info "[$id] 本地文件哈希不符——放弃本地源，回退 urls（fail-closed：不装不明文件）。"
  fi
  for src in "${COMP_URLS[@]}"; do
    info "[$id] 下载（源 $i/$total）：$src"
    if download "$src" "$dest" && [ "$(file_sha256 "$dest")" = "$COMP_SHA256" ]; then
      if [ "$(stat -c %s "$dest")" != "$COMP_SIZE" ]; then
        info "[$id] 体积不符（期望 $COMP_SIZE 字节），换下一源。"
        rm -f "$dest"
      else
        info "[$id] 下载校验通过（sha256 / sizeBytes 一致）。"
        return 0
      fi
    else
      rm -f "$dest"
      info "[$id] 获取失败或哈希不符，尝试下一源。"
    fi
    i=$((i + 1))
  done
  die "[$id] 全部 $total 个源均失败（获取失败或 sha256 不符）——请检查网络 / 镜像源，或改用离线布局目录（--manifest <目录>）。"
}

load_component linux-server
SERVER_VERSION="$COMP_VERSION"
SERVER_ARCHIVE="$COMP_NAME"
info "目标版本：$SERVER_VERSION（linux-server 归档 $SERVER_ARCHIVE）。"
acquire_component linux-server
if [ "$INSTALL_WEBUI" -eq 1 ]; then
  load_component webui
  WEBUI_ARCHIVE="$COMP_NAME"
  acquire_component webui
fi

# ---- 用户与目录 ----
info "[1/5] 创建系统用户与目录 ..."
id -u labelframe >/dev/null 2>&1 || useradd --system --home /opt/labelframe --shell /usr/sbin/nologin labelframe
mkdir -p "$APP_DIR" "$DATA_DIR" "$LOGS_DIR"

# ---- 解包部署（staging 校验通过才替换存量安装；FDD 先做 runtime 前置检查，失败不动存量）----
info "[2/5] 解包校验并部署 -> $APP_DIR ..."
STAGE="$WORK/stage"
mkdir -p "$STAGE"
tar -xzf "$WORK/$SERVER_ARCHIVE" -C "$STAGE" --strip-components=1
[ -f "$STAGE/LabelFrame.Server" ] || die "归档缺少入口 LabelFrame.Server（--strip-components=1 解包后未找到）——归档结构异常。"
chmod +x "$STAGE/LabelFrame.Server"

if [ -f "$STAGE/System.Private.CoreLib.dll" ]; then
  info "归档形态：self-contained（自带 .NET 运行时，无需目标机预装）。"
else
  info "归档形态：framework-dependent（需目标机 .NET 10 ASP.NET Core Runtime）。"
  RUNTIME_OK=0
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | awk '
    {
      for (i = 1; i < NF; i++) {
        if ($i == "Microsoft.AspNetCore.App") {
          split($(i + 1), parts, ".")
          if (parts[1] + 0 >= 10) { found = 1 }
        }
      }
    }
    END { exit(found ? 0 : 1) }
  '; then
    RUNTIME_OK=1
  fi
  if [ "$RUNTIME_OK" -ne 1 ]; then
    die "目标机缺少 .NET 10 ASP.NET Core Runtime（Server 需要 Microsoft.AspNetCore.App >= 10）。
  官方下载页：$DOTNET_DOWNLOAD_URL
  本脚本不自动安装运行时（发行版包管理器差异，DESIGN §6.12 决议）；装好后重跑本脚本即可。
  或改用 self-contained 归档（Release 默认形态，免运行时前置）。"
  fi
  info "运行时检测通过：Microsoft.AspNetCore.App >= 10 已在场。"
fi

if [ -f "$APP_DIR/appsettings.json" ]; then
  cp "$APP_DIR/appsettings.json" "$WORK/appsettings.kept"
  info "保留既有 appsettings.json（用户配置，对齐 Windows 侧不覆盖语义）。"
fi
rm -rf "$APP_DIR"
mv "$STAGE" "$APP_DIR"
if [ -f "$WORK/appsettings.kept" ]; then
  cp "$WORK/appsettings.kept" "$APP_DIR/appsettings.json"
fi
chown -R labelframe:labelframe "$APP_DIR" "$DATA_DIR" "$LOGS_DIR"

# ---- 管理界面 ----
if [ "$INSTALL_WEBUI" -eq 1 ]; then
  info "[3/5] 安装管理界面 -> $DATA_DIR/plugins/web-ui ..."
  WEBUI_DIR="$DATA_DIR/plugins/web-ui"
  rm -rf "$WEBUI_DIR"
  mkdir -p "$WEBUI_DIR"
  (cd "$WEBUI_DIR" && unzip -q "$WORK/$WEBUI_ARCHIVE")
  [ -f "$WEBUI_DIR/index.html" ] || die "管理界面 zip 解压后缺少 index.html——插件包不完整。"
  chown -R labelframe:labelframe "$WEBUI_DIR"
else
  info "[3/5] 跳过管理界面（--no-webui）。"
fi

# ---- systemd ----
info "[4/5] 安装 systemd 服务 $SERVICE ..."
cat > "/etc/systemd/system/$SERVICE.service" <<UNIT
[Unit]
Description=LabelFrame Server（模板库 / 作业中心 / 设备投递 / 调试出图 / 日志）
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=labelframe
Group=labelframe
WorkingDirectory=$APP_DIR
ExecStart=$APP_DIR/LabelFrame.Server
Environment=LABELFRAME_SERVER_LISTEN=http://0.0.0.0:$PORT
Environment=LABELFRAME_SERVER_DB=$DATA_DIR/server.db
Environment=LABELFRAME_SERVER_TEMPLATES_DB=$DATA_DIR/templates.db
Environment=LABELFRAME_SERVER_LOGS_DB=$DATA_DIR/logs.db
Environment=LABELFRAME_SERVER_LOG_FILE=$LOGS_DIR/server.log
Restart=on-failure
RestartSec=3
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null
systemctl restart "$SERVICE"

# ---- 状态与输出 ----
info "[5/5] 等待服务就绪（最长 30 秒）..."
READY=0
for _ in $(seq 1 30); do
  if download "http://127.0.0.1:$PORT/healthz" "$WORK/healthz.json" 2>/dev/null && grep -q '"status":"ok"' "$WORK/healthz.json"; then
    READY=1
    break
  fi
  sleep 1
done
if [ "$READY" -ne 1 ]; then
  systemctl --no-pager --lines=20 status "$SERVICE" || true
  journalctl -u "$SERVICE" -n 30 --no-pager || true
  die "服务 $SERVICE 未在 30 秒内就绪（/healthz 不通）——见上方状态与日志定位。"
fi

SERVER_IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
[ -n "$SERVER_IP" ] || SERVER_IP="<服务器IP>"
download "http://127.0.0.1:$PORT/api/server/info" "$WORK/server-info.json" 2>/dev/null || true
RUNNING_VERSION="$(awk 'match($0, /"version": *"[^"]*"/) { v = $0; sub(/.*"version": *"/, "", v); sub(/".*/, "", v); print v; exit }' "$WORK/server-info.json" 2>/dev/null || true)"
UI_ENABLED="$(awk 'match($0, /"uiEnabled": *(true|false)/) { v = $0; sub(/.*"uiEnabled": */, "", v); sub(/[,}].*/, "", v); print v; exit }' "$WORK/server-info.json" 2>/dev/null || true)"

VERSION_NOTE="未知（/api/server/info 不可达）"
if [ -n "$RUNNING_VERSION" ]; then
  if [ "$RUNNING_VERSION" = "$SERVER_VERSION" ]; then VERSION_NOTE="$RUNNING_VERSION（与清单一致）"; else VERSION_NOTE="$RUNNING_VERSION（注意：与清单 $SERVER_VERSION 不一致）"; fi
fi

echo
echo "================ LabelFrame Server 安装完成 ================"
echo "服务状态：$(systemctl is-active "$SERVICE")"
echo "版本：$VERSION_NOTE"
echo "健康检查：http://127.0.0.1:$PORT/healthz"
if [ "$INSTALL_WEBUI" -eq 1 ] && [ "$UI_ENABLED" = "true" ]; then
  echo "管理界面：http://$SERVER_IP:$PORT/"
elif [ "$INSTALL_WEBUI" -eq 1 ]; then
  echo "管理界面：已解压但服务端未识别（检查 $DATA_DIR/plugins/web-ui/index.html 与服务日志）"
else
  echo "管理界面：未安装（如需：重跑本脚本去掉 --no-webui）"
fi
echo "Windows Client「设置 → 连接方式」服务端地址填：http://$SERVER_IP:$PORT"
echo "如开启防火墙请放行：ufw allow $PORT/tcp"
echo "数据目录：$DATA_DIR（升级重跑本脚本不动此目录）；日志：$LOGS_DIR/server-<yyyyMMdd>.log（journalctl -u $SERVICE）"
echo "升级：重跑本脚本即可（覆盖升级，配置与数据保留）。"
echo "============================================================="
