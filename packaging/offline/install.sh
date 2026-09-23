#!/usr/bin/env bash
# LabelFrame Server 离线部署一键脚本（迭代 101，Issue #213；契约：docs/DESIGN.md 决策 #159）
# 位于离线包内，随包分发（scripts/make-offline-bundle.sh 组包时复制；源文件 packaging/offline/install.sh）。
#
# 用法（离线包解压目录内，无需 root；目标机需已装 Docker Engine 含 compose v2）：
#   bash install.sh
#
# 行为：SHA256SUMS 全件校验（不符即拒，fail-closed）→ docker load 镜像（tag 为 ghcr 全名，load 后本地命中，
#   compose 默认 pull=missing 不再联网拉取）→ docker compose up -d → /healthz 轮询就绪 → 输出访问地址。
# 幂等：重跑无害（重复 load / up 均合法）；升级 = 换新版本离线包目录重跑（数据在命名卷 labelframe-data，不动）。
set -euo pipefail

BUNDLE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$BUNDLE_DIR"
PORT=53961
IMAGE_REPO_DEFAULT="ghcr.io/marci-labs/labelframe-server"

die() { echo "[LabelFrame 离线部署] 错误：$*" >&2; exit 1; }
info() { echo "[LabelFrame 离线部署] $*"; }

command -v docker >/dev/null 2>&1 || die "未找到 docker——本包不含 Docker 本体，请先在目标机安装 Docker Engine。"
docker compose version >/dev/null 2>&1 || die "未找到 docker compose 子命令（需 Docker Compose v2）。"
command -v sha256sum >/dev/null 2>&1 || die "未找到 sha256sum（Ubuntu / Debian：apt-get install -y coreutils）。"

# 就绪探测：宿主 curl / wget 优先，两者皆无则借容器内 curl（最小化主机也可用）
probe_healthz() {
  if command -v curl >/dev/null 2>&1; then
    curl -fsS --max-time 3 "http://127.0.0.1:$PORT/healthz" 2>/dev/null
  elif command -v wget >/dev/null 2>&1; then
    wget -q -T 3 -O - "http://127.0.0.1:$PORT/healthz" 2>/dev/null
  else
    docker exec labelframe-server curl -fsS "http://127.0.0.1:$PORT/healthz" 2>/dev/null
  fi
}

# ---- 1) 完整性校验（fail-closed：U 盘 / 内网共享拷贝后必过此关）----
[ -s SHA256SUMS ] || die "缺 SHA256SUMS——包不完整，请重新解压或重新获取离线包。"
info "[1/4] 校验包完整性（SHA256SUMS，$(wc -l < SHA256SUMS) 条）..."
if ! sha256sum -c SHA256SUMS --quiet; then
  die "SHA256SUMS 校验失败——包不完整或被篡改，拒绝部署。"
fi
info "校验通过：镜像与分发产物逐文件一致。"

# ---- 2) 版本与镜像引用（读 .env；镜像 tag 为 ghcr 全名——与 compose.yml 同源引用一致）----
[ -s .env ] || die "缺 .env——包不完整。"
[ -s compose.yml ] || die "缺 compose.yml——包不完整。"
VERSION="$(awk -F= '/^LABELFRAME_VERSION=/{print $2; exit}' .env)"
[ -n "$VERSION" ] || die ".env 缺 LABELFRAME_VERSION——包异常。"
IMAGE_OVERRIDE="$(awk -F= '/^LABELFRAME_IMAGE=/{print $2; exit}' .env)"
IMAGE="${IMAGE_OVERRIDE:-$IMAGE_REPO_DEFAULT}:$VERSION"
IMAGE_TAR="images/labelframe-server-$VERSION.image.tar.gz"
[ -s "$IMAGE_TAR" ] || die "缺镜像 tar：$IMAGE_TAR（与 .env 版本 $VERSION 不匹配？）"

# ---- 3) docker load（load 后本地命中 ghcr 全名 tag，起容器不再联网拉取）----
info "[2/4] 加载镜像 $IMAGE ..."
docker load -i "$IMAGE_TAR"
docker image inspect "$IMAGE" >/dev/null 2>&1 || die "load 后仍未找到镜像 $IMAGE——镜像 tar 与 .env 版本可能不一致。"
info "镜像就位（本地命中，后续 compose 起容器不再联网拉取）。"

# ---- 4) compose up + 就绪等待 ----
info "[3/4] 启动服务（docker compose up -d）..."
docker compose up -d

info "[4/4] 等待服务就绪（最长 90 秒）..."
READY=0
for _ in $(seq 1 90); do
  if probe_healthz | grep -q '"status":"ok"'; then READY=1; break; fi
  sleep 1
done
if [ "$READY" -ne 1 ]; then
  docker compose ps || true
  docker compose logs --tail=50 || true
  die "服务未在 90 秒内就绪（/healthz 不通）——见上方容器状态与日志定位。"
fi

SERVER_IP=""
# hostname -I 仅 Linux 支持（Windows / macOS 的 hostname 不认 -I）——失败不致命，回退占位提示
hostname -I >/dev/null 2>&1 && SERVER_IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
[ -n "$SERVER_IP" ] || SERVER_IP="<本机 IP>"

echo
echo "================ LabelFrame Server 离线部署完成 ================"
echo "健康检查：http://127.0.0.1:$PORT/healthz"
echo "管理界面：http://$SERVER_IP:$PORT/（已随包预装，开箱可用）"
echo "Windows Client「设置 → 连接方式」服务端地址填：http://$SERVER_IP:$PORT"
echo "分发安装包：把 packages/ 下文件拷入当前目录对应挂载目录即可经下载中心分发——"
echo "  packages/client/*.msi        -> ./client-packages/（客户端安装 / 更新）"
echo "  packages/pda/*.apk           -> ./pda-packages/（PDA 扫下载中心二维码安装）"
echo "  packages/plugin/*.lfplugin   -> ./plugin-packages/（客户端 / PDA 插件管理安装）"
echo "常用命令：docker compose logs -f（跟日志）/ docker compose restart（重启）/ docker compose down（停止，数据卷保留）"
echo "防火墙放行：sudo ufw allow $PORT/tcp"
echo "升级：换新版本离线包目录重跑 install.sh（数据在命名卷 labelframe-data，不动）。"
echo "================================================================="
