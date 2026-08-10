# 迭代 15 规格：打印设置与会话保留 + 连接管理 + 图片打印收敛（删除 ZPL）

> 状态：规格评审中（2026-08-10，主 agent / 后端整理，交前端 hermes 评估；hermes 意见已审阅答复，用户已确认调试采用「后端渲染出图下载」设计，见文末「附二」）
> 协作：本文档定义前后端改动范围与 API 契约；hermes 评估前端部分无异议后，后端实施后端项、前端实施前端项（可并行）。字段名 / 接口以本文档为准。
> 背景：用户反复测试打印定位效果，提出三项优化；并已拍板**彻底删除 ZPL（Vector）打印路径**，统一为整版位图（Skia）图片打印。

---

## 1. 需求（用户原话归纳）

1. **数据与打印页会话保留**：切换到设计器再回来，不希望重新设置「标签（模板 / 字段值）+ 打印方式」。不接受开两个页面；同一标签页内切换视图必须保留设置；**两个浏览器标签页之间不互通**（一个页面改设置，另一个页面不得跟随）。
2. **前端切换连接方式**：在 Web 前端把连接从 Log 切换为真实打印机（TCP / Windows 驱动 / Zebra），并可添加新的连接方式；**同一时间只有一个连接方式生效**；**不为未连接的连接方式做支持**（只维护当前生效方式的参数）。
3. **去掉 ZPL（Vector）打印方式**：打印统一为图片（Skia 渲染整版位图经 `^GF` 直传打印机），前后端同一渲染逻辑保证一致性。另：**调试功能独立出来**——调试模式开启时，直接打印图片（渲染后浏览器下载 / 查看 PNG），**不发送给打印驱动**。
4. DataPrint 会话保留范围（用户已确认）：选中的模板 + 已填字段值 + 调试开关 + 作业进度；**Excel 导入数据与列映射不保留**（切页/刷新后丢弃，重新上传即可）。

## 2. 已确认决策（用户拍板 + 审阅答复定稿）

- **D1 彻底删除 ZPL/Vector**：PC 端（WinHost / Web / 作业 / 配置 / 健康检查 / 测试）不再存在矢量 ZPL 路径；图片打印的物理载体 `^GF` 位图编码保留（重构为独立的图片编码器）。文档 / 脚本残留一并清理（README、scripts/demo-winhost.ps1；DESIGN / ROADMAP 历史记录保留）。
- **D2 连接管理**：默认连接 Log；前端可切换并「先测试、后生效」，测试失败自动回滚并提示；切换结果持久化（重启保留）；同一时间单一连接。
- **D3 连接 UI 位置**：设置页做完整管理；「数据与打印」页顶部放当前连接状态与快速切换。
- **D4 调试独立 + 后端渲染兜底**：调试为独立开关（不再依附「图片打印方式」复选框）；开启后「打印测试 / 批量打印」**不建作业、不发驱动**，由**后端渲染**出图（单张 PNG / 批量 zip）供浏览器下载——**后端是渲染兜底，调试所见 = 打印所出**（同一 Skia 渲染、同 DPI、同 LabelBitmap → 打印机 ^GF 位图）。
- **D5 会话保留实现**：DataPrint 草稿状态提升到全局（内存 / sessionStorage），**禁止 localStorage**（避免跨标签页共享）；Excel 原始数据与列映射不保留。

## 3. 删除项（后端为主，前端联动）

### 3.1 后端 / API / 文档脚本
- 删除 `PrintMode` 枚举（Vector / Image）及 `WinHost.PrintMode` 配置、`LABELFRAME_PRINT_MODE` 环境变量、`packaging/appsettings.json` 中的 `PrintMode`。
- 删除 `SubmitJobRequest.printMode`、`/healthz` 响应的 `printMode` 字段（保留 `transport`）。
- 删除矢量 ZPL 编码：`IZplEncoder`、`ZplEncoder.Encode`（`^A` / `^FB` / `^BC` / `^BQ` 等）、`ZplBoldMode`、`WinHost.BoldMode` 配置与 `LABELFRAME_BOLD_MODE`（**`LabelTextElement.Bold` 属性保留**，Skia 图片渲染继续用 `SKFont.Embolden` 实现加粗）。
- **保留并重构** `^GF` 位图编码：`ZplEncoder.EncodeImage` 抽为独立 `ZplImageEncoder.EncodeImage(LabelBitmap, widthMm, heightMm, dpi)`（图片打印的物理载体）。
- 删除 WinHost 对 `ZplEncoder` / `ITextRasterizer` / `GdiTextRasterizer` 的依赖（Image 模式不需要文本→`^GF` 替换，Skia 直接渲染中文）；`GdiTextRasterizer` 及测试删除。
- `JobSubmissionService`：恒走「Skia 渲染整版位图 → `ZplImageEncoder.EncodeImage`」；`ILabelBitmapRenderer` 保留。**作业项内容统一为 `^GF` 整版位图指令**，`LabelJobItem` 存储字段沿用现有列名（历史命名 `Zpl`，**不改列名 / 不迁移**，文档注明内容语义为“打印指令（^GF 位图）”）。
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

