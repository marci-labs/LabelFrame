# Changelog

本文件记录每个迭代的变更。

## 迭代 0（奠基）— 2026-08-08

- 建立文档体系：README（愿景）、AGENTS、DESIGN、REQUIREMENTS、ROADMAP、CHANGELOG。
- 建立解决方案骨架：`LabelFrame.Core` / `LabelFrame.Server` / `LabelFrame.WinHost`（占位），`LabelFrame.AndroidHost` 目录占位。
- 初始化 git 仓库并推送至 GitHub。
## 迭代 1（契约与 ZPL）— 2026-08-09

- `LabelFrame.Core`：契约 / 版式模型（LabelContract、LabelLayout：文本 / 条码 / 二维码 / 图片 / 线，毫米坐标）、LabelDocument。
- 数据校验：必填字段缺失（含空白）拒绝，返回问题码 `LF_VAL_001`。
- ZPL 编码器：文本、Code128（^BC）、图片占位（^FX），毫米 → 点换算（默认 203 dpi）；二维码 / 线元素显式报错待迭代 2。
- 日志传输（模拟打印机）：`LogPrintTransport`。
- 单元测试：库位码 golden test、校验用例、编码器用例、传输用例（14 个，`dotnet test` 全绿）。
- 新增测试项目 `test/LabelFrame.Core.Tests` 并加入解决方案。
## 迭代 2（WinHost 打印闭环）— 2026-08-09

- 全项目升级 .NET 10；WinHost 目标 `net10.0-windows10.0.26100`。
- `LabelFrame.Core`：作业模型 + SQLite 持久化队列（requestId 幂等、逐张状态、挂起 / 恢复 / 取消、批内顺序、重启不丢作业并把在途 Item 重置续打）；LabelBitmap（1bpp）+ ZPL ^GF 位图编码；TCP 9100 传输；版式元素 JSON 转换器（type 判别）。
- `LabelFrame.WinHost`：本地 HTTP API（POST/GET /api/jobs、suspend/resume/cancel、healthz；模板自包含提交）；打印 Worker 串行打印；GDI 中文栅格化（内嵌 / 本地字体优先，回退微软雅黑）；传输：Log / TCP9100 / Windows 驱动（winspool raw）/ Zebra 官方 SDK（TCP / USB 自动发现 / 驱动）。
- 配置：appsettings.json（WinHost 节）+ LABELFRAME_* 环境变量。
- 测试 53 个全绿（队列 / ^GF / TCP / JSON / 栅格化 / raw / Zebra / 提交服务）；端到端冒烟验证通过。
## 迭代 3（Server 路由）— 2026-08-09

- `LabelFrame.Server`：设备注册 / 心跳 / 目录（在线状态）、作业定向投递（requestId 幂等，SQLite 持久化）、宿主轮询领取、结果回报、作业集中查询；测试入口页面；配置 appsettings（Server 节）+ LABELFRAME_SERVER_*。
- `LabelFrame.WinHost`：Server 路由客户端 + 路由 Worker（领取 → 本地队列打印 → 终态回报）。
- 设备离线语义：作业暂存 Pending，上线轮询即领取（不丢作业）。
- 默认端口：WinHost 53960 / Server 53961。
- 测试 65 个全绿；端到端冒烟：提交 → WinHost 领取打印 → 回报 Completed。
## 迭代 4（模板管理 + 预览）— 2026-08-09

- `LabelFrame.Core.Templates`：模板包模型 + zip 导入导出（manifest.json + images/）+ SQLite 模板存储（CRUD / 分组 / 图片资源）。
- `LabelFrame.WinHost`：模板 API（保存 / 列表 / 详情 / 删除 / 导出 / 导入 / 预览）；预览 PNG（GDI 文本与线 + ZXing 条码 / 二维码 + 图片渲染）；ZXing.Net 0.16.11。
- 测试 79 个全绿；冒烟验证：保存 → 预览 PNG → 导出 zip。

