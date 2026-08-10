# 迭代 15 规格：打印设置与会话保留 + 连接管理 + 图片打印收敛（删除 ZPL）

> 状态：规格评审中（2026-08-10，主 agent / 后端整理，交前端 hermes 评估；hermes 意见已审阅并答复，见文末「附二」）
> 协作：本文档定义前后端改动范围与 API 契约；hermes 评估前端部分无异议后，后端实施后端项、前端实施前端项（可并行）。字段名 / 接口以本文档为准。
> 背景：用户反复测试打印定位效果，提出三项优化；并已拍板**彻底删除 ZPL（Vector）打印路径**，统一为整版位图（Skia）图片打印。

---

## 1. 需求（用户原话归纳）

1. **数据与打印页会话保留**：切换到设计器再回来，不希望重新设置「标签（模板 / 字段值）+ 打印方式」。不接受开两个页面；同一标签页内切换视图必须保留设置；**两个浏览器标签页之间不互通**（一个页面改设置，另一个页面不得跟随）。
2. **前端切换连接方式**：在 Web 前端把连接从 Log 切换为真实打印机（TCP / Windows 驱动 / Zebra），并可添加新的连接方式；**同一时间只有一个连接方式生效**；**不为未连接的连接方式做支持**（只维护当前生效方式的参数）。
3. **去掉 ZPL（Vector）打印方式**：打印统一为图片（Skia 渲染整版位图经 `^GF` 直传打印机），前后端同一渲染逻辑保证一致性。另：**调试功能独立出来**——调试模式开启时，直接打印图片（保存/查看 PNG），**不发送给打印驱动**。
4. DataPrint 会话保留范围：选中的模板 + 已填字段值 + 调试开关 + Excel 导入映射（用户已确认）。

## 2. 已确认决策（用户拍板 + 审阅答复定稿）

- **D1 彻底删除 ZPL/Vector**：PC 端（WinHost / Web / 作业 / 配置 / 健康检查 / 测试）不再存在矢量 ZPL 路径；图片打印的物理载体 `^GF` 位图编码保留（重构为独立的图片编码器）。文档 / 脚本残留一并清理（README、scripts/demo-winhost.ps1；DESIGN / ROADMAP 历史记录保留）。
- **D2 连接管理**：默认连接 Log；前端可切换并「先测试、后生效」，测试失败自动回滚并提示；切换结果持久化（重启保留）；同一时间单一连接。
- **D3 连接 UI 位置**：设置页做完整管理；「数据与打印」页顶部放当前连接状态与快速切换。
- **D4 调试独立**：调试为独立开关（不再依附「图片打印方式」复选框）；开启后作业只出图、不发送驱动。
- **D5 会话保留实现**：DataPrint 草稿状态提升到全局（内存 / sessionStorage），**禁止 localStorage**（避免跨标签页共享）。

## 3. 删除项（后端为主，前端联动）

### 3.1 后端 / API / 文档脚本
- 删除 `PrintMode` 枚举（Vector / Image）及 `WinHost.PrintMode` 配置、`LABELFRAME_PRINT_MODE` 环境变量、`packaging/appsettings.json` 中的 `PrintMode`。
- 删除 `SubmitJobRequest.printMode`、`/healthz` 响应的 `printMode` 字段（保留 `transport`）。
- 删除矢量 ZPL 编码：`IZplEncoder`、`ZplEncoder.Encode`（`^A` / `^FB` / `^BC` / `^BQ` 等）、`ZplBoldMode`、`WinHost.BoldMode` 配置与 `LABELFRAME_BOLD_MODE`（**`LabelTextElement.Bold` 属性保留**，Skia 图片渲染继续用 `SKFont.Embolden` 实现加粗）。
- **保留并重构** `^GF` 位图编码：`ZplEncoder.EncodeImage` 抽为独立 `ZplImageEncoder.EncodeImage(LabelBitmap, widthMm, heightMm, dpi)`（图片打印的物理载体）。
- 删除 WinHost 对 `ZplEncoder` / `ITextRasterizer` / `GdiTextRasterizer` 的依赖（Image 模式不需要文本→`^GF` 替换，Skia 直接渲染中文）；`GdiTextRasterizer` 及测试删除。
- `JobSubmissionService`：恒走「Skia 渲染整版位图 → `ZplImageEncoder.EncodeImage`」；`ILabelBitmapRenderer` 保留。
- **作业项内容**：统一为 `^GF` 整版位图指令；`LabelJobItem` 存储字段沿用现有列名（历史命名 `Zpl`，**不改列名 / 不迁移**，文档注明内容语义为“打印指令（^GF 位图）”）。
- `LogPrintTransport` 语义调整：Log 连接 = 模拟打印（见 §5.3），不再记录 ZPL 文本；相关测试同步调整。
- AndroidHost：`SubmissionService` 不再使用 `ZplEncoder`，改为**整版位图打印**（新增 Android 整版渲染器：文本用 Android.Graphics、条码 / 二维码用 ZXing.Net、线 / 区域 / 图片绘制，输出 `LabelBitmap` → `ZplImageEncoder`）；真机验收放 PDA 联调阶段。
- 文档 / 脚本残留清理：`README.md` 打印模式描述（默认矢量 ZPL…）、`scripts/demo-winhost.ps1`（展示 ZPL 输出）改为图片打印 / Log 模拟说明；`docs/DESIGN.md`、`docs/ROADMAP.md` 的历史决策记录保留不改。