### 4.2 调试模式（后端渲染出图，不建作业、不发驱动）

- 调试为**独立开关**（前端，默认关），与连接方式无关：开启后「打印测试 / 批量打印」**不再提交作业**，改为**后端渲染出图并下载**；关闭后走正常作业流程（发送到当前连接）。
- **单张**：复用现有 `POST /api/print/render-image`（body = SubmitJobRequest 形态，单张 labels），返回 PNG（Content-Disposition 文件名 `label-{index+1}.png`）。
- **批量**：新增 `POST /api/print/render-images`，body = SubmitJobRequest（labels[] 多张），返回 **zip**（内含 `label-{index+1}.png`，序号与 labels 一一对应）；zip 文件名建议 `{templateName}-debug-{yyyyMMddHHmmss}.zip`。
- 渲染一律走后端 Skia（与发送给打印机的位图**同源、同 DPI、同 LabelBitmap**），**后端是渲染兜底**：调试看到的图 = 打印机会打的图。
- 错误统一 `ErrorView`（模板缺字段、渲染失败等，与作业提交同一套校验）。
- **不建作业、不入队、不发驱动、不改作业模型 / SQLite**：无 `debug` 作业字段、无 `debugImagePaths`、无数据库迁移、无重试规则。

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
- 与调试开关的关系：调试开关是「不建作业、渲染出图下载」，在任意连接下生效；Log 本身就是不连真机。两者互不依赖。

### 5.4 测试
- 删除：`ZplEncoderTests`、`ZplEncoderBitmapTests`、`GdiTextRasterizerTests`、Log 传输中 ZPL 文本相关断言。
- 新增/改写（后端）：`ZplImageEncoder` 编码测试；`JobSubmissionService` 恒图片打印断言；连接管理（校验 / 先测试后生效 / 失败回滚 / 持久化 connection.json / 启动优先级 / 400 ErrorView）；`render-image`（单张 PNG）与 `render-images`（批量 zip、序号对应、错误 ErrorView）；Log 模拟打印（保存 PNG + 日志）。
- 新增（前端，hermes）：连接切换交互（测试 / 保存 / 失败回滚提示）、draft 保留逻辑（切 tab / 刷新 / 标签页隔离、Excel 不保留）、调试开关下「打印测试 / 批量打印」的按钮行为与下载（单张 PNG / zip）。
- AndroidHost 编译通过（真机验收待 PDA 联调）。

## 6. 前端实施要点（hermes）

### 6.1 DataPrint 会话保留
- 将 DataPrint 草稿状态提升到全局（`AppContext` 扩展 `printDraft`，或独立 store）；切 tab 不卸载 store。
- **保留范围**：`selectedName`、`valuesByTemplate`（按模板名分键）、调试开关、当前 `jobId`（作业进度在切页后继续显示）。
- **不保留**：Excel 导入数据（`headers/rows/file`）与列映射——切页 / 刷新后丢弃，用户重新上传（用户已确认「丢了就丢了」）。
- **values 合并语义**：每个模板独立维护 `values`（用户输入）与 `dirtyKeys`（本次会话中用户输入过的 key，含主动清空）；加载模板时 `values = { ...testData, ...用户 dirty 的 key }`——**按 key 是否存在合并**（非 truthy 合并），用户清空的字段不被 testData 顶回。
- 持久化可选 sessionStorage（刷新保留，且天然按标签页隔离）；**不使用 localStorage**。

### 6.2 连接切换 UI 与全局状态
- `AppContext` 增加 `transportConfig`（`GET /api/transport` 结果：mode + params），Settings 与 DataPrint 复用；**切换成功后前端立即用响应 `config` 更新全局状态**（不依赖 healthz 10s 轮询）；healthz 轮询仅作后端重启兜底。
- 状态栏 / DataPrint 顶部徽标显示 mode + 关键参数（如 `TCP 192.168.1.50` / `Zebra USB` / `WindowsDriver ZDesigner ZD421-203dpi ZPL` / `LOG`）。
- 设置页：新增「连接方式」分组——模式单选（Log / TCP / Windows驱动 / Zebra），只显示当前模式参数；按钮「测试连接」（testOnly）与「保存并应用」（先测试后生效，失败展示后端 message）；显示当前生效连接。
- DataPrint 页顶部：当前连接徽标 + 快速切换（模式下拉 + 当前模式参数内联，复用设置页逻辑；切换即「测试+应用」，失败回滚并提示）。
- `api` client 增加 `getTransport` / `setTransport` / `testTransport` / `renderImages`；`Healthz` 移除 `printMode`。

