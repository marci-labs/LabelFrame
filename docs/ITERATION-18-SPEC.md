# 迭代 18 规格：无头服务端 + 客户端 UI 回归 + Windows 服务 + 历史清理（0.15.0）

> 状态：2026-08-11 决策已确认（用户拍板 8 项），任务单定稿；后端待开工，前端任务交 hermes 评估。
> 依据：docs/ARCHITECTURE-SPLIT.md（0.15 架构修订）、docs/DESIGN.md（决策 #53-58）。

## 1. 背景与目标

0.14 双包验收反馈：客户端没有可用的界面（打印机连接配置等被移除），服务端却有完整 UI。目标：**服务端无头化 + 客户端恢复完整界面**，同时保留服务端集中部署（模板中心 / 作业中心 / 设备投递），把服务端正式做成 Windows 服务，双 MSI 增加安装完成弹窗，并新增历史数据定期清理。

## 2. 已确认决策（用户拍板，2026-08-11）

1. UI 归属反转：服务端默认不提供界面；客户端（WinHost 127.0.0.1:53960）托管完整 Web UI。
2. 模板 / 作业仍以服务端为中心；客户端 UI 是其前台。
3. 作业走服务端队列（本机打印测试也走队列）；打印机测试页走本机 `/api/printer/test`。
4. Server 数据目录改 `%ProgramData%\LabelFrame\server`（Windows 服务账户下 LOCALAPPDATA 不可靠）。
5. 客户端「服务端地址」机器级配置（WinHost `GET/POST /api/host/config` → `%ProgramData%\LabelFrame\Client\settings.json`）。
6. 历史数据定期清理（作业默认保留 30 天、日志默认保留 90 天，可配置；非终态作业不删）。
7. Server 以 Windows 服务部署；安装完成弹窗（开机自启 / 立即运行，默认勾选）；Client 安装完成弹窗（立即打开，默认勾选）。
8. 版本 0.15.0，双 MSI 同版本。

## 3. 后端任务清单（本仓库维护者实施）

### B1 Server 无头化
- `Program.cs`：删除 Web UI 静态托管（`ResolveWebUiPath` / `UseStaticFiles` / SPA fallback）与测试页（`/`、`/devices`、`/jobs` 的 HTML）；保留 `/healthz` 与全部 API。
- `ServerOptions`：移除 `WebUiPath`（或保留但不再使用，代码注释说明）。
- `scripts/build-server-msi.ps1`：不再复制 web/dist 到发布目录。
- 验收：Server 发布目录无 web 目录；`GET /` 返回 404（仅 API 与 /healthz 可用）；`/healthz` 正常。

### B2 Server 图标
- `LabelFrame.Server.csproj` 增加 `<ApplicationIcon>..\..\assets\labelframe.ico</ApplicationIcon>`。
- 验收：发布 exe 图标为 labelframe LOGO。

### B3 Server Windows 服务
- 引入 `Microsoft.Extensions.Hosting.WindowsServices`；`builder.Host.UseWindowsService()`；服务名常量 `LabelFrameServer`（DisplayName「LabelFrame 服务端」）。
- 控制台模式保留（直接运行 exe / dotnet run 仍是控制台，便于开发）。
- 验收：`sc query LabelFrameServer` 安装后可查；直接运行 exe 正常控制台启动。

### B4 数据目录 ProgramData
- `ServerOptions`：`DatabasePath / TemplatesDbPath / LogsDbPath` 默认改 `%ProgramData%\LabelFrame\server\...`；`LABELFRAME_SERVER_*` 覆盖保留。
- `main-server.wxs` 卸载清理自定义动作：路径从 `%LOCALAPPDATA%\LabelFrame\server` 改为 `%ProgramData%\LabelFrame\server`（+ server.db + appsettings 保留）。
- 验收：服务启动后数据落在 `%ProgramData%\LabelFrame\server`。

### B5 历史数据清理
- `ServerDb`：`DeleteTerminalJobsBeforeAsync(DateTimeOffset cutoff)`（`status IN (Completed, Failed) AND COALESCE(finished_at, created_at) < cutoff`）。
- `Core.Logs.SqliteLogStore`：`DeleteBeforeAsync(DateTimeOffset cutoff)`。
- 新增 `DataCleanupService : BackgroundService`：启动延迟 60 秒后执行，之后按 `CleanupIntervalHours`（默认 24h）周期执行；作业保留 `JobRetentionDays`（默认 30 天）、日志保留 `LogRetentionDays`（默认 90 天）。
- `ServerOptions`：`JobRetentionDays / LogRetentionDays / CleanupIntervalHours` + `LABELFRAME_SERVER_*` 覆盖。
- 测试：只删终态 + 超期；非终态不删；边界（保留期内不删）；配置解析。
- 验收：缩短保留期（如设为 0 即清理全部终态）后重启服务，历史终态作业消失、Pending 保留。

