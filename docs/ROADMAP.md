# LabelFrame 路线图

> 状态总览与迭代计划。每个迭代一条「启动命令」，复制给 AI 执行；完成即更新状态与 CHANGELOG。
> 设计细节见 [DESIGN.md](DESIGN.md)，需求见 [REQUIREMENTS.md](REQUIREMENTS.md)。

## 状态总览

| 迭代 | 主题 | 状态 |
|---|---|---|
| 0 | 奠基：文档体系 + 解决方案骨架 | ✅ 已完成 |
| 1 | 契约与 ZPL | ✅ 已完成 |
| 2 | WinHost 打印闭环 | ✅ 已完成（2026-08-17 真实设备验收通过） |
| 3 | Server 路由 | ✅ 已完成（2026-08-17 真实设备验收通过） |
| 4 | 模板管理 + 预览 | ✅ 已完成（2026-08-17 真实设备抽查通过） |
| 5 | PDA 宿主 | ✅ 已完成（真机验收待执行） |
| 6 | P1 收尾 | ✅ 已完成（蓝牙 / Android 项随迭代 5 受阻） |
| 7 | Studio 模板工具（V1） | ✅ 已完成（2026-08-17 界面验收通过） |
| 8 | Studio 版式编排（V2） | ✅ 已完成（2026-08-17 界面验收通过） |
| 8B | Studio 版式增强（字段编辑 / 元素样式 / 区域布局） | ✅ 已完成（2026-08-17 界面验收通过） |
| 8C | Studio 界面重构（工作台 + 设计器） | ✅ 已完成（2026-08-17 界面验收通过） |
| 8D | 设计器交互重做（容器 / 设计测试分离 / 字段自动推导 / 标尺对齐 / 多选手柄） | ✅ 已完成（2026-08-17 界面验收通过） |
| 9 | Excel 数据导入 | ✅ 已完成 |
| 11 | 单机模式（Host 服务化 + Web Vite/TS 前端 + PDA 测试链路） | 🔄 进行中 |
| 12 | 模板预览值持久化 + 图片打印实验 | ✅ 已完成（2026-08-17 前端 renderLabelImage 取消，不再实施） |
| 13 | 文本排版与二维码参数持久化（元素契约补齐） | ✅ 已完成（2026-08-17 用户验收通过） |
| 14 | 字体加粗（bold）契约 | ✅ 已完成（2026-08-17 联调验收通过） |
| 15 | 打印设置与会话保留 + 连接管理 + 删除 ZPL（图片打印收敛） | ✅ 已完成（2026-08-17 联调验收通过） |
| 16 | 服务端 / 客户端拆分（双安装包） | ✅ 已完成（2026-08-17 用户验收通过） |
| 18 | 无头服务端 + 客户端 UI 回归 + Windows 服务 + 历史清理 + 推送通知（0.15.4） | ✅ 已完成（2026-08-17 联调验收通过） |
| 19 | Ubuntu 服务端部署 + 跨机验证（服务端 Linux / 客户端 Windows） | ✅ 已完成（2026-08-17 真机部署验收通过） |
| 8E | Web 设计器原型 v2（视口缩放 / 条码二维码实时渲染 / 智能参考线 / 文本溢出模式） | ✅ 已完成 |
| 8F | Web 设计器原型 v3（画布留白 + 标尺 / 真实比例 1mm=8点 / 边界约束 / 拖入修复） | ✅ 已完成 |
| 10 | MSI 安装包 | ✅ 已完成 |
| 20 | 服务端管理界面（插件式 UI）+ 设备 IP | ✅ 已完成（2026-08-17 联调冒烟通过） |
| 21 | 自动化发布（ghcr + GitHub Release + MSI 签名通道） | ✅ 已完成（v0.17.0 自动发布成功；ghcr 包已公开） |
| 22 | 打印测试体验 + 传输插件化 + 客户端下载分发 | ✅ 已完成（2026-08-17 迭代结束，本地 0.18.0 测试包验收） |
| 23 | 客户端插件分发——上传服务端 + 客户端安装 / 卸载 | ✅ 已完成（2026-08-17 前端完成 + loadError 补充 + 0.19.0 打包验收） |
| 24 | 客户端批次作业（Batch Print） | ✅ 已完成（2026-08-18：前后端合入 master 67214c3 + 端到端联调附五通过 + Serilog 日志命名修复） |
| 25 | Android PDA 宿主（AndroidHost） | 📋 延后（PDA 事项延后，再排期） |
| 26 | Niimbot 蓝牙打印机传输插件实现 + 真机测试 | 📋 下一轮（顺延自迭代 24，2026-08-18） |
| 27 | 工程治理 P0（日常 CI + API 契约与端点去重 + README/DEPLOY 重组） | ✅ 已完成（2026-08-25） |
| 检查点 | 试点验收（成功衡量） | ✅ 已完成（2026-08-17：扫码枪 50 张 + 连续 100 张压力验证通过） |
| 待需求 | 兼容与扩展（net48 / WMS 模板下发 / TSPL / 统计 / 契约 Pattern 校验） | 待定 |

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

## 迭代 8B：Studio 版式增强（已完成，界面验收待执行）

**目标**：编辑器体验补齐：字段键与显示名可编辑、画布尺寸显眼、新元素默认下排、元素支持文字对齐 / padding / 边框；支持「先画格子（区域）再放元素居中」的编排模式。

**范围**：
- 字段编辑：键（Key）与显示名可直接编辑；重命名字段时同步更新已引用该字段的元素 SourceKey。
- 画布信息：工具栏显眼显示「标签尺寸：宽 × 高 mm」，不随窗口缩放变化。
- 新元素默认排在上一个元素下方（上下结构为主，局部再手动调整为左右）。
- 元素样式（契约扩展，决策 #33）：文本 WidthMm / TextAlign / PaddingMm / BorderMm；矩形元素 BorderMm。
- 区域（格子）布局：`LabelRegionElement` 容器 + 元素 `RegionId` / `RegionHAlign` / `RegionVAlign`；ZPL 输出区域边框并用 `^FB` / 计算定位实现格内对齐；预览同步。

**不在范围**：拖拽自动吸附进格子（先手动选区域 + 对齐）；对齐线 / 撤销重做。

**验收**：
- 新增字段即可编辑键与显示名，改键后元素引用跟随。
- 添加多个元素默认上下排列；可拖到任意位置。
- 画一个库位模板：上半区域放二维码（格内居中、可缩放），下半区域放文本（居中）；保存后预览与打印测试一致。

**完成记录**（2026-08-09）：
- 字段编辑：键 / 显示名 / 必填 / 类型可编辑；重命名自动同步元素 SourceKey。
- 画布：工具栏显眼显示「标签尺寸：宽 × 高 mm」；新元素默认排在上一个下方（超界自动回顶）。
- 元素样式（契约扩展，决策 #33）：文本 WidthMm / TextAlign / PaddingMm / BorderMm；矩形元素 BorderMm；ZPL 用 ^FB / ^GB 实现，预览同步。
- 区域（格子）布局：LabelRegionElement + 元素 RegionId / 对齐锚定；ZPL 与预览按 LabelLayoutResolver 统一计算位置；二维码 ^BQ 与线 ^GB 已补全。
- 测试 99 个全绿（ZPL 区域 / 对齐 / 边框 / 二维码 / 线 / JSON 往返 / Studio 字段联动 / 下排 / 区域往返）。

---

## 迭代 8C：Studio 界面重构（已完成，界面验收待执行）

**目标**：按用户使用顺序重做界面：新建模板 → 画布设计（控件栏拖拽 / 属性分组 / 填充 / 区域 / 实时预览）→ 打印测试 → 返回作业工作台（选模板 / 填数据 / 打印）；状态与日志在底部栏。

**范围**：
- 共享渲染库 `LabelFrame.Rendering`：从 WinHost 抽出预览渲染（GDI + ZXing），WinHost 与 Studio 共用；Studio 画布/预览本地实时渲染。
- 契约扩展（决策 #35）：文本 / 条码 / 二维码支持 `Literal` 固定值或 `SourceKey` 字段填充。
- 作业工作台（主窗口）：模板列表（分组）/ 预览 / 字段数据表单 / 打印 / 状态栏 + 日志栏；导入数据按钮占位（迭代 9）。
- 模板设计器（独立窗口）：控件栏拖元素、画布拖拽 / 区域（拖矩形、元素拖入自动锚定居中）/ 属性面板分组（位置大小 / 字体 / 填充 / 边框 / 对齐 / 锚定）、实时预览、打印测试。
- 菜单栏收拢不常用功能：模板包导入导出、连接设置、打印机状态 / 测试页。

**不在范围**：Excel 导入（迭代 9）；拖角缩放 / 对齐线（若本轮未完成则列入迭代 8D）。

**验收**：
- 新建模板（只问纸张）→ 画布；拖元素、改填充 / 样式，画布实时变化。
- 画区域 → 拖元素入格自动居中；保存 → 打印测试 → 底部状态栏显示结果。
- 返回工作台：模板列表可选、填数据、打印；状态 / 日志可看。
- 测试 105 个全绿。

