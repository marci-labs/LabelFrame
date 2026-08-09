# LabelFrame 路线图

> 状态总览与迭代计划。每个迭代一条「启动命令」，复制给 AI 执行；完成即更新状态与 CHANGELOG。
> 设计细节见 [DESIGN.md](DESIGN.md)，需求见 [REQUIREMENTS.md](REQUIREMENTS.md)。

## 状态总览

| 迭代 | 主题 | 状态 |
|---|---|---|
| 0 | 奠基：文档体系 + 解决方案骨架 | ✅ 已完成 |
| 1 | 契约与 ZPL | ✅ 已完成 |
| 2 | WinHost 打印闭环 | ✅ 已完成（真实设备验收待执行） |
| 3 | Server 路由 | ✅ 已完成（真实设备验收待执行） |
| 4 | 模板管理 + 预览 | ✅ 已完成（真实设备抽查待执行） |
| 5 | PDA 宿主 | ✅ 已完成（真机验收待执行） |
| 6 | P1 收尾 | ✅ 已完成（蓝牙 / Android 项随迭代 5 受阻） |
| 7 | Studio 模板工具（V1） | ✅ 已完成（界面验收待执行） |
| 8 | Studio 版式编排（V2） | ✅ 已完成（界面验收待执行） |
| 9 | Excel 数据导入 | 📋 计划中 |
| 10 | MSI 安装包 | 📋 计划中 |
| 检查点 | 试点验收（成功衡量） | 待定 |
| 待需求 | 兼容与扩展（net48 / WMS 模板下发 / TSPL / 统计） | 待定 |

---

## 迭代 0：奠基（已完成）

**目标**：建立文档体系和解决方案骨架，让后续迭代可以独立会话执行。

**范围**：
- 文档：README（愿景）、AGENTS、DESIGN、REQUIREMENTS、ROADMAP、CHANGELOG。
- 解决方案骨架：`LabelFrame.slnx` + Core / Server / WinHost 项目（占位）、AndroidHost 目录占位。
- git 提交与推送。

**不在范围**：任何业务编码（契约模型、ZPL、API 等均在后继迭代）。

**验收**：
- `dotnet build LabelFrame.slnx` 通过。
- 文档覆盖：愿景、角色、场景、底线、能力、边界、成功衡量、决策记录、迭代计划。
- 仓库无公司 / 业务线品牌字样。

**启动命令**：
> 继续 LabelFrame 迭代 0（奠基）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md，按范围执行；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 1：契约与 ZPL（已完成）

**目标**：跑通「契约 → 校验 → ZPL」，用日志模拟打印机验证输出。

**范围**：
- `LabelFrame.Core`：LabelContract（字段清单）、LabelLayout（版式元素：文本 / 条码 / 二维码 / 图片 / 线，毫米坐标）、数据校验（必填缺失拒绝）。
- ZPL 编码器：文本、Code128、图片占位，毫米 → 点换算。
- 日志传输（模拟打印机）。
- 单元测试：golden test、校验用例。

**不在范围**：作业队列、HTTP API、真实打印机、中文位图（迭代 2）、Android。

**验收**：
- `dotnet test` 全绿。
- 库位码契约 → 校验 → ZPL 输出正确（含 `^BC`）。
- 缺必填字段时校验返回问题码。

**完成记录**（2026-08-09）：
- `LabelFrame.Core`：LabelContract / LabelField（含必填、类型、格式元数据）、LabelLayout（文本 / 条码 / 二维码 / 图片 / 线，毫米坐标）、LabelDocument。
- 数据校验：必填缺失（含空白）返回问题码 `LF_VAL_001`；格式校验留作后续。
- ZPL 编码器：文本（^A）、Code128（^BC，含 ^BY 模块宽度）、图片占位（^FX 注释），毫米 → 点按 DPI 换算；二维码 / 线元素在模型中定义，编码器明确报错待迭代 2。
- 日志传输（模拟打印机）：LogPrintTransport 写入 TextWriter。
- 单元测试 14 个全绿：库位码 golden test、校验用例、毫米换算、转义、不支持元素、日志传输。

**启动命令**：
> 继续 LabelFrame 迭代 1（契约与 ZPL）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 1 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 2：WinHost 打印闭环（已完成，真实设备验收待执行）

**目标**：Windows 上端到端打印闭环：作业队列 + 本地 HTTP API + 真实打印机。