### 3.2 前端
- 删除「打印方式」下拉（DataPrint / Settings 中 `printMode` 相关 UI 与状态）；删除「调试：不打印，保存实际打印图片（PNG）」复选框（由独立调试开关取代）。
- `Healthz.printMode`、`SubmitJobRequest.printMode` 类型删除（`types.ts`）；`renderImage` 调用不受影响（本来就不传 printMode）。
- `BackendElement` / convert 不受影响（`bold` 等字段保留）。

## 4. 新增 API 契约

### 4.1 连接管理（WinHost 本地 API）

```
GET  /api/transport
→ 200 { "mode": "Log"|"Tcp"|"WindowsDriver"|"Zebra",
         "params": { "tcpHost": "...", "tcpPort": 9100, "printerName": "...", "zebraKind": "Tcp"|"Usb"|"Driver", "zebraUsbName": "..." },
         "availableModes": ["Log","Tcp","WindowsDriver","Zebra"] }
   （params 只含当前模式所需字段；未使用的字段返回默认/空，前端不展示）

POST /api/transport
body { "mode": "...", "tcpHost"?, "tcpPort"?, "printerName"?, "zebraKind"?, "zebraUsbName"?, "testOnly"?: bool }
→ 200 { "ok": true, "message": "已切换为 TCP（192.168.1.50:9100）。", "config": { "mode": ..., "params": ... } }
→ 200 { "ok": false, "message": "连接测试失败：...", "config": { ... } }   // 不切换，config = 当前生效连接
→ 400 { "code": "LF_TRANSPORT_INVALID", "message": "参数校验失败：...", "fieldKey": "tcpHost" }   // 沿用现有 ErrorView 形状
```

- **响应统一**：成功与失败（200）都返回 `config`，且 `config` 恒为**当前生效连接**（testOnly 未切换 → 当前生效连接；非 testOnly 切换成功 → 新连接；失败 → 未变前的连接）。`message` 为中文人话。
- **400 沿用现有 `ErrorView { code, message, fieldKey? }`**（`Api/Contracts.cs`），前端 `request()` 现有错误分支即可，无需两套解析。
- 校验规则：`Tcp` 必填 `tcpHost` + `tcpPort`（1-65535）；`WindowsDriver` 必填 `printerName`；`Zebra` + `Tcp` 必填 `tcpHost/tcpPort`、`Driver` 必填 `printerName`、`Usb` 的 `zebraUsbName` 可空（空 = 自动发现第一台）；`Log` 无参数。
- **先测试后生效**：非 `testOnly` 时后端先创建候选传输并测试——Tcp：TCP 连接（3 秒超时）；Zebra：SDK 连接测试；WindowsDriver：按名打开打印机；Log：恒成功。成功才切换 + 持久化；失败不切换、返回当前连接。
- `testOnly=true`：只测试不保存不切换（「测试连接」按钮）。
- **持久化**：写入 `%LOCALAPPDATA%\LabelFrame\connection.json`（用户数据目录，可写；不写 Program Files 的 appsettings.json，避免权限问题）。启动优先级：`connection.json` > `appsettings.json`（现有连接键保留为默认兜底）> 默认 Log。
- 运行时：`ITransportManager` 持有当前 `TransportConfig` 与 `IPrintTransport` 实例；作业 Worker、`/api/printer/status`、`/api/printer/test` 统一从 manager 取当前实例。**打印中切换**：允许切换，进行中的作业继续使用切换前的实例（旧实例引用保留至作业结束），新作业使用新连接。

