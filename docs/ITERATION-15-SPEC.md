# 迭代 15 规格：打印设置与会话保留 + 连接管理 + 图片打印收敛（删除 ZPL）

> 状态：规格评审中（2026-08-10，主 agent / 后端整理，交前端 hermes 评估）
> 协作：本文档定义前后端改动范围与 API 契约；hermes 评估前端部分无异议后，后端实施后端项、前端实施前端项（可并行）。字段名 / 接口以本文档为准。
> 背景：用户反复测试打印定位效果，提出三项优化；并已拍板**彻底删除 ZPL（Vector）打印路径**，统一为整版位图（Skia）图片打印。

---

## 1. 需求（用户原话归纳）

1. **数据与打印页会话保留**：切换到设计器再回来，不希望重新设置「标签（模板 / 字段值）+ 打印方式」。不接受开两个页面；同一标签页内切换视图必须保留设置；**两个浏览器标签页之间不互通**（一个页面改设置，另一个页面不得跟随）。
2. **前端切换连接方式**：在 Web 前端把连接从 Log 切换为真实打印机（TCP / Windows 驱动 / Zebra），并可添加新的连接方式；**同一时间只有一个连接方式生效**；**不为未连接的连接方式做支持**（只维护当前生效方式的参数）。
3. **去掉 ZPL（Vector）打印方式**：打印统一为图片（Skia 渲染整版位图经 `^GF` 直传打印机），前后端同一渲染逻辑保证一致性。另：**调试功能独立出来**——调试模式开启时，直接打印图片（保存/查看 PNG），**不发送给打印驱动**。
4. DataPrint 会话保留范围：选中的模板 + 已填字段值 + 调试开关 + Excel 导入映射（用户已确认）。

## 2. 已确认决策（用户拍板）

- **D1 彻底删除 ZPL/Vector**：PC 端（WinHost / Web / 作业 / 配置 / 健康检查 / 测试）不再存在矢量 ZPL 路径；图片打印的物理载体 `^GF` 位图编码保留（重构为独立的图片编码器）。
- **D2 连接管理**：默认连接 Log；前端可切换并「先测试、后生效」，测试失败自动回滚并提示；切换结果持久化（重启保留）；同一时间单一连接。
- **D3 连接 UI 位置**：设置页做完整管理；「数据与打印」页顶部放当前连接状态与快速切换。
- **D4 调试独立**：调试为独立开关（不再依附「图片打印方式」复选框）；开启后作业只出图、不发送驱动。
- **D5 会话保留实现**：DataPrint 草稿状态提升到全局（内存 / sessionStorage），**禁止 localStorage**（避免跨标签页共享）。

## 3. 删除项（后端为主，前端联动）

### 3.1 后端 / API
- 删除 `PrintMode` 枚举（Vector / Image）及 `WinHost.PrintMode` 配置、`LABELFRAME_PRINT_MODE` 环境变量、`packaging/appsettings.json` 中的 `PrintMode`。
- 删除 `SubmitJobRequest.printMode`、`/healthz` 响应的 `printMode` 字段（保留 `transport`）。
- 删除矢量 ZPL 编码：`IZplEncoder`、`ZplEncoder.Encode`（`^A` / `^FB` / `^BC` / `^BQ` 等）、`ZplBoldMode`、`WinHost.BoldMode` 配置与 `LABELFRAME_BOLD_MODE`（**`LabelTextElement.Bold` 属性保留**，Skia 图片渲染继续用 `SKFont.Embolden` 实现加粗）。
- **保留并重构** `^GF` 位图编码：`ZplEncoder.EncodeImage` 抽为独立 `ZplImageEncoder.EncodeImage(LabelBitmap, widthMm, heightMm, dpi)`（图片打印的物理载体）。
- 删除 WinHost 对 `ZplEncoder` / `ITextRasterizer` / `GdiTextRasterizer` 的依赖（Image 模式不需要文本→`^GF` 替换，Skia 直接渲染中文）；`GdiTextRasterizer` 及测试删除。
- `JobSubmissionService`：恒走「Skia 渲染整版位图 → `ZplImageEncoder.EncodeImage`」；`ILabelBitmapRenderer` 保留。
- `LogPrintTransport` 语义调整：Log 连接 = 模拟打印（见 §5.3），不再记录 ZPL 文本；相关测试同步调整。
- AndroidHost：`SubmissionService` 不再使用 `ZplEncoder`，改为**整版位图打印**（新增 Android 整版渲染器：文本用 Android.Graphics、条码 / 二维码用 ZXing.Net、线 / 区域 / 图片绘制，输出 `LabelBitmap` → `ZplImageEncoder`）；真机验收放 PDA 联调阶段。

