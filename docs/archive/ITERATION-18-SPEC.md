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
- `README.md`：更新部署说明（Server 无 Web UI；访问入口为 Client 127.0.0.1:53960）。
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
- `Program.cs`：启动时读取 settings.json 的 `serverUrl` → 覆盖 / 回填 `ServerUrl`（建议 settings.json 优先，因 UI 可写，实现时注释说明）；新增 `GET /api/host/config` 返回 `{ serverUrl, deviceId, deviceName }`（settings.json 缺失 / 损坏时返回 200 + 默认 serverUrl）、`POST /api/host/config` 更新并持久化（创建目录、临时文件后原子替换）。
- `main.wxs` 卸载清理：Client 清理新增 `%ProgramData%\LabelFrame\Client\settings.json`。
- 测试：config 读写、文件持久化、启动加载、坏文件兜底（缺失 / 损坏返回默认值）。
- 验收：设置页改服务端地址 → 保存后立即生效（无需重启），重启客户端地址保持；另一浏览器打开同一客户端也是新地址；settings.json 缺失 / 损坏时 GET 返回 200 + 默认 serverUrl。

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
### B10 作业列表端点补全（F6 前置）
- Server `GET /api/jobs`：新增可选 `limit` 参数（默认 100，上限 500），按创建时间倒序截断。
- WinHost 新增 `GET /api/jobs`：可选 `limit`（默认 100，上限 500），返回本机作业列表——扩展 JobView 形状与 Server 对齐：新增 `CreatedAt`（LabelJob.CreatedAt）、`FailedItems` / `ErrorMessage`（从 Items 派生，ErrorMessage 取首个失败项消息）、`TargetDeviceId` 返回 null（前端显示「本机」）；前端作业历史列单一映射。
- 测试：Server limit 生效（默认 / 超上限截断）；WinHost 列表返回本地作业。

## 4. 前端任务清单（hermes 实施，评估后可反馈）

> 参考：0.14 移除连接 UI 的提交 `e161d81`；迭代 15 连接管理 UI 原实现 `4155ccf`（恢复时参考其结构与测试）。
> 页面归属变化：前端不再由 Server 托管，由 Client（WinHost 127.0.0.1:53960）托管；API 分两类（Server / 本机）。

### F1 API 客户端双 base
- `types.ts`：
  - `DEFAULT_BASE_URL` 语义改为「服务端地址默认 `http://127.0.0.1:53961`」。
  - 恢复 transport 类型：`TransportMode / TransportConfig / TransportParams / TransportApplyRequest / TransportApplyResponse`（参照 4155ccf）。
  - 新增 `HostConfig { serverUrl: string; deviceId: string; deviceName: string }`；`PrinterStatus` 类型（如需）。
- `client.ts`：拆 `serverApi`（模板 / 作业 / 设备 / 调试出图 / 日志 / Excel → 服务端地址）与 `localApi`（transport / printer / host/config → 页面来源 127.0.0.1:53960）；healthz 探测仍走 serverApi（连接状态灯反映服务端连通）；错误消息区分「服务端」与「本机客户端」。

### F2 机器级配置（后端地址）
- serverBase 优先级：机器级配置（`localApi.getHostConfig().serverUrl`）> localStorage 兜底 > 默认 `http://127.0.0.1:53961`；App 启动先取机器级配置，失败（旧客户端 / 无 API）回退 localStorage；`settings.ts` 移除方案 B 残留检测（比较基准已失效），默认地址改 53961。
- 设置页「后端地址」保存：调 `localApi.setHostConfig({ serverUrl })`，**立即生效**——更新内存 serverBase → 重新探测 → 重拉当前页数据（实现可用整页刷新，sessionStorage 草稿不丢）；「测试连接」探测 serverBase `/healthz`；localStorage 仅作无本地 API 时兜底。
- 连接状态灯：反映服务端连通；单机模式（serverBase 不可达）显示「服务端未连接（单机模式可用）」，与「本机客户端不可用」区分。