### B6 Client 机器级配置
- `HostOptions`：新增 `ConfigPath`（默认 `%ProgramData%\LabelFrame\Client\settings.json`）+ `LABELFRAME_CONFIG` 覆盖。
- `Program.cs`：启动时读取 settings.json 的 `serverUrl` → 覆盖 / 回填 `ServerUrl`（建议 settings.json 优先，因 UI 可写，实现时注释说明）；新增 `GET /api/host/config` 返回 `{ serverUrl }`、`POST /api/host/config` 更新并持久化（创建目录、临时文件后原子替换）。
- `main.wxs` 卸载清理：Client 清理新增 `%ProgramData%\LabelFrame\Client\settings.json`。
- 测试：config 读写、文件持久化、启动加载、坏文件兜底（用默认值）。
- 验收：设置页改服务端地址 → 重启客户端 → 地址保持；另一浏览器打开同一客户端也是新地址。

### B7 Server MSI（服务 + 完成弹窗）
- `ServiceInstall`：Name=`LabelFrameServer`、DisplayName、Type=ownProcess、Start=manual、Account=LocalSystem、ErrorControl=normal；`ServiceControl` Stop=both、Remove=uninstall（升级不删）。
- 安装完成弹窗：确认按钮 + CheckBox「开机自启」（Property=`AUTOSTART`，默认 1）+ CheckBox「立即运行」（Property=`RUNNOW`，默认 1）；条件 `NOT UPGRADINGPRODUCTCODE AND NOT REMOVE`。
- 自定义动作（InstallExecuteSequence，InstallFinalize 后）：`AUTOSTART=1` → `sc config LabelFrameServer start= auto`（否则保持 manual）；`RUNNOW=1` → `net start LabelFrameServer`（已启动则忽略，Return=ignore）。
- AddRemovePrograms 图标（可选，有 labelframe.ico 则加）。
- 验收：全新安装默认两个勾选 → 服务 automatic 且已启动；取消自启 → manual；取消立即运行 → 不启动；升级不弹窗不启动。

### B8 Client MSI（完成弹窗 + 启动）
- 安装完成弹窗：确认 + CheckBox「立即打开」（Property=`OPEN_NOW`，默认 1）；条件 `NOT UPGRADINGPRODUCTCODE AND NOT REMOVE`。
- 自定义动作：确认后启动 `[INSTALLFOLDER]LabelFrame.WinHost.exe`（Impersonate=yes，InstallFinalize 后；Return=ignore）；应用 `OpenBrowser=true` 自动打开界面。
- 验收：全新安装默认勾选 → 客户端托盘启动 + 浏览器打开 `127.0.0.1:53960`；取消勾选 → 不启动；升级不弹窗不启动。

### B9 打包与验证
- `build-server-msi.ps1` / `build-msi.ps1` 版本 0.15.0；产物 `LabelFrame-Server-0.15.0.msi` / `LabelFrame-Client-0.15.0.msi`。
- `dotnet test` 全绿（预期新增 Server 清理 / WinHost config 用例）；Client 包仍含 web/dist（build-msi.ps1 复制）。
- MSI 数据库只读验证：服务安装表、完成弹窗控件、自定义动作与序列、清理路径。
- 冒烟：控制台启动 Server + 本机 Client 联调（Log 模拟）闭环；`/api/host/config` 读写。

## 4. 前端任务清单（hermes 实施，评估后可反馈）

> 参考：0.14 移除连接 UI 的提交 `e161d81`；迭代 15 连接管理 UI 原实现 `4155ccf`（恢复时参考其结构与测试）。
> 页面归属变化：前端不再由 Server 托管，由 Client（WinHost 127.0.0.1:53960）托管；API 分两类（Server / 本机）。

### F1 API 客户端双 base
- `types.ts`：
  - `DEFAULT_BASE_URL` 语义改为「服务端地址默认 `http://127.0.0.1:53961`」。
  - 恢复 transport 类型：`TransportMode / TransportConfig / TransportParams / TransportApplyRequest / TransportApplyResponse`（参照 4155ccf）。
  - 新增 `HostConfig { serverUrl: string }`；`PrinterStatus` 类型（如需）。
- `client.ts`：拆 `serverApi`（模板 / 作业 / 设备 / 调试出图 / 日志 / Excel → 服务端地址）与 `localApi`（transport / printer / host/config → 页面来源 127.0.0.1:53960）；healthz 探测仍走 serverApi（连接状态灯反映服务端连通）；错误消息区分「服务端」与「本机客户端」。

