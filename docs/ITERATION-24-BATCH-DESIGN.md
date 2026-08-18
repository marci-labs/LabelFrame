# 设计方案：客户端批次作业（Batch Print）— 待评审

> 状态：**草稿（待评审，2026-08-18）**。本文是设计方案，不是定稿规格；评审通过后再并入
> `docs/ROADMAP.md` 迭代条目并形成实施规格。任何决策在评审前都可能调整。
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
- 单元测试 / 前端测试 / 端到端冒烟。

### 2.2 不在范围（本轮）

- 服务端任何改动（批次是纯客户端发送层功能，跨端契约不变）。
- 增量进度回报（服务端作业逐批/逐张进度展示）——独立跨端特性，见 §8 Q2。
- AndroidHost（PDA）批次功能——与 WinHost 代码路径不同，延后。
- 把 Server 作业真的拆成多个本地作业——不采用（见 §3.1）。
- 修改作业队列 / 幂等 / 挂起 / 恢复 / 取消 / 重打语义。

## 3. 总体方案

### 3.1 关键决策一：不拆作业，只在发送层节流

用户描述「100 张分了 10 个作业」是口语化的「10 组」；**不建议真的拆成多个本地作业**，原因：

| 方案 | 说明 | 问题 |
|---|---|---|
| A（推荐） | 一个 Server 作业 = 一个本地作业，仅在 `JobPrintWorker` 发送循环里按批暂停 | 队列 / 幂等 / 回报 / 挂起恢复 / 失败重打语义零改动；requestId 一对一 |
| B | 提交时把 100 张拆成 10 个本地作业 | 需引入批间顺序依赖、多作业聚合回报、requestId 映射，复杂度高、易错；不采用 |

结论：**一个本地作业、逐张发送，每发满 N 张暂停间隔**。这样也保留逐张状态与
「失败项单独重打」的既有能力（重打项就是一次普通发送）。

### 3.2 关键决策二：批次计数全局累计（跨作业连续）

节流语义：**每成功发送满 N 张，就停间隔**，计数跨作业连续，不按作业重置。

- 例：作业 A 有 60 张、作业 B 有 60 张，批次 10、间隔 500ms →
  发送序列 1..10 停、11..20 停、……、A 的 60 张发完正好满 6 批，B 的第 1 张起是新一批。
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
- 时序：发送第 1..10 张（连续）→ 完成第 10 张后 `await Task.Delay(500ms)` →
  发送 11..20 → …… → 发送 91..100 → 作业完成，**最后一批之后不额外等待**。
- 额外耗时：`(ceil(100/10) - 1) × 500ms = 9 × 500ms ≈ 4.5s`。
- 间隔是「上一批最后一张发送完成之后、下一批第一张发送之前」，不是每张之间。

## 4. 设置模型与持久化

### 4.1 设置项

| 字段 | 类型 | 默认 | 范围 | 说明 |
|---|---|---|---|---|
| `batchEnabled` | bool | `false` | — | 是否开启批次作业 |
| `batchSize` | int | `10` | ≥ 1 | 每批次打印数量 |
| `batchIntervalMs` | int | `500` | ≥ 0 | 批次打印间隔（毫秒）；0 = 无间隔 |

- `batchEnabled=false` 时忽略 `batchSize / batchIntervalMs`（读取时不强制合法，保存时校验）。
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
  文件缺失 / 损坏读取兜底默认值）。