### F3 恢复「连接方式」分组（设置页）
- 恢复 4155ccf 的实现：模式单选 Log / TCP / Windows 驱动 / Zebra；只显示当前模式参数；「测试连接」（testOnly，不生效）；「保存并应用」（先测试后生效、失败回滚）；当前模式展示。
- 全部调 localApi（`/api/transport`）。

### F4 恢复「打印机」分组（设置页）
- 状态（localApi `/api/printer/status`）与「测试打印」（localApi `/api/printer/test`，直发驱动）。
- Log / 无打印机时按后端返回展示（Log 模拟无状态）。

### F5 数据与打印
- 保持目标设备选择（serverApi `/api/devices`），默认选中**本机设备**：用 `localApi.getHostConfig().deviceId` 匹配列表，命中则选中，否则回退第一台在线；提交 `templateName + targetDeviceId` 到 serverApi。
- 单机降级保留：serverBase 不可达且与页面来源一致（或 `/api/devices` 404）→ 隐藏设备选择、自包含模板提交到 localBase。
- 顶部连接徽标可选恢复（展示本机连接方式，数据来自 localApi）；与状态灯并存时用图例 / 文案标明「本机连接」与「服务端连通」各自含义。

### F6 新增「作业历史」页
- 导航新增「作业历史」；serverApi `GET /api/jobs?limit=100` 列表：时间 / requestId / jobId / 目标设备 / 状态 / 完成-失败张数 / 失败原因；刷新按钮；终态 / 进行中徽标区分。
- 单机降级：指向本机时显示本机作业列表（localBase `GET /api/jobs`，后端 B10 新增）。
- 空态：暂无历史作业；空态提示按模式区分——服务端模式：「终态作业默认保留 30 天，由服务端自动清理」；单机模式：「本机作业不自动清理」（按默认值写死，不承诺与运行时配置实时一致）。

### F7 测试与构建
- `Settings.test.tsx` / `DataPrint.test.tsx` / `settings.test.ts` 更新：双 base、连接方式切换恢复、目标设备、机器级配置、默认地址 53961、移除方案 B 残留用例；新增 hostConfig / transport 用例（参照 4155ccf 测试）。
- 新增作业历史页用例（列表渲染 / 空态 / 降级）。
- `pnpm test` / `pnpm build` / `pnpm lint` 全绿。

## 5. 跨端契约增量（相对 0.14）

| 接口 | 归属 | 说明 |
|---|---|---|
| `GET/POST /api/host/config` | Client 本机 | `{ serverUrl, deviceId, deviceName }` 读写（机器级持久化；缺失 / 损坏返回默认 serverUrl） |
| `GET/POST /api/transport`、`GET /api/printer/status`、`POST /api/printer/test` | Client 本机 | 恢复前端接入（接口 0.14 已在，未删） |
| `GET /api/jobs` | Client 本机 | 新增（B10）：本机作业列表（可选 limit，默认 100 上限 500；扩展 JobView：CreatedAt / FailedItems / ErrorMessage，TargetDeviceId=null），作业历史单机降级用 |
| `GET /api/devices/{deviceId}/jobs/notify?timeout=N` | Server | 长轮询通知：作业到达立即返回 hasPending=true（等效推送）；同时刷新心跳保活 |
| `GET /api/jobs` | Server | 可选 `?limit`（默认 100，上限 500），作业历史用 |
| Server Web UI | —— | 移除 |

## 6. 验收标准（端到端）

1. Server MSI 全新安装：完成弹窗默认勾选自启 + 立即运行 → 服务 `LabelFrameServer` 为 Automatic 且 Running；无 Web UI（浏览器打开 Server 端口只有 API / 404）；exe 图标为 LOGO；数据在 `%ProgramData%\LabelFrame\server`。
2. Client MSI 全新安装：完成弹窗默认勾选立即打开 → 客户端启动并打开 `127.0.0.1:53960`；设置页可切换连接方式（保存并应用）、可改服务端地址（机器级，保存后立即生效、重启保持）；模板设计 / 保存 → 数据与打印选本机设备 → 打印测试 → 作业 Completed；作业历史页可见并可刷新；单机降级时作业历史显示本机作业列表。
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
2. hermes 审阅意见已并入正文（处置见文末「附：审核者答复」），本文档为定稿。
3. 两端无意见后：后端按 §3 开工；前端按 §4 开工（hermes 自行提交）。