## 迭代 5（PDA 宿主）— 2026-08-09

- `LabelFrame.AndroidHost`（net10.0-android）：前台服务 + 开机自启广播、本地 HTTP（127.0.0.1:53970）、IP 9100 传输、Server 注册 / 轮询 / 回报、Android.Graphics 中文栅格化（^GF）、SQLite 作业队列。
- 编译打包成功（Signed APK 约 11MB）；`scripts/build-androidhost.ps1` 一键构建。
- 真机验收（PDA 网页 → Server → 宿主 → IP 打印机、开机自启）待执行；蓝牙在迭代 6。

## 迭代 6（P1 收尾）— 2026-08-09

- 失败项单独重打：`RetryItemAsync`（Failed → Pending，Failed 作业自动恢复）+ API `POST /api/jobs/{jobId}/items/{itemIndex}/retry`。
- 打印机测试页 / 在线状态：`GET /api/printer/status`、`POST /api/printer/test`；TCP `~HS` 基础解析、Zebra 连接即在线、驱动模式不可读回、Log 模拟在线。
- 蓝牙传输随迭代 5 受阻；真实设备字段联调待执行。
## 迭代 7（Studio 模板工具 V1）— 2026-08-09

- `LabelFrame.Studio`（WPF，net10.0-windows）：WinHost 客户端。
  - 连接管理：地址配置、一键启动 / 停止 WinHost、传输模式显示（healthz 新增 transport）。
  - 模板管理：按分组列表、详情（契约字段 + 版式元素）、删除、导出 `.lfpkg`。
  - 模板导入：文件选择 `.lfpkg` → 导入 WinHost 模板库。
  - 测试打印：选模板 → 按契约字段自动生成数据表单 → 预览 PNG → 提交打印作业 → 轮询状态与失败原因。
- 复用 WinHost API，无重复打印逻辑；`StudioClient` 支持注入 HttpClient（可测试）。
- 测试 85 个全绿；界面验收待执行；版式可视化编辑（拖拽画布）为 V2。
## 迭代 8（Studio 版式编排 V2）— 2026-08-09

- `LabelFrame.Studio` 新增版式编辑窗口（EditorWindow）：
  - 画布按 mm 渲染（缩放 50%–250%），元素拖拽移动 / 选中 / 删除。
  - 工具箱添加文本 / 条码 / 二维码 / 图片 / 线元素。
  - 属性面板编辑坐标、尺寸、SourceKey、字体高宽、线宽。
  - 契约字段增删、必填、类型、显示名编辑。
  - 保存（POST /api/templates）+ 刷新预览（WinHost preview PNG）。
- 条码数据仍为纯文本传递，模板元素类型决定条码 / 二维码渲染（无契约变更）。
- 测试 90 个全绿。
## 迭代 8B（Studio 版式增强：字段编辑 / 元素样式 / 区域布局）— 2026-08-09

- 字段编辑：键 Key / 显示名 / 必填 / 类型可编辑；重命名自动同步引用该字段的元素 SourceKey。
- 画布：显眼显示标签尺寸（不随窗口变化）；新元素默认排在上一个下方（上下结构为主）。
- 元素样式（模板包契约扩展，向后兼容）：文本 WidthMm（块宽）/ TextAlign（左/中/右）/ PaddingMm / BorderMm；条码 / 二维码 / 图片 BorderMm。
- 区域（格子）布局：新增 LabelRegionElement 容器；元素可锚定 RegionId + 区域内 H/V 对齐（默认居中）；区域移动元素跟随。
- ZPL 编码：区域边框 ^GB、文本块对齐 ^FB、二维码 ^BQ、线 ^GB（L）；预览渲染同步（共用 LabelLayoutResolver）。
- 测试 99 个全绿。
## 迭代 8C（Studio 界面重构：工作台 + 设计器）— 2026-08-09