### 6.3 调试独立与按钮语义
- DataPrint「调试」独立开关（默认关），说明文案「调试模式：只生成图片，不发送打印驱动（后端渲染）」。
- **按钮语义**（避免两个“出图”入口混淆）：
  - 调试**开**：「打印测试（单张表单）」→ 调 `render-image`，浏览器下载 1 张 PNG；「批量打印」→ 调 `render-images`，下载 zip（全部行）；**隐藏「调试出图（当前表单）」按钮**（与调试开时的打印测试重复）。
  - 调试**关**：「打印测试」正常提交作业到当前连接；「批量打印」正常批量作业；「调试出图」保留为即时预览（renderImage，不建作业）。
- 删除打印方式下拉与旧调试复选框；下载文件名为后端 Content-Disposition 提供值。
- **按钮文案随调试开关联动**（hermes 附三 UX 细节，已采纳）：调试**开**时「打印测试（单张）」文案显示「调试出图（单张）」，「批量打印 N 张」显示「下载调试图片 zip（N 张）」；作业进度区显示「调试模式：不提交作业，出图已下载」提示（不建作业、无进度可等）。调试**关**时「调试出图（当前表单）」按钮改名「出图预览」（即时预览，避免关闭调试后仍带「调试」字样）。

## 7. 不在范围

- 新传输协议（蓝牙等）**实现**：仅留扩展点（枚举 + 工厂注册）。
- **作业模型不改**：本轮不新增 debug 作业字段、不改 SQLite 结构（`LabelJobItem` 列名不改，仅语义为 ^GF 打印指令）。
- Server 路由、模板 API、模板包格式既有契约不变。
- WPF Studio（冻结）不改。
- Android 真机打印效果验收（放 PDA 联调阶段）。

## 8. 验收标准

1. 数据与打印 → 设计器 → 返回：模板、字段值（含清空的字段）、调试开关、作业进度全部保留；Excel 数据与映射不保留（重新上传）；**另开一个标签页**改设置不影响本页（反向亦同）。
2. 设置页 / 数据与打印页可把连接从 Log 切到 TCP（填打印机 IP）：成功 → 状态栏 / 徽标 / healthz **立即**更新为 TCP，`connection.json` 生成；**改错 IP 保存失败** → 提示原因、连接保持原样；重启 WinHost 后连接仍是保存值。
3. 作业提交恒为图片打印：全链路无 `printMode` / Vector / ZPL 残留（配置、healthz、UI、README、demo 脚本）；Log 连接下作业保存 PNG 且作业完成。
4. 调试开关打开：单张「打印测试」下载 1 张 PNG；20 行「批量打印」下载 zip（20 张 PNG），**驱动零发送、不产生作业**；调试关后接真实打印机正常出纸。
5. `dotnet test` / `pnpm test` 全绿；AndroidHost 编译通过（真机验收待 PDA 联调）。
6. 重新构建 MSI 后可覆盖安装，`appsettings.json` 保留机制不受影响。

## 9. 分工与时序

- 后端（本仓库 AI）：§3.1 / §4 / §5（删除 ZPL、连接管理、调试渲染出图、Log 语义、AndroidHost 图片打印、测试）。
- 前端（hermes）：§3.2 / §4（client 类型）/ §6（会话保留、连接切换 UI、调试独立）。
- 时序：本文档定稿（用户确认 + hermes 无新异议）后两端并行；后端完成后可先出调试版 MSI，前端完成后合并联调。
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


## 附二：审阅答复与定稿决策（主 agent / 后端，2026-08-10；用户确认修订 2026-08-10）

> 逐条答复 hermes 审阅意见；凡涉及规格正文的，已同步修订到正文（以正文为准）。