## 附：审阅意见（hermes 追加，2026-08-11）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。

已对照真实代码核对：后端路由与 DTO（Server / WinHost Program.cs、Contracts.cs）、前端工作区当前实现（client.ts / settings.ts / types.ts / DataPrint / Settings / AppContext）、F1 参照物 4155ccf、ARCHITECTURE-SPLIT 0.15 修订。总体结论：任务单方向可实施，前端无阻塞性异议；发现 1 个关键缺口（后端任务单缺条目）与若干规格空白，确认后即可定稿开工。

### 🔴 关键缺口

1. **F6 单机降级路径的端点在后端不存在**。F6 写「单机降级：指向本机时显示本机作业列表（localBase `GET /api/jobs`）」，但 WinHost 当前只有 `POST /api/jobs` 与 `GET /api/jobs/{jobId}`（WinHost/Program.cs:300、319），**没有 `GET /api/jobs` 列表端点**；后端任务清单 §3（B1-B9）也没有新增该端点的条目。前端按 F6 实施后，单机降级时作业历史页必然 404。需拍板：a) B 清单补「WinHost 新增 `GET /api/jobs`（可选 limit，返回与 Server 兼容的列表形状）」；或 b) 删 F6 降级路径，明确降级时作业历史页显示「服务端模式下可用」。（Server 侧 `GET /api/jobs` 存在，Server/Program.cs:94，响应 `ServerJobView[]` 字段齐全，服务端路径无问题。）

### 🟡 规格空白与不一致

2. **`GET /api/jobs` 的 `limit` 参数无后端实施条目**。契约增量（§5 与 ARCHITECTURE-SPLIT §3）声明「可选 ?limit=100（默认 100）」，但 Server 当前实现无 limit 绑定（Server/Program.cs:94 直接 `ListJobsAsync(ct)`，全量返回），后端任务清单 B1-B9 无对应条目，后端很可能漏做；漏做则前端传参被忽略、作业多时全量响应。建议 B 清单补一条。

3. **F5「默认选中本机在线 Client」缺识别机制**。DataPrint 当前实现是「默认选中第一台在线设备」（DataPrint.tsx:234-236）；WinHost 的 DeviceId 默认 = `Environment.MachineName`（HostOptions.cs:60），浏览器无法获取本机机器名，多台 Client 在线时前端无从判定「本机」。建议：a) 保持「第一台在线」（等于现状，F5 表述改为「默认选中在线设备」）；或 b) 本机 DeviceId 随 `/api/host/config` 或 healthz 返回，前端比对 devices 列表。

4. **F2 保存后 serverBase 切换语义未定义**。B6 验收「设置页改服务端地址 → 重启客户端 → 地址保持」暗示重启才生效；但 F2 描述「保存 → setHostConfig → 写 localStorage 兜底 → 测试连接」——若保存后内存 serverBase 不切换，「测试连接」与后续请求仍走旧地址，体验分裂；若立即切换，需定义「已加载数据（模板列表等）是否重拉」。需明确：保存后立即更新内存 serverBase 并重载 / 或明确提示「重启客户端后生效」。

