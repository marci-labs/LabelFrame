# LabelFrame 部署指南

面向管理员 / IT：安装包、Docker、Ubuntu、管理界面插件、分发通道与常见配置。
快速上手见 [README](../README.md)；架构与决策见 [DESIGN](DESIGN.md)。

## 1. 部署形态对照

| 形态 | 服务端 | 打印电脑 | 适用 |
|---|---|---|---|
| 单机 | Server MSI + Client MSI 同机安装 | 同一台 | 一台电脑一台打印机 |
| Windows 服务器 | Server MSI（Windows 服务 `LabelFrameServer`，默认 0.0.0.0:53961） | 每台装 Client MSI | 局域网多机 |
| Linux 服务器 | Docker（推荐）或 systemd | 每台装 Client MSI | 局域网多机 |
| Linux 自动化测试 | Server Docker + Linux Client Docker | `log` 模拟输出 | 无打印机的发布制品 E2E |

默认端口：Server `53961`、Client 本机界面 `53960`、PDA 宿主 `53970`。

## 2. 安装包（MSI）

下载：[GitHub Releases](https://github.com/marci-labs/LabelFrame/releases)。

- **LabelFrame-Server-x.x.x.msi** → `C:\Program Files\LabelFrame\Server`：无头服务端（模板库 / 作业中心 / 设备投递 / 调试出图 / 日志 / Excel），不接打印机、不提供 Web UI；安装为 Windows 服务 `LabelFrameServer`，数据在 `%ProgramData%\LabelFrame\server`。
- **LabelFrame-Client-x.x.x.msi** → `C:\Program Files\LabelFrame\Client`：打印客户端，托管完整界面（模板设计 / 数据与打印 / 连接配置 / 日志 / 作业历史），浏览器打开 `http://127.0.0.1:53960`。

要点：

- 前置：.NET 10 Desktop Runtime（x64）。MSI 内置官方自检（NetCoreCheck）：缺失时弹出可点击的官方下载链接（不自动安装）。
- 单机使用 = 同机安装两个包；多台打印电脑 = 每台装 Client，设置页把服务端地址指向服务端 IP。
- 两个包的 appsettings.json 均为独立用户配置组件：覆盖安装 / 修复不覆盖、卸载保留。卸载时可选是否清除用户数据（默认不清除）。
- 公开下载的 MSI 若未用受信任商业证书签名，Windows 可能提示「未知发布者」，点「仍要运行」即可；内网可把自签根证书加入受信任根消除提示（见 §7 签名）。
- 打印统一为整版位图（Skia 渲染 → `^GF` 直传打印机），与画布预览同源；连接方式在客户端「设置」页配置（先测试后生效）。
- **修改服务端地址后需重启 Client**（打印 Worker 使用启动时的地址）。
- 清理历史安装残留：管理员运行 `scripts\cleanup-residue.ps1`。

## 3. Docker（推荐的服务端部署方式）

镜像 `ghcr.io/marci-labs/labelframe-server`（`latest` 指向最新版）：

```bash
docker pull ghcr.io/marci-labs/labelframe-server:latest
docker run -d --name labelframe-server -p 53961:53961 \
  -v labelframe-data:/var/lib/labelframe/server \
  --restart unless-stopped ghcr.io/marci-labs/labelframe-server:latest
# 或 docker compose -f packaging/ubuntu/docker-compose.yml up -d
curl http://127.0.0.1:53961/healthz   # {"service":"LabelFrame.Server","status":"ok"}
```

- 数据（server.db / templates.db / logs.db）在数据卷 `/var/lib/labelframe/server`；文本日志在挂载目录 `./logs/server.log`，`tail -f` 即可。
- compose 已默认挂载 `./plugins/web-ui`（管理界面插件）与 `./client-packages`（客户端安装包分发），见下文 §5 / §6。
- 自行构建：`docker build -f packaging/ubuntu/Dockerfile -t labelframe-server artifacts/server-linux/linux-x64`。
- 本地构建镜像调试：`LABELFRAME_IMAGE=labelframe-server LABELFRAME_VERSION=0.20.2 docker compose up -d`。

### 3.1 Server + Linux Log Client 本地 E2E

正式镜像 `ghcr.io/marci-labs/labelframe-client` 是仅用于无打印机自动化测试的 Linux 无头 Client。它固定使用 `log` 模拟打印，不包含 TCP 9100、USB、Windows 驱动、Zebra SDK、第三方插件或客户端 Web UI，不能替代物理打印验收。

验证已发布的同版本 Server / Client（Compose 不含 `build`）：

```powershell
$env:LABELFRAME_VERSION = "0.22.0"
powershell -ExecutionPolicy Bypass -File .\scripts\test-linux-client-e2e.ps1 `
  -ComposeFile packaging/e2e/compose.release.yaml -SkipBuild
docker compose -f .\packaging\e2e\compose.release.yaml down
```

验证当前源码候选：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-linux-client-e2e.ps1
# 通过后环境保持运行：http://127.0.0.1:53910
docker compose -f .\packaging\e2e\compose.yaml down
```

脚本验证 Linux 能力边界、模板 / 预览 / 包导入导出、Excel / 日志公共端点、设备注册、幂等、单张 / 多张、离线暂存、Skia 渲染、PNG 数量与条码内容、Server 终态回报、Client 重启持久化及重启后继续领取；数据保存在 Compose 命名卷。端口冲突时传 `-ServerPort <端口>`。完整测试大纲与排障方式见 [LINUX-CLIENT-E2E.md](LINUX-CLIENT-E2E.md)。

## 4. Ubuntu（systemd 裸机部署）

1. Windows 上发布 linux-x64 包：
   ```powershell
   .\scripts\publish-server-linux.ps1            # framework-dependent
   .\scripts\publish-server-linux.ps1 -SelfContained   # 免运行时包
   ```
2. Ubuntu 22.04 / 24.04 安装 ASP.NET Core Runtime（framework-dependent 时需要）。
3. 上传归档后部署：`sudo bash scripts/deploy-server-ubuntu.sh labelframe-server-x.x.x-linux-x64.tar.gz`
   脚本会：建 `labelframe` 用户 → 解压 `/opt/labelframe/server` → 数据目录 `/var/lib/labelframe/server` → 安装并启动 systemd 服务（自启 + 崩溃重启）。
4. 防火墙放行：`sudo ufw allow 53961/tcp`；Windows Client 设置页填 `http://<Ubuntu-IP>:53961`。

## 5. 服务端管理界面（可选插件）

服务端默认无头（仅 `/healthz` + API）。需要管理界面时，把前端 server 构建产物（`web/dist-server`，或 Release 里的 `labelframe-server-webui-x.x.x.zip`）放进插件目录即可——**放入即生效、移除即无头，无需重启**：

- Windows：`%ProgramData%\LabelFrame\server\plugins\web-ui`
- Linux / Docker：`/var/lib/labelframe/server/plugins/web-ui`（compose 默认挂载 `./plugins/web-ui`）

打包脚本：`scripts/package-server-webui.ps1`。管理界面与客户端界面是同一前端的两种构建（工作台 / 设计器 / 在线设备 / 作业历史 / 客户端下载 / 插件管理），无打印机相关内容。

## 6. 分发通道

### 客户端安装包分发（服务端集中下载）

- 目录 `client-packages`（Windows `%ProgramData%\LabelFrame\server\client-packages`；Linux `/var/lib/labelframe/server/client-packages`；环境变量 `LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）。
- 目录直放文件或经管理界面「客户端下载」页上传；客户端设置页「更新与安装包」列出并可下载（不自动升级，下载后自行运行安装）。
- API：`GET/POST /api/client-packages`、`GET/DELETE /api/client-packages/{file}`（路径穿越防护）。

### 传输插件分发（`.lfplugin`）

- 插件包 = zip（根 `manifest.json`：pluginId / name / version 必填 + 插件 DLL）；服务端独立 `plugin-packages` 目录 + `/api/plugin-packages` 上传 / 列表 / 下载（64MB 上限、上传即校验）。
- 客户端设置页「插件管理」：浏览 → 安装（下载 → 三层校验 → 解压到 `%ProgramData%\LabelFrame\Client\plugins\<pluginId>\`）→ **重启客户端生效**；卸载 = 删目录 + 重启。外部插件字节加载，不锁文件。
- 外部插件 DLL 也可手动放入 `%ProgramData%\LabelFrame\Client\plugins`（`LABELFRAME_PLUGINS` 可覆盖），单个加载失败只记日志不影响宿主。
- 连接配置：`%LOCALAPPDATA%\LabelFrame\connection.json`，格式 `{ "pluginId": "tcp9100", "params": { "host": "...", "port": "9100" } }`；旧格式自动迁移。
- 内置传输插件：`log`（模拟打印）、`tcp9100`、`winspool`（Windows 驱动）、`zebra`（Zebra Link-OS SDK）。插件接口见 DESIGN「传输插件」相关决策记录。

## 7. 自动化发布与签名

- 发版两步：① 更新 `docs/ROADMAP.md` 与 `CHANGELOG.md` 提交推送；② 例如 `git tag v0.22.0 && git push origin v0.22.0`。
- CI 自动：构建测试 → 双 MSI（可签名）→ 管理界面插件 zip → Linux 归档 → 同一次构建的 Server / Linux Client 候选镜像通过 Compose E2E → 原镜像推 ghcr.io（版本号 + `latest`）→ GitHub Release。
- MSI 签名：配置 Secret `MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD` 时自动签名，否则跳过。当前为自签证书过渡方案（公开下载仍可能 SmartScreen 提示），正式对外分发建议购买 OV 代码签名证书。本地签名：`scripts\create-signing-cert.ps1` 生成证书，`scripts\build-msi.ps1 -Sign` 使用。

## 8. 配置与环境变量

- 服务端监听 / 数据库路径 / 历史清理保留期（作业默认 30 天、日志默认 90 天）均可用 `LABELFRAME_SERVER_*` 环境变量覆盖（systemd 单元已设默认值）。
- WinHost：`appsettings.json` 的 `WinHost` 节 + `LABELFRAME_*` 环境变量覆盖；常用传输变量示例：
  ```powershell
  $env:LABELFRAME_TRANSPORT = "Zebra"        # Zebra SDK；或 Tcp / WindowsDriver / Log
  $env:LABELFRAME_TCP_HOST = "192.168.1.50"
  $env:LABELFRAME_PRINTER = "ZDesigner ZD421-203dpi ZPL"
  dotnet run --project src\LabelFrame.WinHost
  ```

## 9. 辅助脚本

- `scripts\demo-winhost.ps1`：无打印机验证打印闭环（构建 → 启动 WinHost → 提交含中文作业 → 展示 ZPL）。
- `scripts\generate-icon.ps1`：生成应用图标。
- `scripts\cleanup-residue.ps1`：清理历史安装残留（管理员运行）。
