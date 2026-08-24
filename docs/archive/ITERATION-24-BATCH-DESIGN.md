# 设计方案：客户端批次作业（Batch Print）

> 状态：**已通过两轮评审（2026-08-18）**。审阅记录见文末「附」（第一轮 hermes）与「附三」（第二轮复核）；
> 正文按评审结论修订（修订记录见「附二」），两处 💡 非阻塞建议已落实（见「附四」）。
> 待并入 `docs/ROADMAP.md` 迭代条目并形成实施规格。
>
> 目标：客户端把「向打印机发送」的动作按数量分批、批间加间隔，用于大批量作业时
> 控制打印节奏 / 减轻打印机压力；同时澄清「服务端作业进度 0%→100%」的现象与批次功能的关系。

## 1. 背景与问题

### 1.1 现状：服务端作业进度只有 0% 或 100%（属实）

- 服务端路由作业的进度链路：业务系统提交 → Server 存 `JobPayload(Template, Labels)` →
  客户端轮询领取 → 本地逐张打印 → **本地作业终态后回报一次**。
- 代码依据：
  - 客户端 `ServerRoutingWorker.ReportFinishedAsync`（`src/LabelFrame.WinHost/Routing/ServerRoutingWorker.cs`）
    仅在本地作业状态为 `Completed / Failed / Cancelled` 时调用一次
    `POST /api/devices/{deviceId}/jobs/{jobId}/result`，一次性上报 `CompletedItems / FailedItems`。
  - 服务端 `ServerService.ReportResultAsync`（`src/LabelFrame.Server/ServerService.cs`）只接受
    `Claimed → Completed/Failed` 的终态转移，`ServerJobView.CompletedItems` 在回报前恒为 0；
    Server 作业视图不含逐张 `items`。
  - 因此轮询 `GET /api/jobs/{id}`（DataPrint 联网模式 / Server UI / 业务系统）看到的进度
    是 **0% → 100%** 跳变。
- 对照：**本机（单机）作业**的 `GET /api/jobs/{id}`（WinHost）返回逐张 `items`，
  DataPrint 单机模式可显示逐张进度，不存在 0/100 问题。

### 1.2 本次要做的功能（与进度问题的关系）

用户提出：客户端新增「批次作业」设置——开启后设置「每批次打印数量」与「批次打印间隔」。
例如：服务端作业 100 张、批次 10、间隔 500ms → 客户端每给打印机发 10 张，停 500ms，再发下一组。

**重要澄清**：批次功能解决的是「发送节奏 / 打印机压力」，它**不会**改变服务端作业
0%→100% 的进度展示（回报仍是终态一次）。若希望大批量期间能看到进度，需要另一个独立特性
「增量进度回报」（见 §8 开放问题 Q2），本轮默认不做。

## 2. 范围

### 2.1 范围内（本轮）

- 客户端（WinHost）「批次作业」设置：是否开启、每批次打印数量、批次打印间隔。
- 设置持久化（用户级）+ WinHost API + 设置页 UI 卡片。
- 打印 Worker 按批次节流发送（本机作业与服务端作业都生效，见 §3.3）。
- WinHost 引入 Serilog 文件日志（解决 ILogger 逐张日志不可见，供批间间隔冒烟验证，评审 #2 结论）。
- 单元测试 / 前端测试 / 端到端冒烟。

### 2.2 不在范围（本轮）

- 服务端任何改动（批次是纯客户端发送层功能，跨端契约不变）。
- 增量进度回报（服务端作业逐批/逐张进度展示）——独立跨端特性，见 §8 Q2。
- AndroidHost（PDA）批次功能——与 WinHost 代码路径不同，延后。
- 把 Server 作业真的拆成多个本地作业——不采用（见 §3.1）。
- 修改作业队列 / 幂等 / 挂起 / 恢复 / 取消 / 重打语义。
- 不重构现有 hostLogWriter（TextWriter）通道（HostInfo / LogPrintTransport / 提交摘要仍写 host.log）；
  Serilog 与 host.log 分开文件，后续是否全部收敛到 Serilog 另行评估。

## 3. 总体方案

### 3.1 关键决策一：不拆作业，只在发送层节流

用户描述「100 张分了 10 个作业」是口语化的「10 组」；**不建议真的拆成多个本地作业**，原因：

| 方案 | 说明 | 问题 |
|---|---|---|
| A（推荐） | 一个 Server 作业 = 一个本地作业，仅在 `JobPrintWorker` 发送循环里按批暂停 | 队列 / 幂等 / 回报 / 挂起恢复 / 失败重打语义零改动；requestId 一对一 |
| B | 提交时把 100 张拆成 10 个本地作业 | 需引入批间顺序依赖、多作业聚合回报、requestId 映射，复杂度高、易错；不采用 |

结论：**一个本地作业、逐张发送，每发满 N 张后下一张发送前暂停间隔**。这样也保留逐张状态与
「失败项单独重打」的既有能力（重打项就是一次普通发送）。

### 3.2 关键决策二：批次计数全局累计（跨作业连续）