5. **机器级配置回退链路的默认值语义需明确**。全新安装的 Client 没有 settings.json：若 `GET /api/host/config` 返回 404（或网络错），前端回退 getBaseUrl → 页面来源（127.0.0.1:53960）→ serverApi 指向本机 WinHost，模板 / 作业走单机队列，与决策 2「模板 / 作业仍以服务端为中心」相悖；若返回 200 + 默认 serverUrl（127.0.0.1:53961），则全新安装默认连 Server（与决策一致）。B6 测试有「坏文件兜底（用默认值）」暗示缺失也返回默认值，但规格未明示——建议明确「settings.json 不存在 / 损坏 → 200 返回默认 serverUrl」，并在 B6 验收补一句。另：`DEFAULT_BASE_URL` 53960→53961 后，settings.ts 方案 B 残留检测（`cleaned === DEFAULT_BASE_URL` 视为旧默认残留并忽略，settings.ts:22-24）的比较基准失效——localStorage 旧值 53960 不再等于新默认，非本机来源下将返回 53960（0.15 中这是合法的本机 Client 地址，作为 serverApi 回退值会把请求打到本机）。F2 实施时需重审该逻辑（或删除——0.15 起 serverApi 以机器级配置为主、localStorage 仅兜底）。

6. **F7 测试清单漏 `settings.test.ts`**。DEFAULT_BASE_URL 变更 + 双 base 拆分直接影响 `web/src/lib/settings.test.ts`（方案 B 用例按 DEFAULT_BASE_URL 断言）与 settings.ts 本身；F7 只列了 Settings.test.tsx / DataPrint.test.tsx。建议补「settings.test.ts 同步更新」。

7. **README 活文档残留**。README.md:55、59 仍描述「Server：…Web UI…打开浏览器 http://127.0.0.1:53961 编辑模板与提交作业」——0.15 无头化后该路径不存在；B1 只删代码未列文档更新，「全链路无残留」类检查会卡。建议 B 清单补「README 更新（Server 无 UI、访问入口改 Client 127.0.0.1:53960）」。

8. **F6 空态文案与可配置保留期脱节（轻）**。「终态作业默认保留 30 天后自动清理」写死 30，用户改 `JobRetentionDays` 后文案失真；且规格 F6 要求「文案与服务端配置一致」但没有取配置的途径，前后自相矛盾。可接受（按默认值写死、措辞改「默认保留 30 天」），或服务端配置经某端点暴露给前端——不阻塞定稿。

### 🟢 待决策（需审核者 / 用户拍板）

- 第 1 条 a / b 方案；第 3 条 a / b 方案；第 4 条「立即生效重载」vs「重启生效」。

### 💡 可选建议

- F1 状态灯语义：单机模式（serverBase 不可达）下连接状态灯恒红，建议文案区分「服务端未连接（单机模式可用）」与「本机客户端不可用」，避免误导（F2 已要求错误消息区分，状态灯同理）。
- F5 顶部连接徽标（可选恢复）与状态灯并存时有两个状态源（本机 transport vs 服务端连通），建议图例 / 文案标明各自含义。
- 作业历史页刷新采用手动按钮（F6 已定）即可，不必轮询——与既有作业视图一致。

### ✅ 已核对通过项（附依据，无需修改）

- 契约增量 §5 接口真实存在或已规划新增：WinHost `/api/transport`（Program.cs:269、272）、`/api/printer/status`（:396）、`/api/printer/test`（:399）0.14 保留未删；`/api/host/config` 当前不存在，属 B6 新增，核对无误。
- WinHost 静态托管保留（Program.cs:445-465，webUiPath 非空时启用）——决策 1「客户端托管完整 Web UI」后端无需恢复托管，0.15 Client MSI 继续带 web/dist（B9）即可闭环。
- F1 参照物 4155ccf 存在（前端工作区 git log --all），TransportMode / TransportConfig / TransportApplyRequest / TransportResult / PrinterStatus 类型与 TransportPanel.tsx（305 行）可完整参照恢复。
- 前端 JobView 类型已含 Server 兼容形状（targetDeviceId / deviceStatus / failedItems / errorMessage 可选），作业历史页复用无契约障碍。
- DataPrint 降级逻辑（listDevices 404 / 失败 → standalone 自包含提交）与 F5「单机降级保留」一致。
- AppContext healthz 探测存在（AppContext.tsx:82），F1「healthz 走 serverApi」改造点明确。
- 错误响应 ErrorView（{code, message}）与前端 ApiError 解析已对齐。
- ServerJobView 字段（JobId / RequestId / TargetDeviceId / Status / CreatedAt / TotalItems / CompletedItems / FailedItems / ErrorMessage / DeviceStatus）与 F6 列表列完全匹配（见第 1 条）。
- B1/B3/B4/B5/B7/B8 为纯后端 / MSI 改动，与前端契约无交叉；升级不触发弹窗（NOT UPGRADINGPRODUCTCODE AND NOT REMOVE）与验收 4 一致。