**范围**：
- 作业队列：SQLite 持久化、幂等（requestId）、逐张状态、挂起 / 恢复 / 取消、批内顺序。
- 本地 HTTP API：提交（异步返回 jobId）、进度查询、错误码。
- 传输：TCP 9100、Windows 驱动（USB）。
- 中文渲染：内嵌字体栅格化为位图（^GF）。

**不在范围**：Server 路由（迭代 3）、模板管理（迭代 4）、Android（迭代 5）。

**验收**：
- 真实 Zebra（USB / IP）打出库位码，条码可扫（待真实设备）。
- 批量 50 张连续打印；缺纸挂起、恢复续打；服务重启不丢作业（队列语义已单测，真实设备验收待执行）。
- 中文标签真实打印可读（GDI 栅格化已单测，真实设备验收待执行）。

**完成记录**（2026-08-09）：
- `LabelFrame.Core`：作业模型 + SQLite 持久化队列（requestId 幂等、逐张状态、挂起 / 恢复 / 取消、批内顺序、重启把 in-flight 作业置挂起并重置在途 Item）；`LabelBitmap`（1bpp）+ ZPL `^GF`；TCP 9100 传输；版式元素自定义 JSON 转换器（`type` 判别）。
- `LabelFrame.WinHost`：本地 HTTP API（提交 / 查询 / 挂起 / 恢复 / 取消 / healthz，模板自包含）、打印 Worker、GDI 中文栅格化（内嵌 / 本地字体优先，回退微软雅黑）、传输 Log / TCP9100 / winspool raw / Zebra SDK（TCP / USB / 驱动）。
- 全项目升级 .NET 10；WinHost 目标 `net10.0-windows10.0.26100` 以集成 Zebra 官方 SDK（3.0.3355，避开 5.x 的 MAUI 依赖）。
- 测试 53 个全绿；端到端冒烟验证通过（提交 → 幂等 → Worker 打印 → 进度查询 → 校验 400）。

**启动命令**：
> 继续 LabelFrame 迭代 2（WinHost 打印闭环）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 2 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 3：Server 路由（已完成，真实设备验收待执行）

**目标**：设备注册 + 定向投递，多人 / 多设备并发打印互不干扰；无业务系统也能测试。

**范围**：
- Server：设备注册、设备目录、作业定向投递（请求带发起设备 ID）。
- WinHost 注册到 Server、接收作业。
- Server 测试入口（无业务系统也能提交打印、连打印机验证）。
- 作业状态集中可查。

**不在范围**：模板下发（P2）、Android（迭代 5）。

**验收**：
- 两台设备并发打印互不干扰（定向投递 + 设备轮询已实现，双设备联调待执行）。
- 作业状态可查；设备离线语义明确（离线暂存 Pending，上线轮询即领取）。

**完成记录**（2026-08-09）：
- `LabelFrame.Server`：设备注册 / 心跳 / 目录（在线状态）、作业定向投递（requestId 幂等）、宿主轮询领取（Pending → Claimed）、结果回报、集中查询；SQLite 持久化；测试入口页面（提交 / 设备 / 作业）。
- `LabelFrame.WinHost`：Server 路由客户端（注册 / 领取 / 回报）+ 路由 Worker（领取 → 本地队列打印 → 终态回报）；`LABELFRAME_SERVER_URL` 等配置。
- 默认端口调整：WinHost 53960 / Server 53961（避开 Hyper-V 排除端口段）。
- 测试 65 个全绿；端到端冒烟：业务提交 → WinHost 领取打印 → 回报 Completed。

**启动命令**：
> 继续 LabelFrame 迭代 3（Server 路由）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 3 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 4：模板管理 + 预览（已完成，真实设备抽查待执行）

**目标**：单机模板管理（增删改 + 导入 / 导出模板包）与设计期预览。

**范围**：
- 模板存储：本机文件 / SQLite；契约 + 版式 + 静态图片资源的「模板包」导入导出（zip）。
- 预览渲染：LabelDocument → PNG（设计期，PC）。
- 模板按项目 / 客户分组。

**不在范围**：WMS 模板下发（P2）。

**验收**：
- 模板包可在两台电脑间导入导出（zip：manifest.json + images/）。
- 预览与真实打印效果一致（抽查，待真实设备）。
- 模板按项目分组可用。

**完成记录**（2026-08-09）：
- `LabelFrame.Core.Templates`：模板包模型 + zip 序列化（导入导出）+ SQLite 模板存储（CRUD / 分组列表 / 图片资源）。
- `LabelFrame.WinHost`：模板 API（POST/GET/DELETE、导出 zip、导入 multipart、预览 PNG）；预览渲染（GDI 文本/线 + ZXing 条码/二维码 + 图片）；ZXing.Net 0.16.11。
- 测试 79 个全绿；冒烟验证：保存 → 预览 PNG → 导出 zip → 状态 → 测试页。