**完成记录**（2026-08-09）：
- 共享渲染库 `LabelFrame.Rendering`：预览渲染从 WinHost 抽出，WinHost / Studio 共用（本地实时预览，不依赖网络）。
- 契约扩展（决策 #35）：文本 / 条码 / 二维码支持 `Literal` 固定值或 `SourceKey` 字段填充（向后兼容）。
- 作业工作台（主窗口重写）：菜单栏（新建 / 导入导出 / 打印机状态 / 测试页）、模板列表、本地预览、数据表单、打印、底部状态 + 日志栏。
- 模板设计器（独立窗口，替代旧 EditorWindow）：控件栏（点击添加 / 拖入画布）、画布（毫米网格 / 选择移动 / 画区域拖矩形 / 元素拖入区域自动锚定居中 / 移出解除锚定）、属性分组（位置尺寸 / 文本字体 / 填充 / 内边距边框 / 区域锚定）、测试数据、实时打印预览（350ms 节流）、打印测试、底部状态 + 日志。
- 未完成项（迭代 8D）：拖角缩放、对齐线 / 标尺。

---

## 迭代 8D：设计器交互重做（已完成，界面验收待执行）

**目标**：按用户确认的方案重做设计器：容器控件替代“画区域”、设计 / 测试 Tab 分离、契约字段后台自动推导、标尺 / 对齐吸附 / 框选多选 / 拖角缩放等编辑手感，并修复上一轮缺陷（拖拽重复建元素、固定值不实时渲染、日志栏不横跨 / 不滚底 / 不清空、属性面板常驻）。

**范围**：
- 设计器布局：顶部工具栏（模板名 / 分组 / 纸张尺寸 / 缩放 / 网格 / 保存 / 完成）、左侧控件栏（文本 / 条码 / 二维码 / 图片 / 线 / 容器）与只读字段列表、中间带标尺画布、右侧属性面板（选中控件才显示）、底部横跨状态 + 日志栏；设计 / 测试用 Tab 分开。
- 画布交互：左键选中、8 手柄拖角缩放、Shift / 拖框多选、Delete 删除、中键平移、Ctrl+滚轮缩放（以鼠标为中心）、毫米网格 + 标尺、边缘 / 中心对齐吸附、右键对齐菜单（左 / 水平居中 / 右 / 上 / 垂直居中 / 下）。
- 容器控件：控件栏拖「容器」矩形；元素拖入自动锚定居中；属性面板移除 RegionId / 锚定 UI（后台保留）。
- 契约字段自动推导：字段集合 = 版式元素填充 key 去重；移除字段增删 / 重命名 / 显示名 UI；工作台与测试表单用 Key 作标签。
- 缺陷修复：控件栏拖拽不重复建元素；元素属性变化实时重绘画布与预览；日志栏自动滚底 + 清空按钮。

**不在范围**：Excel 导入（迭代 9）；MSI（迭代 10）；真实条码“所见即所得”渲染（未决）；多选组合 / 撤销重做（未排期）。

**验收**：
- 新建模板 → 拖「容器」划分区域 → 拖元素入容器自动居中；左键选中显示 8 手柄可缩放；中键平移、Ctrl+滚轮缩放、Delete 删除、拖框多选后对齐。
- 固定值与样式修改实时反映在画布与预览；设计 / 测试 Tab 分离；属性面板仅选中时显示。
- 底部状态 + 日志横跨全窗口，日志自动滚动到底、可一键清空。
- `dotnet build` / `dotnet test` 通过（Studio 测试适配字段自动推导）。

**完成记录**（2026-08-09）：
- 设计器重做：设计 / 测试 Tab 分离；控件栏可拖拽项（点击一次添加，修复重复建元素）；标尺 + 网格、左键选中、8 手柄缩放、Shift / 框选多选、Delete 删除、中键平移、Ctrl+滚轮缩放（以鼠标为中心）、边缘 / 中心吸附、右键对齐菜单；属性面板选中才显示；底部状态 + 日志横跨全窗口（自动滚底 + 清空）；固定值 / 样式实时重绘与预览。
- 容器控件替代“画区域”（后台仍为 LabelRegionElement，模板格式不变）；字段后台自动推导（SourceKey 去重，保留旧字段元数据）；移除字段编辑 UI，工作台 / 测试表单用 Key 作标签。
- 测试 109 个全绿（新增字段推导 / 多选删除 / 对齐 / 吸附用例）。

**启动命令**：
> 继续 LabelFrame 迭代 8D。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 8D 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---
## 迭代 8E：Web 设计器原型 v2（已完成）

**目标**：按用户对原型的反馈改善体验：视口缩放模型、条码 / 二维码实时渲染、智能参考线、边框修正、控件精简、文本溢出模式。

**范围**：
- 视口缩放模型：画布容器自动铺满视口（随窗口自适应）；Ctrl+滚轮只缩放画布内容（以鼠标为中心）；「适应窗口」/「实际大小」按钮，设计态与真实尺寸预览分离。
- 条码 / 二维码实时渲染：值变化立即渲染真实条码 / 二维码（JsBarcode / qrcode-generator 本地化）；属性面板预留条码（码制 / 底部文字 / 模块宽）与二维码（纠错级别 / 边距）参数分组。
- 智能参考线：拖动时吸附画布边缘 / 中心与其它元素边缘 / 中心，显示参考线（参考 Figma / Konva snapping 方案）。
- 边框修正：边框为矩形元素外框描边，不描文字。
- 控件栏精简为文本 / 条码 / 二维码（图片 / 线 / 容器移除入口，已有模板仍可加载显示）。
- 文本溢出模式：每元素可配置「自动换行 / 超长截断 / 缩小字体」（参考 BarTender Auto-Fit / Cleverence Label）。

**不在范围**：图片 / 线 / 容器的新交互逻辑（待定）；精确 96dpi 实际大小（当前 1mm≈4px）；正式 UI 迁移（等选型结论）。

**验收**：浏览器打开 index.html 可拖拽设计；条码 / 二维码输入值立即渲染；拖动出现参考线并吸附；文本可切换溢出模式；「适应窗口 / 实际大小」与 Ctrl+滚轮缩放行为正确；连接 WinHost 可加载 / 保存 / 预览。

**完成记录**（2026-08-09）：
- 原型 v2 完成并通过 headless 浏览器自测（元素添加 / 条码二维码渲染 / 页面初始化无异常）。
- 本地化依赖：konva.min.js（9.3.18）、jsbarcode.min.js（3.11.6）、qrcode.min.js（qrcode-generator 1.4.4）。
- 待用户本机验收手感后决定 UI 技术栈（Tauri 2 / Blazor Hybrid / 维持 WPF）。

---
## 迭代 8F：Web 设计器原型 v3（已完成）

**目标**：按第二轮反馈改善原型：画布留白 + 标尺跟随、画布平移不越界、容器不再手动缩放、真实比例 1mm=8 点、文本溢出第四种模式、修复控件拖入不可见。

**范围**：
- 画布实际大小 = 输入尺寸 + 四周 10mm 留白；标尺以 mm 覆盖整个画布并随画布移动 / 缩放；标签内容区边缘在标尺以蓝色刻度标出。
- 中键平移 clamp：画布不超出可视边界。
- 默认画布铺满视口（容器不再手动缩放）；「实际大小」= 1mm=8 点（203dpi 打印比例），可滚动 / 平移查看。
- 文本溢出新增「不限制高度」（按内容实际高度显示全部文字）。
- 修复：控件拖入画布坐标改为基于 clientX/Y 几何换算（HTML5 拖拽期间 Konva 无指针事件，原实现会把元素放到错误 / 越界位置导致看不到）。

**不在范围**：手动参考线（已确认延后）；图片 / 线 / 容器新交互；正式 UI 迁移。

**验收**：拖入控件即可见且位置正确；标尺覆盖含留白的全画布并随画布移动；平移画布不越界；「实际大小」= 1mm=8 点；文本可选「不限制高度」。

**完成记录**（2026-08-09）：原型 v3 完成；二 ~ 四轮修复渲染模型、标尺、平移、控件可见性、吸附定位、二维码、边框内边距通用化、文本模式（缩小适应 / 隐藏）；第五轮：字高独立、内边距上下 / 左右、填充默认固定值、Ctrl+C/V；第六轮：Ctrl+Z/Y 撤销恢复、字高调大才撑高、吸附强化、导出 / 导入设计（剪贴板 JSON）、矩形控件、文本框高度字段；吸附落点修复；第七轮：矩形镂空、图层面板；第八轮：网格吸附兜底、字段填充提示；第九轮：真实 DPI 打印预览、纯打印效果、预览仅显示标签范围；第十轮：文本框自动换行（超右换行、超下隐藏）+ 行间距 + 字体选择 + 垂直对齐（顶 / 中 / 底）；填充切换清理；图层显示名称优化；**纯前端编辑器化**（移除后端按钮，导出 / 导入走快捷键）；headless 自测通过；待用户本机验收后进入「桌面壳」阶段。

---
## 迭代 13：文本排版与二维码参数持久化（元素契约补齐，已完成，用户验收待执行）

