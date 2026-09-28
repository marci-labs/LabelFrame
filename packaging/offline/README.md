# LabelFrame Server 离线部署包（__LABELFRAME_VERSION__，linux-x64）

本包用于**离线 / 内网环境**部署 LabelFrame 服务端：解压即用，部署全程零外网。
（在线环境不必用本包：直接用 Release 的 `compose.yml` + `.env` 联网拉镜像即可，见仓库 `docs/DEPLOY.md` §4。）

## 前置条件

- Linux x64 + Docker Engine（含 `docker compose` v2 子命令）——**本包不含 Docker 本体**，需目标机已安装；
- `sha256sum`（coreutils，一般发行版自带）。

## 解压（固定部署目录）

在放包的目录执行（`--strip-components=1` 剥掉包内版本号顶层目录，命令对任意版本通用；部署与升级始终用**同一目录**——数据卷实际名 = `<部署目录名>_labelframe-data`，见「常见问题 · 数据在哪」）：

```bash
mkdir -p labelframe-offline
tar -xzf labelframe-offline-__LABELFRAME_VERSION__-linux-x64.tar.gz -C labelframe-offline --strip-components=1
cd labelframe-offline
```

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
bash install.sh    # 校验 -> docker load -> 预建挂载目录 -> 自动分发 packages 三件 -> compose up -> 等待就绪并输出访问地址；幂等可重跑
```

### 可选：接入一个现有 Docker 网络

默认不配置时，服务只加入自身 Compose 网络，仍发布宿主端口 53961。若同机其他容器需要通过容器 DNS 访问服务，先由网络所有者创建一个受信任的用户自定义 bridge 网络，然后在离线包部署目录创建 `network.env`：

```bash
docker network create shared-print       # 网络已存在时跳过
printf 'LABELFRAME_EXTERNAL_NETWORK=shared-print\n' > network.env
bash install.sh
```

也可单次运行 `LABELFRAME_EXTERNAL_NETWORK=shared-print bash install.sh`（进程变量优先于 `network.env`）。脚本在启动前检查网络存在且为本地用户自定义 bridge 网络；无效名称或不存在时明确报错。同目录升级时保留 `network.env`，无需重新填写；重建 / 升级均运行 `bash install.sh`，不要绕过脚本只执行 `docker compose up -d`，后者不会应用可选网络配置。清空 `network.env` 中的变量值并重跑可恢复独立模式。一个部署只支持一个外部网络，多网络另行评估。

调用方容器须由其自身部署接入相同网络，`ServerUrl=http://labelframe-server:53961`；宿主机进程仍用 `http://127.0.0.1:53961`，远端 PC / PDA 使用 `http://<宿主机可达 IP>:53961`（需放通防火墙并保证路由可达）。网络别名仅在共享网络内解析，避免在该网络上给其他容器使用同名别名。服务端 API 当前无鉴权，接入共享网络会扩大容器内可达范围，只接入受信任网络。在线版 Release `compose.yml` + `.env` 的直接 Compose 部署暂不支持此选项。

## 分发客户端 / PDA / 插件安装包（离线环境闭环）

`install.sh` 在启动服务前**自动**把 `packages/` 三件拷入对应挂载目录（`cp -f` 幂等覆盖，包内原件保留），部署完成即可用——下载中心三列表开箱非空，客户端 / PDA / 插件安装全程无需外网。对应关系：

| 包内文件 | 拷入目录 | 分发通道 |
|---|---|---|
| `packages/client/*.msi` | `./client-packages/` | 客户端「设置 → 更新与安装包」+ 服务端下载中心 |
| `packages/pda/*.apk` | `./pda-packages/` | PDA 扫下载中心二维码安装 |
| `packages/plugin/*.lfplugin` | `./plugin-packages/` | 客户端 / PDA「插件管理」安装 |

后续增删安装包：直接向上述挂载目录拷入 / 删除文件（即时生效），或经管理界面「下载中心」上传（效果相同，不受宿主目录属主影响）。三个挂载目录由 `install.sh` 预建（部署者属主）并自动分发；走手动三步（不经 `install.sh`）时须在首次 `docker compose up -d` **之前** `mkdir -p` 三目录并自行拷贝 `packages/` 三件——目录若曾被 Docker 守护进程以 root 自动创建，拷入会被拒（处置见「常见问题」；`install.sh` 对不可写目录跳过自动分发并在完成输出标注，处置后重跑即补齐）。

## 常见问题

- **端口 / 防火墙**：默认 `53961`；放行 `sudo ufw allow 53961/tcp`。Windows 客户端连接地址填 `http://<本机 IP>:53961`。
- **看日志**：`docker compose logs -f`，或文本日志 `./logs/server-<yyyyMMdd>.log`。
- **数据在哪**：命名卷，实际卷名 = `<部署目录名>_labelframe-data`（compose 未钉定卷名，卷名随部署目录——如部署目录 `labelframe-offline` → 卷 `labelframe-offline_labelframe-data`，`docker volume ls` 可见）；容器重建 / 同目录升级数据不动。
- **升级**：拿新版本离线包，解压到**当初部署的同一目录**（同「解压」一节命令，`--strip-components=1` 覆盖解压）后重跑 `install.sh`（或三步）即可——数据卷沿用、模板 / 数据库不中断；**勿换新目录升级**：换目录会新建空卷（数据不跟随），且容器名 `labelframe-server` 固定，新目录起服务前须先在旧目录 `docker compose down`。可选清理：删除 `images/` 下旧版本 `.image.tar.gz` 释放磁盘（约 150MB+/ 版本，不影响运行与数据）。
- **拷入安装包报 `Permission denied`**：分发挂载目录（`client-packages/` / `pda-packages/` / `plugin-packages/`）属主为 root——成因是首次 `docker compose up -d` 前目录不存在，被 Docker 守护进程（root）自动创建为 `root:root 0755`，非 root 部署者不可写；此形态下 `install.sh` 会跳过向该目录自动分发并在完成输出标注。处置：`sudo chown -R "$(id -un):$(id -gn)" client-packages pda-packages plugin-packages` 后重跑 `install.sh`（自动补齐分发，幂等）或手工重拷，或改经管理界面「下载中心」上传。预防：首次 up 前先 `mkdir -p` 三目录（`install.sh` 已内置，重跑会检测并提示）。
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