1. **debug 作业链路与存储（🔴#1）** → **设计变更（用户确认）**：调试不再走作业链路，改为「**后端渲染出图、浏览器下载**」——单张 `render-image` 返回 PNG，批量新增 `render-images` 返回 zip；**不建作业、不入队、不发驱动、不改作业模型 / SQLite**（无 debug 字段、无 debugImagePaths、无迁移、无重试规则）。hermes 提出的存储链路问题随设计取消。后端渲染保证「调试所见 = 打印所出」（同一 Skia / DPI / LabelBitmap）。→ 已写入 §2 D4 / §4.2 / §5.4 / §6.3 / §7 / §8。
2. **调试开关与「调试出图」并存（🔴#2）** → 定稿：调试开 → 隐藏「调试出图」按钮，「打印测试 / 批量打印」即为出图下载（PNG / zip）；调试关 → 「打印测试 / 批量打印」正常作业 + 「调试出图」保留为即时预览。→ 已写入 §6.3。
3. **400 响应形状（🟡#3）** → 确认沿用现有 `ErrorView { code, message, fieldKey? }`；成功/失败（200）统一返回 `config` = 当前生效连接。→ 已写入 §4.1。
4. **values 存储粒度与合并（🟡#4）** → 定稿：按模板分键 `valuesByTemplate` + `dirtyKeys`；合并按 **key 是否存在**（非 truthy），用户主动清空的字段不被 testData 顶回。→ 已写入 §6.1。
5. **连接状态刷新（🟡#5）** → 定稿：AppContext 增 `transportConfig`（GET /api/transport），切换成功后前端立即用响应 `config` 更新，不依赖 healthz 10s 轮询（轮询仅兜底重启）。→ 已写入 §6.2。
6. **Excel 保留与体积（🟡#6）** → **范围收敛（用户确认）**：Excel 导入数据与列映射**不保留**（切页/刷新后丢弃，重新上传），会话保留范围 = 模板选择 / 字段值 / 调试开关 / 作业进度；体积问题自然消失。→ 已写入 §1.4 / §6.1。
7. **testOnly 响应语义（🟡#7）** → 定稿：响应统一 `config` = 当前生效连接（见 #3）。→ 已写入 §4.1。
8. **文档 / 脚本残留（🟡#8）** → 确认纳入删除范围：README 打印模式描述、scripts/demo-winhost.ps1 同步改为图片打印 / Log 模拟；DESIGN / ROADMAP 历史记录保留。→ 已写入 §3.1 / §8。
9. **连接状态全局化（💡#9）** → 采纳（同 #5）：AppContext.transportConfig，Settings 与 DataPrint 复用，徽标显示 mode + params。
10. **前端测试项（💡#10）** → 采纳：§5.4 补前端测试（连接切换交互、draft 保留、调试开关下按钮行为与下载）。
11. **debugImageDir 完整目录（💡#11）** → **取消**：调试不再产生作业与磁盘目录，改为浏览器下载，无需完整目录字段。

**已核对通过项**（hermes 第四节）无异议，直接采纳。

**结论**：调试设计改为「后端渲染出图下载（PNG / zip）」，hermes #1 / #11 取消、#6 收敛；其余意见全部吸收。规格正文已定稿，等待 hermes 对定稿的最终确认。

## 附三：定稿确认与补充建议（hermes 追加，2026-08-10）

> 供审核者存档；本节保留作为审阅记录，不视为规格正文。已完整重读定稿正文（含附二答复），逐条核对第一轮 11 项意见落实：

- ✅ #1 / #11：设计变更（调试改为后端渲染出图下载）已写入 §2 D4 / §4.2 / §5.4 / §6.3 / §7 / §8；原存储链路问题随设计取消，无遗留。
- ✅ #2：按钮语义定稿（§6.3）——调试开隐藏「调试出图」、「打印测试 / 批量打印」即出图下载；调试关保留即时预览。无异议。
- ✅ #3 / #7：§4.1 已明确 400 沿用 ErrorView、200 成功 / 失败统一 `config` = 当前生效连接。无异议。
- ✅ #4：§6.1 valuesByTemplate + dirtyKeys、按 key 存在性合并。无异议。
- ✅ #5 / #9：§6.2 transportConfig 全局状态、切换成功后立即用响应 config 更新、不依赖 healthz 10s 轮询。无异议。
- ✅ #6：Excel 数据与列映射不保留（用户确认），会话保留范围 = 模板 / 字段值 / 调试开关 / 作业进度，sessionStorage 体积风险自然消失。无异议。
- ✅ #8：README / demo 脚本残留清理已纳入 §3.1 / §8。
- ✅ #10：前端测试项已补入 §5.4。
- ✅ 第一轮「已核对通过项」（第四节）：无异议。

修订质量检查（第二轮）：正文无编号重排、无旧语义残留（printMode / debugImagePaths 仅出现在删除声明与历史审阅记录）；`render-images` 新契约在 §4.2 / §5.4 / §6.2 / §6.3 / §8 五处一致；§7「作业模型不改」与 §3.1「LabelJobItem 列名不改、仅语义为 ^GF 打印指令」自洽。

**结论：无新异议，规格可定稿，两端可开工。**

以下 3 点为实施期前端将自行落实的 UX 细节（不阻塞定稿、无需改正文；若用户对文案有偏好可拍板）：

1. **按钮文案随调试开关联动**（§6.3 只定义了行为未定义文案）：调试开时「打印测试（单张）」建议显示「调试出图（单张）」、「批量打印 N 张」建议显示「下载调试图片 zip（N 张）」，避免按钮叫「打印」实际却是下载图片。
2. **「调试出图（当前表单）」按钮命名**（调试关时保留为即时预览）：建议改名「出图预览」，避免调试已关闭时按钮仍带「调试」字样。
3. **调试开时的作业进度区**：不建作业，进度区建议显示「调试模式：不提交作业，出图已下载」类提示，避免用户等待进度。


## 附四：定稿确认审阅（主 agent / 后端，2026-08-10）

