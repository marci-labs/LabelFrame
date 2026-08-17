# LabelFrame

面向仓库场景的标签打印框架：模板契约 + 打印服务 + 设备宿主（PC / PDA）。

## 愿景

**方便仓库完成标签打印，提高办公效率。**

围绕这个愿景：
- **方便** = 少步骤、就近打印、零学习成本、少故障、少求人；
- **效率** = 打印快、能批量、不重复劳动、不出错、融进业务动作。

## 当前状态

- 迭代 0（奠基）：文档体系 + 解决方案骨架。
- 迭代 1（契约与 ZPL）：契约 / 版式模型、数据校验、ZPL 编码器（文本 / Code128 / 图片占位）、日志传输，单元测试全绿。
- 迭代 2（WinHost 打印闭环）：作业队列（SQLite 持久化 / 幂等 / 挂起恢复取消）、本地 HTTP API、打印 Worker、GDI 中文栅格化、TCP9100 / Windows 驱动 / Zebra SDK 传输，.NET 10。
- 迭代 3（Server 路由）：设备注册 / 心跳 / 目录、作业定向投递（宿主轮询领取）、结果回报、集中查询、测试入口。
- 迭代 4（模板管理 + 预览）：模板包 zip 导入导出、SQLite 模板库（按分组）、设计期 PNG 预览（ZXing 条码 / 二维码）。
- 迭代 6（P1 收尾）：失败项单独重打、打印机测试页 / 在线状态（本迭代；真实设备字段联调待执行）。
- 迭代 5（PDA 宿主）：AndroidHost（前台服务 / 开机自启 / 本地 HTTP / IP 9100 / Server 轮询 / 中文栅格化）已编译打包（真机验收待执行）。
- 迭代 7（Studio 模板工具 V1）：WPF 可视化界面管理模板、导入 `.lfpkg`、选模板预览并测试打印。
- 迭代 8（Studio 版式编排 V2）：画布拖拽编排元素、属性面板、契约字段编辑、保存与预览。
- 迭代 8B（Studio 版式增强）：字段键 / 显示名可编辑（引用联动）、新元素默认下排、文字对齐 / padding / 边框、区域（格子）布局。
- 迭代 8C（Studio 界面重构）：作业工作台 + 独立模板设计器（控件栏拖拽 / 毫米网格画布 / 区域拖矩形与元素入格居中 / 填充固定值或字段 / 本地实时预览 / 底部状态与日志栏）（本迭代；界面验收待执行）。
- 迭代 8D（设计器交互重做：容器控件 / 设计测试分离 / 字段自动推导 / 标尺对齐 / 框选多选手柄）：已完成（界面验收待执行）。
- 迭代 9（Excel 数据导入）：已完成（选 .xlsx → 列映射 → 批量打印）。
- 迭代 8E/8F（Web 设计器原型 v2/v3）：视口缩放 / 条码二维码实时渲染 / 智能参考线 / 文本溢出 / 画布留白标尺 / 真实比例 1mm=8点；`prototypes/web-designer/`，用于 UI 技术选型评估。
- 迭代 9（Excel 导入）/ 迭代 10（MSI 安装包）：已完成。
- 迭代 13（文本排版与二维码参数持久化）：元素契约补齐（wrap / lineHeight / fitMode / fontFamily / qrEcc / qrMargin / displayValue / paddingH-V）+ Skia 图片打印渲染 + 前端字段映射，前后端已完成（用户验收待执行）；产物 `LabelFrame-0.13.2.msi`。
- 迭代 22（打印测试体验 + 传输插件化 + 客户端下载分发）：✅ 已完成（2026-08-17）——下载 Excel 模板 / 客户端仅本机打印测试 / 作业历史按设备可见；传输插件化（统一接口 + 参数模型 + 注册表 + 外部 DLL 目录加载，卸载 = 删文件 + 重启生效）；客户端下载分发（`client-packages` + Server UI「客户端下载」+ 客户端设置「更新与安装包」）；本地 0.18.0 测试包。
- 迭代 23（客户端插件分发：上传服务端 + 客户端安装 / 卸载）：🔄 进行中（2026-08-17 后端完成——独立 `plugin-packages` 目录 + `/api/plugin-packages` + `/api/plugins` 安装卸载 API + 字节加载修复 Windows 文件锁；前端待 hermes 实施）。
详见 [docs/ROADMAP.md](docs/ROADMAP.md)。

## 组成