### F2 机器级配置（后端地址）
- App 启动：`localApi.getHostConfig()` → `serverUrl` 作为 serverBase；失败（旧客户端 / 无 API）回退现有 localStorage 逻辑（`getBaseUrl`），设置页提示「本机配置接口不可用，使用浏览器本地保存」。
- 设置页「后端地址」保存：调 `localApi.setHostConfig({ serverUrl })`（成功后写 localStorage 兜底）；「测试连接」探测 serverBase `/healthz`。
- 连接状态灯：反映服务端连通（serverBase healthz）。

### F3 恢复「连接方式」分组（设置页）
- 恢复 4155ccf 的实现：模式单选 Log / TCP / Windows 驱动 / Zebra；只显示当前模式参数；「测试连接」（testOnly，不生效）；「保存并应用」（先测试后生效、失败回滚）；当前模式展示。
- 全部调 localApi（`/api/transport`）。

### F4 恢复「打印机」分组（设置页）
- 状态（localApi `/api/printer/status`）与「测试打印」（localApi `/api/printer/test`，直发驱动）。
- Log / 无打印机时按后端返回展示（Log 模拟无状态）。

### F5 数据与打印
- 保持目标设备选择（serverApi `/api/devices`），默认选中本机在线 Client；提交 `templateName + targetDeviceId` 到 serverApi。
- 单机降级保留：serverBase 不可达且与页面来源一致（或 `/api/devices` 404）→ 隐藏设备选择、自包含模板提交到 localBase。
- 顶部连接徽标可选恢复（展示本机连接方式，数据来自 localApi）。

### F6 新增「作业历史」页
- 导航新增「作业历史」；serverApi `GET /api/jobs?limit=100` 列表：时间 / requestId / jobId / 目标设备 / 状态 / 完成-失败张数 / 失败原因；刷新按钮；终态 / 进行中徽标区分。
- 单机降级：指向本机时显示本机作业列表（localBase `GET /api/jobs`）。
- 空态：暂无历史作业；提示「终态作业默认保留 30 天后自动清理」（文案与服务端配置一致）。

### F7 测试与构建
- `Settings.test.tsx` / `DataPrint.test.tsx` 更新：双 base、连接方式切换恢复、目标设备、机器级配置；新增 hostConfig / transport 用例（参照 4155ccf 测试）。
- 新增作业历史页用例（列表渲染 / 空态 / 降级）。
- `pnpm test` / `pnpm build` / `pnpm lint` 全绿。

## 5. 跨端契约增量（相对 0.14）

| 接口 | 归属 | 说明 |
|---|---|---|
| `GET/POST /api/host/config` | Client 本机 | `{ serverUrl }` 读写（机器级持久化） |
| `GET/POST /api/transport`、`GET /api/printer/status`、`POST /api/printer/test` | Client 本机 | 恢复前端接入（接口 0.14 已在，未删） |
| `GET /api/jobs` | Server | 可选 `?limit=100`（默认 100），作业历史用 |
| Server Web UI | —— | 移除 |

## 6. 验收标准（端到端）

1. Server MSI 全新安装：完成弹窗默认勾选自启 + 立即运行 → 服务 `LabelFrameServer` 为 Automatic 且 Running；无 Web UI（浏览器打开 Server 端口只有 API / 404）；exe 图标为 LOGO；数据在 `%ProgramData%\LabelFrame\server`。
2. Client MSI 全新安装：完成弹窗默认勾选立即打开 → 客户端启动并打开 `127.0.0.1:53960`；设置页可切换连接方式（保存并应用）、可改服务端地址（机器级，重启保持）；模板设计 / 保存 → 数据与打印选本机设备 → 打印测试 → 作业 Completed；作业历史页可见并可刷新。
3. 历史清理：缩短保留期（如 0）后重启 Server，终态超期作业与超期日志被删，Pending / Claimed 保留。
4. 升级 0.14 → 0.15：不弹完成弹窗、不自动启动服务 / 程序；用户数据保留（0.14 的 `%LOCALAPPDATA%` 数据不迁移，当前无业务使用）。
5. `dotnet test` / `pnpm test` 全绿；双 MSI 可独立安装、同机可联调。

## 7. 不在范围

- PDA 联调、AndroidHost（延后）。
- Ubuntu / Linux 部署服务端（后续迭代）。
- 作业 / 日志归档导出、清理前备份。
- 多打印机并行、作业筛选高级分页（仅 `limit`）。
- 旧 0.14 `%LOCALAPPDATA%\LabelFrame\server` 数据迁移脚本。

## 8. 开工协作流程

1. 本文档 + ARCHITECTURE-SPLIT（0.15 修订）+ DESIGN（决策 #53-58）+ ROADMAP 已提交（本次）。
2. 用户将前端任务清单（§4）交 hermes 评估；hermes 反馈问题 → 本文档修订定稿。
3. 两端无意见后：后端按 §3 开工；前端按 §4 开工（hermes 自行提交）。