### 3.2 前端
- 删除「打印方式」下拉（DataPrint / Settings 中 `printMode` 相关 UI 与状态）；删除「调试：不打印，保存实际打印图片（PNG）」复选框（由独立调试开关取代）。
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
→ 200 { "ok": true, "message": "已切换为 TCP（192.168.1.50:9100）。", "config": { ... } }
→ 200 { "ok": false, "message": "连接测试失败：...", "current": { ... } }   // 不切换，保持原连接
→ 400 参数校验失败（如 WindowsDriver 未填打印机名）
```

- 校验规则：`Tcp` 必填 `tcpHost` + `tcpPort`（1-65535）；`WindowsDriver` 必填 `printerName`；`Zebra` + `Tcp` 必填 `tcpHost/tcpPort`、`Driver` 必填 `printerName`、`Usb` 的 `zebraUsbName` 可空（空 = 自动发现第一台）；`Log` 无参数。
- **先测试后生效**：非 `testOnly` 时后端先创建候选传输并测试——Tcp：TCP 连接（3 秒超时）；Zebra：SDK 连接测试；WindowsDriver：按名打开打印机；Log：恒成功。成功才切换 + 持久化；失败不切换、返回当前连接。
- `testOnly=true`：只测试不保存不切换（「测试连接」按钮）。
- **持久化**：写入 `%LOCALAPPDATA%\LabelFrame\connection.json`（用户数据目录，可写；不写 Program Files 的 appsettings.json，避免权限问题）。启动优先级：`connection.json` > `appsettings.json`（现有连接键保留为默认兜底）> 默认 Log。
- 运行时：`ITransportManager` 持有当前 `TransportConfig` 与 `IPrintTransport` 实例；作业 Worker、`/api/printer/status`、`/api/printer/test` 统一从 manager 取当前实例。**打印中切换**：允许切换，进行中的作业继续使用切换前的实例（旧实例引用保留至作业结束），新作业使用新连接。

### 4.2 调试模式（作业级）

```
POST /api/jobs  body 增加可选  "debug": bool（默认 false）
```

- `debug=true`：Worker 对每张标签渲染整版位图 → PNG 保存到 `%LOCALAPPDATA%\LabelFrame\debug\{jobId}\label-{index+1}.png`；**不调用传输**（即使当前连接是真实打印机）；作业状态照常推进至 Completed；逐张无失败。
- `JobView` 增加可选 `debugImagePaths: string[]`（相对用户数据目录的路径，如 `debug/{jobId}/label-1.png`），前端据此展示「已保存 N 张调试图片」与目录提示。
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
- 新增/改写：`ZplImageEncoder` 编码测试；`JobSubmissionService` 恒图片打印断言；连接管理（校验 / 先测试后生效 / 失败回滚 / 持久化 connection.json / 启动优先级）；调试作业（保存 PNG、不调用传输、JobView.debugImagePaths）；Log 模拟打印（保存 PNG + 日志）。
- AndroidHost 编译通过（真机验收待 PDA 联调）。

## 6. 前端实施要点（hermes）

### 6.1 DataPrint 会话保留
- 将 DataPrint 草稿状态（`selectedName`、`values`、调试开关、Excel `mapping` 等）提升到全局（`AppContext` 扩展 `printDraft`，或独立 store）；切 tab 不卸载 store。
- 持久化可选 sessionStorage（刷新保留，且天然按标签页隔离）；**不使用 localStorage**。
- 模板切换/重载时保留用户已填字段值（与 testData 合并，用户值优先）。

### 6.2 连接切换 UI
- 设置页：新增「连接方式」分组——模式单选（Log / TCP / Windows驱动 / Zebra），只显示当前模式参数；按钮「测试连接」（testOnly）与「保存并应用」（先测试后生效，失败展示后端 message）；显示当前生效连接。
- DataPrint 页顶部：当前连接徽标（如 `LOG` / `TCP 192.168.1.50`）+ 快速切换（建议：模式下拉 + 当前模式参数内联，复用设置页逻辑；切换即「测试+应用」，失败回滚并提示）。
- `api` client 增加 `getTransport` / `setTransport` / `testTransport`；`Healthz` 移除 `printMode`。

### 6.3 调试独立
- DataPrint「调试」独立开关（默认关），说明文案「调试模式：只生成图片，不发送打印驱动」；开启后提交作业 `debug: true`，作业进度区展示保存的 PNG 张数与目录；关闭后按当前连接真实打印。
- 删除打印方式下拉与旧调试复选框；保留「调试出图（当前表单）」按钮（renderImage）。

## 7. 不在范围

- 新传输协议（蓝牙等）**实现**：仅留扩展点（枚举 + 工厂注册）。
- Server 路由、模板 API、模板包格式、作业模型既有契约不变（仅 JobView 增加可选 `debugImagePaths`、SubmitJobRequest 增加可选 `debug`）。
- WPF Studio（冻结）不改。
- Android 真机打印效果验收（放 PDA 联调阶段）。

## 8. 验收标准

1. 数据与打印 → 设计器 → 返回：模板、字段值、调试开关、Excel 映射全部保留；**另开一个标签页**改设置不影响本页（反向亦同）。
2. 设置页 / 数据与打印页可把连接从 Log 切到 TCP（填打印机 IP）：成功 → healthz / 状态栏 / 徽标更新为 TCP，`connection.json` 生成；**改错 IP 保存失败** → 提示原因、连接保持原样；重启 WinHost 后连接仍是保存值。
3. 作业提交恒为图片打印：全链路无 `printMode` / Vector / ZPL 残留（配置、healthz、UI、日志）；Log 连接下作业保存 PNG 且作业完成。
4. 调试开关打开：20 行批量 → `debug\{jobId}\` 下 20 张 PNG，**驱动零发送**；关闭调试接真实打印机正常出纸。
5. `dotnet test` / `pnpm test` 全绿；AndroidHost 编译通过（真机验收待 PDA 联调）。
6. 重新构建 MSI 后可覆盖安装，`appsettings.json` 保留机制不受影响。

## 9. 分工与时序

- 后端（本仓库 AI）：§3.1 / §4 / §5（删除 ZPL、连接管理、调试作业、Log 语义、AndroidHost 图片打印、测试）。
- 前端（hermes）：§3.2 / §4（client 类型）/ §6（会话保留、连接切换 UI、调试独立）。
- 时序：hermes 先评估本文档；确认后两端并行；后端完成后可先出调试版 MSI，前端完成后合并联调。
- 完成定义：验收标准全满足 → 更新 ROADMAP / CHANGELOG / DESIGN（决策记录）；Conventional Commits；不推 tag。