### 待审核者确认清单

1. 第 1 条：WinHost 补 `GET /api/jobs`（a）还是删 F6 降级路径（b）？
2. 第 3 条：保持「第一台在线」（a）还是暴露本机 deviceId（b）？
3. 第 4 条：保存后立即生效重载（a）还是提示重启生效（b）？
4. 第 5 条：确认「settings.json 缺失 / 损坏 → GET /api/host/config 返回 200 + 默认 serverUrl」；F2 实施时重审方案 B 残留检测。
5. 第 6、7 条：是否补入任务清单（settings.test.ts、README 更新）？

## 附：审核者答复（2026-08-11，规格定稿）

对 hermes 审阅意见的逐条处置（已并入正文 §3 / §4 / §5）：

1. 🔴 1（WinHost 缺 `GET /api/jobs`）：采用方案 a——新增 B10：WinHost `GET /api/jobs`（可选 limit，形状与 Server 兼容），F6 单机降级保留。
2. 🟡 2（Server limit 漏实现）：采纳——B10：Server `GET /api/jobs` 支持 `?limit`（默认 100，上限 500）。
3. 🟡 3（本机设备识别）：采用 b + a 兜底——`/api/host/config` 返回 `deviceId / deviceName`，F5 优先选中本机设备，未命中回退第一台在线。
4. 🟡 4（保存生效语义）：采用 a——保存后立即生效：更新内存 serverBase → 重新探测 → 重拉当前页数据（可整页刷新，session 草稿不丢）。
5. 🟡 5（回退默认值语义）：确认——settings.json 缺失 / 损坏 → `GET /api/host/config` 返回 200 + 默认 serverUrl（127.0.0.1:53961）；`settings.ts` 移除方案 B 残留检测。
6. 🟡 6（测试清单）：采纳——F7 补 `settings.test.ts`。
7. 🟡 7（README 残留）：采纳——B1 补 README 更新。
8. 🟡 8（空态文案）：轻处理——F6 空态按默认值写死「终态作业默认保留 30 天，由服务端自动清理」。
9. 💡 建议：全部采纳——状态灯区分「服务端未连接（单机模式可用）」/「本机客户端不可用」；连接徽标与状态灯加图例；作业历史手动刷新不轮询。
10. ✅ 核对通过项：无需修改。

## 附二：第二轮审阅意见（hermes 追加，2026-08-11）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。

已拉取定稿提交 7cbd300 并完整重读修订后全文（正文 + 首轮附录 + 审核者答复）。结论：首轮 5 项确认清单全部落实、可接受；修订引入 1 处实施期会咬实现的契约含糊（B10 形状声明）与 4 处小不一致，如下。

### 首轮意见落实核对（答复 1-10 → 正文）

- 答复 1（B10 WinHost `GET /api/jobs`）→ §3 B10 ✓；答复 2（Server limit）→ §3 B10 ✓。
- 答复 3（本机设备识别 b+a 兜底）→ §4 F5 + §3 B6（host/config 返回 deviceId/deviceName）✓；答复 4（立即生效）→ §4 F2 + §3 B6 验收 ✓。
- 答复 5（缺失 / 损坏返回 200+默认 + 移除方案 B）→ §3 B6 + §4 F2 ✓；答复 6（settings.test.ts）→ §4 F7 ✓；答复 7（README）→ §3 B1 ✓。
- 答复 8（空态文案写死）→ §4 F6 ✓（但见第 10 条新问题）；答复 9（💡 全部采纳）→ §4 F2 状态灯文案、F5 徽标图例 ✓；答复 10（核对项）→ 无修改 ✓。
- 首轮附录与「附：审核者答复」均保留完整 ✓。

