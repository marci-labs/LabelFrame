#!/usr/bin/env bash
# LabelFrame Server Ubuntu 部署脚本（迭代 19）
# 用法（root / sudo）：sudo bash scripts/deploy-server-ubuntu.sh <labelframe-server-...-linux-x64.tar.gz>
set -euo pipefail

ARCHIVE="${1:?用法: sudo bash $0 <labelframe-server-...-linux-x64.tar.gz>}"
APP_DIR=/opt/labelframe/server
DATA_DIR=/var/lib/labelframe/server\nLOGS_DIR=/var/lib/labelframe/logs
SERVICE=labelframe-server

if [ "$(id -u)" -ne 0 ]; then
  echo "请用 root 或 sudo 运行。"
  exit 1
fi
if [ ! -f "$ARCHIVE" ]; then
  echo "找不到归档：$ARCHIVE"
  exit 1
fi

echo "[1/4] 创建用户与目录 ..."
id -u labelframe >/dev/null 2>&1 || useradd --system --home /opt/labelframe --shell /usr/sbin/nologin labelframe
mkdir -p "$APP_DIR" "$DATA_DIR" "$LOGS_DIR"
chown -R labelframe:labelframe "$APP_DIR" "$DATA_DIR" "$LOGS_DIR"

echo "[2/4] 解压发布物 -> $APP_DIR ..."
mkdir -p "$APP_DIR"
tar -xzf "$ARCHIVE" -C "$APP_DIR" --strip-components=1
chown -R labelframe:labelframe "$APP_DIR"

echo "[3/4] 安装 systemd 单元 ..."
cat > /etc/systemd/system/$SERVICE.service <<'UNIT'
[Unit]
Description=LabelFrame Server（模板库 / 作业中心 / 设备投递 / 调试出图 / 日志）
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=labelframe
Group=labelframe
WorkingDirectory=/opt/labelframe/server
ExecStart=/opt/labelframe/server/LabelFrame.Server
Environment=LABELFRAME_SERVER_LISTEN=http://0.0.0.0:53961
Environment=LABELFRAME_SERVER_DB=/var/lib/labelframe/server/server.db
Environment=LABELFRAME_SERVER_TEMPLATES_DB=/var/lib/labelframe/server/templates.db
Environment=LABELFRAME_SERVER_LOGS_DB=/var/lib/labelframe/server/logs.db\nEnvironment=LABELFRAME_SERVER_LOG_FILE=/var/lib/labelframe/logs/server.log
Restart=on-failure
RestartSec=3
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable "$SERVICE"
systemctl restart "$SERVICE"

echo "[4/4] 状态 ..."
sleep 2
systemctl --no-pager --lines=15 status "$SERVICE"
echo "本机冒烟：curl http://127.0.0.1:53961/healthz"
echo "如开启防火墙请放行：ufw allow 53961/tcp"
echo "Windows Client 设置页「服务端地址」填：http://<本机IP>:53961"