> 对 hermes「附三：定稿确认与补充建议」的审阅记录。

- **结论：通过**。hermes 已逐条核对第一轮 11 项意见与定稿正文一致，无新异议；规格定稿，两端可开工。
- **采纳 3 条 UX 细节**（已写入 §6.3）：① 按钮文案随调试开关联动（调试开 →「调试出图（单张）」/「下载调试图片 zip（N 张）」）；② 调试关时「调试出图」按钮改名「出图预览」；③ 调试开时作业进度区显示「调试模式：不提交作业，出图已下载」。
- 无其余修改意见；正文维持 commit 71eb445 定稿状态（仅 §6.3 增加 UX 文案说明）。

**下一步**：后端按 §5 开工（删除 ZPL 链路 → 连接管理 /api/transport + connection.json → render-image/render-images → Log 模拟打印 → AndroidHost 图片打印 → 测试）；前端（hermes）按 §6 并行开工。


## 附五：后端实施记录（2026-08-10）

- 删除矢量 ZPL 全链路（`IZplEncoder` / `ZplEncoder.Encode` / `ZplBoldMode` / `PrintMode` / `printMode` / `ITextRasterizer` / `GdiTextRasterizer` + 测试）；`^GF` 编码重构为 `ZplImageEncoder`；作业项统一存整版位图指令（沿用列名 `Zpl`，无迁移）。
- 连接管理：`ITransportManager` / `TransportConfig`；`GET/POST /api/transport`（400 沿用 ErrorView；200 统一 `config`=当前生效连接；先测试后生效、失败回滚；持久化 connection.json；启动优先级 connection.json > appsettings > 默认 Log）；Tcp / Raw / Zebra 增加连接测试；Worker / 状态 / 测试页统一取当前连接；测试页改为 Skia 渲染 ^GF。
- 调试出图：`POST /api/print/render-images`（批量 zip）；`render-image` 保留（单张 PNG）；不建作业、不发驱动、不改作业模型 / SQLite。
- Log 模拟打印：摘要日志 + PNG 保存到 `print\{jobId}\`。
- AndroidHost：`AndroidLabelRenderer`（Android.Graphics + ZXing）→ `ZplImageEncoder`，替换 ZplEncoder。
- 测试 143 全绿（Core 60 / Server 8 / Studio 25 / WinHost 50）；AndroidHost 编译通过。
- 前端（hermes）待实施：§6.1 会话保留、§6.2 连接切换 UI、§6.3 调试独立与按钮语义。
## 附六：前端联调观察与交付说明（hermes 追加，2026-08-10）

> 前端实施完成、与后端工作区实现联调后的观察记录；本节保留为记录，不视为规格正文。

1. **TCP 连接测试对不可达地址判定成功（建议后端复核）**：浏览器实测 `POST /api/transport`（mode=Tcp，tcpHost=10.255.255.1:9100，不可达）返回 `ok:true` 并完成切换与持久化（connection.json 落盘）。前端按契约忠实执行（ok:true → 应用响应 config）；判定逻辑在后端——疑为 TCP 连接测试未按 §4.1 的 3 秒超时 / 失败判定执行。建议后端以真实不可达地址复核 §5.2 连接管理测试路径。
2. 前端交付范围确认：§3.2 删除项、§4 client 类型（getTransport / setTransport / testTransport / renderImages）、§6.1-6.3 全部落地；`pnpm test` 91 全绿（新增 27 个）、`pnpm build` / `pnpm lint` 通过；与后端工作区实现联调通过（GET/POST /api/transport、render-image、render-images），徽标 / 切换 / 回滚 / 下载均实测通过。

## 附七：审阅答复——TCP 连接测试判定复核（主 agent / 后端，2026-08-10）

针对 hermes 附六第 1 条（Tcp 不可达地址 10.255.255.1:9100 返回 `ok:true` 并切换持久化）：

- **后端判定逻辑核对**：`TransportManager.ApplyAsync` 顺序 = `Validate`（参数校验）→ `CreateTransport` → `TestAsync`（Tcp → `Tcp9100PrintTransport.TestConnectionAsync`：TCP 三次握手 + 3 秒 `CancelAfter` 超时，任何异常返回 false）→ 失败不切换、成功才切换并持久化 connection.json。与 §4.1「先测试后生效」一致。
- **「可达」语义**：当前判定 = TCP 连接（三次握手）成功即视为可达。若目标 IP 在测试网段内存在可响应 9100 的监听（网关 / 其它设备 / 打印机），连接成功属正常；若指应用层无打印机响应，TCP 连接成功 ≠ 打印机就绪，需要进一步区分（后续可考虑发送 `~HS` 探测，本轮不做）。
- **加固（本次合入）**：① `TestConnectionAsync` 对 IP 字面量走 `IPAddress` 直连路径（避免 DNS 差异）；② 新增回归测试：本地 `TcpListener` 开启时返回 true、关闭后返回 false，确保成功 / 失败两向判定稳定。
- 若仍复现（目标 IP 确认无监听且 host.log 显示已切换），请提供 host.log 时间戳与目标网段说明，再以真实不可达地址复测。


## 附八：二次审阅答复——用户实测反馈（主 agent / 后端，2026-08-10）

针对用户实测（USB 打印机配 Zebra/TCP 测试通过但打印失败；LOG 模拟打印看不到输出）：

1. **「连接成功」的判定升级**：原测试只验证 TCP 三次握手（能连端口 ≠ 打印机），已升级为「连接 + `~HS` 主机状态探测」——TCP 与 Zebra 都改为发送 `~HS` 并等待打印机响应，无响应判定失败（不切换、不持久化）。Windows 驱动模式仍以「能打开打印机」为准（无读回）。
2. **LOG 输出位置与可见性**：Log 模拟打印 PNG 保存到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\label-N.png`（host.log 有摘要）。此前 UI 未提示路径，已把 `printImageDir` / `printImageCount` 附到作业视图（JobView），前端作业进度区显示「模拟打印图片（Log）：<目录>（N 张）」。
3. **用户环境注意**：本机 `%LOCALAPPDATA%\LabelFrame\connection.json` 留存了此前保存的 WindowsDriver 连接（真实打印机配置）——当前生效连接不是 Log，因此看不到模拟打印输出；在 Web 设置页 / 数据与打印页切到 Log 即可看到 PNG。升级后旧的 Zebra/TCP 误判配置不会再被接受（~HS 探测）。
4. 端到端验证（本机 Log 模式）：提交作业 → 响应与查询均返回 `printImageDir`，`label-1.png` 落盘（1 张）。