**启动命令**：
> 继续 LabelFrame 迭代 4（模板管理 + 预览）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 4 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 5：PDA 宿主（已完成，真机验收待执行）

**目标**：Android / PDA 上跑通「网页 → Server → PDA 宿主 → IP 打印机」与本地直连。

**范围**：
- AndroidHost：前台服务、开机自启、本地服务（本地 HTTP / JS 桥预留）。
- 传输：IP 9100；蓝牙在迭代 6。
- 注册 Server、接收定向投递。
- PDA 单张同步快捷路径。

**不在范围**：蓝牙（迭代 6）。

**验收**：
- PDA 网页 → Server → PDA 宿主 → IP 打印机打出物料码（真机验收待执行）。
- 开机自启、前台服务常驻；失败回执明确（真机验收待执行）。

**完成记录**（2026-08-09）：
- 用户更新 Visual Studio 后已安装 .NET Android workload（android 36.1.43），并补齐 JDK 17（Microsoft OpenJDK）与 Android SDK（platforms;android-36、build-tools 36.0.0）。
- `LabelFrame.AndroidHost`（net10.0-android）：前台服务（ForegroundService.TypeDataSync）+ 开机广播（BOOT_COMPLETED / MY_PACKAGE_REPLACED）、本地 HTTP（127.0.0.1:53970，TcpListener 极简实现）、IP 9100 传输（复用 Core）、Server 注册 / 轮询领取 / 回报（ServerPoller）、Android.Graphics 中文栅格化（^GF）、SQLite 作业队列（lib.e_sqlite3.android）。
- `dotnet build` 编译打包成功（com.labelframe.androidhost-Signed.apk，约 11MB）；`scripts/build-androidhost.ps1` 一键构建。
- 真机验收（PDA 网页 → Server → 宿主 → IP 打印机、开机自启、厂商 ROM 保活）待执行。

**启动命令**：
> 继续 LabelFrame 迭代 5（PDA 宿主）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 5 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 6：P1 收尾（已完成，Android/蓝牙项随迭代 5 受阻）

**目标**：补齐 P1 能力并完成试点验收准备。

**范围**：
- PDA 蓝牙传输（随迭代 5 受阻，待 Android workload）。
- 失败项单独重打（已完成）。
- 打印机测试页 / 在线状态（已完成；~HS / Zebra 状态字段待真实设备联调）。
- 模板按项目分组（迭代 4 已完成）。

**完成记录**（2026-08-09）：
- `LabelJobQueue.RetryItemAsync`：Failed Item → Pending（清错误），Failed 作业自动恢复 Pending。
- `GET /api/printer/status` + `POST /api/printer/test`；TCP `~HS` 基础解析、Zebra 连接即在线、驱动模式不可读回、Log 模拟在线。
- API `POST /api/jobs/{jobId}/items/{itemIndex}/retry`。
- 测试 79 个全绿。

**不在范围**：P2 项。

**验收**：各项有真实设备验收；试点指标（扫码通过率、重打 / 漏打率、批量成功率、耗时对比）可测量。

**启动命令**：
> 继续 LabelFrame 迭代 6（P1 收尾）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 6 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 7：Studio 模板工具（V1）（已完成，界面验收待执行）

**目标**：Windows 上可视化管理模板、导入并测试打印，不依赖命令行 / 手工 JSON。

**范围**：
- `LabelFrame.Studio`（WPF，net10.0-windows）：WinHost 连接管理（地址 / 一键启动 / 传输模式显示）。
- 模板管理：按分组列表、详情（契约字段 + 版式元素）、删除、导出 `.lfpkg`。
- 模板导入：文件选择 `.lfpkg` → 导入 WinHost 模板库。
- 测试打印：选模板 → 按契约字段生成数据表单 → 实时预览 PNG → 提交打印作业 → 查看状态 / 失败原因。
- 复用 WinHost API：`/api/templates*`、`/api/jobs`、`/api/printer/*`。

**不在范围**：版式可视化编辑（拖拽画布，V2）；模板分组管理界面（可后续补）。

**验收**：
- Studio 可列出 / 删除 / 导出模板，导入 `.lfpkg` 后立即可见。
- 选模板填数据 → 预览 PNG → Log 传输测试打印成功并显示作业状态。
- 一键启动 / 连接 WinHost（默认 127.0.0.1:53960）。