| 项目 | 说明 |
|---|---|
| `LabelFrame.Core` | 契约 / 版式模型、数据校验、ZPL 编码、作业队列（迭代 1 起实现） |
| `LabelFrame.Server` | 无头服务端：模板库 / 作业中心 / 设备投递 / 调试出图 / 日志（迭代 3 起实现；迭代 18 起以 Windows 服务运行、默认不提供 Web UI；迭代 20 起可选管理界面插件 `plugins/web-ui`） |
| `LabelFrame.WinHost` | Windows 打印宿主（迭代 2 起实现） |
| `LabelFrame.Studio` | Windows 模板工具：管理 / 导入导出 / 预览 / 测试打印（迭代 7 起实现） |
| `LabelFrame.AndroidHost` | Android / PDA 打印宿主（迭代 5 起实现） |


## Docker 部署（服务端管理界面插件挂载）

Docker 镜像 `ghcr.io/marci-labs/labelframe-server`（离线包 `artifacts/labelframe-server-0.16.0.docker.tar`，`docker load` 导入）默认无头；
需要管理界面时，把插件文件放进**挂载目录**即可（无需重启容器）：

- **容器内挂载目录（即 Server 的插件目录）**：`/var/lib/labelframe/server/plugins/web-ui`
- **宿主机目录（compose 默认）**：`./plugins/web-ui`

操作：
1. 解压 `labelframe-server-webui-0.16.0.zip` 到宿主机 `./plugins/web-ui`（zip 内的 index.html 等文件直接放该目录下）；
2. `docker compose up -d`（`packaging/ubuntu/docker-compose.yml` 已默认挂载该目录）；
3. 浏览器访问 `http://<服务器IP>:53961` 打开管理界面；移除该目录内容即恢复无头。

Windows 裸机部署同理：解压到 `%ProgramData%\LabelFrame\server\plugins\web-ui`。

## 自动化发布（迭代 21）

仓库已接入 GitHub Actions 自动发布（`.github/workflows/release.yml`），发新版本只需两步：

1. 更新 `docs/ROADMAP.md` 与 `CHANGELOG.md`，提交推送；
2. 打 tag 推送：`git tag v0.17.0 && git push origin v0.17.0`。

CI 自动完成：构建测试 → 打包（Server / Client MSI、管理界面插件 zip、Linux 归档）→ 推送 Docker 镜像到 ghcr.io → 创建 GitHub Release（附件含安装包）。

- **Docker 镜像**：`docker pull ghcr.io/marci-labs/labelframe-server:0.17.0`（`latest` tag 指向最新版；镜像默认无头，管理界面插件按上文挂载）。
- **安装包**：GitHub Release 页面下载：https://github.com/marci-labs/LabelFrame/releases
- **MSI 签名说明**：仓库配置 `MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD` 两个 Secret 时，MSI 会自动用该证书签名（当前为自签证书过渡方案，公开下载仍可能提示「未知发布者 / SmartScreen」，内网部署可把自签根证书加入受信任根消除警告；正式对外分发建议后续购买 OV 代码签名证书）；未配置 Secret 时跳过签名、正常发布。
- **本地覆盖**：compose 默认拉取 ghcr 镜像，可用环境变量覆盖（本地构建镜像调试）：`LABELFRAME_IMAGE=labelframe-server LABELFRAME_VERSION=0.16.0 docker compose up -d`。
- 打包脚本：CI 通过 `-WixPath` 指定 WiX（dotnet tool 版）；签名密码从环境变量 `MSI_SIGN_PASSWORD` 读取，仓库内不再有明文默认密码。

## 服务端管理界面（可选插件，迭代 20）

默认服务端保持无头（仅 /healthz + API）；需要时把前端 server 构建产物（`web/dist-server`）解压到插件目录即生效——放进去无需重启、移除即恢复无头：
- Windows：`%ProgramData%\LabelFrame\server\plugins\web-ui`
- Linux：`/var/lib/labelframe/server/plugins/web-ui`

打包：`scripts/package-server-webui.ps1`（先由前端产出 `web/dist-server`，产物 `artifacts/labelframe-server-webui-<version>.zip`）；Docker compose 已含可选卷挂载示例（`./plugins/web-ui`）。

## 安装包（迭代 10）

一键构建 MSI（需已安装 WiX Toolset v7 与 node / pnpm）：

```powershell
# 先构建前端产物
cd web; pnpm install; pnpm build; cd ..
# 一键打包（联网发布 self-contained + WiX 构建）
.\scripts\build-msi.ps1
```

