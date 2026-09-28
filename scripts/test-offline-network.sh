#!/usr/bin/env bash
# 离线包网络接入回归：隔离 Docker 命令，验证部署入口的配置与失败语义。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REAL_DOCKER="$(command -v docker || true)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/bin" "$TMP/bundle/images" "$TMP/bundle/packages/client" \
  "$TMP/bundle/packages/pda" "$TMP/bundle/packages/plugin"
cp "$ROOT/packaging/offline/install.sh" "$TMP/bundle/install.sh"
cp "$ROOT/packaging/ubuntu/docker-compose.yml" "$TMP/bundle/compose.yml"
printf 'LABELFRAME_VERSION=0.30.0\n' > "$TMP/bundle/.env"
printf 'fixture\n' > "$TMP/bundle/images/labelframe-server-0.30.0.image.tar.gz"
printf 'fixture\n' > "$TMP/bundle/packages/client/client.msi"
printf 'fixture\n' > "$TMP/bundle/packages/pda/pda.apk"
printf 'fixture\n' > "$TMP/bundle/packages/plugin/plugin.lfplugin"
(cd "$TMP/bundle" && sha256sum images/* packages/*/* > SHA256SUMS)

cat > "$TMP/bin/docker" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$DOCKER_CALLS"
if [ "$1" = network ]; then
  [ "$5" = shared-print ] || exit 1
  printf 'bridge|shared-print|local\n'
fi
exit 0
MOCK
cat > "$TMP/bin/curl" <<'MOCK'
#!/usr/bin/env bash
printf '{"status":"ok"}\n'
MOCK
chmod +x "$TMP/bin/docker" "$TMP/bin/curl"
export PATH="$TMP/bin:$PATH" DOCKER_CALLS="$TMP/calls"

run() { (cd "$TMP/bundle" && bash install.sh > "$TMP/out" 2>&1); }
fail() { echo "失败：$*" >&2; cat "$TMP/out" >&2; exit 1; }

run || fail '默认部署'
[ ! -e "$TMP/bundle/.labelframe-network.compose.yml" ] || fail '默认模式生成覆盖文件'
grep -q 'compose -f compose.yml up -d' "$DOCKER_CALLS" || fail '默认模式未使用原 Compose'

printf 'LABELFRAME_EXTERNAL_NETWORK=shared-print\n' > "$TMP/bundle/network.env"
: > "$DOCKER_CALLS"
run || fail '共享网络部署'
grep -q 'name: shared-print' "$TMP/bundle/.labelframe-network.compose.yml" || fail '覆盖文件网络名'
grep -q 'compose -f compose.yml -f .labelframe-network.compose.yml up -d' "$DOCKER_CALLS" || fail '共享网络未应用覆盖文件'
if [ -n "$REAL_DOCKER" ]; then
  (cd "$TMP/bundle" && "$REAL_DOCKER" compose -f compose.yml -f .labelframe-network.compose.yml config --format json) > "$TMP/config.json" || fail '真实 Compose 解析失败'
  grep -q '"name": "shared-print"' "$TMP/config.json" || fail '真实 Compose 缺少共享网络'
fi
run || fail '同目录重跑未保持网络'

printf 'LABELFRAME_EXTERNAL_NETWORK=missing\n' > "$TMP/bundle/network.env"
: > "$DOCKER_CALLS"
if run; then fail '不存在网络被接受'; fi
grep -q '不存在' "$TMP/out" || fail '缺少可诊断错误'
! grep -q ' up -d' "$DOCKER_CALLS" || fail '错误网络仍启动了服务'

printf 'LABELFRAME_EXTERNAL_NETWORK=bad:name\n' > "$TMP/bundle/network.env"
if run; then fail '无效网络名被接受'; fi
grep -q '网络名无效' "$TMP/out" || fail '无效名称缺少错误'

printf 'LABELFRAME_EXTERNAL_NETWORK=\n' > "$TMP/bundle/network.env"
run || fail '空值未退回独立模式'
[ ! -e "$TMP/bundle/.labelframe-network.compose.yml" ] || fail '空值未清理覆盖文件'
echo '离线网络配置回归通过'