节流语义：**每发满 N 张后，下一张发送前暂停间隔**（「发送前暂停」，评审 #1 结论），
计数跨作业连续，不按作业重置。

- 例：作业 A 有 60 张、作业 B 有 60 张，批次 10、间隔 500ms →
  发送 1..10 连续 → 第 11 张发送前停 500ms → …… → A 第 60 张发完后、B 第 1 张发送前
  停一次（60 恰为批界）→ B 61..70 ……。作业 A 末张后、B 首张前暂停；100 张单作业
  最后一张后无下一张、不暂停（见 §3.4）。
- 理由：节流是「打印机节奏」机制，与作业边界无关；全局计数实现最简、行为可预期，
  恰好匹配「每发 10 张停一下」的直觉。
- 备选（开放问题 Q3）：按作业独立计数——作业之间不等待、每个作业内部按批暂停。
  若用户更想要「作业间不等待」，改一行即可。

### 3.3 关键决策三：本机作业与服务端作业统一生效

`JobPrintWorker` 是唯一的发送出口（本机提交与服务端领取都走它），在 Worker 层节流
即两类作业统一生效。理由：批次是「打印机发送节奏」配置，与作业来源无关；实现一处、
行为一致。
备选（开放问题 Q1）：仅对服务端作业生效（本机打印不节流）。

### 3.4 发送节奏时序（示例）

- 配置：开启、批次 10、间隔 500ms；Server 作业 100 张。
- 时序：发送第 1..10 张（连续）→ 领取到第 11 张后、发送前 `await Task.Delay(500ms)` →
  发送 11..20 → …… → 发送 91..100 → 作业完成，**无第 101 张、不额外等待**。
- 实现语义：**发送前暂停（claim-then-delay）**——`ClaimNextItemAsync` 领取到下一张、
  且 `sendsSinceBatch > 0 && sendsSinceBatch % batchSize == 0` 时先延迟再发送
  （评审 #1 结论，统一 §3.2 / §5 / §7 四处语义；空队列 / 无下一张时不等待）。
- 额外耗时：`(ceil(100/10) - 1) × 500ms = 9 × 500ms ≈ 4.5s`。
- 间隔是「上一批最后一张发送完成之后、下一批第一张发送之前」，不是每张之间。

## 4. 设置模型与持久化

### 4.1 设置项

| 字段 | 类型 | 默认 | 范围 | 说明 |
|---|---|---|---|---|
| `batchEnabled` | bool | `false` | — | 是否开启批次作业 |
| `batchSize` | int | `10` | ≥ 1 | 每批次打印数量 |
| `batchIntervalMs` | int | `500` | ≥ 0 | 批次打印间隔（毫秒）；0 = 无间隔 |

- `batchEnabled=false` 时节流逻辑忽略 `batchSize / batchIntervalMs`；读取时两者仍参与 Normalize（见下）。
- **读取 Normalize（评审 #3 结论，与损坏兜底并为一条规则）**：文件缺失 / 损坏 / 值越界
  统一回默认值——`batchSize < 1 → 10`、`batchIntervalMs < 0 → 500`、`batchEnabled` 非 bool → `false`；
  GET 永不返回非法值，前端输入框恒为合法。
- 保存校验：`batchSize ≥ 1`、`batchIntervalMs ≥ 0`；非法返回 400 + 中文原因。

### 4.2 存储位置：用户级新文件

- 文件：`%LOCALAPPDATA%\LabelFrame\print-settings.json`（与 `connection.json` 同级），内容：
  ```json
  { "batchEnabled": false, "batchSize": 10, "batchIntervalMs": 500 }
  ```
- 为什么不用机器级 `%ProgramData%\LabelFrame\Client\settings.json`（`/api/host/config`）：
  批次是**操作偏好**（每个操作员可不同），不是机器级服务地址；ProgramData 写权限与多用户语义不合适。
- 为什么不复用 `connection.json`：那是传输连接配置（pluginId + params），语义不同，
  混入会让连接配置与打印节奏互相干扰。
- 读写实现：`PrintSettingsStore`（原子写：先写临时文件再替换，与 `HostConfigStore` 同模式；
  读取时 Normalize，缺失 / 损坏 / 越界兜底默认值，见 §4.1）。

