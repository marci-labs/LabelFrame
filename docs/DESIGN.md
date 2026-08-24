# LabelFrame 设计文档

## 1. 愿景与目标

愿景：**方便仓库完成标签打印，提高办公效率。**

目标（由愿景推导）：
- 操作工在业务动作中一键触发打印，就近出纸，零学习成本；
- 文员可在 PC 上批量打印，无需关心打印机细节；
- 管理员一次配置，多设备可复制，故障可解释；
- 业务系统通过简单契约接入，不依赖具体打印机型号。

## 2. 核心概念（术语表）

| 术语 | 含义 |
|---|---|
| 契约（LabelContract） | 一个标签场景的字段清单（Key / DisplayName / 必填 / 类型 / 格式），可版本化 |
| 版式（LabelLayout） | 标签布局：尺寸 + 元素（文本 / 条码 / 二维码 / 图片 / 线），引用契约版本，毫米坐标 |
| 标签文档（LabelDocument） | 版式 + 数据解析后的中间结果，与打印机指令无关 |
| 作业（Job） | 一次打印请求 = N 张标签，逐张状态，可挂起 / 恢复 / 取消，批内顺序 |
| 设备（Device） | 一台运行宿主的 PC 或 PDA，向 Server 注册 |
| 宿主（Host） | 设备上的本地打印服务（WinHost / AndroidHost） |
| 编码器（Encoder） | LabelDocument → 打印机指令（ZPL 优先，预留 TSPL / CPCL / 图片） |
| 传输（Transport） | 把指令送到打印机：TCP 9100 / Windows 驱动 / 蓝牙 / 日志模拟 |
| 模板包（TemplatePackage） | 契约 + 版式 + 静态图片资源的可导入导出单元（zip） |

## 3. 总体架构

```mermaid
flowchart LR
    subgraph 业务侧
        WMS[业务系统 / 网页]
    end
    subgraph LabelFrame
        S[Server<br/>设备注册 · 作业投递 · 测试入口]
        subgraph 设备A[PC]
            W[WinHost]
            P1[打印机 USB / IP]
        end
        subgraph 设备B[PDA]
            A[AndroidHost]
            P2[打印机 IP / 蓝牙]
        end
    end
    WMS -->|"HTTP 提交作业"| S
    S -->|"定向投递"| W
    S -->|"定向投递"| A
    W --> P1
    A --> P2
```

### 3.1 两种打印模式

- **路由模式（主）**：业务系统提交作业（requestId + 目标设备 + labels[]）→ Server 校验并投递到目标设备宿主 → 宿主作业队列逐张打印 → 状态可查 / 可通知。
- **直连模式**：PDA 网页或本机程序直接调用本机宿主（本地 HTTP / JS 桥），不经 Server；适合就近单张，页面有数据时可脱离服务器。

### 3.2 一次请求 = 多张

调用方一次提交 N 张标签（每张可不同），异步返回 jobId，进度与失败项可查；出库拆分、批量、补打统一为同一模型。

## 4. 关键技术决策（决策记录）