## 附九：前端修复任务单（hermes 实施，2026-08-10）——PDA 远程访问失败：baseUrl 默认值写死 127.0.0.1

> 后端只出文档，前端由 hermes 照此实施；实施并 push 后合入再打新 MSI（0.13.2）。

### 现象
- PDA 浏览器打开 `http://192.168.1.3:53960` 页面能正常加载，但点击「打印测试 / 批量打印」服务端无任何作业/日志。
- PC 上打开同一页面操作正常。

### 根因
- `web/src/lib/api/types.ts`：`export const DEFAULT_BASE_URL = 'http://127.0.0.1:53960'`
- `web/src/lib/settings.ts` `getBaseUrl()`：无存储值时返回 `DEFAULT_BASE_URL`。
- 页面加载走浏览器地址栏（`192.168.1.3:53960`）与 SPA 内部 `fetch` 的 baseUrl 无关：PDA 上所有 API 请求发往 **PDA 自身的 127.0.0.1:53960** → 全部失败（连接状态灯显示「未连接」）。

### 修复要求（前端）
1. **`getBaseUrl()` 默认值改为页面自身来源**：
   - 有存储值 `labelframe.baseUrl` 时仍优先（设置页覆盖行为不变，`setBaseUrl` 不变）。
   - 无存储值时返回 `window.location.origin`（如 `http://192.168.1.3:53960`；PC 打开时即 `http://127.0.0.1:53960`，行为一致）。
   - 需要 `typeof window !== 'undefined'` 守卫（Node 测试环境无 window 时回退 `DEFAULT_BASE_URL`）；返回前去掉尾部 `/`（沿用现有 `.replace(/\/+$/, '')`）。
2. **存储残留自动纠正（方案 B，已拍板）**：`getBaseUrl()` 增加判定——「存储值（去尾部斜杠）== `DEFAULT_BASE_URL` 且 `window.location.origin` ≠ 该默认值」时**忽略存储值**（视为旧版设置的残留，返回 origin）。PC（origin 即 `http://127.0.0.1:53960`）不触发；显式非默认存储值仍优先（跨机访问等场景保留，设置页可覆盖）。
3. **新建 `settings.test.ts`**（全仓当前无 settings 单测，非「同步调整」），用例：
   - 无存储值 → 返回 `window.location.origin`（jsdom 下用变量断言，不写死 `http://localhost:3000`）；
   - 有存储值 → 返回存储值；
   - 存储值 == 默认且 origin ≠ 默认（方案 B）→ 忽略存储值、返回 origin；
   - 无 window（Node）→ 回退 `DEFAULT_BASE_URL`；
   - 尾部 `/` 归一化。
   - 实施提示（hermes 附十第 3 条，已采纳）：`@vitest-environment jsdom` pragma；`vitest.setup.ts` 内存 Storage 为跨文件单例，用例先 `removeItem` 清 key 防污染。
4. **不改后端 / CORS**（WinHost 已启用宽松 CORS；页面与 API 同源后无跨域问题）。