产物（0.14.0 起双安装包，安装目录统一在 `C:\Program Files\LabelFrame\` 下用子目录区分）：`artifacts\LabelFrame-Server-0.15.0.msi` → `C:\Program Files\LabelFrame\Server`：无头服务端（模板库 / 作业中心 / 设备投递 / 调试出图 / 日志 / Excel，不接打印机、**不提供 Web UI**，安装为 Windows 服务 `LabelFrameServer`，默认监听 0.0.0.0:53961，数据在 `%ProgramData%\LabelFrame\server`）；`artifacts\LabelFrame-Client-0.15.0.msi`（约 14MB）→ `C:\Program Files\LabelFrame\Client`：打印客户端（**托管完整界面**：模板设计 / 数据与打印 / 连接配置 / 日志 / 作业历史；默认 ServerUrl=http://127.0.0.1:53961，单机模式保留）。单机使用 = 同机安装两个包；两个包的 appsettings.json 均为独立用户配置组件（覆盖安装 / 修复不覆盖、卸载保留）。卸载时会询问是否清除用户数据（默认不勾选；勾选则删除本程序产生的模板 / 作业 / 日志 / 连接与打印配置 / 机器级配置），覆盖升级不触发清理。

前置要求：目标机需安装 **.NET 10 Desktop Runtime**（x64，下载：https://dotnet.microsoft.com/download/dotnet/10.0）。安装 MSI 时会用 .NET 官方自检程序（NetCoreCheck）实时检测：已安装则直接继续（无需重启），缺失则弹出可点击的官方下载链接对话框（不自动安装）。
公开下载的 MSI 若未使用受信任商业证书签名，Windows 可能提示「未知发布者 / Windows 已保护你的电脑」，点「仍要运行」即可；内网部署可把自签根证书加入受信任根消除提示（见「自动化发布」）。
打印：统一为整版位图（Skia 渲染 → `^GF` 直传打印机），与画布预览同源（迭代 15 起移除矢量 ZPL）。连接方式（Log / TCP / Windows 驱动 / Zebra）在**客户端设置页**配置（先测试后生效、持久化 connection.json），服务端无打印机连接 UI；服务端地址为机器级配置（`%ProgramData%\LabelFrame\Client\settings.json`，经 `/api/host/config` 读写）。
干净电脑使用（单机）：安装 `LabelFrame-Server-0.15.0.msi` 与 `LabelFrame-Client-0.15.0.msi` 两个包。Server 装完弹窗（开机自启 / 立即运行，默认勾选）→ 服务随系统自启；Client 装完弹窗（立即打开，默认勾选）→ 浏览器打开 **http://127.0.0.1:53960** 进入完整界面（模板设计 / 数据与打印 / 连接配置 / 日志 / 作业历史）。多台打印电脑：每台装 Client，在设置页把服务端地址指向服务端电脑 IP 即可（机器级配置，同机浏览器一致）。

## Ubuntu 服务端部署（迭代 19，服务端 Linux + 客户端 Windows）

1. 发布 linux-x64 包（需 .NET SDK；Windows 上执行）：
   ```powershell
   .\scripts\publish-server-linux.ps1            # framework-dependent
   .\scripts\publish-server-linux.ps1 -SelfContained   # 免运行时包
   ```
   产物：`artifacts\labelframe-server-0.15.4-linux-x64.tar.gz`。
2. Ubuntu（22.04 / 24.04）安装运行时（framework-dependent 时需要）：https://dotnet.microsoft.com/download/dotnet/10.0（ASP.NET Core Runtime）。
3. 上传归档后部署（root / sudo）：
   ```bash
   sudo bash scripts/deploy-server-ubuntu.sh labelframe-server-0.15.4-linux-x64.tar.gz
   ```
   脚本会：建 `labelframe` 用户 → 解压到 `/opt/labelframe/server` → 数据目录 `/var/lib/labelframe/server` → 安装并启动 systemd 服务 `labelframe-server`（自启 + 崩溃重启）。
4. 验证与跨机使用：
   ```bash
   curl http://127.0.0.1:53961/healthz        # {"service":"LabelFrame.Server","status":"ok"}
   sudo ufw allow 53961/tcp                   # 如开启防火墙
   ```
   Windows Client 设置页「服务端地址」填 `http://<Ubuntu-IP>:53961` → 测试连接 → 保存并生效；之后设备注册 / 模板 / 作业（推送通知 <1s 领取）/ 调试出图 / 日志全链路走 Ubuntu 服务端，打印仍在 Windows 本机。