| # | 决策 | 决定 | 后果 |
|---|---|---|---|
| 1 | 契约与版式分离 | 字段契约稳定、版式易变，两者分开版本化 | 改版式不碰契约；契约升级后可软校验旧版式（Drifted） |
| 2 | 中间文档模型 + 毫米坐标 | 版式用毫米，编码器按 DPI 换算 | 同模板可跨 203/300 dpi；便于预览与多指令集 |
| 3 | 编码器抽象 | `ILabelEncoder`，ZPL 优先 | 换打印机只增加编码器 |
| 4 | 中文渲染下沉编码器 | 中文文本栅格化为位图（^GF），条码始终原生指令 | 不依赖打印机固件；条码质量不妥协 |
| 5 | 异步作业模型 | 提交即返回 jobId；逐张状态持久化；幂等 requestId；挂起 / 恢复 / 取消；批内顺序 | 大批量不阻塞调用方；不重打不漏打 |
| 6 | 设备定向投递 | 作业目标 = 发起设备；Server 维护设备目录精准投递 | 多人并发互不干扰；替代广播方案 |
| 7 | 本地服务统一入口 | 所有打印经设备宿主；直连与路由并存 | PC / PDA 同构 |
| 8 | 预览是设计期能力 | 预览渲染不进打印主链路，模板设计 / 调试用 | 正式流程零预览开销 |
| 9 | 运行平台 | .NET 10（2026-08-09 起）；WinHost 目标 `net10.0-windows10.0.26100`（Zebra SDK 需要 Windows SDK 投影）；Win7/8 用 net48 版 WinHost（尽量兼容，有真实需求再做） | 官方 SDK 与最新 LTS 能力；兼容后置 |
| 10 | 模板管理先单机 | 单机 CRUD + 模板包导入导出；WMS 下发后置 | 「标准模板包」格式从第一天定，下发复用同一格式 |
| 11 | Server 自带测试入口 | 无业务系统也能提交打印、连接打印机验证 | 系统独立可测 |
| 12 | 迭代 1 编码器范围 | ZPL 编码器先覆盖文本 / Code128 / 图片占位；二维码 / 线元素进入模型但编码器显式报错（NotSupportedException），迭代 2 补全 ^BQ / ^GB | 迭代 1 验收聚焦 ^BC，避免范围膨胀 |
| 13 | 问题码约定 | 校验问题码统一 `LF_VAL_xxx`，消息中文可读 | 故障可解释，后续错误码沿用此约定 |
| 14 | 作业模型与 SQLite 持久化 | 一次请求 = 1 个 Job + N 个 Item（逐张）；SQLite 表 `jobs` / `job_items`，`request_id` 唯一索引实现幂等；Item 存编码后的 ZPL（不可变，避免重打） | 服务重启不丢作业；重放同一 requestId 不重复建单 |
| 15 | 本地 HTTP API（迭代 2 契约） | 提交请求自包含模板（contract + layout + labels[]），因为模板管理在迭代 4；端点：`POST /api/jobs`、`GET /api/jobs/{id}`、`POST /api/jobs/{id}/suspend|resume|cancel`、`GET /healthz` | 无模板库也能端到端打印；迭代 4 再支持模板引用 |
| 16 | 挂起 / 恢复语义 | 传输异常（如打印机离线）→ 当前 Item 记 Failed，若仍有未打 Item 则 Job 挂起；恢复后从未打印的 Pending Item 续打（不重打 Failed，失败项单独重打在迭代 6）；取消 → 剩余 Pending/Printing Item 置 Cancelled；服务重启把 in-flight（Printing）Job 置 Suspended，并把在途（Printing）Item 重置为 Pending，恢复后续打优先保证不漏打（不重打语义在真实设备联调时确认） | 符合底线「不重打、不漏打」；TCP 无法感知缺纸，以发送异常近似 |
| 17 | 中文渲染架构 | Core 定义 `LabelBitmap`（1bpp）+ ZPL `^GF` 编码；WinHost 用 GDI（System.Drawing，Windows 专属）把非 ASCII 文本元素栅格化为位图并替换为图片元素；ASCII 仍用原生 `^A` 文本 | 不依赖打印机固件中文字库；Android 迭代 5 用平台位图实现同契约 |
| 18 | 传输分层 | TCP 9100 在 Core（跨平台，Android 复用）；Windows 驱动（USB）用 winspool P/Invoke raw 打印，放在 WinHost | 每台打印机串行，一台设备一次只处理一个 Item |
| 19 | WinHost 配置 | `appsettings.json` 的 `WinHost` 节 + `LABELFRAME_*` 环境变量覆盖；默认监听 127.0.0.1:53960、Log 传输（联调）、数据库 %LOCALAPPDATA%\\LabelFrame\\jobs.db | 一次配置可复制；无真实打印机时日志模拟 |
| 20 | Zebra 官方 SDK | 传输新增 Zebra 模式（`Zebra.Printer.SDK 3.0.3355`，Link-OS）：TCP / USB（自动发现）/ Windows 驱动统一连接；避开 5.x 引入的 MAUI/WinUI 依赖；轻量 TCP9100 / winspool raw 保留作备选 | 官方 USB 直连与打印机状态（迭代 6）可用；Zebra 模式要求 Win10+ |
| 21 | Server 投递采用「宿主轮询」 | WinHost 注册后周期轮询 Server 领取定向作业，完成后回报结果；不要求宿主开放入站端口 | PC / PDA 同构，天然穿透防火墙；设备在线以心跳（轮询）为准 |
| 22 | 设备离线语义 | 作业投递给离线设备时在 Server 暂存（Pending），设备上线轮询即领取；不设过期 | 符合「不丢作业」底线；后续可按需加过期/通知 |
| 23 | 模板包格式 | zip：`manifest.json`（name / group / contract / layout）+ `images/` 图片资源；模板库 SQLite（Core.Templates，按分组列表） | 两台电脑间可导入导出；WMS 下发复用同一格式 |
| 24 | 预览渲染 | LabelDocument → PNG（设计期）：文本 / 线用 GDI，条码 / 二维码用 ZXing，图片用模板资源或位图；毫米 → 像素按 DPI | 预览与打印同坐标体系，抽查一致 |
| 25 | 失败项单独重打 | Failed Item → Pending（清错误），Failed 作业自动恢复 Pending 由 Worker 续打 | 补打不重建整单；不重打已完成项 |
| 26 | 在线状态 / 测试页 | `GET /api/printer/status` + `POST /api/printer/test`；TCP 用 `~HS` 基础解析（字段映射待真实设备联调），Zebra SDK 3.x PrinterStatus 无公开字段先按「连接成功 = 在线」，驱动模式不可读回 | 故障可解释；真实设备联调确认字段语义 |
| 27 | Android 本地 HTTP | AndroidHost 用 TcpListener 极简 HTTP（仅 127.0.0.1:53970），不承载完整 ASP.NET Core | 包体小、依赖少；JS 桥同端口预留 |
| 28 | Android 中文渲染与存储 | Android.Graphics Bitmap → LabelBitmap（^GF，与 WinHost 同契约）；SQLite 用 lib.e_sqlite3.android | 跨宿主中文输出一致 |
| 29 | Studio 模板工具架构 | `LabelFrame.Studio`（WPF，net10.0-windows）作为 WinHost 的 HTTP 客户端：模板管理 / 导入导出 / 预览 / 测试打印全部复用 WinHost API；V1 不做版式可视化编辑（V2 再加画布） | 零重复逻辑，测试打印走生产同一条打印链路；V2 拖拽画布不改变模板包契约 |
| 30 | Studio V2 版式编排 | 画布用 WPF 原生元素（Canvas + TextBlock/Border/Line），mm → px 按缩放换算；条码 / 二维码在画布上为带 SourceKey 的占位框，真实效果由「刷新预览」（WinHost preview PNG）确认 | 拖拽流畅、无需本地条码渲染；编排所见即模板结构 |
| 31 | 后续规划（迭代 9/10） | Excel 导入：列 → 契约字段映射，批量生成标签数据；MSI 安装包（WiX）：安装 WinHost + Studio、生成 appsettings.json（端口 / 传输 / 打印机 / 数据库）、开始菜单快捷方式 | 交付形态完整；业务侧只传文本，模板决定条码 / 二维码 |
| 32 | Excel 读取选型（预研） | 迭代 9 采用第三方 `TemplateFrame.Excel.Simple 1.0.4`：`SimpleExcel.Read(Stream, tableName)` 读取 xlsx → 表头 + 数据行，再按列映射契约字段；底层 `DocumentFormat.OpenXml 3.3.0`；仅作依赖引用，仓库命名仍为 `LabelFrame.*` | 省去自研 xlsx 解析；列映射与批量提交逻辑由我们实现 |
| 33 | 元素样式与区域（格子）布局 | 文本元素可选 `WidthMm`（块宽）/ `TextAlign`（Left/Center/Right）/ `PaddingMm` / `BorderMm`；矩形元素可选 `BorderMm`；新增区域元素 `LabelRegionElement`（X/Y/W/H/BorderMm）；任何元素可选 `RegionId` + `RegionHAlign/RegionVAlign`（Start/Center/End）锚定到区域 | 支持「先画格子再放元素居中」的编排模式；格子保存、移动元素跟随；旧模板无新属性行为不变（向后兼容） |
| 34 | Studio 2.0 界面（迭代 8C） | 两个工作区：作业工作台（模板列表 / 预览 / 数据表单 / 打印 / 状态日志栏）+ 独立模板设计器（控件栏 / 画布 / 属性分组 / 填充 / 区域 / 实时预览 / 打印测试）；不常用功能收进菜单栏 | 文员日常打印与设计分离；画布所见即所得即实时预览 |
| 35 | 元素内容来源（填充） | 文本 / 条码 / 二维码支持两种来源：`Literal` 固定值（如标题）或 `SourceKey` 字段填充；编码与预览取值 = Literal ?? data[SourceKey] | 固定文本无需建字段；旧模板无 Literal 行为不变 |
| 36 | 容器控件（替代“画区域”） | 设计器控件栏提供「容器」（内部仍是 `LabelRegionElement`，模板包格式不变）；元素拖入容器自动锚定居中；属性面板不再暴露 RegionId / 对齐锚定 UI（后台能力保留） | 用户无需理解区域概念；画格子 → 放元素居中的编排方式不变 |
| 37 | 契约字段自动推导 | 字段集合 = 版式中「字段填充」元素的 `SourceKey` 去重（按元素顺序）；移除字段增删 / 重命名 / 显示名编辑 UI；显示名统一取 Key；加载旧模板保留契约字段顺序与元数据（IsRequired / Type），未被元素引用的字段不再保留 | 字段建立是后台逻辑，用户只负责绑定控件；工作台 / 测试表单用 Key 作标签 |
| 38 | 设计器 2.0 交互与布局（迭代 8D） | 设计 / 测试用 Tab 分开；左键选中、8 手柄拖角缩放、框选多选、Delete 删除、中键平移、Ctrl+滚轮缩放（以鼠标为中心）、标尺 + 网格、边缘 / 中心对齐吸附与右键对齐菜单；属性面板选中控件才显示（默认收起）；底部状态 + 日志栏横跨全窗口（自动滚底 + 清空）；控件栏拖拽不再重复建元素；元素属性变化实时驱动画布重绘与预览 | 用户编辑手感接近主流设计器；设计界面与打印测试界面职责分离 |
| 39 | UI 技术选型评估（2026-08-09，用户已选 A 方案） | 后端（Core / Rendering / WinHost / Server / AndroidHost）保持 .NET 10 不动；Studio UI 层评估 Web 技术栈（Tauri 2 / Blazor Hybrid / 纯浏览器），先用本地 Web 原型验证拖拽设计器体验，再决定是否重写 UI；原型直接调 WinHost API，复用全部后端能力 | UI 是纯客户端，可独立替换；原型成本低、结论直观；无论换栈与否，Excel 读取 / 批量打印等业务逻辑做成可复用服务 |
| 40 | 单机服务与前端工程化（2026-08-09，用户确认） | 单机模式 = 演进 WinHost 为单进程服务（模板库 / 作业 / 打印传输 / 静态托管 Web UI / Excel 导入 / PDA 日志）；前端另起 `web/`（Vite + React + TS + Konva），prototypes 原型冻结不改；后端 C#，前端 JS/TS；Excel 解析在后端复用 TemplateFrame.Excel.Simple | 一台 PC = 一个进程 + 浏览器；前后端并行开发，前端按 FRONTEND-SPEC.md 交付，主 agent 联调 |
| 41 | 模板测试数据 testData（契约扩展） | 模板包与模板 API 增加可选 `testData`（键 → 值字典，向后兼容）；PDA 打印测试与 PC 打印测试共用；manifest / SQLite 模板表同步扩展 | 服务端定义测试数据，PDA 点击模板即可本地测试打印 |
| 42 | PDA 测试模式（迭代 11） | AndroidHost 配置 pc_host（PC 单机服务地址）→ 本地 HTTP 增加 `GET /api/pc/templates` 与 `POST /api/pc/templates/{name}/print-test`（拉模板详情 → 服务端 testData 本地打印 → 终态日志回传 PC）；内置 PDA 测试页（浏览器打开 127.0.0.1:53970） | PDA 开箱即用测试打印；日志回传 PC 便于远程调试；Manifest 允许明文 HTTP（内网） |
| 43 | MSI 安装包方案（迭代 10，2026-08-09；2026-08-10 修复） | WiX v7 打包：WinHost 发布产物（win-x64 **framework-dependent**，含 web/dist）+ 桌面 / 开始菜单快捷方式（Target=#WinHostExe）；WinHost 为 WinExe + 自动开浏览器 + host.log + 本机优雅关闭 + **系统托盘（原生 P/Invoke，无 WinForms）**；名称统一 LabelFrame；L 型图标；自签名证书脚本；目标机需 .NET 10 Desktop Runtime（MSI 约 10MB）。2026-08-10 修复：MSI 以 `-arch x64` 构建为 **x64 包**（此前是 32 位包，`ProgramFiles64Folder` 不生效，错装到 `Program Files (x86)`）；版本升至 0.11.1 以支持覆盖已装的 0.11.0 | 干净电脑装 .NET 10 Runtime 后：安装 MSI（`C:\Program Files\LabelFrame`）→ 双击图标 → 浏览器打开即用；正式签名需商业证书 |
| 44 | MSI 运行时缺失处理（2026-08-10，用户决策；2026-08-10 修复检测） | MSI 检测 .NET Desktop Runtime（x64）：缺失时全 UI 安装显示带可点击官方下载链接的对话框（MSI Hyperlink 控件），静默 / 基础 UI 由 LaunchCondition 拦截并提示链接；**放弃 Burn 引导自动下载安装**运行时（曾尝试 WiX v7 Bal 构建 Bundle，扩展加载与依赖链复杂、收益低，用户确认不做）。检测实现改为 WiX NetFx 扩展 `DotNetCompatibilityCheck`（内置官方 NetCoreCheck 自检，检查 Microsoft.WindowsDesktop.App ≥ 10.0.0、RollForward=latestMajor、x64）：不再读取注册表（原方案读 `InstalledVersions\...\sharedfx` 默认值，而运行时版本是**命名值**，且 MSI 为 32 位视图，导致装了运行时仍误报缺失）；NetCoreCheck 实时检测，**装完运行时无需重启**即可识别 | 干净电脑需先装 .NET 10 Desktop Runtime 再装 MSI；MSI 保持约 10MB、不联网自动安装 |
| 45 | 模板预览值 + 测试默认值 + 图片打印（迭代 12，2026-08-10，后端已实施（SkiaSharp 渲染器），前端 renderLabelImage 待实施） | 元素 JSON 新增 `previewValue`（text/barcode/qrcode 字段填充模式写入，固定值仍用 literal）；保存模板时后端自动用元素预览值派生并覆盖 `testData`，作为 PC / PDA 打印测试的初始默认值（单一事实来源=元素预览值）；新增 `PrintMode`（Vector 默认 / Image）：Image 模式用 **SkiaSharp 渲染器**（canvas 类 2D，与前端同源）把整张标签渲染为 1bpp 位图经 `^GF` 直传打印机，用于评估定位与效果；`SubmitJobRequest` 增加可选 `template.name`（取模板图片资源）与 `printMode`（请求覆盖配置） | 先实验评估图片打印效果，再决定是否替代矢量 ZPL；预览值丢失 bug 由前后端契约修复 |
| 46 | 文本垂直对齐契约（迭代 12，2026-08-10） | 文本元素新增 `heightMm`（0=按字高）与 `verticalAlign`（Top/Middle/Bottom，默认 Top 兼容旧模板）；前端保存时写入元素高度与垂直对齐；Skia / GDI 渲染器按框高垂直对齐绘制，边框按框高；修复「打印比前端预览整体偏上」（此前前端框内居中、后端顶部对齐且高度未持久化） | 旧模板需在编辑器重新保存一次才带 heightMm/verticalAlign；矢量 ZPL 打印暂不区分垂直对齐 |
| 47 | 元素契约第二批字段 + 垂直对齐默认值统一（迭代 13，2026-08-10，后端已实施；前后端已完成，用户验收待执行） | 文本 `wrap / lineHeight / fitMode / fontFamily`（默认 Microsoft YaHei）、二维码 `qrEcc / qrMargin`（默认 M / 2）、条码 `displayValue`（默认 true）、通用双边内边距 `paddingH / paddingV`（`PaddingHMm / PaddingVMm`，0=未设，缺失时回退 `paddingMm`）写入版式契约；写方向非默认才写、读回默认（向后兼容，无数据库迁移）。决策 A：`VerticalAlign` 默认由 Top 改为 **Middle**（与前端一致），旧模板无 `heightMm` 时渲染器框高兜底 = `max(字高 + 2×最大内边距, 10mm)`。Skia 图片打印按这些字段真实绘制（换行 / 行距 / 溢出处理 / 字体 / QR 参数 / 条码文字 / 双边内边距）；新字段**不参与** ZPL 矢量编码 | 导入 → 保存 → 重开逐字段一致；图片打印与前端预览同源；旧模板行为 = 前端现状 |
| 48 | MSI 升级保留用户配置 appsettings.json（0.12.2，2026-08-10） | appsettings.json 从自动文件清单中剔除，改为 main.wxs 中 GUID 固定的独立组件，标记 `NeverOverwrite="yes"`（升级 / 修复不覆盖）+ `Permanent="yes"`（卸载不删除）；新装仍写入 packaging 默认配置 | 覆盖安装 / 修复不丢用户配置；卸载保留该文件（属用户数据）；后续新增默认配置项需用户手动合并或应用层兜底默认值 |
| 49 | 字体加粗契约与打印方案（迭代 14，2026-08-10，后端已实施） | 文本元素 JSON 新增 `bold?: boolean`（true 才写 / 默认 false，旧模板兼容）。ZPL 无标准加粗修饰符：方案 A（默认，可配置）粗体字体变体映射（`"0"→"1"`，`ZplEncoder` 可注入映射表，WinHost `LABELFRAME_BOLD_MODE=FontVariant`）；方案 B `WidthScale` 宽度 ×1.15 放大兜底。Skia 用 `SKFont.Embolden` 渲染且测量一致（与前端 Konva `fontStyle:bold` 同源） | 小字号打印可试加粗对比；方案 A 依赖打印机内置粗体字体编号，不同机型可能需调整映射表；矢量 ZPL 加粗为近似，Image 模式最保真 |
| 50 | 图片打印收敛 + 连接管理 + 调试出图（迭代 15，2026-08-10，后端已实施，前后端已完成，用户验收待执行） | 彻底删除矢量 ZPL（`ZplEncoder.Encode` / PrintMode / GdiTextRasterizer 等），打印统一 Skia / Android 整版位图经 `^GF`（`ZplImageEncoder`）；连接管理 `ITransportManager` + `GET/POST /api/transport`（单一连接、先测试后生效、失败回滚、持久化 `%LOCALAPPDATA%\LabelFrame\connection.json`）；调试 = 后端渲染出图下载（单张 `render-image` PNG / 批量 `render-images` zip），不建作业不改作业模型；Log 模拟打印保存 PNG | 打印所见即所得（前后端同源渲染）；连接切换免改文件重启；调试零纸张验证；ZPL 仅保留 ^GF 物理载体 |
| 51 | 迭代 15 前端：DataPrint 会话保留 + 连接管理 UI + 调试独立（2026-08-10，前端已实施） | ① 会话草稿提升 AppContext（`printDraft`：selectedName / valuesByTemplate+dirtyKeysByTemplate / debugMode / jobId），sessionStorage 持久化（刷新保留、标签页天然隔离，**禁 localStorage**）；values 加载 = testData 与用户 dirty 的 key 按 **key 存在性**合并（清空不被顶回）；Excel 数据与列映射不保留。② 连接状态全局化：`transportConfig`（GET /api/transport），切换成功立即用响应 config 更新（healthz 轮询仅兜底重启）；设置页「连接方式」分组（模式单选 / 只显当前模式参数 / testOnly 测试 / 先测试后生效失败回滚）+ DataPrint 顶部徽标与快速切换。③ 调试独立开关（默认关）：开 → 打印按钮改后端渲染出图下载（render-image 单张 PNG / render-images 批量 zip，不建作业不发驱动）、隐藏「出图预览」；关 → 正常作业 +「出图预览」即时预览 | 同标签页切视图不丢设置、标签页间不互通；调试所见 = 打印所出；旧版后端无 /api/transport 时优雅降级（徽标回退 healthz mode） |
| 52 | 服务端 / 客户端拆分（2026-08-11，用户拍板 6 项，待实施） | 拆为双安装包：Server（模板 / 作业 / 设备投递 / Web UI / 调试出图 / 日志，无打印机依赖，Windows 服务）与 Client（本机打印执行 / 作业领取 / 连接配置，托盘部署）；作业提交改 `templateName + labels`、pending 响应附带模板；调试出图在 Server（Skia 同源）、最终打印以 Client 渲染为准；保留单机模式（Server + Client 同机） | 多台打印 PC 共用一个服务端、职责清晰、部署解耦；跨端契约变更按 docs/archive/ARCHITECTURE-SPLIT.md 实施 |
| 53 | UI 归属反转（迭代 18，2026-08-11） | 修订拆分决策 1：服务端默认不提供界面（不再打包 / 托管 web/dist，仅留 /healthz 与 API）；客户端（WinHost 127.0.0.1:53960）托管完整 Web UI（模板设计 / 数据与打印 / 日志 / 设置 / 作业历史）；模板 / 作业 / 设备投递 / 调试出图仍以服务端为中心 | 用户体验回到单机形态，同时保留集中部署；跨端契约增量按 docs/archive/ITERATION-18-SPEC.md 实施 |
| 54 | 服务端 Windows 服务部署（迭代 18） | Server 以 Windows 服务 `LabelFrameServer`（LocalSystem）运行（`UseWindowsService`，控制台模式保留供开发）；安装完成弹窗含「开机自启（默认勾选）/ 立即运行（默认勾选）」，按勾选 `sc config start= auto` / `net start`；升级不触发；卸载停止并删除服务 | 部署即服务、无人值守；0.14 及以前是控制台进程，安装形态变化 |
| 55 | 服务端数据目录改 ProgramData（迭代 18） | Server 的 server.db / templates.db / logs.db 默认 `%ProgramData%\LabelFrame\server`（服务账户 LOCALAPPDATA 指向系统账户目录不可靠）；环境变量覆盖保留 | 数据机器级、可预期；卸载清理路径同步更新；当前无存量数据需迁移 |
| 56 | 历史数据定期清理（迭代 18） | Server 后台任务按 `CleanupIntervalHours`（默认 24h）删除终态（Completed / Failed）且超过 `JobRetentionDays`（默认 30 天）的作业、超过 `LogRetentionDays`（默认 90 天）的日志；非终态作业不删 | 避免历史作业 / 日志无限积累；保留期可配置 |
| 57 | 客户端机器级 ServerUrl（迭代 18） | WinHost 新增 `GET/POST /api/host/config`（返回 serverUrl + deviceId/deviceName），持久化 `%ProgramData%\LabelFrame\Client\settings.json`；前端读写机器级配置（localStorage 仅兜底，缺失 / 损坏返回默认值） | 同机任何浏览器 / 用户配置一致；符合客户端本机配置原则 |
| 58 | 客户端安装完成弹窗（迭代 18） | Client MSI 完成后弹窗含「立即打开（默认勾选）」，确认启动客户端并打开界面；升级不触发 | 装完即可使用，无需手动找入口 |
| 59 | 服务端跨平台部署（迭代 19，2026-08-11） | Rendering / Server 多目标 `net10.0;net10.0-windows`：Windows 专属代码（GDI 预览、UseWindowsService、图标、WindowsServices 包）条件编译；Server 数据目录按平台默认（Windows %ProgramData%\LabelFrame\server / Linux /var/lib/labelframe/server），环境变量优先；Linux 用 systemd（Type=simple），Windows 用 Windows 服务；Client 仍仅 Windows | Ubuntu 可部署服务端，跨机验证（服务端 Linux + 客户端 Windows）；API / 契约不变 |
| 60 | 安装包先停运行程序 + 作业完成回报独立循环（迭代 19 反馈，2026-08-11） | ① Server MSI 安装 / 卸载先 `sc stop LabelFrameServer`（StopServerService，Return=ignore），停机超时缩短为 5s；Client MSI 用 `KillWinHost`（`taskkill /F /IM LabelFrame.WinHost.exe`，序列最前）强制结束 `LabelFrame.WinHost.exe`。② `ServerRoutingWorker` 回报改为独立 1s 循环，本地作业终态后立即回报，不再等 20s 长轮询 | 覆盖更新 / 卸载不再残留运行态；「已领取 → 已完成」延迟消除；进度仍为终态跳变（逐张进度回报待后续按需扩展） |
| 61 | 设备 IP 记录与按 IP 查找（迭代 20，2026-08-11） | `devices` 表新增 `last_ip`（注册 / 心跳时记录服务端所见的来源 IP，每次刷新）；`DeviceView.lastIp`；新增 `GET /api/devices/by-ip/{ip}`；`POST /api/jobs` 支持可选 `targetIp`；WinHost `/api/host/config` 返回本机 `ips`（状态栏展示） | 业务系统可按 IP 定位设备再触发打印；IP 是便捷查找不是身份，deviceId 仍是唯一稳定键（DHCP / NAT 会变化） |
| 62 | 服务端管理界面插件形态（迭代 20，2026-08-11） | 静态前端包目录 `plugins/web-ui`（`Server.WebUiPath`，环境变量可覆盖）作为插件：中间件运行时检测目录存在即托管（放入即生效、无需重启），移除即恢复无头；默认服务端仍无头（不推翻 #53）；不含打印机相关内容；新增 `GET /api/server/info` | 按需“安装”界面、部署简单；不做 .NET 程序集插件（避免过度设计）；无鉴权（局域网） |
| 63 | 前端双构建模式（迭代 20，2026-08-11） | 同一前端工程 `VITE_UI_MODE=client|server`：`web/dist`（Client 包）与 `web/dist-server`（服务端 UI 插件）两产物；Server UI 菜单 = 工作台 / 设计器 / 数据与打印 / 在线设备 / 作业历史 / 设备日志，移除设置与打印机相关内容；数据与打印的目标设备改为在线设备选择器（仅在线可选） | 单一代码库双产物、避免双份维护；Server UI 无打印机概念（服务端无驱动） |- Web 设计器原型 v2 已实现（`prototypes/web-designer/`）：视口自适应 + 内容缩放、条码 / 二维码实时渲染（JsBarcode / qrcode-generator）、智能参考线吸附、文本溢出三模式、边框修正、控件精简为文本 / 条码 / 二维码。