### 修订引入的新问题

7. 🟡 **B10「形状与 Server 兼容：JobView[]」不成立，实施期会咬实现**。WinHost 现有 JobView（WinHost/Api/Contracts.cs:27-34）只有 JobId / RequestId / Status / TotalItems / CompletedItems / Items / PrintImageDir / PrintImageCount，**没有 CreatedAt / TargetDeviceId / FailedItems / ErrorMessage**；而 Server 的 ServerJobView（Server/Contracts.cs:31-41）含后四者。F6 列表列「时间 / 目标设备 / 完成-失败张数 / 失败原因」在单机降级（localBase `GET /api/jobs`）时全部无数据源（时间列连字段都没有）。建议拍板：a) B10 明确 WinHost `GET /api/jobs` 返回扩展形状——补 CreatedAt（LabelJob 已有该字段，LabelJob.cs:16，DTO 映射加一行即可），FailedItems / ErrorMessage 从 Items 派生，TargetDeviceId 返回 null / 本机标识（前端显示「本机」或「—」）；或 b) 前端降级模式对应列显示「—」，并在 F6 写明哪些列降级为空。推荐 a（后端派生成本低，前端列表组件保持单一映射）。

8. 🟡 **F1 HostConfig 类型漏 deviceId / deviceName**。B6（返回三字段）、§5 表、F5（`localApi.getHostConfig().deviceId` 匹配依据）均使用三字段，F1 类型仍只写 `HostConfig { serverUrl: string }`（§4 F1 第 2 行）——类型定义与用法不一致，建议补全。

9. 🟡 **§6 验收 2 残留「重启保持」表述**。B6 验收已改「保存后立即生效（无需重启），重启客户端地址保持」，§6 验收 2 仍写「可改服务端地址（机器级，重启保持）」——建议统一为「保存后立即生效、重启保持」。

10. 🟡 **F6 空态文案在单机降级模式不成立**。「终态作业默认保留 30 天，由服务端自动清理」仅对 Server 队列成立；单机降级显示的是本机 WinHost 作业，本机队列无自动清理（B5 清理只挂在 Server 的 DataCleanupService）。答复 8「写死」可接受，但建议按模式区分：服务端模式显示保留期提示，单机模式不显示（或「本机作业不自动清理」）。

11. 🟡 **§5 表 limit 描述未同步「上限 500」（轻）**。B10 与 ARCHITECTURE-SPLIT §3 均写「默认 100，上限 500」，§5 表仍只写「默认 100」——前端固定传 100 无实际影响，建议统一。

### ✅ 修订质量检查

- 编号无重排：B1-B10 顺序正确；F1-F7 无重复标题。
- 处置与正文一一对应：答复 1-10 均在 §3 / §4 找到落地（除上述 8-11 残留）。
- 首轮附录（153 行起）与「附：审核者答复」保留完整，未删除。
- 验收 2 已补「单机降级时作业历史显示本机作业列表」，与 B10 呼应 ✓。
- host/config 三字段在 B6 / §5 / ARCHITECTURE-SPLIT §3 / DESIGN #57 四处一致（除 F1 类型，见第 8 条）。

### 结论

无阻塞性异议，可定稿；第 7 条需审核者拍板（a / b），第 8-11 条为正文小修订，建议随第 7 条一次修订到位。确认后前端按最终版开工。

### 待审核者确认清单

1. 第 7 条：WinHost `GET /api/jobs` 返回扩展形状（a，推荐）还是前端降级列留空（b）？
2. 第 8 条：F1 补 `HostConfig.deviceId / deviceName`？
3. 第 9-11 条：正文统一（§6 验收 2 措辞、F6 空态文案按模式区分、§5 表补上限 500）？