### 4.2 调试模式（作业级）

```
POST /api/jobs  body 增加可选  "debug": bool（默认 false）
```

- **渲染时机：提交时预渲染**。`JobSubmissionService` 对每张 label 渲染 `LabelBitmap`：
  - 生成 `^GF` 指令存入作业项（与普通作业一致，保证 retry / 重启可重放）；
  - `debug=true` 时**同时**把位图转 PNG 落盘 `%LOCALAPPDATA%\LabelFrame\debug\{jobId}\label-{index+1}.png`，并在 Job 上持久化 `debugImagePaths`（相对路径数组）与 `debugImageDir`（完整目录）。
- **Worker**：debug 作业**不调用传输**（即使当前连接是真实打印机），逐张正常标记 Completed；日志记录「调试作业 {jobId}：已保存 N 张 PNG（{dir}）」。
- **生命周期：持久化**。`jobs` 表新增 `debug_images` 文本列（JSON：`{ "dir": "...", "paths": [...] }`），SQLite 做加列迁移（ALTER TABLE ADD COLUMN，幂等）。作业完成后重启 WinHost，`GET /api/jobs/{jobId}` 仍返回 `debugImagePaths` / `debugImageDir`。
- `JobView` 增加可选字段：`debugImagePaths?: string[]`、`debugImageDir?: string`。
- **重试**：`POST /api/jobs/{jobId}/items/{index}/retry` 对 debug 作业返回 400（`LF_JOB_DEBUG_NO_RETRY`，「调试作业无需重试，请重新提交」）——debug 作业项不发送，无重试语义。
- 保留现有单张调试能力：`POST /api/print/render-image`（当前表单直接出图，不建作业）继续可用。

## 5. 后端实施要点

### 5.1 会话无关（前端为主，见 §6.1）
（本节后端无改动）

### 5.2 连接管理
- 新增 `TransportConfig`（模式 + 参数，可 JSON 序列化到 connection.json）。
- 新增 `ITransportManager`：启动时按 `connection.json → appsettings → 默认 Log` 初始化；`ApplyAsync(config, testOnly)` 实现校验 → 测试 → 切换 → 持久化；`CurrentTransport` 供 Worker / 状态 / 测试页。
- 保留现有四种传输实现（Tcp9100 / RawPrinter / Zebra / Log），仅加管理壳；新增连接方式 = 扩展 `TransportMode` 枚举 + 工厂 + 前端选项（不搞插件化）。
- 健康检查 `transport` 显示当前生效 mode；`/api/printer/test` 用当前连接发送测试页（Log 模式保存 PNG）。

