# LabelFrame Server 离线部署包（__LABELFRAME_VERSION__，linux-x64）

本包用于**离线 / 内网环境**部署 LabelFrame 服务端：解压即用，部署全程零外网。
（在线环境不必用本包：直接用 Release 的 `compose.yml` + `.env` 联网拉镜像即可，见仓库 `docs/DEPLOY.md` §4。）

## 前置条件

- Linux x64 + Docker Engine（含 `docker compose` v2 子命令）——**本包不含 Docker 本体**，需目标机已安装；
- `sha256sum`（coreutils，一般发行版自带）。

## 三步部署

```bash
# 1. 校验并加载镜像（U 盘 / 内网共享拷贝后建议先校验）
sha256sum -c SHA256SUMS                                            # 完整性校验（镜像 + 分发产物）
docker load -i images/labelframe-server-__LABELFRAME_VERSION__.image.tar.gz

# 2. 启动（compose.yml 与 .env 已预配好：端口 53961 / 时区 / 数据路径 / 挂载目录）
#    先预建三个分发挂载目录：目录若缺失，up 时会被 Docker 守护进程（root）自动创建为 root:root，
#    非 root 部署者随后拷入安装包将被拒（详见「常见问题」；install.sh 已内置本步）
mkdir -p client-packages pda-packages plugin-packages
docker compose up -d

# 3. 验证
curl http://127.0.0.1:53961/healthz        # {"service":"LabelFrame.Server","status":"ok"}
```

管理界面 **http://\<本机 IP\>:53961/** 已随包预装（`plugins/web-ui/`），开箱即用。

## 一键脚本（等价于上面三步）

```bash
bash install.sh    # 校验 -> docker load -> 预建挂载目录 -> compose up -> 等待就绪并输出访问地址；幂等可重跑
```

## 分发客户端 / PDA / 插件安装包（离线环境闭环）

把 `packages/` 下的文件拷入当前目录的对应挂载目录，服务端各页面即可分发（也可经管理界面上传，效果相同）：

| 包内文件 | 拷入目录 | 分发通道 |
|---|---|---|
| `packages/client/*.msi` | `./client-packages/` | 客户端「设置 → 更新与安装包」+ 服务端下载中心 |
| `packages/pda/*.apk` | `./pda-packages/` | PDA 扫下载中心二维码安装 |
| `packages/plugin/*.lfplugin` | `./plugin-packages/` | 客户端 / PDA「插件管理」安装 |

三个挂载目录须由部署者创建（首次 `docker compose up -d` **之前** `mkdir -p`，`install.sh` 已内置）；若曾被 Docker 守护进程以 root 自动创建，直接拷入会被拒——处置见「常见问题」。也可不经目录直接经管理界面「下载中心」上传（效果相同，不受宿主目录属主影响）。

## 常见问题

- **端口 / 防火墙**：默认 `53961`；放行 `sudo ufw allow 53961/tcp`。Windows 客户端连接地址填 `http://<本机 IP>:53961`。
- **看日志**：`docker compose logs -f`，或文本日志 `./logs/server-<yyyyMMdd>.log`。
- **数据在哪**：命名卷 `labelframe-data`（`docker volume ls` 可见）；容器重建 / 升级数据不动。
- **升级**：拿新版本离线包，在新目录重复三步即可（旧目录 `docker compose down` 停止）。
- **拷入安装包报 `Permission denied`**：分发挂载目录（`client-packages/` / `pda-packages/` / `plugin-packages/`）属主为 root——成因是首次 `docker compose up -d` 前目录不存在，被 Docker 守护进程（root）自动创建为 `root:root 0755`，非 root 部署者不可写。处置：`sudo chown -R "$(id -un):$(id -gn)" client-packages pda-packages plugin-packages` 后重拷，或改经管理界面「下载中心」上传。预防：首次 up 前先 `mkdir -p` 三目录（`install.sh` 已内置，重跑会检测并提示）。
- **起不来排查**：`docker compose ps` 看状态，`docker compose logs --tail=100` 看报错；常见为端口被占用（改 `compose.yml` 端口映射）或数据卷权限。
- **为何离线可用**：镜像以 ghcr 全名 tag 导入本机（`docker load`），compose 起容器时本地命中即不再联网拉取。

## 包内容一览

| 文件 / 目录 | 说明 |
|---|---|
| `images/labelframe-server-__LABELFRAME_VERSION__.image.tar.gz` | 服务端 Docker 镜像（`docker save` 导出） |
| `compose.yml` + `.env` | 部署描述与预配环境（版本已钉定；与在线分发附件同源） |
| `install.sh` / `README.md` | 一键部署脚本 / 本文档 |
| `SHA256SUMS` | 镜像与分发产物哈希（`install.sh` 强制校验） |
| `packages/` | 客户端 MSI / PDA APK / 官方传输插件包（见上表） |
| `plugins/web-ui/` | 服务端管理界面（已预解压，随容器挂载生效） |