5. 配置：监听 / 数据库路径 / 历史清理保留期均可用 `LABELFRAME_SERVER_*` 环境变量覆盖（systemd 单元已设默认值）。
6. **Docker 镜像（推荐，迭代 19）**：已构建 `labelframe-server:0.15.4` 并导出离线包 `artifacts\labelframe-server-0.15.4.docker.tar`（106MB）。Ubuntu 上：
   ```bash
   docker load -i labelframe-server-0.15.4.docker.tar
   # 单条命令运行（数据卷持久化 + 崩溃自动重启 + 端口映射）
   docker run -d --name labelframe-server -p 53961:53961 \
     -v labelframe-data:/var/lib/labelframe/server \
     --restart unless-stopped labelframe-server:0.15.4
   # 或 docker compose -f packaging/ubuntu/docker-compose.yml up -d
   curl http://127.0.0.1:53961/healthz
   sudo ufw allow 53961/tcp   # 如开启防火墙
   ```
   **日志查看**：文本日志写到挂载目录 `./logs/server.log`（compose 默认）或宿主机 `/opt/store/labelframe/logs/server.log`（生产推荐），`tail -f` 即可；数据（server.db / templates.db / logs.db）在数据卷 `/var/lib/labelframe/server`。
   自行构建：`docker build -f packaging/ubuntu/Dockerfile -t labelframe-server:0.15.4 artifacts/server-linux/linux-x64`（基础镜像含 Skia 依赖）。
7. 跨机使用：Windows Client 设置页「服务端地址」填 `http://<Ubuntu-IP>:53961` → 测试连接 → 保存并生效。**注意：修改服务端地址后需重启 Client**（打印 Worker 使用启动时的地址连接服务端）。

辅助脚本：
- `scripts\generate-icon.ps1`：生成应用图标（双色 L 型，assets\labelframe.ico）。
- `scripts\create-signing-cert.ps1`：生成自签名代码签名证书（openssl + .NET 重封装）；`scripts\build-msi.ps1 -Sign` 签名 MSI（需本机有 signtool，或用环境变量 SIGNFILE 指定）。正式分发建议购买商业代码签名证书。
- `scripts\cleanup-residue.ps1`：管理员运行，清理历史安装残留（旧目录 / 快捷方式 / 注册表 / 数据目录）。


无需打印机即可验证 WinHost 打印闭环（日志传输模拟打印机）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\demo-winhost.ps1
```

脚本会：构建 → 启动 WinHost（127.0.0.1:53999，日志传输）→ 提交一个含中文的库位码作业（2 张）→ 展示生成的 ZPL。

接真实 Zebra 打印机（USB / IP / 驱动）时，设置环境变量后运行 WinHost：

```powershell
$env:LABELFRAME_TRANSPORT = "Zebra"        # Zebra SDK；或 Tcp / WindowsDriver / Log
$env:LABELFRAME_TCP_HOST = "192.168.1.50"  # Zebra TCP 模式
$env:LABELFRAME_PRINTER = "ZDesigner ZD421-203dpi ZPL"  # Zebra Driver / WindowsDriver 模式
$env:LABELFRAME_DB = "C:\LabelFrame\jobs.db"
dotnet run --project src\LabelFrame.WinHost
```

本地 API（默认 127.0.0.1:53960，演示脚本用 53999）：
- `POST /api/jobs`：提交作业（requestId + 自包含模板 + labels[]），返回 jobId。
- `GET /api/jobs/{jobId}`：进度与逐张状态。
- `POST /api/jobs/{jobId}/suspend|resume|cancel`：挂起 / 恢复 / 取消。

## 传输插件与客户端分发（迭代 22）

### 传输插件化（WinHost）

连接方式抽象为**传输插件**：统一接口（发送 / 状态 / 测试）+ 参数模型（前端按 spec 动态渲染表单）+ 注册表按需装配。
内置插件：`log`（模拟打印）、`tcp9100`（TCP 9100）、`winspool`（Windows 驱动）、`zebra`（Zebra Link-OS SDK）。

- **外部插件**：把厂商自研插件 DLL 放进插件目录 `%ProgramData%\LabelFrame\Client\plugins`（`LABELFRAME_PLUGINS` 可覆盖）→ 重启客户端即出现在可用插件列表，配置指定 `pluginId + params` 即启用；单个插件加载失败只记日志、不影响宿主启动。**卸载 = 删除插件文件 + 重启生效**（运行时热卸载见 DESIGN 未决）。
- **配置**：`%LOCALAPPDATA%\LabelFrame\connection.json` 新格式 `{ "pluginId": "tcp9100", "params": { "host": "...", "port": "9100" } }`；旧格式（`Mode` / `TcpHost` 等）自动迁移，老配置零改动。
- **API**：`GET /api/transport`（pluginId / displayText / availablePlugins spec）、`POST /api/transport`（pluginId + params，先测试后生效）、`GET /api/transport/plugins`（已装配插件列表）。
- **下载 Excel 模板**：数据与打印页「下载 Excel 模板」→ `POST /api/import/excel-template`（Server 与客户端都实现），按契约字段 + testData 生成 xlsx，可直接套用 Excel 导入做打印测试。

### 客户端下载分发（Server）

- **安装包目录**：`client-packages`（Windows `%ProgramData%\LabelFrame\server\client-packages`；Linux `/var/lib/labelframe/server/client-packages`；`LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）。目录直放文件或经管理界面「客户端下载」页上传都支持。
- **API**：`GET /api/client-packages`（列表）、`POST /api/client-packages`（上传）、`GET /api/client-packages/{file}`（下载）、`DELETE /api/client-packages/{file}`（删除）；文件名只允许普通文件名（路径穿越防护）。
- **客户端更新**：设置页「更新与安装包」列出服务端可用安装包，下载默认从服务端地址获取（不自动升级，下载后自行运行安装）。
- **Docker**：`docker-compose.yml` 已挂载 `./client-packages:/var/lib/labelframe/server/client-packages`，宿主机放入安装包即对局域网客户端可下载。