### 4.3 WinHost API（新增）

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/host/print-settings` | 返回 `{ batchEnabled, batchSize, batchIntervalMs }`（缺失 / 损坏 / 越界统一 Normalize 回默认值，见 §4.1） |
| POST | `/api/host/print-settings` | 请求同上；校验（`batchSize ≥ 1`、`batchIntervalMs ≥ 0`）；**仅回环可写**（与 `/api/host/config` 一致）；保存**即生效**（无需重启） |

- 保存后直接更新内存中的设置对象（注册为单例注入 `JobPrintWorker`），无需重启客户端。
- 旧 WinHost 无此端点：前端 404 优雅降级（不渲染该卡片或显示版本提示，参照「插件管理」卡片做法）。

### 4.4 前端 UI（设置页）

- 设置页新增「打印批次」卡片（置于「连接方式」卡片之下，即 Settings.tsx:276 与 315 之间）：
  - 开关「开启批次作业」（默认关）；
  - 数字输入「每批次打印数量」（默认 10，min 1）；
  - 数字输入「批次打印间隔（毫秒）」（默认 500，min 0）；
  - 「保存」按钮 + 成功 / 失败提示（参照「服务端地址」保存交互）；
  - 提示文案：如「开启后，大批量作业将每 N 张一批发送到打印机，批与批之间间隔 N 毫秒」；
    关闭时两个数字输入禁用置灰。
- 404 降级：参照插件管理卡片 `err.code === 'HTTP_404'` 模式（Settings.tsx:95）与
  installedOldWinHost 提示（:477），实施期前端自行落实。

## 5. 代码落点（实施建议）

1. **WinHost 新增 `PrintSettings`（选项模型）与 `PrintSettingsStore`**（`src/LabelFrame.WinHost/`，
   与 `HostConfigStore` / `HostOptions` 同目录），含默认值、读取 Normalize（回默认值）与
   保存 Validate（返回问题）；**单例并发可见性（评审 #8）**：API 线程写、Worker 线程读，
   读写统一走 lock（沿用 `HostConfigStore._gate` 风格）或 volatile，保证可见性。
2. **WinHost 新增 API**：`GET/POST /api/host/print-settings`（`Program.cs` 中注册，POST 回环校验）。
3. **`JobPrintWorker` 增加批次节流**（`src/LabelFrame.WinHost/Jobs/JobPrintWorker.cs`）：
   - 注入 `PrintSettings`；
   - 维护内存计数 `int sendsSinceBatch`（进程内，不持久化）；
   - **发送前暂停**：`ClaimNextItemAsync` 领取到下一张后、`SendAsync` 前，
     若 `enabled && sendsSinceBatch > 0 && sendsSinceBatch % batchSize == 0`
     → `await Task.Delay(batchIntervalMs, stoppingToken)`；
   - 每次 `SendAsync` 成功、`CompleteItemAsync` 后：`sendsSinceBatch++`；
   - 可测性：把「是否应暂停」抽成纯函数 / 小类型（如 `BatchPrintPolicy.ShouldPauseBeforeSend(int sendsCompleted)`），
     Worker 集成测试用 FakeTransport + 短间隔断言发送时间序列。
4. **不改**：`LabelJobQueue`、`JobSubmissionService`、`ServerRoutingWorker`、`ServerService`、`RoutingJson`。
5. **前端**：`web/src/lib/api/client.ts`（`localApi.getPrintSettings / setPrintSettings`）、
   `web/src/lib/api/types.ts`（`PrintSettings`）、`web/src/pages/Settings.tsx`（卡片）、
   `web/src/pages/Settings.test.tsx`（用例）。
6. **WinHost 引入 Serilog 文件日志**（评审 #2 结论 + 用户拍板，2026-08-18）：
   - 包：`Serilog.AspNetCore`（含 `Serilog.Sinks.File`）；
   - 配置：`builder.Host.UseSerilog(...)`（或 `builder.Logging.ClearProviders().AddSerilog(...)`），
     文件 sink 输出到 `%LOCALAPPDATA%\LabelFrame\logs\app-20260818.log`（`app-.log` + `RollingInterval.Day` 按天滚动；注：Serilog.Sinks.File 的 `{Date}` 为字面量、不会被替换，实现以此为准，联调附五实证、
     带时间戳与级别），使 `JobPrintWorker` 的逐张 ILogger 日志（「开始打印 / 打印完成」）
     落盘带时间戳 → 端到端冒烟按「打印完成」时间戳断言批间间隔 ≈ 500ms；
   - 不重构现有 hostLogWriter（TextWriter）通道（HostInfo / LogPrintTransport /
     提交摘要仍写 host.log），Serilog 与 host.log 分开文件，避免双写同一文件。

## 6. 与现有功能的交互

| 功能 | 影响 |
|---|---|
| 幂等 / requestId | 无（一个 Server 作业仍对应一个本地作业） |
| 挂起 / 恢复 / 取消 | 无；间隔 `Task.Delay` 可随停止令牌取消；恢复后继续按新计数节流。**取消**：间隔窗口内已领取在途的 1 张可能仍会发出（与现状 claim→send 之间的竞态一致，仅窗口变宽，不改变行为） |
| 失败项单独重打 | 重打项就是一次普通发送，计入节流计数 |
| 传输插件 / 切换连接 | 每次发送仍取当前传输；节流不感知传输类型 |
| Log 模拟打印 | 同样生效：Worker 逐张发送带间隔（PNG 在提交时已一次性保存，间隔体现在逐张发送 / Serilog 逐张日志时间戳，而非 PNG 保存时机） |
| 打印机测试页（单张） | **不计入批次计数**：`POST /api/printer/test` 直发（直接 `Transport.SendAsync`，不经 JobPrintWorker / 队列），无批次等待（评审 #9 修正） |
| 服务重启 | 节流计数是内存态，重启清零（节流是节奏机制，非正确性机制；不丢不重打） |

## 7. 测试计划

- **WinHost 单测**：
  - `PrintSettings` Normalize / 校验：默认值、`batchSize < 1`、`batchIntervalMs < 0`、
    非法输入读取回默认值、保存返回问题。
  - `PrintSettingsStore`：默认兜底 / 损坏兜底 / 原子写。
  - API：GET 兜底（含越界 Normalize）、POST 校验 400、非回环写拒绝 403。
  - Worker 节流集成：FakeTransport 记录时间戳——禁用时无间隔；启用时 25 张 / 批次 5 / 间隔 X
    → 第 6 / 11 / 16 / 21 张发送前各停一次（共 4 次），每批 5 张连续、不足一批不等待；
    跨作业累计（两个作业连续各 5 张 → 第 5 张后、B 首张前等待一次）。
- **前端测试**：卡片渲染 / 开关联动禁用输入 / 保存成功与失败 / 旧 WinHost 404 降级。
- **端到端冒烟**：Server 提交 100 张 → 客户端开启批次 10 / 500ms → 从 Serilog 日志
  （`logs/app-*.log`）按「打印完成」时间戳断言每 10 张间隔约 500ms →
  作业最终回报 Completed（进度仍 0%→100%，符合现状，避免实施后被误判为 bug）。

## 8. 开放问题（评审重点）

> 2026-08-18 用户确认：除日志改用 Serilog 外其余无异议。以下 Q1 / Q3 / Q4 / Q5 / Q7
> 按推荐值实施（Q1 全局生效、Q3 全局累计、Q4 默认 10 / 500ms / 间隔允许 0、
> Q5 AndroidHost 本轮不做、Q7 命名 BatchPrint）；评审组如仍有异议可再调整。

- **Q1 适用范围**：批次节流全局生效（本机 + 服务端作业，推荐）还是仅服务端作业？→ 按推荐：全局生效。
- **Q2 是否同时解决进度 0/100**：本轮只做批次（推荐，符合用户明确范围）；若加最小版
  「每批回报一次进度」——那属于跨端契约变更（`ReportResultAsync` 需允许非终态更新
  `CompletedItems`，作业状态保持进行中），按 AGENTS 需先讨论再改。若拍板实施，批次边界
  （每批完成）是天然回报点，**届时再讨论契约**；本轮 Server 零改动（评审 #4 已改措辞）。
  **结论：本轮不做。**
- **Q3 批次计数**：全局累计（推荐，跨作业连续）还是按作业独立？→ 按推荐：全局累计。
- **Q4 默认值与范围**：默认关闭、批次 10、间隔 500ms、间隔允许 0——是否合适？→ 按推荐。
- **Q5 AndroidHost**：本轮不做（推荐）？→ 按推荐。
- **Q6 迭代编号（推荐已采纳）**：把「迭代 24」主题改为本功能（Niimbot 顺延下一轮）；
  正式启动迭代时同步修正 ROADMAP 状态表，并把 DESIGN.md 中 Android「迭代 24 → 25」的
  两处排期矛盾一并改掉（评审 #5）。
- **Q7 命名**：设置项 / API 用「批次作业 BatchPrint」命名是否 OK（界面文案可再调）？→ 按推荐。

## 9. 风险

- 大批量 + 长间隔会显著拉长总耗时（间隔次数 = 批数 - 1），需在 UI 提示估算；
  100 张 / 10 批 / 500ms 仅增加约 4.5s，可接受。
- 间隔期间服务端仍显示 0%：如用户期望看到进度，需 Q2 的增量回报，本轮不做需明确告知。
- 间隔窗口内取消：已领取在途的 1 张可能仍会打印（与现状一致，窗口略宽，见 §6）。
- 内存计数在服务重启后清零，只影响节奏不影响正确性。

## 附：审阅意见（hermes 追加，2026-08-18）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。以下意见已对照当前仓库代码逐条核对（工作区 = commit 49e0eb1）。

### 🔴 关键缺口

**1. 批次暂停语义四方矛盾（§3.2 ↔ §3.4 ↔ §5.3 ↔ §7），照 §5.3 实现必与 §3.4 冲突**

- §3.4：100 张 / 批 10 额外耗时 = `(ceil(100/10) - 1) × 500ms` = 9 次 ≈ 4.5s，且「最后一批之后不额外等待」；
- §5.3 草图：`SendAsync 成功、CompleteItemAsync 后：sendsSinceBatch++; 若 enabled && sendsSinceBatch % batchSize == 0 → Task.Delay(...)` —— 100 张时第 10/20/…/100 张后各停一次，共 **10 次**（含最后一张后），耗时 5s，与 §3.4 数学直接不符；作业已 Completed 后 worker 还会多等一次（随后才 claim 到 null 进入空闲）；
- §3.2 例「A 60 张正好满 6 批，B 第 1 张起是新一批」隐含 **A 末张（恰好是批界）之后要停**；§3.4 说作业末批后不停——同一位置两种语义；
- §7 测试「两个作业连续各 5 张 → 第 10 张后等待」：第 10 张是 B 的末张，按「末批后不等待」原则不应等待，测试期望与原则矛盾。

建议统一为 **「发送前暂停」（claim-then-delay）**：`ClaimNextItemAsync` 取到下一张且 `sendsSinceBatch > 0 && sendsSinceBatch % batchSize == 0` 时先 `await Task.Delay(batchIntervalMs, stoppingToken)` 再 `SendAsync`。该语义同时满足 §3.2（A 末张后、B 首张前暂停）与 §3.4（100 张后无下一张、不暂停），且空队列时不会白等。§5.3 代码草图与 §7 两处测试期望需同步修改（25 张 / 批 5 → 等待 4 次；跨作业 5+5 → 第 5 张后等待），`BatchPrintPolicy` 纯函数相应改为发送前判定（如 `ShouldPauseBeforeSend(int sendsCompleted)`）。

**2. 端到端冒烟「host.log 显示每 10 张间隔约 500ms」无时间戳来源，不可测**

- Log 传输每次发送写 host.log 一行属实（`LogPrintTransport.SendAsync`，`src/LabelFrame.Core/Transport/LogPrintTransport.cs:22`），但该行**没有时间戳前缀**，无法测出间隔；
- JobPrintWorker 逐张日志走 `ILogger<JobPrintWorker>`（JobPrintWorker.cs:56/62），WinHost 未注册任何 logger provider（无 AddLogging / FileLoggerProvider），WinExe 无控制台 → ILogger 输出不可见、不进 host.log；host.log 是独立 TextWriter（Program.cs:56、814），仅 HostInfo（带 `[yyyy-MM-dd HH:mm:ss]` 前缀，Program.cs:85）/ 插件加载 / 提交摘要写入；
- 建议二选一：a) Log 传输行加 `[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]` 前缀（Core 一行改动，与 HostInfo 格式一致，顺带补模拟打印日志无时间戳的老问题）；b) 冒烟降级为「脚本提交 → 轮询作业状态至 Completed，记录总耗时 ≈ (批数−1)×间隔」粗验（Log 模式发送本身瞬时，总耗时主要由暂停构成）。间隔断言的可靠手段仍是 §7 的 Worker 集成测试（FakeTransport 记录时间戳）。

### 🟡 规格空白与不一致

**3. GET 对「存在但非法」值的语义未定义**：§4.1「读取时不强制合法」+ §4.3「缺失 / 损坏兜底默认值」——文件存在但 `batchSize: 0`（手工编辑）时 GET 返回 0？前端输入框 min=1 会显示非法值、保存即 400。建议读取时 Normalize（越界回默认值或钳制），与损坏兜底并为一条规则。

**4. §8 Q2「接口可预留」与 §2.2「跨端契约不变」表述冲突**：预留 = 本轮设计好但不用（Server 零改动），还是本轮就加接口？若 Server 零改动是硬边界，建议删去「接口可预留」或改为「Q2 若拍板实施，批次边界是天然回报点，届时再讨论契约」。

**5. AndroidHost 排期文档间冲突（影响 Q6 拍板）**：DESIGN.md 未决问题「Android PDA 宿主…延后至迭代 24」「libe_sqlite3 的 16KB 适配待迭代 24 真机构建验证」；ROADMAP 迭代 21/22「不在范围」均写「PDA（AndroidHost，延后至迭代 25）」（ROADMAP.md:666、683）。Q6 定迭代号时需一并澄清 Niimbot 与 AndroidHost 各自顺延到哪轮。

### 🟢 待决策（沿用文档 §8）

**6. Q6 迭代编号**：ROADMAP.md:696 迭代 24 已排 Niimbot（「下一轮」），本设计若改占迭代 24 需用户拍板——文档已列，无异议。
**7. Q2 进度回报本轮不做**：同意推荐（跨端契约变更需先讨论）。§7 冒烟已写「进度仍 0%→100%，符合现状」，建议保留此句，避免实施后被误判为 bug。

### 💡 可选建议

**8. PrintSettings 单例并发可见性**：API 线程写、Worker 线程读，.NET 中 bool/int 读写原子够用，但建议字段加 volatile 或沿用 HostConfigStore._gate 的 lock 风格（HostConfigStore.cs:9），避免告警与扩展踩坑。
**9. 计数语义一句话说清**：§6 已写「重打项计入节流计数」，建议顺带注明「测试打印页（单张测试页）同样计入」（它也走 JobPrintWorker 发送路径），保持计数语义无歧义。
**10. 前端落点可落地**：卡片位置「连接方式之下」即 Settings.tsx:276 与 315 之间，与现有卡片顺序一致；404 降级参照插件管理卡片 `err.code === 'HTTP_404'` 模式（Settings.tsx:95）与 installedOldWinHost 提示（:477），实施期前端自行落实、不阻塞定稿。

### ✅ 已核对通过项（依据）

- §1.1 三处代码依据全部属实：ServerRoutingWorker 仅本地终态回报一次（ServerRoutingWorker.cs:158-180）；ReportResultAsync 仅接受 Claimed → 终态（ServerService.cs:210），CompletedItems 回报前恒 0；ServerJobView 无逐张 items（Server Contracts.cs:38-47）；WinHost JobView 含 Items（WinHost Api/Contracts.cs:27-42）。
- §4.2 存储位置：connection.json 确在 %LOCALAPPDATA%\LabelFrame（TransportManager.cs:55）；HostConfigStore 原子写（临时文件 + File.Move overwrite，HostConfigStore.cs:40-54）与缺失 / 损坏兜底模式属实，PrintSettingsStore 复用可行。
- §4.3 回环校验与 /api/host/config 一致（Program.cs:598-599，IsLoopback 失败 Results.Forbid()=403）；GET 无回环限制、POST 仅回环，新端点镜像一致；JobPrintWorker 由 DI 创建（AddHostedService，Program.cs:135），注入 PrintSettings 单例实现「保存即生效」可行。
- §5「不改」清单核对无误：LabelJobQueue 按作业 FIFO 整批消化（LabelJobQueue.cs:77-107）、单 Worker 串行，全局计数无并发竞争，§3.2 跨作业连续示例成立；重打 = RetryItemAsync 重置 Pending 后重新入队（LabelJobQueue.cs:248），计入计数，§6「重打计入」属实。
- §5 前端落点全部存在：client.ts localApi（getHostConfig:293）、types.ts（HostConfig:262）、Settings.tsx（服务端地址:237 / 连接方式:276 / 插件管理:382）、Settings.test.tsx；设置页仅 client 构建有（App.tsx:25-31 server 构建无 settings tab），与 WinHost-only 端点匹配，无需 server 构建分支。
- §6 交互表：挂起后队列不再领取该作业（Suspended 不在 ClaimNextItemAsync 扫描范围，LabelJobQueue.cs:84-85）；Log 模式每次发送写 host.log 一行（LogPrintTransport.cs:20-24）；服务重启计数清零属内存态，与文档一致。
- 「不拆作业（方案 A）」与现有 requestId 一对一 / 幂等 / 终态回报模型兼容，同意推荐。

### 待审核者确认清单

1. 批次暂停语义：采用「发送前暂停（claim-then-delay）」统一 §3.2/§3.4/§5.3/§7，还是「发送后暂停 + 末张除外」？（#1）
2. 端到端冒烟：接受 Log 传输行加时间戳（Core 一行），还是降级为总耗时粗验？（#2）
3. GET 对存在但非法的设置值：回默认值 / 钳制 / 原样返回？（#3）
4. 「接口可预留」一句：删除还是改写？（#4）
5. Q6 拍板时一并确认 AndroidHost 排期（DESIGN.md 迭代 24 vs ROADMAP 迭代 25）。（#5）

（正文未做任何修改；本节为审阅记录。）

## 附二：评审结论与修订记录（2026-08-18，主 Agent 按用户拍板修订）

> 用户确认（2026-08-18）：日志用 Serilog（成熟 NuGet 包）；其余意见无异议。
> 下表记录每条评审的结论与正文落点。

| 评审条 | 结论 | 正文落点 |
|---|---|---|
| #1 暂停语义 | 采纳「发送前暂停（claim-then-delay）」 | §3.2 / §3.4 / §5 / §7 已统一修订 |
| #2 冒烟不可测 | 采纳用户拍板：WinHost 引入 Serilog 文件日志（`Serilog.AspNetCore` + `Serilog.Sinks.File`），逐张 ILogger 日志落盘带时间戳；LogPrintTransport 行不改 | §5.6（新增）/ §7 |
| #3 GET 非法值 | 采纳「读取 Normalize 回默认值」，与损坏兜底并为一条规则 | §4.1 / §4.3 |
| #4 措辞冲突 | 改写：删除「接口可预留」，改为「届时再讨论契约；本轮 Server 零改动」 | §8 Q2 |
| #5 AndroidHost 排期 | 以 ROADMAP「延后至迭代 25」为准；DESIGN.md 两处「迭代 24」待启动迭代时修正 | §8 Q6 |
| #6 迭代编号 | 推荐「迭代 24 改为本功能、Niimbot 顺延」已确认，启动时更新 ROADMAP / DESIGN / CHANGELOG | §8 Q6 |
| #7 冒烟保留进度句 | 采纳（保留「进度仍 0%→100%，符合现状」） | §7 |
| #8 并发可见性 | 采纳（lock 或 volatile） | §5.1 |
| #9 测试页计数 | 部分采纳：测试页为直发路径、**不计入**计数（评审所述「走 JobPrintWorker」为误判，已按实际代码修正） | §6 |
| #10 前端落点 | 采纳（行号已核对，实施期落实） | §4.4 / §5.5 |

**其余修订**：
- §6「Log 模拟打印」行修正：PNG 在提交时一次性保存，间隔体现在逐张发送 / Serilog 逐张日志时间戳，而非 PNG 保存时机。
- §6「取消」行补充：间隔窗口内已领取在途的 1 张可能仍会打印（与现状竞态一致，窗口略宽）；§9 风险同步记录。

**待办（迭代正式启动时执行）**：ROADMAP 状态表（迭代 24 主题、Niimbot 顺延）、
DESIGN.md（Android 排期两处）、CHANGELOG。

## 附三：第二轮复核意见（hermes 追加，2026-08-18）

> 复核对象：98801de 修订版正文 + 附二修订记录；逐条对照仓库代码核实。

### 落实核对（对照附二表逐条）

✅ **#1 发送前暂停**：§3.2 / §3.4 / §5.3 / §7 四处已统一为 claim-then-delay 且互洽——§7「25 张/批 5 → 第 6/11/16/21 张前各停一次（共 4 次）」「跨作业 5+5 → 第 5 张后、B 首张前等待一次」与 §3.4 的 9×500ms 数学一致，无误。
✅ **#2 Serilog 冒烟链路**：§2.1 范围、§5.6、§7 已落地；包依赖声称属实（本机 NuGet 缓存 serilog.aspnetcore nuspec 确认传递依赖 Serilog.Sinks.File，无需单独引用）；host.log 通道不动、LogPrintTransport 行不改，两套日志分开，与 §2.2 一致；WinHost 测试项目无启动 Web 主机的用例（test/ 下无 WebApplication/CreateBuilder/Program.Main 引用），Serilog 引入不影响现有测试。
✅ **#3 读取 Normalize**：§4.1 / §4.3 统一为「缺失 / 损坏 / 越界回默认值」（batchSize<1→10、batchIntervalMs<0→500、batchEnabled 非 bool→false），GET 永不返回非法值，与保存校验（400）不冲突。
✅ **#4 措辞**：§8 Q2 已删「接口可预留」，改为「届时再讨论契约；本轮 Server 零改动」，与 §2.2「服务端任何改动」一致。
✅ **#5 AndroidHost 排期**：以 ROADMAP「延后至迭代 25」为准；DESIGN.md 两处（Android PDA 宿主条目、AndroidHost 构建依赖条目）列入启动待办，合理。
✅ **#6 迭代编号**：Q6「迭代 24 改为本功能、Niimbot 顺延」已确认并记录启动待办。
✅ **#7 进度句**：§7 保留「进度仍 0%→100%，符合现状，避免实施后被误判为 bug」。
✅ **#8 并发可见性**：§5.1 已注明 lock（HostConfigStore._gate 风格）或 volatile。
✅ **#9 测试页计数**：**附二纠正属实**——`POST /api/printer/test` 为直发路径（Program.cs:567-587，直接 `CurrentTransport.SendAsync`，不经 JobPrintWorker / 队列），不计入计数、不受节流；第一轮 #9 所述「测试页也走 JobPrintWorker 发送路径」为我方误判，以修订版为准。§6 行「不计入批次计数：直发…无批次等待」准确。
✅ **#10 前端落点**：§4.4 / §5.5 已写入行号（Settings.tsx:276/315、:95、:477），实施期落实。

### 修订质量检查

- 编号 / 结构：§5 新增第 6 条（Serilog）编号连续；附二表引用「§5.1 / §5.5 / §5.6」与正文实际编号一致；无重复标题。
- 旧表述残留：§3.4 原「最后一批之后不额外等待」已改写为「无第 101 张、不额外等待」，与 claim-then-delay 自洽；§6「Log 模拟打印」行已修正（PNG 在提交时一次性保存，间隔体现在逐张发送 / Serilog 时间戳），无「PNG 保存之间」旧表述残留。
- 新语义核对：「取消」行新增「间隔窗口内已领取在途的 1 张可能仍会发出」——属实（claim 时已置 Printing，取消置 Cancelled 后 SendAsync 仍会执行，CompleteItemAsync 对 Cancelled 作业直接返回不改变状态，LabelJobQueue.cs:123-125），「窗口变宽」表述准确（现状窗口≈0，加节流后 = 间隔时长）；§9 风险已同步。
- 与第一轮 ✅ 项复核：正文修订未触碰 §1.1 代码依据、存储位置（%LOCALAPPDATA%\LabelFrame）、回环 403 校验、前端落点等已核对项。

### 💡 非阻塞细节（实施期落实，不阻塞定稿）

1. §5.6 日志文件名「`app-.log`（按天 / 大小滚动）」：Serilog File sink 的按天滚动需 `{Date}` 占位符，建议写为 `app-{Date}.log`（或注明 RollingInterval.Day），否则字面文件名不会滚动。
2. §4.1「`batchEnabled=false` 时忽略 `batchSize / batchIntervalMs`（仍参与读取校验，见下）」——「忽略…仍参与校验」措辞自相矛盾，建议改为「节流逻辑忽略这两个值；读取时仍参与 Normalize」。

### 结论

无新异议：10 条意见全部落实或合理处置（含 #9 误判纠正），四方语义（§3.2 / §3.4 / §5.3 / §7）已互洽，修订未引入新矛盾，**可定稿**。两处 💡 由实施期按上述建议落实即可，无需再往返。

（正文未做任何修改；本节为审阅记录。）

## 附四：💡 非阻塞建议落实记录（2026-08-18，主 Agent）

> 附三两条非阻塞建议已在正文落实（不再往返）：

1. §5.6 日志文件名改为 `app-{Date}.log`（`RollingInterval.Day` 按天滚动），避免字面文件名不滚动。
2. §4.1 措辞改为「`batchEnabled=false` 时节流逻辑忽略 `batchSize / batchIntervalMs`；
   读取时两者仍参与 Normalize」，消除「忽略…仍参与校验」自相矛盾。

其余按附三结论：10 条意见全部落实或合理处置（含 #9 误判纠正），四方语义互洽，**可定稿**。
## 附五：联调测试记录（2026-08-18，hermes 前端会话）

> 前端实施方联调（master 67214c3 合并前后端后执行）；测试环境全隔离（测试 Server 53963 + 测试 WinHost 53962 +
> 模拟旧 WinHost stub 53964），生产 53960/53961 未受影响；按 §7 测试计划执行，前端零缺陷，后端 1 项待修（见下）。

### ✅ 验证结果清单

**① WinHost API `/api/host/print-settings`（§4.3）**
- GET 默认值 `{batchEnabled:false, batchSize:10, batchIntervalMs:500}` ✓
- POST 保存 200 + 回显；保存即生效（GET 立即反映，无需重启）；文件落盘 ✓
- POST 非法值（batchSize=0 / batchIntervalMs=-1）→ 400 + 中文原因「每批次打印数量需 ≥ 1。」✓
- 读取 Normalize：手改文件 `batchIntervalMs:-5` → GET 回默认 500，永不返回非法值 ✓

**② 前端设置页「打印批次」卡片（§4.4）**
- 卡片位于「连接方式」之下、「打印机」之上；渲染当前设置与提示文案 ✓
- 开关联动：关闭时数量/间隔输入禁用置灰，开启后恢复 ✓
- 保存成功提示「已保存并立即生效。」，GET 确认持久化 ✓
- 404 降级：旧 WinHost stub（print-settings 恒 404）下显示「当前客户端版本不支持批次作业。」且不渲染表单 ✓

**③ 端到端冒烟（§7，Server 提交 100 张 → 批次 10 / 500ms）**
- 作业终态 Completed（100/100，0 失败），进度 0%→100% 符合现状预期 ✓
- Serilog 逐张日志 100 条「第 N 张打印完成」序号连续；节流日志恰好 9 次
  （已发送 10/20/…/90 张各暂停 500ms，最后一批后无下一张不等待——发送前暂停语义精确符合）✓
- 批界间隔 9 次均值 686ms = 500ms 暂停 + ~186ms Log 传输固有耗时（批内均值 162ms），扣除固有耗时 ≈ 524ms ≈ 500ms ✓
- 关闭批次后 30 张作业批界间隔仅 41/43ms（无暂停）——开关语义正确 ✓

### 🔧 后端待修（只记录，前端不修）

1. **Serilog 日志文件名含字面 `{Date}`**：实际文件 `app-{Date}20260818.log`（Program.cs:64 同时使用 `{Date}` 占位符与
   `rollingInterval: RollingInterval.Day`，两者互斥——Serilog 仅按 rolling 追加日期后缀、不替换 `{Date}`），
   与 §5.6「`app-{Date}.log` 按天滚动」的命名意图不符（滚动功能本身正常）。
   建议二选一：去掉 `rollingInterval` 保留 `{Date}`，或去掉 `{Date}` 保留 `RollingInterval.Day`。

   **处理（主 Agent，2026-08-18 已修复并提交）**：实证 Serilog.Sinks.File 5.0.0 下 `{Date}` 为字面量——「去掉
   `rollingInterval` 保留 `{Date}`」同样不会替换（产物为字面 `app-{Date}.log`）；已改为 `app-.log` +
   `RollingInterval.Day`（尾连字符惯例），实际产物 `app-20260818.log`，按天滚动正常。§5.6 已同步更正。

### 🔧 联调环境事故（已恢复，非产品缺陷）

测试 WinHost 切换传输时 connection.json 写入了生产路径——`Environment.GetFolderPath(LocalApplicationData)`
在 Windows 走 KnownFolder API、**不读 `LOCALAPPDATA` 环境变量**，该路径无 env 覆盖、无法隔离。
已按生产 WinHost 当前生效连接（Zebra USB 自动发现）原值恢复，生产进程未受影响；测试 Serilog 日志已从生产
`logs/` 目录移走。教训：传输切换 / 批次联调前先备份 connection.json 并记录 logs 目录清单（已记入 hermes 笔记）。

### 结论

前后端联调全部通过（API 契约 / UI 交互 / 404 降级 / E2E 节流时序 / 终态回报）；前端无缺陷；
后端仅 1 项日志命名偏差（低优先级，不阻塞功能）。待修项由主 Agent 裁决排期。

（正文未做任何修改；本节为联调测试记录。）