### 验收
- PDA 打开 `http://192.168.1.3:53960`：连接状态灯「已连接」；数据与打印 → 打印测试：服务端（Log 模拟 / 真实打印机）产生输出（Log 时作业进度显示图片目录）。
- **残留场景**：PDA 曾保存过默认地址（127.0.0.1）的浏览器，修复后无需手动改设置页，方案 B 自动忽略残留、按 origin 连接。
- PC 打开 `http://127.0.0.1:53960` 行为不变。
- `pnpm test` / `pnpm build` / `pnpm lint` 全绿。
- **备注**：验收与文档统一用 `127.0.0.1` 打开（`localhost` 在 Windows 可能优先解析 `::1`，行为不承诺）。

### 备注（后端已完成，不需 hermes 处理）
- 本地 UI 打开地址规范化（`ToLocalUiUrl`：`0.0.0.0` 监听时浏览器/托盘跳 `127.0.0.1`）已在后端提交 `1b9df9a`。
- 连接测试 `~HS` 探测、Log 模拟打印目录展示等已合入。




## 附十：前端修复任务单审阅意见（hermes 追加，2026-08-10）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。

### 已核对通过（附代码依据）
- 根因事实全部属实：`web/src/lib/api/types.ts:6` 的 `DEFAULT_BASE_URL`、`web/src/lib/settings.ts:16-20` 的 `getBaseUrl()` 无存储值回退、存储 key `labelframe.baseUrl`（settings.ts:6）、`setBaseUrl` 行为（settings.ts:22-25）。
- 修复面完整：全仓 API 请求统一走 `web/src/lib/api/client.ts` 的 `getBaseUrl()`（3 处 fetch），无其他硬编码后端地址；healthz 轮询 / 连接状态灯同路径，改默认值即整体生效。
- `typeof window` 守卫与现有 `getLocalStorage`（settings.ts:8-14）模式一致；`window.location.origin` 无尾部斜杠，沿用 `.replace(/\/+$/, '')` 幂等无副作用。
- 「不改后端 / CORS」结论成立：WinHost 宽松 CORS 已启用，页面与 API 同源后无跨域。
- 备注引用的 `1b9df9a` 真实存在：`src/LabelFrame.WinHost/Program.cs:593-603` `ToLocalUiUrl`（0.0.0.0 / * / + / :: / [::] → 127.0.0.1）。
- 存储按 origin 隔离、设计自洽：PDA（192.168.1.3 origin）无存储值 → 默认返回 origin；PC（127.0.0.1 origin）有 / 无存储值均正确。
- `AppContext.changeBaseUrl`（web/src/state/AppContext.tsx:116-122）保存后重读 `getBaseUrl()`，不受影响；`Settings.test.tsx` 不依赖默认地址。

### 待审核者确认
1. **「现有依赖 DEFAULT_BASE_URL 的用例」不存在**：全仓 grep 仅 `types.ts` / `settings.ts` 两处引用，`web/src` 下无 `settings.test.ts`。任务单第 2 条实为**新建** settings 单测而非「同步调整」，建议措辞改为「新建 settings 测试（含以下用例）」。
2. **存储值 = 127.0.0.1 的残留场景，PDA 验收可能不成立**：设置页保存是纯 localStorage 操作、不依赖 API——PDA 用户在旧版打开过设置页（输入框默认显示 127.0.0.1）并点过保存，则存储值优先 → 修复后 PDA 依旧连自身回环失败，「PDA 打开 → 已连接」验收对该类用户不成立。需拍板：
   - 方案 A：保持「存储值优先」（简单，残留用户手动改设置页）；
   - 方案 B：`getBaseUrl()` 加一条「存储值 == DEFAULT_BASE_URL 且 `window.location.origin` ≠ 该值 → 忽略存储值」——自动纠正明显错误配置；PC 用户（origin 即 127.0.0.1:53960）不受影响。
3. **vitest 环境写法（不阻塞定稿，实施提示）**：默认环境 node（web/vitest.config.ts），「有 / 无存储值」两用例需 `// @vitest-environment jsdom` pragma；jsdom 下 `window.location.origin` 为 `http://localhost:3000`，断言应引用变量而非写死；`vitest.setup.ts` 的内存 Storage 桩为模块级单例跨文件共享，用例需先 `removeItem` 清 key 防污染。

### 可选建议（不阻塞）
- **localhost 打开的行为变化**：后端默认仅监听 `127.0.0.1`（`src/LabelFrame.WinHost/HostOptions.cs:26`、`packaging/appsettings.json`）。未存设置的用户若用 `http://localhost:53960` 打开页面，改后 API 请求发往 localhost（Windows 上解析优先 ::1，依赖浏览器 IPv4 回退兜底）——建议验收与文档统一用 127.0.0.1 打开。
- `Settings.tsx:86/103` 的占位与提示文案（默认 127.0.0.1）在存储覆盖语义下仍准确，无需改；若采纳方案 B 可加一句 PDA 提示，非必须。
- file:// 打开 dist 的开发者场景 origin 为 "null"，原本也无后端可用，忽略即可。