- 共享渲染库 `LabelFrame.Rendering`（GDI + ZXing）：预览渲染从 WinHost 抽出，WinHost 与 Studio 共用；Studio 画布 / 预览本地实时渲染。
- 契约扩展：文本 / 条码 / 二维码支持 `Literal` 固定值或 `SourceKey` 字段填充（向后兼容）。
- 作业工作台（主窗口重写）：菜单栏、模板列表、本地预览、数据表单、打印、底部状态栏 + 日志栏。
- 模板设计器（独立窗口）：控件栏点击 / 拖入、画布毫米网格、选择移动、画区域（拖矩形）、元素拖入区域自动锚定居中 / 移出解除锚定、属性分组（位置尺寸 / 文本字体 / 填充 / 内边距边框 / 区域锚定）、测试数据、实时打印预览（节流）、打印测试、底部状态 + 日志。
- 待办（迭代 8D）：拖角缩放、标尺 / 对齐线。
- 测试 105 个全绿。
## 迭代 8D（设计器交互重做）— 2026-08-09

- 设计器重做（`DesignerWindow`）：
  - 设计 / 测试用 Tab 分离：测试 Tab 放测试数据（字段由版式自动推导）、实时打印预览、打印测试。
  - 控件栏改为可拖拽项（文本 / 条码 / 二维码 / 图片 / 线 / 容器），点击添加一次、拖入画布定位，修复“拖拽一次建两个元素”问题。
  - 画布：毫米标尺 + 网格；左键选中、8 手柄拖角缩放、Shift/Ctrl 点击与拖框多选、Delete 删除、中键平移、Ctrl+滚轮缩放（以鼠标为中心）；移动时边缘 / 中心自动吸附到画布与其它元素；右键对齐菜单（左 / 水平居中 / 右 / 上 / 垂直居中 / 下）。
  - 容器控件替代“画区域”：控件栏拖「容器」矩形；元素拖入容器自动锚定居中；属性面板移除 RegionId / 锚定 UI（后台能力保留，模板格式不变）。
  - 属性面板仅在选中元素时显示（默认收起）：单选显示元素属性，多选显示对齐工具。
  - 底部状态 + 日志栏横跨全窗口，日志自动滚动到底、可一键清空。
  - 固定值 / 字段 / 样式修改实时重绘画布并节流刷新打印预览。
- 契约字段后台自动推导：字段集合 = 版式「字段填充」元素 SourceKey 去重（保留旧契约字段顺序与元数据）；移除字段增删 / 重命名 / 显示名 UI；工作台与测试表单统一用 Key 作标签。
- `MainWindow`：数据表单标签改用字段 Key；日志自动滚底 + 清空按钮。
- 测试 109 个全绿（新增字段推导 / 多选删除 / 对齐 / 吸附用例）。
## 迭代 9（Excel 数据导入）— 2026-08-09

- 新增 `ExcelImportService`（Studio 服务层，UI 栈无关）：读取 .xlsx（标题行 + 数据行）、列 → 字段映射建议（Key 忽略大小写匹配）、按行生成标签数据字典；基于 `TemplateFrame.Excel.Simple` 1.0.5。
- 主窗口「导入数据(Excel)…」：选模板 → 选 .xlsx → 映射确认窗口（列 → 字段 Key 可手工调整）→ 批量打印（一次提交多张，复用 `/api/jobs`）→ 轮询作业状态；首行数据自动刷新预览，状态栏显示文件名与行数。
- Web 设计器原型 `prototypes/web-designer/`：Konva 画布（控件栏 / 容器 / 手柄缩放 / 多选对齐 / 中键平移 / Ctrl+滚轮缩放 / 标尺网格）+ WinHost API（连接 / 加载 / 保存 / 预览），用于 UI 技术选型评估（决策 #39）。
- 测试 112 个全绿（新增 Excel 读取 / 映射建议 / 行数据生成用例）。