**目标**：补齐元素契约缺失字段（wrap / lineHeight / fitMode / fontFamily / qrEcc / qrMargin / displayValue / paddingH-V），导入→保存→重开逐字段一致；Skia 图片打印按这些字段真实渲染，与前端预览一致；旧模板向后兼容。
**范围**：
- 后端：C# 模型属性 + LabelElementJsonConverter 读写（非默认才写）+ SkiaLabelRenderer 渲染支持（wrap 换行+行距+超高整体缩小、overflow 隐藏、fontFamily、qrEcc/qrMargin、displayValue、paddingH/V）+ VerticalAlign 默认统一 Middle + 旧模板框高兜底 10mm + 测试。
- 前端（hermes）：convert.ts 字段映射 + convert.test.ts；ElementNode wrap=true 超高改整体缩小。
**不在范围**：ZPL 矢量路径（新字段不参与）；barcodeFormat（仅 CODE128）。
**验收**：见 docs/ITERATION-13-CONTRACT.md §6。
**完成记录**（2026-08-10，前后端已完成，用户验收待执行）：
- C# 模型补齐元素契约第二批字段：文本 `wrap / lineHeight / fitMode / fontFamily`（默认 Microsoft YaHei）、二维码 `qrEcc / qrMargin`（默认 M / 2）、条码 `displayValue`（默认 true）、通用双边内边距 `paddingH / paddingV`（`PaddingHMm / PaddingVMm`，0=未设，缺失时回退 `paddingMm`）。
- 决策 A：`VerticalAlign` 默认由 Top 改为 **Middle**；写规则改「非 Middle 才写」；Skia 渲染器旧模板无 `heightMm` 时框高兜底 = `max(字高 + 2×max(双边内边距), 10mm)`。
- `LabelElementJsonConverter` 读写：非默认才写（wrap=true、displayValue=false、fitMode 非 shrink、lineHeight 非 1.2、fontFamily 非默认、qrEcc 非 M、qrMargin 非 2、paddingH/V >0、verticalAlign 非 Middle），旧模板无新字段读回默认（向后兼容）。
- `SkiaLabelRenderer` 渲染支持：wrap 自动换行 + 行距（lineHeight 倍数）+ 超高整体缩小（最小 1.5mm）、overflow 隐藏裁剪、fontFamily 字体族（CJK 系统回退）、qrEcc / qrMargin 传 ZXing、条码 displayValue 底部文字（条码占剩余高度）、双边内边距内容区；以上字段不参与 ZPL 矢量编码（契约 §4.9）。
- 测试 152 个全绿（新增字段往返 / 省略规则 / paddingMm 兜底 / wrap 换行与超高缩小 / overflow 不缩小 / 字体族 / QR 参数与静区 / 条码文字 / 双边内边距 / 旧模板默认 Middle / ZPL 不变量）。
- 前端（hermes，commit 8294bef）：convert.ts 字段映射（paddingH/paddingV/fontFamily/wrap/lineHeight/fitMode/qrEcc/qrMargin/displayValue，写方向非默认才写、读回 ?? 默认 + paddingMm 兜底）；ElementNode TextContent wrap=true 超高改整体缩小（最小 1.5mm，与 Skia §4.4 一致）；convert.test.ts 64 用例全绿（+7 新增）。
- 复现验证：100×60 方案往返关键差异清零（wrap/lineHeight/qrEcc/paddingV 均保留；剩余仅默认值显式化，显示一致）。
- 文档归档：ITERATION-13-SPEC / CONTRACT 标记已完成；DESIGN 决策 #47 更新为前后端完成；CHANGELOG 记录。
- 产物 `LabelFrame-0.12.1.msi`（2026-08-10）：含迭代 13 前后端合并版（元素契约第二批字段 + Skia 渲染 + 前端映射），用户测试验收待执行。
- 前端修复（0.12.1，commit abf58a0）：画布中文长文本字高失真——含 CJK 文本改用 wrap=`char` 逐字换行（与 Skia 打印语义一致），shrink 按行数估算换行后总高只对超高缩小；70×50 方案 MaterialName 修复后两行 2.85mm；64 单测 + build + lint 全绿。产物 `LabelFrame-0.12.1.msi`。

**启动命令**：
> 继续 LabelFrame 迭代 13（后端）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ITERATION-13-SPEC.md、docs/ITERATION-13-CONTRACT.md（含 Hermes 评估结论，已通过）。按 ITERATION-13-CONTRACT.md §3 字段对照、§4 Skia 渲染语义、§7 分工实施后端：C# 模型属性（wrap/lineHeight/fitMode/fontFamily/qrEcc/qrMargin/displayValue/paddingH/paddingV，VerticalAlign 默认改 Middle，PaddingHMm/PaddingVMm）、LabelElementJsonConverter 读写（非默认才写）、SkiaLabelRenderer 渲染支持、测试与验收；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

## 迭代 16：服务端 / 客户端拆分（双安装包，已完成，用户验收待执行）

**目标**：把单机 WinHost 拆分为两个部署包——服务端（模板 / 作业 / 设备投递 / Web UI / 调试出图 / 日志，无打印机依赖）与客户端（本机打印执行 / 作业领取 / 连接配置，托盘部署），多台打印 PC 共用一个服务端；保留单机模式作为旧版迁移路径。
**范围**：见 docs/ARCHITECTURE-SPLIT.md（职责边界 / 跨端契约 / 部署 / 迁移 / 实施规划）。
**不在范围**：PDA（延后）；云部署 / 服务端高可用；多语言。
**验收**：见 docs/ARCHITECTURE-SPLIT.md §7 完成定义。
**完成记录**（2026-08-11，后端骨架）：
- Server 迁入模板库（CRUD / 导入导出 / 预览，Core.Templates）、作业提交支持 `templateName` 引用（pending 载荷附带模板 + 图片 base64）、调试出图（render-image / render-images）、日志接收（SqliteLogStore 移至 Core.Logs）、Excel 导入、Web UI 静态托管（SPA fallback）；TFM 改 net10.0-windows 以引用 Skia Rendering。
- Client（WinHost）：TemplateDto 增 Images（base64），JobSubmissionService 优先用内联图片、否则按 Name 本地加载；路由 Worker 透传 Server 附带模板；保留单机模式。
- 测试 147 全绿（Core 60 / Server 10 / Studio 25 / WinHost 52）；Server 新增 templateName 解析 / 模板不存在用例。
- 前端（hermes）待实施（迭代 17）：Web UI 指向 Server（移除打印机连接项）、数据与打印新增目标设备选择。
- 前端（hermes，e161d81）：移除打印机连接 UI、目标设备选择（listDevices + targetDeviceId + templateName）、单机降级；105 用例全绿。
- 双 MSI（0.14.0）：LabelFrame-Server → Program Files\LabelFrame\Server（默认 0.0.0.0:53961）；LabelFrame-Client → Program Files\LabelFrame\Client（默认 ServerUrl=127.0.0.1:53961）；文件清单 GUID 按包加盐。
**启动命令**：
> 继续 LabelFrame 迭代 16。先读 AGENTS.md、docs/ARCHITECTURE-SPLIT.md、docs/ROADMAP.md；按拆分设计实施：Server 迁入模板/作业/Web UI/调试出图/日志，Client 默认路由领取，pending 附带模板；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

---
## 迭代 15：打印设置与会话保留 + 连接管理 + 删除 ZPL（前后端已完成，联调验收待执行）