### 待审核者确认清单
1. 任务单第 2 条是否改为「新建 settings 测试」？
2. 存储值 = 127.0.0.1 残留场景：选方案 A 还是方案 B（或另定）？
3. 是否在任务单补充「验收用 127.0.0.1 打开，localhost 场景不承诺」备注？

结论：任务单方向正确、事实准确、修复面完整，可照常实施；仅 #2 为影响验收成立面的决策点。


## 附十一：审阅答复与任务单定稿（主 agent / 后端，2026-08-11）

> 对 hermes「附十：前端修复任务单审阅意见」的逐条答复；附九已按此定稿更新（以正文为准）。

1. **第 1 条（措辞）→ 确认**：任务单第 2 条改为「**新建** `settings.test.ts`」，删除「现有依赖 DEFAULT_BASE_URL 的用例同步调整」措辞（全仓确认无 settings 单测）。
2. **第 2 条（存储残留决策）→ 拍板方案 B**：`getBaseUrl()` 增加「存储值（去尾部斜杠）== `DEFAULT_BASE_URL` 且 `window.location.origin` ≠ 该默认值 → 忽略存储值、返回 origin」的自动纠正。理由：单机模式主场景下页面来源即服务端；PDA 旧版保存过默认值的残留会自动修复，避免用户困惑；PC（origin=127.0.0.1:53960）不触发；显式非默认存储值仍优先（跨机访问保留，设置页仍可覆盖）。
3. **第 3 条（vitest 实施提示）→ 采纳**：jsdom pragma、origin 用变量断言、`vitest.setup.ts` 内存 Storage 单例跨文件共享需 `removeItem` 清 key 防污染，均写入附九实施提示。
4. **可选建议 → 采纳**：验收与文档统一用 `127.0.0.1` 打开（`localhost` 在 Windows 可能优先解析 `::1`，行为不承诺），已写入附九验收备注；Settings 文案不改；file:// 场景忽略。

**结论**：任务单定稿，hermes 可照附九实施；实施并 push 后合入，再打新 MSI（0.13.2）。

## 附十二：任务单定稿复核——dev 模式回归风险（hermes 追加，2026-08-11）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。

### 附十一答复落实情况（复核通过）
- 第 1 条（新建 settings.test.ts）→ 正文第 3 条已重写，旧措辞「现有依赖 DEFAULT_BASE_URL 的用例同步调整」已删除。
- 第 2 条（方案 B）→ 正文第 2 条判定条件与答复一致（存储值去斜杠 == DEFAULT_BASE_URL 且 origin ≠ 默认 → 忽略存储值）；验收栏已补「残留场景」项。
- 第 3 条（vitest 提示）→ 正文第 3 条实施提示已含 jsdom pragma / origin 变量断言 / removeItem 清 key。
- 可选建议（127.0.0.1 验收备注）→ 验收栏已补。
- 修订质量：编号无重排、无旧措辞残留、正文与答复无歧义；附九已定稿，可照此实施。

### 新发现：pnpm dev 开发模式回归（建议拍板）
- 依据：`web/vite.config.ts` 无 `server.proxy` 配置（仅 port 5173）；`web/src/lib/api/client.ts` 全部请求走 `getBaseUrl()`。
- 现象：dev 页面 origin = `http://localhost:5173` ≠ 默认值，方案 B + 默认改 origin 后——无存储值时返回 `localhost:5173`（API 全发往 vite dev server，无后端）；dev 下保存过 127.0.0.1 的浏览器（联调常见）方案 B 判定「存储值 == 默认且 origin ≠ 默认」→ 连存储值也被忽略，同样失败。旧行为（无存储值 → 127.0.0.1:53960 直连，宽松 CORS 放行）在 dev 下可用。
- 影响面：验收标准只含 PDA / PC 生产场景，dev 模式不在验收内——严格按附九实施不违反验收，但迭代 15 前端交付一直用 dev 模式联调，属实际回归。

### 建议方案（供拍板）
- 方案 ①：`vite.config.ts` 加 `server.proxy = { '/api': 'http://127.0.0.1:53960' }`（dev 下同源走代理，与「页面自身来源」哲学一致；一行配置、零风险，生产不受影响——生产由 WinHost 静态托管，无 vite）。
- 方案 ②：不加 proxy，接受 dev 模式退化（开发时手动在设置页填非默认地址，或改用 build + WinHost 联调）。
- 方案 ③：getBaseUrl 特判 dev origin（不推荐，把 dev 逻辑写进生产代码）。

### 待审核者确认清单
1. dev 回归处理：选方案 ①（加 dev proxy，推荐）、②（接受退化）还是 ③？

结论：定稿正文无需改动；仅 dev 联调链路需一个配套决策。