| 64 | 发布渠道与自动化发布（迭代 21，2026-08-12） | 镜像发布到 ghcr.io（组织 `marci-labs`），安装包走 GitHub Release；推送 `v*` tag 触发 GitHub Actions 自动完成测试 / 打包 / 推镜像 / 建 Release；版本号唯一来源 = tag；Release 不含 docker 离线包（镜像在 ghcr 拉取，离线包保留本地脚本按需导出） | 无需单独申请镜像仓库、无拉取限流；发版动作收敛为打 tag；ghcr 新包首次需手动设为 Public |
| 65 | MSI 签名过渡（迭代 21，2026-08-12） | 先用自签证书 + GitHub Secret（`MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD`）走通签名链路：Secret 存在即签名、不存在则跳过；脚本不再含明文默认密码；正式对外分发再购 OV 证书 | 公开下载仍可能 SmartScreen 提示未知发布者；内网 / 域环境可推受信任根消除警告 |
| 66 | CI 测试环境确定性（迭代 21，2026-08-12） | 测试进程用 `[ModuleInitializer]` 一次性初始化 SQLitePCLRaw；SQLite 存储类自行 `EnsureInitialized()`（幂等）；测试避免依赖本机时区 / 字体 / 端口时序（设备列表日期断言改为任意 MM-dd、Skia 阈值取「有墨迹」级别、TCP 状态测试加就绪同步） | CI（UTC / 不同字体 / 高负载并行）下稳定通过；生产代码不再依赖宿主先初始化 SQLite provider |
| 67 | 传输插件统一接口与参数模型（迭代 22，2026-08-17） | `ITransportPlugin`（Id / DisplayName / Description / Parameters / Create）→ 返回 `IPrintTransport`（发送，接口不变）+ 可选 `IPrinterStatusProvider`（状态）+ 可选 `ITestableTransport`（连接测试）；参数模型 = `TransportParameterSpec`（Key / 中文标签 / 类型 String|Int|Bool|Select / 必填 / 默认 / 枚举 / 提示）+ `TransportPluginParameters`（弱类型字典强类型取值）+ `ITransportPluginContext`（宿主日志 + 数据目录）；注册表 `ITransportPluginRegistry` 按需装配 | 第三方厂商可自研插件接入（TSPL / CPCL、蓝牙、云打印）；内置四模式（log / tcp9100 / winspool / zebra）走同一接口，机制统一 |
| 68 | 传输插件加载 / 卸载 / 使用（迭代 22，2026-08-17） | 加载 = 启动扫描插件目录（默认 `%ProgramData%\LabelFrame\Client\plugins`，`LABELFRAME_PLUGINS` 可覆盖）`*.dll`，collectible AssemblyLoadContext 反射发现 `ITransportPlugin`，单个失败只记日志不影响宿主；使用 = 配置 `pluginId + params` 即启用（TransportManager 从注册表创建 / 校验 / 测试 / 持久化，作业 Worker / 状态 / 测试页链路零改动）；卸载 = 删除插件文件 + 重启生效 | 插件机制完整可测，迭代 23 接精成打印机；运行时热卸载（ALC unload）因依赖固定与线程安全问题本轮不做（记未决） |
| 69 | connection.json 兼容演进（迭代 22，2026-08-17） | 新格式 `{ "pluginId": "tcp9100", "params": { "host": "...", "port": "9100" } }`；旧 `{ Mode, TcpHost, ... }` 读取时自动映射（Log→log、Tcp→tcp9100、WindowsDriver→winspool、Zebra→zebra；TcpHost→host、TcpPort→port、PrinterName→printerName、ZebraKind→kind、ZebraUsbName→usbName）；`LABELFRAME_TRANSPORT` 环境变量同样映射 | 老配置零迁移；API 响应保留旧字段兼容旧前端 |
| 70 | 打印测试体验与权限边界（迭代 22，2026-08-17） | 「下载 Excel 模板」= `POST /api/import/excel-template`（Server 与 WinHost 都实现，生成逻辑放 Core `LabelFrame.Core.Excel`，复用 `TemplateFrame.Excel.Simple` 的 `SimpleExcel.Write`，决策 4A）；客户端仅本机打印测试（在线走服务端路由、未注册 / 离线降级本机直连并提示，决策 1A）；客户端状态栏 / DataPrint 显示本机设备名；作业历史 `GET /api/jobs?deviceId=` 过滤（客户端只看自己、服务端看全部） | 边界明确（客户端不能给其他客户端发打印测试）；测试上手更容易；作业历史按设备可见 |
| 71 | 客户端下载分发（迭代 22，2026-08-17） | 服务端 `client-packages` 目录（`LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）+ GET（列表）/ POST（上传）/ GET（下载）/ DELETE API（文件名路径穿越防护）；目录直放文件与页面上传都支持（决策 3A）；Server UI 新增「客户端下载」页；客户端设置「更新与安装包」默认从服务端获取；Ubuntu / Docker compose 挂载 `./client-packages:/var/lib/labelframe/server/client-packages` | 安装包集中分发、管理员可维护；客户端更新默认走服务端（不依赖外部渠道）；无鉴权（沿用局域网模型，风险记录） |
| 72 | 插件包分发闭环（迭代 23，2026-08-17） | 插件包 = zip（根 `manifest.json`：pluginId/name/version 必填 + 可选 description/author/minHostVersion）+ 插件 DLL，后缀 `.lfplugin`；服务端独立 `plugin-packages` 目录 + `/api/plugin-packages`（列表含元数据与 valid/invalid 状态、上传即校验、路径穿越防护，`LABELFRAME_SERVER_PLUGIN_PACKAGES` 可覆盖，Docker 挂载 `./plugin-packages`）；客户端安装到 `plugins/<pluginId>/` 每插件一目录（决策 3A），设置页「插件管理」卡片安装 / 卸载，与「更新与安装包」UI 并列（决策 7A）；三层校验（zip 完整性 + manifest 必填 + 临时 ALC 预检核对插件 id，内置插件 id 拒绝，决策 5A/6A）；覆盖安装允许、不做版本比较（决策 4A）；包大小上限 64MB；不做签名（局域网无鉴权模型，风险记录） | 厂商插件包可经服务端集中分发、客户端界面安装 / 卸载（重启生效），形成完整闭环；后续厂商打印机插件（如精成）可直接用该通道分发 |
| 73 | 外部插件字节加载（迭代 23，2026-08-17） | `PluginDirectoryLoader` 由 `LoadFromAssemblyPath` 改为 `LoadFromStream` 字节加载（依赖解析回退默认上下文 / 包内伴生 DLL 字节加载）——Windows 下不锁插件 DLL 文件：「卸载 = 删除插件文件 + 重启生效」与覆盖安装真正可用（LoadFromAssemblyPath 会锁文件，已加载插件无法删除）；运行中进程继续使用内存镜像，重启后按新文件装配 | 卸载 / 覆盖安装不再被文件锁卡死（联调冒烟实证）；副作用：插件 `Assembly.Location` 为空（字节加载），插件自定位资源需改用上下文数据目录（`ITransportPluginContext.DataDirectory`），文档注明 |
| 74 | 客户端批次作业（Batch Print，迭代 24，2026-08-18） | WinHost 新增批次节流：PrintSettings（默认 关 / batchSize 10 / batchIntervalMs 500；读取 Normalize 缺失 / 损坏 / 越界回默认值；保存校验 batchSize≥1、batchIntervalMs≥0）+ PrintSettingsStore（%LOCALAPPDATA%\LabelFrame\print-settings.json，原子写）+ GET/POST /api/host/print-settings（仅回环可写、保存即生效、单例 lock 跨线程可见）；JobPrintWorker「发送前暂停（claim-then-delay）」——领取下一张后、SendAsync 前按 BatchPrintPolicy.ShouldPauseBeforeSend（enabled && 已发送数满批次倍数）wait Task.Delay(batchIntervalMs)，计数内存态、跨作业全局累计、不持久化；本机 + 服务端作业统一生效，测试页直发不计入；WinHost 引入 Serilog 文件日志（Serilog.AspNetCore → %LOCALAPPDATA%\LabelFrame\logs\app-20260818.log，RollingInterval.Day）供批间间隔冒烟验证，host.log 通道不动 | 大批量控制打印节奏 / 减轻打印机压力；不拆作业、队列 / 幂等 / 挂起恢复 / 重打语义零改动；服务端进度仍为终态一次（增量进度回报未决 Q2，届时再讨论契约） |
| 75 | API 契约与端点共享库（迭代 27，2026-08-25） | 新增 `LabelFrame.Api`：Server / WinHost 重复的 DTO（SubmitJobRequest / TemplateDto / LabelDto / TemplatePackageDto / PreviewRequest / PushLogRequest / ExcelTemplate* / ErrorView）与模板 / 调试出图 / Excel / 日志端点收敛为共享实现（端点经 Options 传入各自错误码前缀，两宿主对外错误码不变）；xlsx 文本解析下沉 Core（ExcelTableReader），两宿主移除 TemplateFrame.Excel.Simple 直接引用 | 一处修复两端生效（AndroidHost 后续可复用）；共享后行为统一——WinHost 预览 DPI 取宿主配置并统一 Skia 同源渲染、数据缺省回退 testData；模板不存在错误码新增 LF_TPL_001（WinHost 原误用 LF_JOB_001，Server 保持 LF_SRV_006）；ErrorView 统一 Code / Message / FieldKey（Server 原两字段，向后兼容）；render-image(s) 图片解析 = base64 附带优先、按名回退模板库 |
| 76 | 日常 CI（迭代 27，2026-08-25） | 新增 `.github/workflows/ci.yml`：push master / PR 触发，dotnet restore / build / test + 前端 lint / 双模式测试 / 双模式构建（命令与 release.yml test job 一致）；同分支新推送取消旧运行；不改动发布流水线 | 主干回归在提交时即被发现（此前唯一工作流仅 v* tag 触发，是评审发现的最大质量关卡缺口）；AGENTS「非 CI 迭代不修改 CI 工作流」约束下，本项经用户批准的 P0 治理清单执行 |
| 77 | 数据层并发模型（迭代 28，2026-08-25） | 服务端领取 = 单条 `UPDATE ... RETURNING`（原子圈定 + 置 Claimed）；ServerService 信号量收窄到仅提交路径（requestId 幂等「查询-再插入」，DB request_id UNIQUE 兜底跨进程）；注册（单条 UPSERT）/ 领取 / 回报不再进程内串行化；WinHost 打印 Worker 空转改为 `HasPendingItemsAsync`（EXISTS）轻量探测后再走完整领取；TransportManager 配置 / 实例读写加锁、ApplyAsync 串行化 | 多设备并发操作不再全局排队；领取在多实例 / 并发下不重复（原 SELECT-后-UPDATE 无事务）；空闲时不再每 200ms 全量加载作业（含 ZPL） |
| 78 | 全局异常处理与错误契约（迭代 28，2026-08-25） | 两宿主接入共享 `GlobalExceptionHandler`：未捕获异常统一 500 + ErrorView（LF_INTERNAL_001 + 中文提示），不透出堆栈 / 内部路径；上传端点「catch(Exception)→400 且透出 ex.Message」改为确定性错误 400、意外故障 500；render 端点 base64 非法 400 + 中文原因（原裸 500 空响应体） | 状态码语义不失真（服务端故障不再误报 4xx）、不泄露内部信息；前端收到的错误形状统一（code / message / fieldKey） |
| 79 | 安全边界：局域网信任模型（迭代 28，2026-08-25） | 明确决策：定位内网部署，Server / WinHost API 不做鉴权（沿用决策 #62/#71/#72 的局域网模型）；插件包不加下载哈希校验——三层校验（zip 完整性 + manifest + ALC 预检）已覆盖传输损坏，而同信道哈希对主动篡改无防护意义，真实防护需签名 | 攻击面记录：局域网内任何主机可上传插件包 / 安装包（客户端会下载并执行插件 DLL）、可提交作业、可读日志。缓解 = 部署边界（内网 / 防火墙）+ 插件安装仍需本机界面操作 + 作业只投递到已注册设备。升级触发条件：跨网段 / 公网暴露、陌生第三方插件分发 → 先加插件包签名（manifest 加签名块 + 客户端验签），API 鉴权（token）次之 |
| 80 | 程序优化批次：SQLite WAL + 数据层基建 + 质量门禁（迭代 29，2026-08-25） | ① 全库连接启用 WAL（公共 SqliteSupport.OpenAsync 统一 PRAGMA，不可用静默回退）+ 四存储连接串 / 打开 / 时间格式化收拢公共 LabelFrame.Core.Data.SqliteSupport；② AnalysisLevel=latest-recommended + 警告即错误（AndroidHost 实验性除外），豁免逐项注明理由（CA2007/CA1031/CA1848/CA1873 + 测试目录 CA1707/CA1861）；③ 覆盖率只收集不设门禁（首份基线：Server 88% / Api 63% / WinHost 59% / Core 49% / Rendering 31%，类级均值） | WAL 下读写并发不再互阻（Server 长轮询 + 提交并发受益）；分析器清零过程顺带真修——ZPL 输出固定 InvariantCulture、三处信号量持有者实现 IDisposable、Forbid() 依赖认证设施改显式 403（原运行时 500）、UseExceptionHandler 配套 AddProblemDetails（原宿主启动即崩，集成测试发现）；死代码 LabelPreviewRenderer（GDI 预览）移除、Rendering 收敛单 TFM |
## 5. API 概览

错误响应统一为 `{ code, message, fieldKey? }`（问题码约定：`LF_API_xxx` 通用请求 / `LF_JOB_xxx` 作业 / `LF_IO_xxx` 传输 / `LF_TPL_xxx` 模板 / `LF_SRV_xxx` 服务端 / `LF_VAL_xxx` 校验）；未捕获异常统一 500 + `LF_INTERNAL_001`。

### 5.1 Server（默认 0.0.0.0:53961，无鉴权——局域网信任模型见决策 #79）

| 分组 | 端点 |
|---|---|
| 设备 | `POST /api/devices`（注册 / 心跳）、`GET /api/devices`（目录）、`GET /api/devices/by-ip/{ip}` |
| 作业 | `POST /api/jobs`（requestId 幂等；templateName 引用模板库或自包含 template；targetDeviceId / targetIp 定向）、`GET /api/jobs?deviceId=`（历史，按设备过滤）、`GET /api/jobs/{jobId}` |
| 投递 | `GET /api/devices/{id}/jobs/notify?timeout=`（长轮询通知 + 心跳保活）、`GET /api/devices/{id}/jobs/pending`（领取，Pending → Claimed 原子）、`POST /api/devices/{id}/jobs/{jobId}/result`（回报终态） |
| 模板 | `POST/GET /api/templates`、`GET/DELETE /api/templates/{name}`、`GET /api/templates/{name}/export`、`POST /api/templates/import`、`POST /api/templates/{name}/preview` |
| 调试出图 | `POST /api/print/render-image`（单张 PNG）、`POST /api/print/render-images`（批量 zip） |
| 分发 | `/api/client-packages`（列表 / 上传 / 下载 / 删除）、`/api/plugin-packages`（同前，含 manifest 元数据与 valid 状态） |
| Excel / 日志 | `POST /api/import/excel-template`（按契约生成模板）、`POST /api/import/excel`（解析表头 + 数据行）、`POST/GET /api/logs` |
| 其他 | `GET /api/server/info`、`GET /healthz` |

投递方式：宿主轮询（决策 #21）；设备离线作业暂存（决策 #22）。

### 5.2 WinHost（默认 127.0.0.1:53960，客户端本机）

模板 / 调试出图 / Excel / 日志端点与 Server 完全一致（共享实现 `LabelFrame.Api`，宿主前缀错误码除外）。宿主专属：

| 分组 | 端点 |
|---|---|
| 作业 | `POST /api/jobs`（自包含模板，本地打印）、`GET /api/jobs?limit=`、`GET /api/jobs/{id}`、`POST /api/jobs/{id}/suspend|resume|cancel`、`POST /api/jobs/{id}/items/{index}/retry`（失败项重打） |
| 连接 | `GET /api/transport`（当前连接 + 可用插件）、`POST /api/transport`（切换 / 测试，先测试后生效）、`GET /api/transport/plugins` |
| 插件 | `GET /api/plugins/installed`、`POST /api/plugins/install`、`POST /api/plugins/uninstall`（安装 / 卸载重启生效） |
| 机器级 | `GET/POST /api/host/config`（ServerUrl；仅回环可写）、`GET/POST /api/host/print-settings`（批次节流；仅回环可写）、`POST /api/host/shutdown`（仅回环） |
| 打印机 | `GET /api/printer/status`、`POST /api/printer/test` |
| 其他 | `GET /healthz`（含当前连接插件信息） |

## 6. 风险与未决问题

**真机 / 联调待确认**（集中管理见 [ACCEPTANCE-BACKLOG.md](ACCEPTANCE-BACKLOG.md)）：

- Zebra `~HS` 状态字段映射与 SDK 3.x `PrinterStatus` 语义（`GET /api/printer/status` 展示准确性）待真实设备确认。
- TCP 9100 无法感知缺纸 / 卡纸：以「发送异常 → 作业挂起」近似，真实缺纸语义待真机验证。
- Android PDA 宿主（排期见 ROADMAP 迭代 25）：前台服务厂商 ROM 保活差异、Android 16 的 16KB 页适配（SQLitePCLRaw）待真机验证。

**兼容性**：

- Zebra SDK 要求 Win10+；Win7/8 只能用 tcp9100 / winspool。
- net48 版 WinHost（HttpListener、netstandard2.0 约束）有真实需求再做。

**暂不做（有需求再排）**：

- 内嵌中文字体文件：加载机制已实现（内嵌优先、回退系统字体），实际字体文件（开源中文 TTF，体积大）未加入资源。
- `^GF` 数据量优化（二进制 / 压缩模式、字库缓存）。
- Server 暂存作业无过期策略（设备长期离线时需人工处理，可加过期 / 通知）。
- 契约字段 Pattern 校验（仅存储元数据，不执行）。
- 打印计数 / 库存联动（如需只提供事件接口）。
- 传输插件运行时热卸载 / 热替换（卸载 = 删文件 + 重启生效）。
- Server UI「仅在线设备可选」的提交竞态：现为前端提交时校验在线（尽力而为）；彻底消除需后端原子校验，会改变离线暂存语义（决策 #22），需要时再评估。
- 工程治理遗留：WinHost 专属端点 HTTP 集成测试（需先抽 host builder）；ServerService 提交幂等下沉 DB（多实例需求出现再做）；覆盖率阈值门禁（数据已在 CI 收集）。
- 插件包签名 / 服务端鉴权：升级触发条件见决策 #79；正式对外分发需购买 OV 代码签名证书（自签证书无法消除公开下载的 SmartScreen 提示）。