**目标**：① 数据与打印页会话保留（同一标签页内切视图不丢设置、标签页间不互通）；② 前端切换连接方式（Log / TCP / Windows驱动 / Zebra，单一连接生效，先测试后生效、失败回滚、持久化）；③ 彻底删除 ZPL（Vector），打印统一整版位图（Skia + ^GF），调试独立为「只出图不发送驱动」。
**范围**：见 docs/ITERATION-15-SPEC.md。
**不在范围**：新传输协议（蓝牙等）实现（仅留扩展点）；WPF Studio；Server 路由既有契约（仅 JobView 增可选 debugImagePaths、SubmitJobRequest 增可选 debug）。
**验收**：见 docs/ITERATION-15-SPEC.md §8。
**完成记录**（2026-08-10，后端部分）：
- 删除矢量 ZPL：`IZplEncoder` / `ZplEncoder.Encode` / `ZplBoldMode` / `PrintMode` / `printMode` / `ITextRasterizer` / `GdiTextRasterizer` 全链路移除（配置、healthz、UI 字段、README、demo 脚本）；`^GF` 编码重构为 `ZplImageEncoder`；作业项内容统一为整版位图指令（沿用列名）。
- 连接管理：`ITransportManager` + `TransportConfig`，`GET/POST /api/transport`（单一连接、先测试后生效、失败回滚、400 沿用 ErrorView），持久化 `%LOCALAPPDATA%\LabelFrame\connection.json`（启动优先级 connection.json > appsettings > 默认 Log）；Tcp / Raw / Zebra 增加连接测试；Worker / 状态 / 测试页统一取当前连接；测试页改为 Skia 渲染 ^GF。
- 调试出图：`POST /api/print/render-images`（批量 zip）；`render-image` 保留（单张 PNG）；调试不建作业、不发驱动、不改作业模型 / SQLite。
- Log 模拟打印：`LogPrintTransport` 只记摘要，作业层渲染 PNG 保存到 `print\{jobId}\`。
- AndroidHost：`AndroidLabelRenderer`（Android.Graphics + ZXing）整版位图渲染 → `ZplImageEncoder`，替换 ZplEncoder（真机验收待 PDA 联调）。
- 测试 143 全绿（Core 60 / Server 8 / Studio 25 / WinHost 50）；AndroidHost 编译通过。
- 产物 `LabelFrame-0.13.2.msi`（2026-08-10）：含迭代 15 前后端合并版，可覆盖 0.12.x / 0.11.x 安装；用户测试验收与 PDA 联调待执行。
**启动命令**：
> 继续 LabelFrame 迭代 15。先读 AGENTS.md、docs/ITERATION-15-SPEC.md（含已确认决策）；hermes 评估前端无异议后，后端实施 §3.1/§4/§5，前端实施 §3.2/§6；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

**完成记录**（2026-08-10，前端部分，hermes）：
- §3.2 删除：DataPrint / Settings 的 printMode 下拉与旧「调试：不打印保存 PNG」复选框、`Healthz.printMode` / `SubmitJobRequest.printMode` 类型、Settings 服务端打印方式提示。
- §6.1 会话保留：AppContext 增 `printDraft`（selectedName / valuesByTemplate + dirtyKeysByTemplate / debugMode / jobId），sessionStorage 持久化（刷新保留、标签页天然隔离，禁 localStorage）；values 加载 = testData 与用户 dirty 的 key 按 **key 存在性**合并（用户清空不被 testData 顶回）；Excel 数据与列映射不保留。
- §6.2 连接管理 UI：AppContext 增 `transportConfig`（GET /api/transport），切换成功后立即用响应 config 更新全局状态（不依赖 healthz 10s 轮询）；设置页「连接方式」分组（模式单选 / 只显示当前模式参数 / 测试连接 testOnly / 保存并应用先测试后生效失败回滚）；DataPrint 顶部连接徽标 + 快速切换；状态栏 / 导航徽标显示 mode + 关键参数。
- §6.3 调试独立：独立开关（默认关）与按钮文案联动（调试开 →「调试出图（单张）」/「下载调试图片 zip（N 张）」，隐藏「出图预览」；调试关 →「打印测试 / 批量打印」正常作业 +「出图预览」即时预览）；下载文件名用后端 Content-Disposition 值。
- 测试 91 全绿（新增 27 个：draft 纯逻辑 12 + 设置页连接切换 5 + DataPrint 保留 / 调试 / 下载 10）；`pnpm build` / `pnpm lint` 通过；与后端工作区实现联调通过（/api/transport、render-image、render-images）。

---
## 迭代 14：字体加粗（bold）契约（前后端已实施，联调验收待执行）

**目标**：小字号（1.8~3mm）文本打印笔画过细，提供「加粗」设置试印对比；文本元素 JSON 新增 `bold?: boolean`（true 才写 / 默认 false，旧模板兼容）。
**范围**：
- 前端（hermes，已提交 ae16d0d）：属性面板加粗复选框 + 画布 `fontStyle: bold`；convert.ts 字段映射 + 单测；属性面板两项修复（数字输入受控同步、右侧面板滚动条）。
- 后端（本仓库）：`LabelTextElement.Bold` + 转换器读写；ZPL 加粗方案 A（粗体字体变体映射，默认 `"0"→"1"`，可配置 `ZplBoldMode` / `LABELFRAME_BOLD_MODE`）与方案 B（宽度放大兜底）；Skia `Embolden` 渲染与度量一致。
**不在范围**：条码 / 二维码加粗（仅文本）；字体文件打包。
**验收**：见 docs/ITERATION-14-SPEC.md §4.3。
**完成记录**（2026-08-10，后端部分）：模型/转换器/ZPL/Skia 已实施，测试 156 全绿；产物 `LabelFrame-0.12.3.msi`（含迭代 14 前后端合并版），待用户试印对比与联调验收。

---
## 迭代 12：模板预览值持久化 + 图片打印实验（规格评审中）

**目标**：修复字段填充控件预览值保存后丢失；让预览值自动成为打印测试默认值；提供整版位图直传打印的实验模式，评估打印效果与定位。
**范围**：
- 前端：元素 JSON 增加并读写 `previewValue`；测试默认值统一由元素预览值生成；DataPrint 预填提示；（可选）打印方式切换下拉。
- 后端：元素模型 + JSON 转换器支持 `previewValue`；保存模板时自动派生 `testData`；新增 `PrintMode`（Vector / Image）与整版位图 `^GF` 打印；`SubmitJobRequest` 增加 `template.name` / `printMode`。
**不在范围**：图片打印方案的最终定型（先实验评估）；其他打印指令集（TSPL 等）。
**验收**：见 `docs/ITERATION-12-SPEC.md` 第 6 节。
**进度（2026-08-10）**：规格 v3 双方确认；**后端已完成**：previewValue / testData 读改写 / PrintMode 图片打印 / template.name / healthz，且图片渲染定稿为 **SkiaSharp**（0.11.5，修复 CJK 字段缺失，133 个测试全绿，70×50 模板端到端验证通过）；待前端（hermes）按第 3 节实施 `renderLabelImage` 后联调。
**收尾（2026-08-17）**：前端 `renderLabelImage` 按用户决定取消（不再实施），迭代 12 关闭；后端 previewValue / PrintMode 图片打印能力保留。
**启动命令**：
> 继续 LabelFrame 迭代 12。先读 `docs/ITERATION-12-SPEC.md`、`docs/DESIGN.md`、`docs/ROADMAP.md`；前端按规格第 3 节实施，后端已完成第 4 节；提交用 Conventional Commits；不推 tag。

---## 迭代 11：单机模式（进行中）

**目标**：单机模式打印测试闭环：一台 PC = 一个 C# 进程（演进 WinHost）+ 浏览器（Vite + React + TS 前端）；从 PC 到 PDA 的测试链路逐个走通。

**范围**：
- 后端（C#，主 agent 实施）：
  - WinHost 演进：静态托管 `web/dist`；模板 API 增加 `testData`（契约扩展 #41）；新增 `POST /api/import/excel`（复用 TemplateFrame.Excel.Simple）与 PDA 日志端点（`POST/GET /api/logs`）。
  - AndroidHost 演进：配置指向 PC Host，拉模板列表 → 测试打印（用服务端 testData）→ 日志回传。
- 前端（hermes 按 `docs/FRONTEND-SPEC.md` 实施）：`web/` Vite + React + TS + Konva；工作台 / 设计器（移植原型交互）/ 数据与打印（Excel 导入 + 批量打印）/ PDA 日志 / 设置。
- 联调（主 agent）：前端产物与后端 API 对接，走通单机打印测试与 PC→PDA 链路。

**不在范围**：远端模板服务器（迭代 12）；结构优化（迭代 13）；WPF Studio 维护（冻结）。

**验收**：
- 单机：启动 WinHost（含静态 UI）→ 浏览器编辑模板 → 保存 → Excel 导入批量打印 → 作业进度/失败可见。
- PDA：配置 PC 地址 → 模板列表 → 点模板测试打印（testData）→ PC 端可见 PDA 日志。

**完成记录**（2026-08-09，后端部分）：
- 契约扩展 #41：模板 `testData`（Core TemplatePackage / SQLite / 模板包 manifest / WinHost API 全链路，旧库自动迁移）。
- WinHost 演进：Web UI 静态托管（web/dist 自动探测 + SPA fallback）、`POST /api/import/excel`（TemplateFrame.Excel.Simple）、PDA 日志 `POST/GET /api/logs`（SQLite，可配置路径）、宽松 CORS、`WebUiPath` / `LABELFRAME_*` 配置。
- AndroidHost 演进（决策 #42）：`pc_host` 配置、`GET /api/pc/templates`、`POST /api/pc/templates/{name}/print-test`（testData 本地打印 + 终态日志回传）、内置 PDA 测试页（127.0.0.1:53970）、Manifest 明文 HTTP。
- 前端规格 `docs/FRONTEND-SPEC.md` 定稿（含 hermes 两轮审阅结论），hermes 并行开发 `web/`（Vite + React + TS + Konva + pnpm + Vitest）。
- 测试 118 个全绿；AndroidHost 编译通过（已知 SQLite 16KB 页警告）。

**启动命令**：
> 继续 LabelFrame 迭代 11（单机模式）。'先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 11 小节）与 docs/FRONTEND-SPEC.md；后端按契约实现，前端按规格交付后联调；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---
## 迭代 9：Excel 数据导入（已完成）

**目标**：Studio 里导入 Excel 数据，批量预览 / 打印。

**范围**：
- 选择 `.xlsx`，列 → 契约字段映射（自动按列名匹配，可手工调整）。
- 按行生成标签数据；批量预览（抽样）与「批量打印」。
- 复用 WinHost `/api/jobs`（一次提交多张）。
- xlsx 读取采用 `TemplateFrame.Excel.Simple`（`SimpleExcel.Read` → 表头 + 数据行，底层 DocumentFormat.OpenXml；见 DESIGN 决策 #32）。

**不在范围**：Excel 模板导出、公式 / 样式保真。

**验收**：50 行 Excel → 一次提交打印成功，失败行可定位（作业轮询可看失败原因）。

**完成记录**（2026-08-09）：
- 新增 `ExcelImportService`（Studio 服务层，UI 栈无关）：`SimpleExcel.Read` 读取 .xlsx（标题行 + 数据行），列 → 字段映射建议（按 Key 忽略大小写匹配），按行生成标签数据字典；底层 `TemplateFrame.Excel.Simple` 1.0.5（决策 #32）。
- 主窗口「导入数据(Excel)…」接入：选模板 → 选 .xlsx → 映射确认窗口（可手工调整列 → 字段 Key）→ 批量打印（一次提交多张 labels，复用 `/api/jobs`）→ 轮询作业状态；导入后自动用首行数据刷新预览，状态栏显示文件名与行数。
- 契约字段沿用自动推导（迭代 8D），Excel 列可映射到任意已推导字段 Key。
- Web 设计器原型 `prototypes/web-designer/`（决策 #39）：Konva 画布（控件栏 / 容器 / 手柄缩放 / 多选对齐 / 中键平移 / Ctrl+滚轮 / 标尺网格）+ WinHost API（连接 / 加载 / 保存 / 预览），用于 UI 技术选型评估。
- 测试 112 个全绿（新增 Excel 读取 / 映射建议 / 行数据生成用例）。

---

## 迭代 10：MSI 安装包（已完成）

**目标**：Windows 安装包，安装即配置好（WinHost + Studio）。

**范围**：
- WiX（或等效 MSI 工具）打包 WinHost / Studio / Core 运行产物。
- 安装时生成 appsettings.json：监听端口、传输模式（默认 Log，可改 TCP / Zebra / 驱动）、打印机地址 / 名称、数据库路径。
- 开始菜单快捷方式；可选把 WinHost 注册为 Windows 服务（自启）。
- 卸载清理数据目录选项。

**不在范围**：数字签名 / 自动更新。

**验收**：全新 Windows 机器安装后，双击图标自动启动服务并打开浏览器（http://127.0.0.1:53960），直接模板编辑与打印测试。

**完成记录**（2026-08-09）：
- WinHost 单机 UX：OutputType 改 WinExe（无控制台）、启动自动打开默认浏览器（OpenBrowser 可关）、Log 传输写宿主日志文件（host.log）、本机 `POST /api/host/shutdown` 优雅关闭。
- 一键打包：`scripts/publish-winhost.ps1`（framework-dependent win-x64 + web/dist，需目标机 .NET 10 Desktop Runtime）与 `scripts/build-msi.ps1`（WiX v7 构建）。
- MSI：桌面 + 开始菜单快捷方式「LabelFrame 标签打印」，默认 appsettings.json（端口 53960 / Log 传输 / 开浏览器），卸载清理。
- 产物 `artifacts\LabelFrame-0.11.0.msi`（约 9.7MB，framework-dependent）；MSI 数据库只读验证 443 文件 + 快捷方式正确；发布版 exe 冒烟通过（healthz / 静态页 / 优雅关闭 / 日志文件）。
- 已知：本沙箱 Windows Installer 服务受限无法执行实际安装，真机安装验收待执行。
- MSI 增加 .NET Desktop Runtime（x64）检测：缺失时全 UI 安装显示带可点击官方下载链接的对话框（MSI Hyperlink 控件）；静默 / 基础 UI 由 LaunchCondition 拦截；不自动安装（2026-08-10 用户确认放弃 Burn 自动引导方案）。
- 修复运行时误报缺失（2026-08-10）：改用 WiX NetFx 扩展 DotNetCompatibilityCheck（官方 NetCoreCheck 自检，x64 Desktop >= 10.0.0、latestMajor），替换失效的注册表搜索（版本号为命名值 + 32 位视图导致已装仍误报）；装完运行时无需重启。产物已重建：`artifacts\LabelFrame-0.11.0.msi`（约 10.3MB）。
- 修复托盘崩溃与安装目录（0.11.1，2026-08-10）：托盘 P/Invoke 两个 API 声明错 DLL（`GetCurrentThreadId` / `GetModuleHandle` 在 `kernel32.dll`），启动即崩导致「装完没反应」；已修正并加异常保护。MSI 改 `-arch x64` 构建（此前 32 位包错装 `Program Files (x86)`），现安装到 `C:\Program Files\LabelFrame`；产物 `artifacts\LabelFrame-0.11.1.msi`（约 10.3MB），可覆盖 0.11.0。
- ZPL 输出 `^PW` / `^LL`（0.11.2，2026-08-10）：按模板宽高换算点数，一张作业严格走一张标签长度，避免多出纸；产物 `artifacts\LabelFrame-0.11.6.msi`（约 14.1MB，含迭代 12 前后端合并版 + SkiaSharp 渲染器 + 文本垂直对齐契约）。
- 升级保留用户配置（0.12.2，2026-08-10）：appsettings.json 改为独立组件 NeverOverwrite + Permanent，覆盖安装 / 修复不覆盖、卸载保留，避免更新覆盖用户配置；决策 #48。

---


## 迭代 18：无头服务端 + 客户端 UI 回归（0.15.0，进行中）

**目标**：0.14 双包验收反馈收敛——服务端不再提供界面并以 Windows 服务部署；客户端恢复完整界面（模板设计 / 数据与打印 / 连接配置 / 日志 / 作业历史）；新增历史数据定期清理；双 MSI 安装完成弹窗。

**范围**：
- 后端：Server 无头化（移除 web/dist 托管与测试页）、Windows 服务（服务注册 / 自启 / 立即运行 / 卸载删除）、数据目录改 `%ProgramData%\LabelFrame\server`、历史清理后台任务、Server 图标（labelframe.ico）；WinHost 新增 `/api/host/config`（机器级 ServerUrl）；双 MSI 安装完成弹窗与自定义动作；打包 0.15.0。
- 前端（hermes）：API 客户端双 base（Server / 本机）；恢复「连接方式」「打印机」分组（参照 4155ccf）；「后端地址」改读写机器级配置；新增「作业历史」页（服务端 /api/jobs）；数据与打印保持目标设备选择。
- 验收：双 MSI 全新安装 → Server 服务自启 / 立即运行按勾选生效；Client UI 在 127.0.0.1:53960 可用，连接切换 / 模板设计 / 打印测试 / 作业历史正常；历史作业按保留期清理；`dotnet test` / `pnpm test` 全绿。

**不在范围**：PDA 联调、Ubuntu 部署服务端（后续迭代）、作业 / 日志归档导出、自定义数据路径迁移。

**启动命令**：
> 继续 LabelFrame 迭代 18。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ARCHITECTURE-SPLIT.md（0.15 修订）、docs/ITERATION-18-SPEC.md；按范围实施后端；前端任务单交 hermes 评估后再开工；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

**后端完成记录（2026-08-11）**：
- Server 无头化（移除 web/dist 托管与测试页）、Windows 服务（`UseWindowsService`，服务名 LabelFrameServer）、数据目录改 `%ProgramData%\LabelFrame\server`、历史清理后台服务（作业 30 天 / 日志 90 天 / 周期 24h，可配置）、exe 图标 labelframe.ico；`GET /api/jobs` 支持 limit（默认 100 上限 500）。
- WinHost：机器级配置 `GET/POST /api/host/config`（serverUrl + deviceId/deviceName，持久化 `%ProgramData%\LabelFrame\Client\settings.json`，缺失 / 损坏返回默认值）；`GET /api/jobs` 本机作业列表（扩展 JobView：CreatedAt / FailedItems / ErrorMessage / TargetDeviceId=null）。
- 双 MSI 0.15.0：Server 注册服务 + 安装完成弹窗（开机自启 / 立即运行，默认勾选，sc config / net start）；Client 安装完成弹窗（立即打开，默认勾选）；卸载清理路径含 ProgramData；测试 156 全绿（Server 13 / WinHost 58 / Core 60 / Studio 25）。
- 修复 0.15.1：ServiceInstall 移入 exe 组件（服务二进制须为组件 KeyPath），解决 0.15.0 安装后服务未注册；产物升级 0.15.1。
- 修复 0.15.2：安装完成弹窗动作改为按钮 DoAction 触发（sc config / net start / 启动客户端），解决弹窗后动作不执行导致服务未自启/未运行；产物升级 0.15.2。
- 简化 0.15.3：Server 服务注册即自动 + 安装时启动（ServiceInstall Start=auto + ServiceControl Start=install），完成弹窗仅提示；双包版本 0.15.3。
- 0.15.4：Server 长轮询通知（notify 端点，作业到达立即唤醒客户端，等效推送）；客户端安装弹窗改为非阻塞启动（cmd /c start）；弹窗文字去掉括号说明；双包版本 0.15.4。
## 迭代 19：Ubuntu 服务端部署 + 跨机验证（进行中）

**目标**：服务端可部署到 Ubuntu（systemd），Windows Client 通过 HTTP 指向 Linux Server，验证跨机全链路（设备注册 / 模板库 / 作业 / 推送通知 / 调试出图 / 日志 / 历史清理）。

**范围**：
- Rendering / Server 多目标框架（`net10.0;net10.0-windows`）；Windows 专属代码（GDI 预览、UseWindowsService、图标、WindowsServices 包）条件编译。
- Server 数据目录按平台默认（Linux `/var/lib/labelframe/server`）；`LABELFRAME_SERVER_*` 环境变量覆盖。
- 发布脚本 `scripts/publish-server-linux.ps1`（framework-dependent / self-contained）；systemd 单元 + Ubuntu 部署脚本；可选 Dockerfile。
- 跨机验证：Windows Client 指向 Ubuntu Server 全链路；本机可用 Docker/WSL 模拟 Linux 服务端，否则交付真机验证清单。

**不在范围**：Linux 客户端、PDA、TLS/鉴权、高可用。
**完成记录（2026-08-11）**：多目标框架（Rendering / Server net10.0 + net10.0-windows）、Skia Linux 原生库、平台默认数据目录、publish-server-linux.ps1 / deploy-server-ubuntu.sh / systemd 单元 / Dockerfile；测试 162 全绿；linux-x64 归档 6.7MB；Windows MSI 回归正常。
- Docker 镜像 `labelframe-server:0.15.4` + 离线包（106MB）+ compose；容器内 healthz / SQLite / Skia 出图验证通过；Windows Client → Linux 容器跨机闭环（注册 Online、作业领取 131ms）通过。
- 迭代 19 反馈修复（2026-08-11）：Server / Client MSI 在覆盖安装与卸载前先停止运行中的程序（Server 先 sc stop LabelFrameServer 并把停机超时缩短为 5s；Client 用 taskkill（KillWinHost）强制结束 WinHost）；ServerRoutingWorker 完成回报改为独立 1s 循环，本地终态后立即回报 Server，不再被 20s 长轮询阻塞（新增回归测试）。

**启动命令**：
> 继续 LabelFrame 迭代 19（Ubuntu 服务端部署）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ARCHITECTURE-SPLIT.md、docs/ITERATION-19-SPEC.md；按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

## 迭代 20：服务端管理界面（插件式 UI）+ 设备 IP（进行中）

**目标**：① 客户端连接服务端后，状态栏显示本机 IP（方便调试）；② 服务端提供可选管理界面——插件形式（静态前端包放入 `plugins/web-ui` 即生效，无需重启），无任何打印机相关内容，保留工作台 / 设计器，新增“在线设备”菜单，数据与打印可浏览全部在线设备并选择其一发送打印测试。

**范围**：
- 后端：设备 `last_ip` 记录与迁移、`DeviceView.lastIp`、`GET /api/devices/by-ip/{ip}`、`POST /api/jobs` 支持 `targetIp`；`Server.WebUiPath` + 静态托管中间件（运行时检测 + SPA fallback）、`GET /api/server/info`、插件 zip 产物、compose 卷挂载示例；WinHost `/api/host/config` 增加 `ips`。
- 前端（hermes）：`VITE_UI_MODE=server` 构建模式（产出 `web/dist-server`）；Server UI 菜单裁剪（移除设置 / 打印机相关内容），新增在线设备页与数据与打印“在线设备选择器”；客户端状态栏显示本机 IP。

**不在范围**：服务端打印机相关内容、服务端 UI 鉴权、.NET 程序集插件、作业模型变更。

**验收**：设备注册 / 心跳后 `/api/devices` 含 `lastIp`；by-ip 查找与 `targetIp` 提交可用；插件目录放入后浏览器打开服务端根路径即管理界面（无需重启），移除后恢复无头；Server UI 无打印机内容、在线设备选择 → 打印测试正常；客户端状态栏显示 IP；`dotnet test` / `npm test` 全绿。

**进度（2026-08-11，后端）**：后端已完成——设备 `last_ip` 列与旧库迁移、注册/心跳记录来源 IP（统一 IPv4 文本 MapToIPv4）、`DeviceView.lastIp`、`GET /api/devices/by-ip/{ip}`、`POST /api/jobs` 支持 `targetIp`（`targetDeviceId` 优先）；`Server.WebUiPath` 插件式静态托管（启动确保目录存在，放入 index.html 即托管、移除即无头）+ `GET /api/server/info`（listenUrl / uiEnabled / version）+ 插件 zip 打包脚本 `scripts/package-server-webui.ps1` + compose 卷挂载示例；WinHost `/api/host/config` 增加 `ips`（本机 IPv4 枚举，过滤回环）。测试 176 全绿（Core 60 / Server 29 / Studio 25 / WinHost 62）+ 端到端冒烟（lastIp 记录 / by-ip / targetIp 解析 / 插件放入即托管 / 移除恢复无头）。前端（hermes，be87548）已按最终版实施——`VITE_UI_MODE` 双构建（`web/dist` + `web/dist-server`）、K1 同源 baseUrl、K2 无单机降级、Server UI 菜单裁剪 + 在线设备页 + 数据与打印在线设备选择器（K3 提交前现拉校验）、客户端状态栏本机 IP；前端测试 151 全绿（client / server 双分支）。0.16.0 双 MSI 与插件包已打包：`artifacts/LabelFrame-Server-0.16.0.msi`、`artifacts/LabelFrame-Client-0.16.0.msi`、`artifacts/labelframe-server-webui-0.16.0.zip`（插件端到端验证：放入即托管、静态资源与 API 正常）。0.16.0 收尾（2026-08-12）：Docker 镜像 `labelframe-server:0.16.0` + 离线包 `labelframe-server-0.16.0.docker.tar`，compose 默认挂载插件目录（`./plugins/web-ui:/var/lib/labelframe/server/plugins/web-ui`），容器内验证插件托管与 API 正常；Client 安装完成弹窗改为 WinHost `--install-finished` TopMost 弹窗（MSI 原生弹窗会被 Windows 焦点策略挡到后台），并修复“确认”关闭与重启宿主链路。联调冒烟待执行。

**启动命令**：
> 继续 LabelFrame 迭代 20（服务端管理界面插件 + 设备 IP）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ARCHITECTURE-SPLIT.md、docs/ITERATION-20-SPEC.md；按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。
## 迭代 21：自动化发布（进行中）

**目标**：发新版本时全自动——测试 → 打包 PC 端安装包（Server / Client MSI、管理界面插件 zip、Linux 归档）→ 构建并推送 Server 端 Docker 镜像（ghcr.io）→ 创建 GitHub Release。

**范围**：
- `.github/workflows/release.yml`：推送 `v*` tag 触发（另支持手动指定版本号）；版本号单一来源 = tag；测试（dotnet + 前端 client / server 双模式）→ 打包 → ghcr 推送（`:版本` + `:latest`）→ Release 附件。
- 打包脚本：新增 `-WixPath`（CI 用 dotnet tool 版 WiX 7.0.0）；MSI 签名证书改走 GitHub Secret（`MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD`），有则签名、无则跳过；移除脚本内明文默认密码。
- compose 默认指向 `ghcr.io/marci-labs/labelframe-server`（`LABELFRAME_VERSION` / `LABELFRAME_IMAGE` 可覆盖）；README 补充自动发布与拉取说明。
- 仓库转公开并转移到组织 `marci-labs`（GitHub 侧操作，gh 辅助）。

**不在范围**：PDA（AndroidHost）构建与发布（真机验收通过后再排期）；Docker 多架构（arm64）；正式商业代码签名证书购买（后续迭代）。

**验收**：推送 `v*` tag 后 CI 全流程通过；`docker pull ghcr.io/marci-labs/labelframe-server:<版本>` 可用；Release 附件含双 MSI、插件 zip、linux-x64 归档；MSI 有 Secret 时签名、无 Secret 时跳过；`dotnet test` / 前端双模式测试全绿。

**进度（2026-08-12）**：方案已定并实施完成——仓库已转移至组织 `marci-labs` 并转公开；推送 `v0.17.0` tag 后 CI 全流程通过（测试 / 双 MSI / 插件 zip / Linux 归档 / ghcr 镜像 / GitHub Release 附件），首次自动发布成功。实施中修复：CI 环境相关测试稳定性（TCP 状态测试同步等待、Skia 渲染阈值放宽、设备列表测试时区无关化、SQLitePCLRaw 测试进程 ModuleInitializer + SqliteLogStore 自初始化）；WiX 打包链（dotnet tool 版 WiX 7 + NetFx 扩展 + OSMF EULA）；artifact 下载路径对齐；插件打包步骤 `$?` 判断。**待办（2026-08-15 更新）**：ghcr 包已设为 Public（网页操作，组织包可见性不支持 API 修改）并通过匿名 `docker pull` 验证；MSI 签名 Secret 可随时补充（有则自动签名，可选）。

**启动命令**：
> 继续 LabelFrame 迭代 21（自动化发布）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md；按范围实施；提交用 Conventional Commits；仓库内容不得出现公司 / 业务线品牌字样。

## 迭代 22：打印测试体验 + 传输插件化 + 客户端下载分发（已完成）

**目标**：围绕「易用性 / 权限边界 / 插件化 / 分发」四项收口——打印测试更好上手、客户端与服务的边界明确、传输方式插件化、客户端安装包由服务端分发。

**已定稿范围**（2026-08-17 用户拍板，详见 [docs/ITERATION-22-SPEC.md](ITERATION-22-SPEC.md)）：
- 打印测试：客户端与服务端「数据与打印」界面新增「下载 Excel 模板」入口（`POST /api/import/excel-template`，按契约字段 + testData 生成 xlsx），便于直接套用 Excel 导入做打印测试。
- 权限边界：客户端只能选择本机做打印测试（决策 1A：本机在线走服务端路由、未注册 / 离线降级本机直连并提示）；服务端可自由选设备打印测试。
- 客户端显示本机设备名称（状态栏 + DataPrint 目标标签）。
- 作业历史可见性：`GET /api/jobs?deviceId=` 过滤——客户端只看自己的作业历史，服务端看全部。
- 传输插件化（决策 2A）：统一接口（`ITransportPlugin` → `IPrintTransport` / `IPrinterStatusProvider` / `ITestableTransport`）、参数模型（`TransportParameterSpec` / `TransportPluginParameters`）、注册表按需装配（内置 log / tcp9100 / winspool / zebra + 外部 DLL 目录扫描）；卸载 = 删除文件 + 重启生效（运行时热卸载记未决）；`connection.json` 新格式兼容旧配置。
- 客户端下载分发（决策 3A）：服务端 `client-packages` 目录 + 上传 / 下载 / 删除 API；Server UI 新增「客户端下载」页；客户端设置「更新与安装包」默认从服务端获取；Ubuntu / Docker 允许挂载 `client-packages` 卷。
- Excel 模板生成放 Core 共享（决策 4A：`LabelFrame.Core.Excel`，复用 `TemplateFrame.Excel.Simple` 写能力）。

**完成记录（2026-08-17，用户确认迭代结束）**：
- 前后端实现 + 联调完成——8 项场景全过（下载 Excel 模板 → 导入批量打印、客户端仅本机、作业历史按设备、安装包上传下载删除 + 路径穿越、传输插件含外部 DLL 装载 / 卸载、契约核对、Docker 挂载）。
- 前端联调修复（hermes 54e77c9）：Excel 列映射按显示名自动匹配、插件参数全量字符串序列化（修复 HTTP 400）、null 默认值不显示字面量；剪贴板降级修复（d6d1b3e）。
- 后端缺陷修复（293e593）：外部插件删除后重启回退默认连接（决策 2A 卸载语义）+ healthz 透出 pluginId / displayText（规格附五）。
- 测试：dotnet 215 全绿（Core 78 / Server 37 / WinHost 75 / Studio 25）；web 179 全绿；本地 0.18.0 测试包（Client / Server MSI + 管理界面插件 zip）打包验收。

**不在范围**：具体厂商打印机插件实现（迭代 24）；PDA（AndroidHost，延后至迭代 25）；传输插件运行时热卸载 / 热替换（未决）；客户端自动升级（仅提供服务端下载）。

**验收**：`dotnet build` / `dotnet test` 与 web `pnpm test` 全绿；联调冒烟（下载 Excel 模板 → 导入批量打印、客户端仅本机、作业历史按设备过滤、安装包上传下载删除、插件加载卸载）后按 DoD 执行。✅ 已满足。

**启动命令**：
> 继续 LabelFrame 迭代 22（打印测试体验 + 传输插件化 + 客户端下载分发）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md；本轮先与用户讨论定稿范围与插件化方案（插件如何加载 / 卸载 / 使用、统一接口与参数模型、服务端下载分发流程），再按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

## 迭代 23：客户端插件分发——上传服务端 + 客户端安装 / 卸载（已完成）

**目标**：把迭代 22 的传输插件机制做成可分发闭环——插件包可上传到服务端（独立目录 + API + Server UI 管理），客户端在界面里浏览服务端可用插件 → 安装 / 卸载已安装的插件。

**已定稿范围**（2026-08-17 用户拍板决策 1-7，详见 [docs/ITERATION-23-SPEC.md](ITERATION-23-SPEC.md)）：
- 插件包上传服务端（决策 1A/2A）：zip（根 `manifest.json` + 插件 DLL），后缀 `.lfplugin`；独立 `plugin-packages` 目录 + `/api/plugin-packages`（列表含元数据与 valid 状态 / 上传 / 下载 / 删除，路径穿越防护）；Server UI 新增「插件管理」页（与「客户端下载」并列）。
- 客户端安装（决策 3A/4A/5A/6A/7A）：设置页新增「插件管理」卡片（与「更新与安装包」并列）——浏览服务端可用插件 → 安装（前端下载 → 本机 WinHost 三层校验 [zip CRC + manifest 必填 + 临时 ALC 预检核对插件 id，内置 id 拒绝] → 解压到 `plugins/<pluginId>/`）→ 重启生效；可查看已安装插件与状态（已加载 / 待重启 / 加载失败 / 手动放置）；覆盖安装允许、不做版本比较；包大小上限 64MB。
- 客户端卸载（决策 3A 延续）：`source:"package"` 插件可卸载（删目录 → 重启生效；与决策 2A 一致）；平铺手动 DLL 只读；运行时热卸载 / 热替换仍不做。
- 校验 / 签名（决策 5A）：不做签名（无鉴权局域网模型，风险记录）；zip 完整性 + manifest 必填 + 安装预检；`minHostVersion` 仅展示暂不校验。

**不在范围**：具体厂商插件实现（精成打印机顺延至迭代 24）；运行时热卸载 / 热替换（未决）；插件包签名 / 服务端鉴权（沿用无鉴权模型）；`minHostVersion` 版本门槛校验；客户端自动升级；PDA（AndroidHost，延后至迭代 25）。

**验收**：`dotnet build` / `dotnet test` 与 web `pnpm test` 全绿；联调冒烟（上传插件包 → 客户端安装 → 重启后配置启用 → 卸载 → 重启后消失，含非法包 / 内置 id / id 不匹配 / 覆盖安装 / 路径穿越 / 单机模式等边界）后按 DoD 执行。

**完成记录（2026-08-17，用户确认定稿后实施）**：
- 前后端按规格实施完成 + 端到端联调冒烟通过（16 步）：上传 .lfplugin → 服务端列表元数据 → 客户端安装（重启前 loaded=false）→ 重启后装配 / loaded=true → 配置启用 → 卸载 → 重启后消失 → 卸载当前连接插件后重启回退 log → Server 删除插件包；字节加载修复 Windows 文件锁（决策 #73）。
- 前端（hermes 重做完成，4dcdbce）：任务书（含评审附一 + 主 Agent 拍板附二）定稿后实施——Server UI「插件管理」页 + 客户端设置「插件管理」卡片 + 插件包 API + 64MB 预检纯函数；web 212 用例全绿（双模式）。
- 后端：Core（插件包读取 / zip-slip / 子目录扫描 / 字节加载 / 防覆盖）/ WinHost（/api/plugins 安装卸载）+ Server（/api/plugin-packages）+ docker-compose 挂载；loadError 结构化补充（LoadWithErrors 透出，8172299/fa8e5f0）；dotnet 265 全绿（Core 108 / Server 45 / WinHost 87 / Studio 25）。
- 打包：本地 0.19.0 测试包（Client / Server MSI + webui 插件 zip + 示例 .lfplugin 合法/坏包）打包验收；ServerOptions 版本号同步 0.19.0。

**启动命令**：
> 继续 LabelFrame 迭代 23（客户端插件分发：上传服务端 + 客户端安装 / 卸载）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ITERATION-23-SPEC.md；按规格实施（后端 = 主 Agent、前端 = hermes，前端以契约为准、接口未就绪用 mock / 测试替身并注明假设，不修改对方范围文件）；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

## 迭代 24：客户端批次作业（Batch Print）（已完成）

**目标**：客户端把「向打印机发送」的动作按数量分批、批间加间隔，用于大批量作业时控制打印节奏 / 减轻打印机压力；同时澄清「服务端作业进度 0%→100%」现象与批次功能的关系（批次不改变进度展示，进度回报仍为终态一次）。

**已定稿设计方案**（2026-08-18 过两轮评审「可定稿」，详见 [docs/ITERATION-24-BATCH-DESIGN.md](ITERATION-24-BATCH-DESIGN.md)）：
- 客户端（WinHost）「批次作业」设置：是否开启（默认关）、每批次打印数量（默认 10）、批次打印间隔 ms（默认 500）；用户级持久化 `%LOCALAPPDATA%\LabelFrame\print-settings.json` + `GET/POST /api/host/print-settings`（仅回环可写、保存即生效）+ 设置页「打印批次」卡片。
- `JobPrintWorker`「发送前暂停（claim-then-delay）」：领取下一张且已发送数满批次 N 的倍数时先延迟再发送；批次计数内存态、跨作业全局累计；本机与服务端作业统一生效；不拆作业、队列 / 幂等 / 挂起恢复 / 重打语义零改动。
- WinHost 引入 Serilog 文件日志（`Serilog.AspNetCore` → `%LOCALAPPDATA%\LabelFrame\logs\app-20260818.log`，`RollingInterval.Day`），逐张 ILogger 日志落盘带时间戳，供端到端冒烟断言批间间隔。
- 读取 Normalize（缺失 / 损坏 / 越界回默认值）；测试页直发不计入批次计数。

**不在范围**：服务端任何改动（跨端契约不变）；增量进度回报（§8 Q2，独立跨端特性，本轮不做）；AndroidHost（延后至迭代 25）；把 Server 作业拆成多个本地作业；重构现有 hostLogWriter（TextWriter）通道。

**验收**：`dotnet build` / `dotnet test` 与 web `pnpm test` 全绿；端到端冒烟（Server 提交 100 张 → 批次 10 / 500ms → Serilog 日志按「打印完成」时间戳断言每 10 张间隔约 500ms → 终态回报 Completed）；按 DoD 更新 ROADMAP / CHANGELOG / DESIGN。

**启动命令**：本迭代拆为**前端 / 后端两个独立会话并行实施**（后端 = WinHost：PrintSettings + API + Worker 节流 + Serilog；前端 = web：设置页卡片 + API client + 测试），命令由用户分别下发，两会话互不修改对方范围文件。
**后端实施完成（2026-08-18，WinHost）**：
- 新增 PrintSettings（选项模型：默认 关 / batchSize 10 / batchIntervalMs 500，读取 Normalize：缺失 / 损坏 / 越界回默认值）与 PrintSettingsStore（%LOCALAPPDATA%\LabelFrame\print-settings.json，原子写：临时文件 + 替换）。
- 新增 GET/POST /api/host/print-settings（POST 校验 batchSize≥1 / batchIntervalMs≥0，非法 400；仅回环可写 403；保存即生效——更新内存单例，无需重启）；PrintSettings 单例注入 JobPrintWorker，读写 lock 保证跨线程可见性。
- JobPrintWorker 实现「发送前暂停（claim-then-delay）」：领取下一张后、SendAsync 前，若已开启且已发送数满批次倍数则 wait Task.Delay(batchIntervalMs, stoppingToken)；发送成功、CompleteItemAsync 后计数 +1（内存态、跨作业全局累计、不持久化）；判定抽为 BatchPrintPolicy.ShouldPauseBeforeSend 纯函数。
- WinHost 引入 Serilog（Serilog.AspNetCore，传递依赖 Serilog.Sinks.File）文件日志 → %LOCALAPPDATA%\LabelFrame\logs\app-20260818.log（RollingInterval.Day、时间戳 + 级别），JobPrintWorker 逐张 ILogger 日志落盘；host.log（hostLogWriter）通道不动，两套日志分开文件。
- 测试：新增 50 个（模型 Normalize / 校验、存储兜底 / 原子写、API GET 兜底 / POST 400 / 非回环 403、BatchPrintPolicy、Worker 节流集成 FakeTransport 时间序列：25 张/批 5 → 第 6/11/16/21 张前各停一次共 4 次、跨作业 5+5 → 第 5 张后 B 首张前等待一次、不足一批不等待、禁用无间隔）；dotnet build 0 错误、dotnet test 315 全绿（Core 108 / Server 45 / Studio 25 / WinHost 137）。前端（web 设置页卡片 + API client + 测试）由并行会话实施，端到端冒烟待两会话合并后执行。

**联调完成（2026-08-18，附五）**：前后端合入 master（67214c3）后按 §7 端到端冒烟全部通过——API 契约（GET/POST /api/host/print-settings：默认值 / 保存即生效 / 400 校验 / Normalize）/ 设置页「打印批次」卡片（渲染 / 开关联动禁用 / 保存提示 / 旧 WinHost 404 降级）/ Server 100 张 → 批次 10/500ms → Serilog 100 条逐张日志 + 节流恰好 9 次（批界间隔 ≈ 500ms，扣除 Log 传输固有耗时后 ≈ 524ms）→ 终态 Completed；前端零缺陷。后端 1 项待修（Serilog 日志文件名含字面 {Date}，产物 app-{Date}20260818.log）已修复：实证 Serilog.Sinks.File 的 {Date} 为字面量，改为 app-.log + RollingInterval.Day → 实际产物 app-20260818.log（见 CHANGELOG 与设计文档附五处理记录）。

**完成（2026-08-18）**：验收标准全部满足——`dotnet build` / `dotnet test` 315 全绿、web `pnpm test` 双模式 219×2 全绿、端到端联调附五通过、Serilog 日志命名待修项已修复；迭代状态更新为 ✅ 已完成。服务端零改动（跨端契约不变）；下一轮迭代 26（Niimbot 蓝牙打印机插件，顺延自本迭代）。

**发布（2026-08-18）**：推送 `v0.20.0` tag 触发 GitHub Actions 自动发布——Server Docker 镜像推 ghcr.io（`ghcr.io/marci-labs/labelframe-server:0.20.0` / `latest`）、PC 安装包（Server / Client MSI、服务端 webui 插件 zip、Linux 归档）上传 GitHub Release；ServerOptions 版本号同步 0.20.0。

**验证发布（2026-08-18）**：推送 `v0.20.1` tag 验证发布流水线稳定（内容与 v0.20.0 相同，无功能变更）——GitHub Actions 全流程通过，产物同上；ServerOptions 版本号同步 0.20.1。

**验证发布（2026-08-18）**：推送 `v0.20.2` tag 再次验证发布流水线稳定（内容与 v0.20.1 相同，无功能变更）——GitHub Actions 全流程通过，产物同上；ServerOptions 版本号同步 0.20.2。

---

## 迭代 26：Niimbot 蓝牙打印机传输插件实现 + 真机测试（下一轮，顺延自迭代 24）

**目标**：基于迭代 22 传输插件机制（+ 迭代 23 分发闭环），实现 Niimbot（小标蓝牙热敏标签打印机）的传输插件并真机测试——填补需求 P1「蓝牙传输」缺口（迭代 6 曾因蓝牙受阻，本轮以插件方式补上）。

**范围**（承接迭代 22 / 23，会话中细化）：
- 调研 Niimbot 打印机通信协议（BLE 特征 / 指令集），按 `ITransportPlugin` 接口实现传输插件（连接 / 发送 / 状态 / 测试），参数模型独立（蓝牙设备名 / 地址等）。
- 打包 / 装载 / 注册表装配，配置指定插件与参数即启用（可经迭代 23 分发安装：`.lfplugin` 上传服务端 → 客户端安装 → 重启生效）。
- 真机（Niimbot 打印机）验收：连接、打印、状态、异常恢复。

**不在范围**：精成打印机插件（顺延，待用户确认需求）；PDA / AndroidHost（延后至迭代 25）；运行时热卸载 / 热替换（未决）。

**验收**：会话中定稿后按 DoD 执行；`dotnet build` / `dotnet test` 与 web `pnpm test` 全绿；真机联调冒烟后按 DoD 收尾。

**启动命令**：
> 继续 LabelFrame 迭代 26（Niimbot 蓝牙打印机传输插件）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ITERATION-23-SPEC.md；先调研 Niimbot 协议并讨论定稿范围（蓝牙方案 / 参数模型 / 真机验收方式），再按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

## 迭代 25：Android PDA 宿主（AndroidHost，延后）

**目标**：交付可真机使用的 Android PDA 宿主——本地 HTTP / JS 桥、TCP9100 打印、复用 Server 注册 / 轮询链路；真机验收通过后再纳入自动发布。

**范围**（承接迭代 5 架构设计，见 docs/DESIGN.md 未决问题）：
- 安装 .NET Android workload（Android SDK 36 / JDK 17 已配齐），编译并验证 AndroidHost。
- 本地 HTTP / JS 桥 + TCP9100 传输；设备注册 / 心跳 / 轮询复用 Server 路由；作业领取 → 本地打印 → 终态回报。
- Android 16 16KB 页要求验证（SQLitePCLRaw 已升 2.1.13，libe_sqlite3 适配待确认，目标消除 XA0141 警告）。
- 蓝牙打印（P1，配对 / 重连策略）；开机自启与前台服务保活（厂商 ROM 差异真机确认）。
- 真机验收：注册 / 心跳 / 模板下发 / 作业打印 / 离线恢复 / 断网重连。

**不在范围**：PDA 构建与自动发布（真机验收通过后再排期）；Docker 多架构。

**验收**：真机（PDA）完成设备注册与端到端打印；现有 `dotnet build` / `dotnet test` 保持全绿。

**启动命令**：
> 继续 LabelFrame 迭代 25（Android PDA 宿主）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md；按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。
## 迭代 27：工程治理 P0（日常 CI + API 契约与端点去重 + 文档重组）（已完成）

**背景**：2026-08-25 仓库多维度评审（流程 / 代码质量 / 产品 / 测试 CI）确定的 P0 治理项——此前唯一工作流 release.yml 仅发版 tag 触发，日常提交无质量反馈回路；Server 与 WinHost 的 API 契约与端点成片复制且已漂移出真实缺陷；README 演变为迭代流水账，新用户上手路径被淹没。

**范围**：
1. 日常 CI：新增 `.github/workflows/ci.yml`（push master / PR 触发；命令与 release.yml 的 test job 一致：dotnet restore / build / test + 前端 lint / 双模式测试 / 双模式构建；同分支新推送取消旧运行）。不改动 release.yml。
2. API 契约与端点去重：新增共享库 `src/LabelFrame.Api`——DTO（SubmitJobRequest / TemplateDto / LabelDto / TemplatePackageDto / PreviewRequest / PushLogRequest / ExcelTemplate* / ErrorView）+ 错误码注册表 `ApiErrorCodes` + 端点映射（模板 CRUD / 导入导出 / 预览、调试出图、Excel 模板与导入、设备日志，端点经 Options 传入各自错误码前缀，两宿主对外错误码不变）；Server / WinHost 各删除约 150 行重复实现；xlsx 文本解析下沉 Core（`ExcelTableReader`），两宿主移除 TemplateFrame.Excel.Simple 直接引用。
3. 漂移缺陷修复（共享后行为统一）：WinHost 模板预览 DPI 硬编码 203 → 取宿主配置；预览渲染统一 Skia 同源（原 WinHost 用 GDI LabelPreviewRenderer）+ 请求数据缺省回退模板 testData；模板不存在错误码 WinHost 原误用 LF_JOB_001 → 新增 LF_TPL_001（Server 保持 LF_SRV_006）；ErrorView 统一 Code / Message / FieldKey 三字段（前端按可选读取，兼容）；render-image(s) 图片资源解析统一为「base64 附带优先、按名回退本地模板库」；WinHost 端点魔法字符串（LF_TRANSPORT_* / LF_PLUGIN_*）→ ApiErrorCodes 常量。
4. 文档重组：README 重写（定位 + 三类角色快速开始 + 部署形态对照 + 仓库结构 + 开发；216 → 约 110 行，迭代流水账移除、状态以本文档为准）；新增 docs/DEPLOY.md 承接部署运维细节（MSI / Docker / Ubuntu / 管理界面插件 / 分发通道 / 签名 / 配置）。

**不在范围**：AndroidHost 第三套端点并入（不在解决方案，另排）；ClientPackagesService / PluginPackagesService 同构去重；Program.cs 巨文件拆分与 WebApplicationFactory 集成测试；docs/ 归档分层与 ROADMAP 排序修复（P1）；数据层 N+1 / 领取事务 / 并发锁（P2）。

**验收**：dotnet build 0 错误；dotnet test 315 全绿（Core 108 / Server 45 / Studio 25 / WinHost 137）；对外 JSON 契约不变（漂移修复点均向后兼容）；按 DoD 更新 ROADMAP / CHANGELOG / DESIGN。

**完成（2026-08-25）**：三项 P0 全部完成——ci.yml 日常 CI、LabelFrame.Api 共享库落地（两端各删约 150 行重复端点与 DTO）、README 重写 + DEPLOY.md 拆分；dotnet build / dotnet test 315 全绿。

---

## 检查点：试点验收（已完成）

按 [REQUIREMENTS.md](REQUIREMENTS.md) §8 成功衡量执行：
- 先测基线，再测新系统，同指标对比；
- 真实扫码枪抽 50 张；连续 100 张压力验证（含重启 / 断网）；
- 产出试点对比报告。

**完成记录（2026-08-17，用户确认）**：真实扫码枪抽 50 张 + 连续 100 张压力验证（含重启 / 断网）已通过，试点验收完成。

## 待需求（有真实需求再排）

- net48 版 WinHost（Win7 / 8 老电脑，尽量兼容）。
- WMS 模板下发（复用模板包格式）。
- 其他打印机指令集（TSPL / CPCL）。
- 打印历史统计。
- 多打印机并行。
- 契约字段 Pattern 校验（迭代 1 仅存元数据未执行；2026-08-17 列为未来事项，现阶段不处理）。