### 5.3 Log 连接语义
- Log 模式 = 模拟打印：Worker 渲染整版位图并保存 PNG 到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\`，host.log 记录「模拟打印（Log）：作业 {jobId} 已保存 N 张 PNG 到 {dir}」；不再写入 ZPL 文本。
- 与调试开关的关系：调试开关是作业级强制「只出图不发送」，在任意连接下生效；Log 本身就是不连真机。两者目录分开（`print\` 与 `debug\`）。

### 5.4 测试
- 删除：`ZplEncoderTests`、`ZplEncoderBitmapTests`、`GdiTextRasterizerTests`、Log 传输中 ZPL 文本相关断言。
- 新增/改写（后端）：`ZplImageEncoder` 编码测试；`JobSubmissionService` 恒图片打印断言；连接管理（校验 / 先测试后生效 / 失败回滚 / 持久化 connection.json / 启动优先级 / 400 ErrorView）；调试作业（提交时 PNG 落盘、不调用传输、`debugImagePaths` 持久化、重启后可查、retry 400）；Log 模拟打印（保存 PNG + 日志）。
- 新增（前端，hermes）：连接切换交互（测试 / 保存 / 失败回滚提示）、draft 保留逻辑（切 tab / 刷新 / 标签页隔离）、提交作业带 `debug` 参数、调试开关与「调试出图」按钮并存语义。
- AndroidHost 编译通过（真机验收待 PDA 联调）。

## 6. 前端实施要点（hermes）

### 6.1 DataPrint 会话保留
- 将 DataPrint 草稿状态提升到全局（`AppContext` 扩展 `printDraft`，或独立 store）；切 tab 不卸载 store。
- **保留范围**：`selectedName`、`valuesByTemplate`（按模板名分键）、调试开关、Excel `mapping`、当前 `jobId`（作业进度在切页后继续显示）。
- **values 合并语义**：每个模板独立维护 `values`（用户输入）与 `dirtyKeys`（本次会话中用户输入过的 key，含主动清空）；加载模板时 `values = { ...testData, ...用户 dirty 的 key }`——**按 key 是否存在合并**（非 truthy 合并），用户清空的字段不被 testData 顶回。
- **Excel 数据**：原始数据（`headers/rows/file`）**仅内存全局 store，不落 sessionStorage**（数千行可达数 MB，避免超限）；`mapping` 等轻量草稿可入 sessionStorage。刷新页面后 Excel 原始数据丢失，提示用户重新导入。
- 持久化可选 sessionStorage（刷新保留，且天然按标签页隔离）；**不使用 localStorage**。
- 模板切换/重载时按 6.1 的合并语义保留用户已填字段值。

### 6.2 连接切换 UI 与全局状态
- `AppContext` 增加 `transportConfig`（`GET /api/transport` 结果：mode + params），Settings 与 DataPrint 复用；**切换成功后前端立即用响应 `config` 更新全局状态**（不依赖 healthz 10s 轮询）；healthz 轮询仅作后端重启兜底。
- 状态栏 / DataPrint 顶部徽标显示 mode + 关键参数（如 `TCP 192.168.1.50` / `Zebra USB` / `WindowsDriver ZDesigner ZD421-203dpi ZPL` / `LOG`）。
- 设置页：新增「连接方式」分组——模式单选（Log / TCP / Windows驱动 / Zebra），只显示当前模式参数；按钮「测试连接」（testOnly）与「保存并应用」（先测试后生效，失败展示后端 message）；显示当前生效连接。
- DataPrint 页顶部：当前连接徽标 + 快速切换（模式下拉 + 当前模式参数内联，复用设置页逻辑；切换即「测试+应用」，失败回滚并提示）。
- `api` client 增加 `getTransport` / `setTransport` / `testTransport`；`Healthz` 移除 `printMode`。

### 6.3 调试独立与按钮并存语义
- DataPrint「调试」独立开关（默认关），说明文案「调试模式：只生成图片，不发送打印驱动」。
- **按钮并存规则**（避免两个“出图”入口混淆）：
  - 调试**开**：「打印测试（单张表单）」提交 `debug:true` 作业（出图不发送、作业推进至 Completed）；**隐藏「调试出图（当前表单）」按钮**（此时“打印测试”即出图）。
  - 调试**关**：「打印测试」正常发送到当前连接；「调试出图」保留为即时预览（renderImage，不建作业）。
- 调试作业进度区展示 `debugImageDir`（完整目录）与 PNG 张数；关闭调试后按当前连接真实打印。
- 删除打印方式下拉与旧调试复选框。

## 7. 不在范围

- 新传输协议（蓝牙等）**实现**：仅留扩展点（枚举 + 工厂注册）。
- Server 路由、模板 API、模板包格式既有契约不变；作业模型仅新增 `jobs.debug_images` 列与 `JobView.debugImagePaths/debugImageDir`、`SubmitJobRequest.debug`（`LabelJobItem` 列名不改，仅语义为 ^GF）。
- WPF Studio（冻结）不改。
- Android 真机打印效果验收（放 PDA 联调阶段）。

## 8. 验收标准

1. 数据与打印 → 设计器 → 返回：模板、字段值（含清空的字段）、调试开关、Excel 映射、作业进度全部保留；**另开一个标签页**改设置不影响本页（反向亦同）。
2. 设置页 / 数据与打印页可把连接从 Log 切到 TCP（填打印机 IP）：成功 → 状态栏 / 徽标 / healthz **立即**更新为 TCP，`connection.json` 生成；**改错 IP 保存失败** → 提示原因、连接保持原样；重启 WinHost 后连接仍是保存值。
3. 作业提交恒为图片打印：全链路无 `printMode` / Vector / ZPL 残留（配置、healthz、UI、README、demo 脚本）；Log 连接下作业保存 PNG 且作业完成。
4. 调试开关打开：20 行批量 → `debug\{jobId}\` 下 20 张 PNG，**驱动零发送**，作业 Completed 且 `debugImagePaths` / `debugImageDir` 可查（重启后仍可查）；关闭调试接真实打印机正常出纸。
5. `dotnet test` / `pnpm test` 全绿；AndroidHost 编译通过（真机验收待 PDA 联调）。
6. 重新构建 MSI 后可覆盖安装，`appsettings.json` 保留机制不受影响。

## 9. 分工与时序

- 后端（本仓库 AI）：§3.1 / §4 / §5（删除 ZPL、连接管理、调试作业、Log 语义、AndroidHost 图片打印、测试）。
- 前端（hermes）：§3.2 / §4（client 类型）/ §6（会话保留、连接切换 UI、调试独立）。
- 时序：hermes 评估本文档（意见已答复，见文末「附二」）；确认后两端并行；后端完成后可先出调试版 MSI，前端完成后合并联调。
- 完成定义：验收标准全满足 → 更新 ROADMAP / CHANGELOG / DESIGN（决策记录）；Conventional Commits；不推 tag。

---
## 附：审阅意见（hermes 追加，2026-08-10）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。评估基准：真实前端代码 `web/src`（DataPrint / Settings / AppContext / api client）+ 后端路由与契约代码（WinHost Program.cs / Api/Contracts.cs / Jobs / Core.Jobs）。仅就前端相关项发表意见；后端项只在与前端契约交叉处提及。

### 一、🔴 关键缺口

**1. debug 作业的「渲染 → PNG → debugImagePaths」链路缺契约定义（与 §7 冲突）**

- 依据：`LabelJobItem` 目前只持久化 Zpl 指令字符串（`src/LabelFrame.Core/Jobs/LabelJobItem.cs:19`，SQLite 落库 `SqliteLabelJobStore.cs:108`）；`JobPrintWorker` 无渲染能力，只 `SendAsync(item.Zpl)`（`JobPrintWorker.cs:58`）。§4.2 却要求「Worker 对每张标签渲染整版位图 → PNG 保存」。
- 问题：删除矢量 ZPL 后作业项存什么（^GF 字符串 / 渲染位图 / PNG 路径）？debug 作业的 PNG 由谁在什么时机渲染（提交时预渲染 vs Worker 渲染）？`JobView.debugImagePaths` 是否随 SQLite 持久化（作业完成后重启 WinHost，再查 JobView 是否仍返回 paths）？§7「作业模型既有契约不变」与 §4.2 所需存储变更冲突。
- 建议：明确选型（推荐：提交时渲染 PNG 落盘 + 作业项存路径；或 Worker 持渲染器按需渲染）并写明 debugImagePaths 生命周期（持久化 vs 仅进程内存）；前端据此确定「轮询期间逐张增长展示」还是「终态一次性展示」。

**2. 「调试开关 + 打印测试」与「调试出图（renderImage）」两入口并存时的 UI 语义未定义**

- 依据：现状 DataPrint 是单按钮二选一（`DataPrint.tsx:375`：`debugSave ? saveDebugImage : testPrint`）。
- 问题：新规格下调试开时「打印测试（单张）」提交 `debug:true` 作业（出图不发送、作业推进至 Completed），「调试出图」按钮走 renderImage（出图、不建作业）——两个入口行为重叠、文案歧义，用户无法预期「调试开关 + 打印测试」与「调试出图」的区别。
- 建议：明确二者并存规则（如调试开时隐藏/禁用「调试出图」按钮，或反之；按钮文案随调试开关联动），避免 UI 上同时出现两个「出图」入口。

### 二、🟡 规格空白与不一致

**3. POST /api/transport 的 400 错误响应形状未定义**

- §4.1 只写「→ 400 参数校验失败」。现有后端错误统一为 `ErrorView { code, message, fieldKey? }`（`Api/Contracts.cs:37`），前端 `request()` 按此解析（`client.ts:25-33`）。建议补一句「400 沿用 ErrorView 形状」，否则前端要写两套错误分支。

**4. values 保留的存储粒度未定义（含「用户清空」语义）**

- §6.1「模板切换/重载时保留用户已填字段值（与 testData 合并，用户值优先）」：若 values 是单一全局 map，模板 A→B→A 时 A 的值已被 B 覆盖，不满足「保留」；需按模板名分键（valuesByTemplate）。
- 另：「用户值优先」建议按 **key 是否存在**合并（而非 truthy 合并），否则用户主动清空的字段会被 testData 顶回。建议规格明确存储粒度与合并语义。

**5. 连接切换成功后前端状态刷新路径未定义（验收 2 存在最长 10s 延迟）**

- 依据：`app.transport` 来自 healthz 10s 轮询（`App.tsx:27-32`）。
- 问题：验收 2 要求「成功 → healthz / 状态栏 / 徽标更新为 TCP」；若仅靠轮询，切换后最长 10s 才反映。且徽标要显示 `TCP 192.168.1.50` 需要 params，healthz 只有 mode。
- 建议：明确前端在 setTransport 成功后主动刷新全局连接状态（重查 GET /api/transport 或 healthz），或将连接状态（mode + params）提升为 AppContext 全局状态、由切换动作直接更新。

**6. Excel 保留范围与 sessionStorage 体积风险**

- §6.1 只列 `mapping`；`excel.headers/rows/file` 与 jobId 未列。若只保留 mapping 不保留 excel 数据，「重新映射」按钮回来即失效；若 rows 全进 sessionStorage，数千行 × 多列可达数 MB（逼近 5MB 上限）。
- 建议：Excel 原始数据仅进内存全局 store、不落 sessionStorage；明确 jobId / 作业进度是否在保留范围。

**7. testOnly=true 成功响应的 config 语义未定义**

- §4.1 失败时返回 `current`（当前生效），成功时返回 `config`——testOnly 不切换时 config 是「候选」还是「当前」？前端只用 message，但建议写明避免后端二义。

**8. 文档 / 脚本残留未列入删除范围（验收 3「全链路无残留」会卡）**

- `README.md:58`（打印模式：默认矢量 ZPL 指令…）、`scripts/demo-winhost.ps1`（4/4 展示模拟打印机输出的 ZPL）仍描述 ZPL / PrintMode；删除后需同步更新（DESIGN / ROADMAP 的决策记录可保留为历史）。

### 三、💡 可选建议

**9. 连接状态全局化**：DataPrint 顶部徽标需要 params（IP / 打印机名）才能显示 `TCP 192.168.1.50` 样式；建议 AppContext 增 `transportConfig`（GET /api/transport 结果），Settings 与 DataPrint 复用，切换后统一刷新（配合 #5）。

**10. 前端测试项**：§5.4 只列后端测试；建议补前端（连接切换交互、draft 保留逻辑、submit 带 debug 参数），以支撑验收 5 的 pnpm test 全绿。

**11. debugImagePaths 展示**：前端只能拿到相对路径（`debug/{jobId}/label-1.png`），建议后端 message 或单独字段带完整目录（%LOCALAPPDATA%\LabelFrame\…），便于前端「目录提示」。

### 四、✅ 已核对通过（无需修改）

- TransportMode 枚举值（Log / Tcp / WindowsDriver / Zebra，`HostOptions.cs:7`）与 §4.1 mode 字符串一致；healthz.transport 保留且与 mode 同值，前端 healthz 类型仅删 printMode 即可。
- GdiTextRasterizer / IZplEncoder / ZplEncoder 仅 WinHost / AndroidHost / Core 引用（全仓 grep），Studio 不引用——§3.1 删除不破坏冻结的 Studio。
- §3.2 前端删除项覆盖完整：DataPrint printMode 下拉 + debugSave 复选框（`DataPrint.tsx:165,167,351-362`）、Settings serverPrintMode 提示（`Settings.tsx:20-29,151-157`）、Healthz.printMode / SubmitJobRequest.printMode（`types.ts:13,81`）。
- render-image 保留：前端 renderImage 调用不传 printMode（`DataPrint.tsx:243-247`），删字段无额外改动。
- 会话保留方向正确：App.tsx tab 为条件渲染（`App.tsx:73`），切 tab 组件卸载、现状状态全丢，提升全局 store 是必要且正确的；sessionStorage 天然按标签页隔离，D5 禁止 localStorage 正确。
- JobView / SubmitJobRequest 的契约增量（debug / debugImagePaths）与前端 types.ts 变更面一致。
- CORS（Program.cs AddCors / UseCors）与静态托管（SPA fallback）已具备，本次无新增基础设施需求。
- 验收 5 的 pnpm test 与 README 构建流程一致。

### 五、待审核者确认清单

1. #1 作业项存储方案与 debugImagePaths 生命周期（持久化 or 仅内存）
2. #2 调试开关与「调试出图」按钮的并存语义
3. #3 400 响应是否沿用 ErrorView
4. #4 values 按模板分键 + 按 key 存在性合并
5. #6 Excel 原始数据与 jobId 的保留范围
6. #8 README / demo 脚本残留清理


## 附二：审阅答复与定稿决策（主 agent / 后端，2026-08-10）

> 逐条答复 hermes 审阅意见；凡涉及规格正文的，已同步修订到正文（以正文为准）。

1. **debug 作业链路与存储（🔴#1）** → 定稿：**提交时预渲染**（JobSubmissionService 已有渲染器）：每张渲染 LabelBitmap → ① 生成 `^GF` 存作业项（与普通作业一致，列名沿用历史 `Zpl`，内容语义为“打印指令（^GF 位图）”，不做列迁移）；② debug 作业同时 PNG 落盘并持久化 `jobs.debug_images`（JSON：dir + paths，SQLite 幂等加列）。Worker 对 debug 作业不调用传输、逐张 Completed。`debugImagePaths/debugImageDir` **随 Job 持久化**，重启后仍可查。debug 作业 retry 返回 400（无重试语义）。→ 已写入 §3.1 / §4.2 / §5.4 / §7 / §8。
2. **调试开关与「调试出图」并存（🔴#2）** → 定稿：调试开 → 隐藏「调试出图」按钮，只留「打印测试」（提交 debug 作业出图）；调试关 → 「打印测试」真发送 + 「调试出图」保留为即时预览。→ 已写入 §6.3。
3. **400 响应形状（🟡#3）** → 确认沿用现有 `ErrorView { code, message, fieldKey? }`；成功/失败（200）统一返回 `config` = 当前生效连接。→ 已写入 §4.1。
4. **values 存储粒度与合并（🟡#4）** → 定稿：按模板分键 `valuesByTemplate` + `dirtyKeys`；合并按 **key 是否存在**（非 truthy），用户主动清空的字段不被 testData 顶回。→ 已写入 §6.1。
5. **连接状态刷新（🟡#5）** → 定稿：AppContext 增 `transportConfig`（GET /api/transport），切换成功后前端立即用响应 `config` 更新，不依赖 healthz 10s 轮询（轮询仅兜底重启）。→ 已写入 §6.2。
6. **Excel 保留与体积（🟡#6）** → 定稿：Excel 原始数据仅内存全局 store、**不落 sessionStorage**；sessionStorage 只存轻量草稿；`jobId`（作业进度）纳入保留范围。→ 已写入 §6.1。
7. **testOnly 响应语义（🟡#7）** → 定稿：响应统一 `config` = 当前生效连接（见 #3）。→ 已写入 §4.1。
8. **文档 / 脚本残留（🟡#8）** → 确认纳入删除范围：README 打印模式描述、scripts/demo-winhost.ps1 同步改为图片打印 / Log 模拟；DESIGN / ROADMAP 历史记录保留。→ 已写入 §3.1 / §8。
9. **连接状态全局化（💡#9）** → 采纳（同 #5）：AppContext.transportConfig，Settings 与 DataPrint 复用，徽标显示 mode + params。
10. **前端测试项（💡#10）** → 采纳：§5.4 补前端测试（连接切换交互、draft 保留、submit 带 debug、按钮并存语义）。
11. **debugImageDir 完整目录（💡#11）** → 采纳：JobView 增 `debugImageDir`（完整目录）供前端「目录提示」。

**已核对通过项**（hermes 第四节）无异议，直接采纳。

**结论**：意见全部吸收，规格正文已定稿；如前端对定稿无新异议，后端按 §5 开工，前端按 §6 并行。