**完成记录**（2026-08-09）：
- `LabelFrame.Studio`（WPF，net10.0-windows）：连接管理（地址 / 一键启动 / 停止 / 传输模式显示）、模板列表（分组过滤）/ 详情 / 删除 / 导出、`.lfpkg` 导入、按契约字段生成数据表单、实时预览 PNG、提交测试打印作业并轮询状态。
- 全部复用 WinHost API（`/api/templates*`、`/api/jobs`、`/api/printer/*`）；WinHost healthz 增加 transport 字段。
- 测试 85 个全绿（StudioClient 6 个）；界面验收（打开 Studio 手动操作）待执行。

**启动命令**：
> 继续 LabelFrame 迭代 7（Studio 模板工具 V1）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 7 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 8：Studio 版式编排（V2）（已完成，界面验收待执行）

**目标**：在 Studio 里可视化编排模板版式（拖拽元素、缩放、属性编辑、字段编辑），保存后立即预览。

**范围**：
- 画布：按模板尺寸（mm）渲染，缩放（50%–200%）；元素拖拽移动、选中高亮、删除。
- 工具箱：添加文本 / 条码 / 二维码 / 图片 / 线元素。
- 属性面板：编辑 X/Y、宽/高、字体高宽、SourceKey、线宽等。
- 契约字段编辑：增删字段、必填 / 类型 / 显示名。
- 保存：POST /api/templates 全量保存（contract + layout）；刷新预览确认真实条码 / 二维码效果。

**不在范围**：画布内真实条码 / 二维码渲染（占位框 + WinHost 预览）；对齐线 / 吸附 / 撤销重做。

**验收**：
- 新建 / 打开模板 → 拖拽添加元素 → 调整属性 → 保存 → 刷新预览与打印测试一致。
- 字段增删后数据表单同步变化。

**完成记录**（2026-08-09）：
- `EditorWindow`（WPF）：画布按 mm 渲染（100% = 4px/mm，缩放 50%–250%），元素拖拽移动 / 选中高亮 / 删除；工具箱添加文本 / 条码 / 二维码 / 图片 / 线；属性面板编辑坐标 / 尺寸 / SourceKey / 字体 / 线宽；契约字段增删与必填 / 类型 / 显示名。
- 保存走 `POST /api/templates`（全量 contract + layout），刷新预览（WinHost preview PNG）确认真实条码 / 二维码。
- 测试 90 个全绿（Studio 11 个：加载 / 添加 / 换算 / 往返 / 保存校验）。

---

## 迭代 9：Excel 数据导入（计划中）

**目标**：Studio 里导入 Excel 数据，批量预览 / 打印。

**范围**：
- 选择 `.xlsx`，列 → 契约字段映射（自动按列名匹配，可手工调整）。
- 按行生成标签数据；批量预览（抽样）与「批量打印」。
- 复用 WinHost `/api/jobs`（一次提交多张）。

**不在范围**：Excel 模板导出、公式 / 样式保真。

**验收**：50 行 Excel → 一次提交打印成功，失败行可定位。

---

## 迭代 10：MSI 安装包（计划中）

**目标**：Windows 安装包，安装即配置好（WinHost + Studio）。

**范围**：
- WiX（或等效 MSI 工具）打包 WinHost / Studio / Core 运行产物。
- 安装时生成 appsettings.json：监听端口、传输模式（默认 Log，可改 TCP / Zebra / 驱动）、打印机地址 / 名称、数据库路径。
- 开始菜单快捷方式；可选把 WinHost 注册为 Windows 服务（自启）。
- 卸载清理数据目录选项。

**不在范围**：数字签名 / 自动更新。

**验收**：全新 Windows 机器安装后，打开 Studio 即连本机 WinHost（Log 模式）并可导入模板打印。

---

## 检查点：试点验收（待定）

按 [REQUIREMENTS.md](REQUIREMENTS.md) §8 成功衡量执行：
- 先测基线，再测新系统，同指标对比；
- 真实扫码枪抽 50 张；连续 100 张压力验证（含重启 / 断网）；
- 产出试点对比报告。

## 待需求（有真实需求再排）

- net48 版 WinHost（Win7 / 8 老电脑，尽量兼容）。
- WMS 模板下发（复用模板包格式）。
- 其他打印机指令集（TSPL / CPCL）。
- 打印历史统计。
- 多打印机并行。