### 4.3 WinHost API（新增）

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/host/print-settings` | 返回 `{ batchEnabled, batchSize, batchIntervalMs }`（缺失 / 损坏兜底默认值） |
| POST | `/api/host/print-settings` | 请求同上；校验（`batchSize ≥ 1`、`batchIntervalMs ≥ 0`）；**仅回环可写**（与 `/api/host/config` 一致）；保存**即生效**（无需重启） |

- 保存后直接更新内存中的设置对象（注册为单例注入 `JobPrintWorker`），无需重启客户端。
- 旧 WinHost 无此端点：前端 404 优雅降级（不渲染该卡片或显示版本提示，参照「插件管理」卡片做法）。

### 4.4 前端 UI（设置页）

- 设置页新增「打印批次」卡片（置于「连接方式」卡片之下）：
  - 开关「开启批次作业」（默认关）；
  - 数字输入「每批次打印数量」（默认 10，min 1）；
  - 数字输入「批次打印间隔（毫秒）」（默认 500，min 0）；
  - 「保存」按钮 + 成功 / 失败提示（参照「服务端地址」保存交互）；
  - 提示文案：如「开启后，大批量作业将每 N 张一批发送到打印机，批与批之间间隔 N 毫秒」；
    关闭时两个数字输入禁用置灰。

## 5. 代码落点（实施建议）

1. **WinHost 新增 `PrintSettings`（选项模型）与 `PrintSettingsStore`**（`src/LabelFrame.WinHost/`，
   与 `HostConfigStore` / `HostOptions` 同目录），含默认值、校验（`Normalize` 或 `Validate` 返回问题）。
2. **WinHost 新增 API**：`GET/POST /api/host/print-settings`（`Program.cs` 中注册，POST 回环校验）。
3. **`JobPrintWorker` 增加批次节流**（`src/LabelFrame.WinHost/Jobs/JobPrintWorker.cs`）：
   - 注入 `PrintSettings`；
   - 维护内存计数 `int sendsSinceBatch`（进程内，不持久化）；
   - 每次 `SendAsync` 成功、`CompleteItemAsync` 后：`sendsSinceBatch++`；
     若 `enabled && sendsSinceBatch % batchSize == 0` → `await Task.Delay(batchIntervalMs, stoppingToken)`。
   - 可测性：把「是否应暂停」抽成纯函数 / 小类型（如 `BatchPrintPolicy.ShouldPauseAfterSend(int sendsCompleted)`），
     Worker 集成测试用 FakeTransport + 短间隔断言发送时间序列。
4. **不改**：`LabelJobQueue`、`JobSubmissionService`、`ServerRoutingWorker`、`ServerService`、`RoutingJson`。
5. **前端**：`web/src/lib/api/client.ts`（`localApi.getPrintSettings / setPrintSettings`）、
   `web/src/lib/api/types.ts`（`PrintSettings`）、`web/src/pages/Settings.tsx`（卡片）、
   `web/src/pages/Settings.test.tsx`（用例）。

## 6. 与现有功能的交互

| 功能 | 影响 |
|---|---|
| 幂等 / requestId | 无（一个 Server 作业仍对应一个本地作业） |
| 挂起 / 恢复 / 取消 | 无；间隔 `Task.Delay` 可随停止令牌取消；恢复后继续按新计数节流 |
| 失败项单独重打 | 重打项就是一次普通发送，计入节流计数 |
| 传输插件 / 切换连接 | 每次发送仍取当前传输；节流不感知传输类型 |
| Log 模拟打印 | 同样生效（间隔体现在 PNG 保存之间），便于本地验证 |
| 打印机测试页（单张） | 不足一批，无实际等待 |
| 服务重启 | 节流计数是内存态，重启清零（节流是节奏机制，非正确性机制；不丢不重打） |

## 7. 测试计划

- **WinHost 单测**：
  - `PrintSettings` 校验：默认值、`batchSize < 1`、`batchIntervalMs < 0`、非法输入返回问题。
  - `PrintSettingsStore`：默认兜底 / 损坏兜底 / 原子写。
  - API：GET 兜底、POST 校验 400、非回环写拒绝 403。
  - Worker 节流集成：FakeTransport 记录时间戳——禁用时无间隔；启用时 25 张 / 批次 5 / 间隔 X
    → 每 5 张后间隔 ≥ X 且不足一批不等待；跨作业累计（两个作业连续各 5 张 → 第 10 张后等待）。
- **前端测试**：卡片渲染 / 开关联动禁用输入 / 保存成功与失败 / 旧 WinHost 404 降级。
- **端到端冒烟**：Server 提交 100 张 → 客户端开启批次 10 / 500ms → host.log 显示每 10 张间隔约 500ms
  → 作业最终回报 Completed（进度仍 0%→100%，符合现状）。

## 8. 开放问题（评审重点）

- **Q1 适用范围**：批次节流全局生效（本机 + 服务端作业，推荐）还是仅服务端作业？
- **Q2 是否同时解决进度 0/100**：本轮只做批次（推荐，符合用户明确范围）；或加最小版
  「每批回报一次进度」——那属于跨端契约变更（`ReportResultAsync` 需允许非终态更新
  `CompletedItems`，作业状态保持进行中），按 AGENTS 需先讨论再改。批次边界正好是天然回报点，
  接口可预留。**请用户拍板。**
- **Q3 批次计数**：全局累计（推荐，跨作业连续）还是按作业独立？
- **Q4 默认值与范围**：默认关闭、批次 10、间隔 500ms、间隔允许 0——是否合适？
- **Q5 AndroidHost**：本轮不做（推荐）？
- **Q6 迭代编号**：把「迭代 24」主题改为本功能（Niimbot 顺延），还是新增迭代号？**请用户拍板。**
- **Q7 命名**：设置项 / API 用「批次作业 BatchPrint」命名是否 OK（界面文案可再调）？

## 9. 风险

- 大批量 + 长间隔会显著拉长总耗时（间隔次数 = 批数 - 1），需在 UI 提示估算；
  100 张 / 10 批 / 500ms 仅增加约 4.5s，可接受。
- 间隔期间服务端仍显示 0%：如用户期望看到进度，需 Q2 的增量回报，本轮不做需明确告知。
- 内存计数在服务重启后清零，只影响节奏不影响正确性。