## 附三：审核者答复（第二轮，2026-08-11，规格定稿）

对 hermes 第二轮审阅意见的逐条处置（已并入正文 §3 / §4 / §5 / §6）：

1. 🟡 7（B10 形状不成立）：采用方案 a——WinHost `GET /api/jobs` 返回扩展 JobView：新增 `CreatedAt`（LabelJob.CreatedAt）、`FailedItems` / `ErrorMessage`（从 Items 派生）、`TargetDeviceId` 返回 null（前端显示「本机」）；前端作业历史列保持单一映射。
2. 🟡 8（F1 HostConfig 类型漏字段）：采纳——`HostConfig { serverUrl, deviceId, deviceName }`。
3. 🟡 9（§6 验收 2 措辞）：采纳——统一为「保存后立即生效、重启保持」。
4. 🟡 10（F6 空态文案单机不成立）：采纳——按模式区分：服务端模式「终态作业默认保留 30 天，由服务端自动清理」，单机模式「本机作业不自动清理」。
5. 🟡 11（§5 表 limit 上限未同步）：采纳——补「默认 100 上限 500」。
6. ✅ 修订质量检查：无需修改。

第二轮无阻塞性异议，本文档为最终定稿。

## 附四：定稿确认（hermes 追加，2026-08-11）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。

已拉取最终定稿提交 52e3c7c 并完整重读全文（正文 + 附 / 审核者答复 / 附二 / 附三）。附三 6 条处置全部落地，无新异议，**确认可定稿开工**；前端将以本文档最终版（含附三处置后的 §3 / §4 / §5 / §6）为实现基准。

### 附三处置落实核对

1. 答复 1（B10 扩展 JobView）→ §3 B10：CreatedAt / FailedItems / ErrorMessage（取首个失败项）/ TargetDeviceId=null + 前端列单一映射 ✓
2. 答复 2（F1 HostConfig 三字段）→ §4 F1 ✓
3. 答复 3（§6 验收 2 措辞）→ §6 验收 2「保存后立即生效、重启保持」✓
4. 答复 4（F6 空态按模式）→ §4 F6 两模式文案 ✓
5. 答复 5（§5 limit 上限）→ 基本落实：Client 行已补「默认 100 上限 500」；**Server 行（§5 表）仍写「可选 ?limit=100（默认 100）」未带「上限 500」**——与 B10 正文、ARCHITECTURE-SPLIT §3 不一致，但 B10 正文为后端实现依据、前端固定传 100，零影响；如需完全一致可顺手补，不阻塞。
6. 答复 6（质量检查）→ 无修改 ✓

### 修订质量检查

- 编号连续无重排：B1-B10、F1-F7、附 → 附二 → 附三 ✓
- 附录链完整保留（首轮 / 审核者答复 / 二轮 / 附三）✓
- 作业历史页「列单一映射」所需字段两端已对齐：WinHost 扩展后与 ServerJobView 均含 CreatedAt / TargetDeviceId / FailedItems / ErrorMessage / Status / CompletedItems / TotalItems ✓（WinHost 多 Items 等字段，前端忽略；Server 多 DeviceStatus，可选展示）
- 契约四处（B10 / §5 / ARCHITECTURE-SPLIT §3 / F6）对 GET /api/jobs 的描述一致（除上述 Server 行上限细节）✓

### 结论

无新异议，可定稿开工。

### 非阻塞细节（实施期前端自行落实，不须回复）

- 作业历史「目标设备」列显示 deviceId（默认即机器名）；如需设备展示名可复用已加载的 devices 列表映射，不新增请求。
- 「时间」列显示本地时间格式（如 MM-dd HH:mm:ss），刷新按钮手动触发。
- 前端 JobView 类型新增 CreatedAt / FailedItems / ErrorMessage 建议声明为可选（防御性，兼容旧后端响应）。