### 权限边界与作业历史（迭代 22）

- 客户端（本机界面）打印测试目标固定**本机**：本机已注册且在线 → 经服务端路由（作业进服务端历史）；本机未注册 / 离线 → 降级本机直连并提示原因。服务端管理界面可自由选在线设备打印测试。
- 客户端状态栏显示本机设备名称（`/api/host/config.deviceName`）。
- 作业历史：客户端只看自己的作业（`GET /api/jobs?deviceId={本机}`），服务端 UI 看全部。
### 插件包分发（迭代 23）

- **插件包格式**：`.lfplugin`（zip：根 `manifest.json`——pluginId / name / version 必填 + 可选 description / author / minHostVersion + 插件 DLL）。
- **服务端**：独立 `plugin-packages` 目录（Windows `%ProgramData%\LabelFrame\server\plugin-packages`；Linux `/var/lib/labelframe/server/plugin-packages`；`LABELFRAME_SERVER_PLUGIN_PACKAGES` 可覆盖）+ `GET /api/plugin-packages`（列表含元数据与 valid 状态）/ `POST`（上传，zip + manifest 校验、64MB 上限）/ `GET /{fileName}`（下载）/ `DELETE`（删除）；Server UI 新增「插件管理」页（与「客户端下载」并列）；Docker 挂载 `./plugin-packages`。
- **客户端安装 / 卸载**：设置页「插件管理」卡片（与「更新与安装包」并列）浏览服务端可用插件 → 安装（下载 → 三层校验 [zip + manifest / 内置插件 id 拒绝 / 临时 ALC 预检核对插件 id] → 解压到 `%ProgramData%\LabelFrame\Client\plugins\<pluginId>\`）→ **重启客户端生效**；可查看已安装插件与状态（已加载 / 待重启 / 加载失败 / 手动放置）；已安装插件可卸载（删目录 → 重启生效）。运行时热卸载 / 热替换不做（见 DESIGN 未决）。
- **API（WinHost）**：`GET /api/plugins/installed`、`POST /api/plugins/install`（multipart，64MB 上限）、`POST /api/plugins/uninstall`（`{ pluginId }`）；安装 / 卸载失败统一 400 `ErrorView` + 中文原因。
- **安全**：文件名 / 插件 ID 路径穿越防护（共享 `SafeFileName`）、zip 解压 zip-slip 防护、外部插件禁止覆盖内置插件 ID（决策 6A）；外部插件字节加载（`LoadFromStream`）不锁文件，「卸载 = 删除文件 + 重启生效」在 Windows 下可用（决策 #73）。

## Studio 使用（迭代 7）

```powershell
# 先构建并启动 WinHost（或让 Studio 一键启动）
dotnet run --project src\LabelFrame.WinHost
# 再启动 Studio
dotnet run --project src\LabelFrame.Studio
```

Studio 连接 WinHost（默认 127.0.0.1:53960）后：按分组浏览模板 → 导入 `.lfpkg` → 选模板填数据 → 刷新预览 → 打印测试（走 WinHost 当前传输：Log / TCP / Zebra / 驱动）。
## 文档

- [docs/DESIGN.md](docs/DESIGN.md) —— 架构设计与决策记录
- [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) —— 需求：场景、底线、能力、边界、成功衡量
- [docs/ROADMAP.md](docs/ROADMAP.md) —— 迭代计划与状态
- [AGENTS.md](AGENTS.md) —— 给 AI 协作的常驻约束