## 附五：后端完成记录（2026-08-11，0.15.0 已打包）

- B1：Server 无头化（web/dist 托管与测试页移除，仅 /healthz + API）；build-server-msi.ps1 不再复制 web/dist 并清理旧发布目录；README 部署说明更新。
- B2：Server csproj 加 ApplicationIcon（assets/labelframe.ico）。
- B3：`builder.Host.UseWindowsService`（服务名 LabelFrameServer）；控制台模式保留。
- B4：ServerOptions 默认数据目录改 `%ProgramData%\LabelFrame\server`；卸载清理路径同步。
- B5：`ServerDb.DeleteTerminalJobsBeforeAsync` + `SqliteLogStore.DeleteBeforeAsync` + `DataCleanupService`（JobRetentionDays=30 / LogRetentionDays=90 / CleanupIntervalHours=24 + LABELFRAME_SERVER_* 覆盖）；测试覆盖清理、非终态保留、配置解析。
- B6：WinHost `GET/POST /api/host/config`（serverUrl + deviceId/deviceName，回环可写，持久化 ProgramData settings.json，缺失 / 损坏返回默认值，启动加载覆盖 ServerUrl）；HostConfigStore + 测试。
- B7：Server MSI 注册服务（Start=demand，LocalSystem）+ 完成弹窗（AUTOSTART/RUNNOW 默认 1）+ `sc config` / `net start` 自定义动作（仅新装）；移除 Server 桌面 / 开始菜单快捷方式（服务部署下会与端口冲突）。
- 修复 0.15.1：ServiceInstall / ServiceControl 移入 `LabelFrame.Server.exe` 所在组件（generate-files.ps1 生成），修复 0.15.0 服务未注册问题。
- 修复 0.15.2：完成弹窗动作改为按钮 `DoAction` 触发（弹窗后 InstallUISequence 动作不执行），修复自启 / 立即运行 / 立即打开未生效；产物升级 0.15.2。
- 简化 0.15.3（用户拍板）：Server 服务安装改为 `ServiceInstall Start=auto` + `ServiceControl Start=install`（注册即自动 + 安装时启动），移除勾选项与 sc/net 自定义动作，完成弹窗仅提示；实装验证 AUTO_START + RUNNING；双包版本 0.15.3。
- B8：Client MSI 完成弹窗（OPEN_NOW 默认 1）+ LaunchClient 自定义动作；卸载清理新增 `%ProgramData%\LabelFrame\Client\settings.json`。
- B10：Server `GET /api/jobs` 支持 limit；WinHost 新增 `GET /api/jobs`（扩展 JobView：CreatedAt / FailedItems / ErrorMessage / TargetDeviceId=null）。
- 验证：`dotnet test` 156 全绿；双 MSI 数据库校验（服务表 / 弹窗 / 动作 / 序列 / 清理路径）；冒烟——Server 无头启动（GET / 404、/api/jobs 空、ProgramData 落盘）、Client 机器级配置读写、作业列表、Web UI 页面可达。


## 附六：0.15.4 联调反馈修复（2026-08-11）

- 推送等效：新增 `GET /api/devices/{deviceId}/jobs/notify?timeout=N`（Server 长轮询，作业到达立即返回；同时刷新心跳）；WinHost `ServerRoutingWorker` 改为「注册 → 长轮询等待 → 立即领取 → 回报」，网络异常回退间隔重试；`IServerJobPoller.WaitForJobAsync`。打印从“提交→最多等 5s 轮询”变为“提交→<1s 唤醒领取”。
- 客户端安装弹窗修复：`LaunchClient` 改用 `cmd.exe /c start "" "[#WinHostExe]"` 非阻塞启动（此前 msiexec 等待 GUI 进程退出导致弹窗无法关闭）。
- 弹窗文字简化：去掉「（默认勾选）」「（默认不勾选）」括号说明。
- 测试 162 全绿（新增 PendingJobNotifier 4 个 + WaitForJobAsync 2 个）；产物 0.15.4。
