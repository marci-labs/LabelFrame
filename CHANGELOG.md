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

## 迭代 8E（Web 设计器原型 v2）— 2026-08-09

- 视口缩放模型：画布容器自动铺满视口（随窗口自适应）；Ctrl+滚轮只缩放画布内容（以鼠标为中心）；「适应窗口」/「实际大小」按钮，设计态与真实尺寸预览分离。
- 条码 / 二维码实时渲染：值变化立即渲染真实条码 / 二维码（JsBarcode / qrcode-generator 本地化）；属性面板预留条码（码制 / 底部文字 / 模块宽）与二维码（纠错级别 / 边距）参数分组。
- 智能参考线：拖动时吸附画布边缘 / 中心与其它元素边缘 / 中心并显示参考线（参考 Figma / Konva snapping 做法）。
- 边框修正：边框为矩形元素外框描边，不再描文字。
- 控件栏精简为文本 / 条码 / 二维码（图片 / 线 / 容器移除入口，已有模板仍可加载显示）。
- 文本溢出模式：每元素可配置「自动换行 / 超长截断 / 缩小字体」（参考 BarTender Auto-Fit / Cleverence Label）。
- 原型经 headless 浏览器自测通过（元素添加 / 条码二维码渲染 / 初始化无异常）。

## 迭代 8F（Web 设计器原型 v3）— 2026-08-09

- 画布留白 + 标尺跟随：画布实际大小 = 输入尺寸 + 四周 10mm 留白；标尺以 mm 覆盖整个画布并随画布移动 / 缩放；内容区边缘标蓝刻度。
- 画布平移不越界：中键拖拽 clamp 到可视边界。
- 容器不再手动缩放：默认画布铺满视口；「实际大小」= 1mm=8 点（203dpi 打印比例），可滚动 / 平移查看。
- 文本溢出新增「不限制高度」模式（按内容实际高度显示全部文字）。
- 修复控件拖入不可见：drop 坐标改为基于 clientX/Y 几何换算（原依赖 Konva 指针状态，HTML5 拖拽期间无指针事件导致元素被放到错误 / 越界位置）。
- 核心修复：stage 尺寸 = 逻辑尺寸 × 比例尺（原实现 stage.scale 只缩放绘制内容，canvas 容器仍是逻辑尺寸，放大时内容被 canvas 裁剪 → 网格范围变化 / 拖入元素在裁剪区外不可见 / 标尺错位）。
- 标尺与网格对齐：标尺 0 点与画布左缘对齐（左上角空块布局）；网格覆盖整个画布（含 10mm 留白）并与标尺同比例缩放。
- 适应窗口 / 实际大小统一为「比例尺预设」：设计时按点处理，需要真实比例时再换算点与 mm。
- 第三轮修复：
  - 控件不可见根因：Konva 9.3 的 Text 无 clipFunc，文本渲染抛异常中断 render（网格先画好所以可见）。改为 Group clip 裁剪；条码 / 二维码未绑定显示「未绑定」虚线占位。
  - 标尺画进 Konva（与内容同坐标系），放大 + 平移后标尺 / 网格不再错位；HTML 标尺移除。
  - 中键平移改用原生 DOM + document 级 mouseup 复位（修复松开后仍拖拽的粘滞）。
- 第四轮修复：
  - 吸附 / 位置换算统一用 `getClientRect({ relativeTo: layer })` 逻辑坐标（原用绝对视觉坐标，比例尺放大后吸附与定位偏差 total 倍）。
  - 二维码同步 canvas 渲染（模块遍历手绘，去掉异步 Image 加载）。
  - 属性面板下拉 / 勾选补 commit（修复条码底部文字、码制、纠错等切换不刷新）。
  - 边框 / 内边距通用化（文本 / 条码 / 二维码一致）；文本模式收敛为「缩小适应 / 溢出显示」（文本框 = 遮罩区域，抛弃自动换行 / 不限制高度）。
- 第五轮改进：拉伸文本框不再改变字高（字高独立，遮罩区域变化）；溢出模式文案改「隐藏」；内边距拆上下 / 左右；填充默认固定值，字段填充 = 键名称 + 填充值（预览）；新增 Ctrl+C / Ctrl+V 复制粘贴（偏移 5mm）。
- 第六轮改进：Ctrl+Z / Ctrl+Y 撤销恢复（上限 100 步）；字高调大才撑高文本框；吸附强化（边完全重合 + 参考线醒目）；导出 / 导入设计到剪贴板（labelframe-web-design JSON，Ctrl+Shift+C/V）；控件栏新增矩形控件（边框 + 可选填充，保存映射 region）；文本框基础属性新增高度字段（遮罩高度，与条码 / 二维码一致）。
- 第七轮改进：矩形镂空（仅边框，移除填充色）；新增图层面板（控件列表 / 点击选中同步画布 / 置顶上移下移置底 / 列表 Delete 删除）。
- 第八轮修复：网格吸附兜底（无参考目标时贴最近 1mm 网格，消除 0.2 小数偏移）；字段填充提示明确「打印以外界数据为准，预览值被忽略」。
- 第九轮改进：移除适应窗口 / 实际大小按钮，改为 DPI 选择框（203/300）+「预览打印效果」按钮——按所选 DPI 以真实打印比例显示（203→1mm≈8点、300→1mm≈12点），再点退出回到适应窗口。
- 第十轮改进：文本框自动换行（超过显示区域换行，仍超出整体缩小字体至最小 1.5mm）+ 行间距（默认 1.2）+ 字体选择（雅黑 / 宋体 / 黑体 / 楷体 / Arial / Consolas）；单行保留缩小 / 隐藏。
- 补充：文本垂直对齐（顶端 / 居中 / 底部），配合换行使用。
- 纯前端编辑器化：移除连接 / 模板 / 保存 / 预览 / 导出导入按钮与 IP 等后端元素；导出 / 导入设计保留快捷键（Ctrl+Shift+C / Ctrl+Shift+V）并写入操作提示；顶部仅保留纸张 / DPI / 预览 / 缩放 / 网格；修复中键误触发控件选中（仅左键响应点击）。

## 迭代 11（单机模式，后端部分）— 2026-08-09

- 契约扩展：模板 testData（Core / SQLite / 模板包 / API 全链路，旧库自动迁移）。
- WinHost 演进：Web UI 静态托管（web/dist + SPA fallback）、Excel 导入 API、PDA 日志端点、宽松 CORS。
- AndroidHost 演进：PDA 测试模式（pc_host 配置 / 拉模板列表 / 点击模板用 testData 本地打印 / 终态日志回传 PC / 内置测试页）。
- 前端规格 docs/FRONTEND-SPEC.md 定稿（hermes 两轮审阅全部落定），前端并行开发中。
- 测试 118 个全绿；AndroidHost 编译通过。

## 迭代 10（MSI 安装包）— 2026-08-09

- WinHost 单机 UX：WinExe（无控制台）、启动自动打开浏览器、Log 写 host.log、本机优雅关闭端点。
- 一键打包脚本：publish-winhost.ps1（self-contained + web/dist）+ build-msi.ps1（WiX v7）。
- MSI：桌面 / 开始菜单快捷方式、默认配置、卸载清理；产物 LabelFrame-0.11.0.msi（约 47MB）。
- 发布版冒烟通过；MSI 数据库验证 443 文件 + 2 快捷方式（Target=#WinHostExe）；沙箱无法实际安装 / 签名（Windows Installer 服务与 CryptoAPI 受限），真机验收与签名待执行。
- 名称统一为 LabelFrame（安装目录 / 快捷方式 / 卸载显示）；新增应用图标（蓝底白色 L 型，嵌入 exe 与快捷方式）。
- 新增脚本：generate-icon / create-signing-cert（openssl 自签名 + .NET 重封装）/ cleanup-residue（管理员清理历史残留）。
- 发布改 framework-dependent：MSI 56.5MB → 9.7MB（目标机需 .NET 10 Desktop Runtime）；系统托盘改原生 P/Invoke 实现（无 WinForms 依赖）。
- 安装结构修复：web/dist 与 assets 子目录正确（解决白屏 / JS 404）；安装目录 Program Files\LabelFrame。
- MSI 增加 .NET Desktop Runtime（x64）检测：缺失时全 UI 安装显示带可点击官方下载链接的对话框（MSI Hyperlink 控件，点击直达下载页）；静默 / 基础 UI 由 LaunchCondition 拦截并提示链接；不自动安装（2026-08-10 用户确认放弃 Burn 自动引导方案）。
- 修复运行时误报缺失（2026-08-10）：检测从注册表搜索改为 WiX NetFx 扩展 DotNetCompatibilityCheck（内置官方 NetCoreCheck 自检，检查 x64 Microsoft.WindowsDesktop.App >= 10.0.0、RollForward=latestMajor）。原注册表搜索读 sharedfx 键默认值，而运行时版本号是命名值，且 32 位 MSI 读 32 位视图，导致已装 Desktop Runtime 仍提示未安装；现改为实时自检，装完运行时**无需重启**即可识别。
- 修复托盘 P/Invoke 崩溃（0.11.1，2026-08-10）：`GetCurrentThreadId` / `GetModuleHandle` 被错误声明为从 `user32.dll` 导入（实际在 `kernel32.dll`），托盘线程启动即抛 `EntryPointNotFoundException`，未处理异常直接杀死宿主进程——这是「装完啥也不显示 / 页面打不开」的根因；已改为正确 DLL，并给托盘循环加异常保护：托盘出问题只记日志，不再让宿主退出。
- MSI 改为 **x64 包**（0.11.1，2026-08-10）：此前 MSI 是 32 位包，`ProgramFiles64Folder` 不生效导致装到 `Program Files (x86)`；现 `wix build -arch x64`，安装到 `C:\Program Files\LabelFrame`；版本 0.11.1 支持直接覆盖已装的 0.11.0。
- ZPL 编码器显式输出 `^PW` / `^LL`（0.11.2，2026-08-10）：按模板宽高换算点数（70×50 @203dpi → `^PW559` / `^LL400`），避免打印机沿用旧标签长度导致一张作业走多张纸；新增对应单元测试。
## 迭代 12（模板预览值 + 图片打印，后端部分，2026-08-10）

- 元素 JSON 新增 `previewValue`（字段填充模式预览值持久化，text/barcode/qrcode 非空时输出，旧模板向后兼容）。
- `TemplateStore.SaveAsync` testData 读-改-写：数据库现有值 → 并入显式传入 → 被元素预览值派生覆盖；旧模板显式测试数据不再因前端不传而被清空。
- 新增 `PrintMode`（Vector 默认 / Image）：Image 模式整版渲染 1bpp 位图经 `^GF` 直传打印机，与画布预览同源；`SubmitJobRequest` 支持 `printMode` 覆盖、`template.name` 取模板图片；`/healthz` 返回 `printMode`。
- 修复：预览渲染器文本无显式块宽时被裁成 1px 导致图片打印空白（改为按文本实际宽度绘制）。
- 修复：Log 传输重复打开同一 host.log 触发文件锁、ZPL 被静默丢弃（复用宿主日志写入器）。
- 测试 127 个全绿（新增 previewValue 往返、testData 派生/保留/覆盖、EncodeImage、RenderLabelBitmap、Image 打印模式用例）。
- 产物 `LabelFrame-0.11.3.msi`（2026-08-10）：含迭代 12 前后端合并版（预览值持久化 + 测试默认值 + 打印方式选择 + 图片打印），可覆盖 0.11.x 安装。
- 图片打印调试与清晰度优化（0.11.4，2026-08-10）：打印位图改为单比特网格对齐（去抗锯齿灰度，避免 1bpp 阈值切字发虚）；新增 `POST /api/print/render-image` 与前端「调试：不打印，保存实际打印图片（PNG）」复选框（图片打印方式下显示），用于排查文字清晰度 / 定位是渲染问题还是打印机问题。产物 `LabelFrame-0.11.4.msi`。
- 后端渲染器改为 SkiaSharp（0.11.5，2026-08-10，方案 2）：新增 `SkiaLabelRenderer`（canvas 类 2D 渲染，与前端同源规则：文本超出框宽缩小适应、左中右对齐、内边距/边框、线条/区域、ZXing 条码二维码、模板图片），图片打印与「保存打印图片」均切换；修复 GDI 渲染的 CJK / 右对齐 / 长文本缺失问题（含生僻字开头只匹配小字体、行高为负导致裁剪空矩形）。你的 70×50 模板四个字段（MaterialName / CompanyName / Specification / WarehouseName）端到端验证全部渲染。产物 `LabelFrame-0.11.5.msi`。
- 文本垂直对齐契约与前后端同源（0.11.6，2026-08-10）：文本元素新增 `heightMm` + `verticalAlign`（Top/Middle/Bottom），前端保存时写入元素高度与垂直对齐；Skia / GDI 渲染器按框高垂直对齐绘制，修复「打印比前端预览整体偏上」（此前前端在元素框内垂直居中、后端顶部对齐，且高度未持久化）。端到端验证：MaterialName / CompanyName / WarehouseName 文字均落在框中部。产物 `LabelFrame-0.11.6.msi`。
- 修复 0.11.6 回归（0.11.7，2026-08-10）：无 `heightMm` 的旧模板 + 1mm 内边距时，内框高（字高−2×内边距）被算成负数，裁剪区塌成 1px 导致文字几乎全部消失；现裁剪高度至少一行，旧模板恢复顶部对齐可见，新模板保持居中。产物 `LabelFrame-0.11.7.msi`。

## 迭代 13（文本排版与二维码参数持久化，后端部分，2026-08-10）

- 元素契约补齐第二批字段：文本 `wrap / lineHeight / fitMode / fontFamily`（默认 Microsoft YaHei）、二维码 `qrEcc / qrMargin`（默认 M / 2）、条码 `displayValue`（默认 true）、通用双边内边距 `paddingH / paddingV`（`PaddingHMm / PaddingVMm`，0=未设，缺失时回退 `paddingMm`，`paddingMm` 保留兼容）。
- 决策 A：`VerticalAlign` 默认由 Top 改为 Middle（与前端一致）；`LabelElementJsonConverter` 写规则改「非 Middle 才写」；旧模板无 `heightMm` 时 Skia 渲染器框高兜底 = `max(字高 + 2×最大双边内边距, 10mm)`（与前端读回兜底一致）。
- `LabelElementJsonConverter` 读写：非默认才写（wrap=true、displayValue=false、fitMode 非 shrink、lineHeight 非 1.2、fontFamily 非默认、qrEcc 非 M、qrMargin 非 2、paddingH/V >0），旧模板无新字段读回默认，无数据库迁移（layout 整块 JSON）。
- `SkiaLabelRenderer` 渲染支持：wrap 自动换行 + 行距（lineHeight 倍数）+ 超高整体缩小（最小 1.5mm）、overflow 隐藏裁剪不缩小、fontFamily 字体族（含 CJK 系统回退）、qrEcc / qrMargin 传 ZXing、条码 displayValue 底部数值文字（条码占剩余高度）、文本 / 条码 / 二维码双边内边距内容区（= 元素框减 padding）。
- ZPL 矢量路径不变量：新排版字段不参与 ZPL 编码，矢量输出与现状一致（新增不变量测试）。
- 测试 152 个全绿（新增字段往返 / 省略规则 / paddingMm 兜底 / wrap 换行与超高缩小 / overflow 不缩小 / 字体族 / QR 纠错与静区 / 条码文字 / 双边内边距 / 旧模板默认 Middle / ZPL 不变量）。
## 迭代 13（文本排版与二维码参数持久化，前后端已完成，2026-08-10）

- 前端（hermes）：`convert.ts` 的 `BackendElement` 补齐 `paddingH/paddingV/fontFamily/wrap/lineHeight/fitMode/qrEcc/qrMargin/displayValue`；写方向按契约非默认才写（wrap=true、displayValue=false、verticalAlign 非 Middle、fitMode 非 shrink 等）；读回 `?? 默认`（paddingH/V ?? paddingMm 旧模板兜底）；`ElementNode.tsx` TextContent wrap=true 超高由裁剪改为整体缩小（最小 1.5mm），与后端 Skia 渲染语义一致；convert.test.ts 64 用例全绿（+7 新增）。
- 复现验证：100×60 方案导入 → 保存 → 重开，关键差异清零（wrap / lineHeight / qrEcc / paddingV 均保留；剩余仅默认值显式化，显示一致）。
- 文档归档：`docs/ITERATION-13-SPEC.md` / `docs/ITERATION-13-CONTRACT.md` 标记已完成；ROADMAP 迭代 13 状态更新为「已完成（用户验收待执行）」；DESIGN 决策 #47 更新为前后端完成。
- 产物 `LabelFrame-0.12.0.msi`（2026-08-10）：含迭代 13 前后端合并版（元素契约第二批字段 + Skia 图片打印渲染 + 前端字段映射与 wrap 超高缩小），可覆盖 0.11.x 安装；用户测试验收待执行。
## 迭代 13 前端修复（0.12.1，2026-08-10）

- 修复画布中文长文本字高失真（commit abf58a0）：Konva `wrap='word'` 按空格分词，中文无空格永不换行 → 长文本单行溢出被 shrink 缩小；改为含 CJK 文本用 `wrap='char'` 逐字换行（与 Skia 打印语义一致），纯 ASCII 保持 `word`；shrink 缩小循环按「单行宽 ÷ 内容宽 = 行数」估算换行后总高，只对超高整体缩小（最小 1.5mm），并补 `lineHeight` 依赖。
- 实测：70×50 方案 MaterialName（fontH 3, wrap）修复前单行 1.59mm，修复后两行 2.85mm；64 单测 + build + lint 全绿。
- 产物 `LabelFrame-0.12.1.msi`（2026-08-10）：含该前端修复，可覆盖 0.12.0 / 0.11.x 安装。
## 打包优化（0.12.2，2026-08-10）

- MSI 升级不再覆盖用户配置：`appsettings.json` 从自动文件清单（AppFiles）中剔除，改为 `main.wxs` 中 GUID 固定的独立组件，标记 `NeverOverwrite="yes"`（升级 / 修复不覆盖）+ `Permanent="yes"`（卸载不删除）。新装仍写入默认配置；已改过的配置在后续更新中保留。
- 说明：`appsettings.json` 属于用户数据，卸载时也会保留（与 %LOCALAPPDATA%\LabelFrame 下的数据库一致）；需要全新默认配置时可手动删除该文件后重装。
- 产物 `LabelFrame-0.12.2.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 14（字体加粗 bold 契约，后端部分，2026-08-10）

- 前端（hermes，commit ae16d0d）：属性面板「加粗（打印更清晰）」复选框 + 画布 `fontStyle:'bold'`；convert.ts 契约字段 `bold`（true 才写 / 读回 ?? false）+ 单测；属性面板数字输入受控同步、右侧面板滚动条两项修复。
- 后端：`LabelTextElement.Bold`（bool，默认 false）+ `LabelElementJsonConverter` 写 `bold: true`（true 才写）、读回默认 false，旧模板兼容。
- ZPL（Vector）：新增 `ZplBoldMode`（默认 `FontVariant` 方案 A：粗体字体变体映射 `"0"→"1"`；`WidthScale` 方案 B：宽度 ×1.15 放大兜底）；`ZplEncoder` 构造函数可注入模式与映射表；WinHost `HostOptions.BoldMode` + `LABELFRAME_BOLD_MODE` 环境变量可配置。
- Skia（Image）：测量与绘制字体统一 `SKFont.Embolden = text.Bold`，换行 / shrink 度量按加粗字体计算，与前端预览一致。
- 测试 156 全绿（新增 bold 往返/省略、ZPL 方案 A/B、Skia 加粗墨迹对比）。
## 迭代 14 打包（0.12.3，2026-08-10）

- 含迭代 14 前后端合并版：文本加粗（`bold` 契约 + ZPL 方案 A/B + Skia Embolden）+ 前端加粗设置与属性面板两项修复。
- `appsettings.json` 保留用户配置机制沿用（独立组件 NeverOverwrite + Permanent）。
- 产物 `LabelFrame-0.12.3.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 15（打印设置与会话保留 + 连接管理 + 删除 ZPL，后端部分，2026-08-10）

- 彻底删除矢量 ZPL：移除 `IZplEncoder` / `ZplEncoder.Encode` / `ZplBoldMode` / `PrintMode`（配置 + `LABELFRAME_PRINT_MODE`）/ `SubmitJobRequest.printMode` / `/healthz.printMode` / `ITextRasterizer` / `GdiTextRasterizer` 及对应测试；`^GF` 位图编码重构为 `ZplImageEncoder`；作业项内容统一为整版位图指令（沿用历史列名，无迁移）；README / demo 脚本同步清理。
- 连接管理：`ITransportManager` + `TransportConfig`；`GET /api/transport`、`POST /api/transport`（单一连接、先测试后生效、失败自动回滚、400 沿用 ErrorView、响应统一 `config`=当前生效连接）；持久化 `%LOCALAPPDATA%\LabelFrame\connection.json`（启动优先级 connection.json > appsettings > 默认 Log）；Tcp / Windows 驱动 / Zebra 增加连接测试能力；打印 Worker / 打印机状态 / 测试页统一从管理器取当前连接；测试页改为 Skia 渲染整版位图 ^GF。
- 调试出图：新增 `POST /api/print/render-images`（批量渲染全部行返回 zip，`label-{n}.png`）；保留 `POST /api/print/render-image`（单张 PNG）；调试不建作业、不发驱动、不改作业模型 / SQLite。
- Log 模拟打印：`LogPrintTransport` 只记录摘要（不再写大段指令）；作业层渲染 PNG 保存到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\` 并写 host.log 摘要。
- AndroidHost：新增 `AndroidLabelRenderer`（Android.Graphics + ZXing）整版位图渲染 → `ZplImageEncoder`，替换 ZplEncoder（真机验收待 PDA 联调）。
- 测试 143 全绿（Core 60 / Server 8 / Studio 25 / WinHost 50）；AndroidHost 编译通过；前端（hermes）已实施会话保留 / 连接切换 UI / 调试开关（见下条）。


## 迭代 15（打印设置会话保留 + 连接管理 + 删除 ZPL，前端部分，2026-08-10）

- DataPrint 会话保留（§6.1）：草稿提升全局 AppContext（`printDraft`：selectedName / valuesByTemplate + dirtyKeysByTemplate / debugMode / jobId），sessionStorage 持久化（刷新保留、标签页天然隔离；**禁 localStorage**）；values 按 **key 存在性**合并（用户主动清空的字段不被 testData 顶回）；Excel 数据与列映射不保留。
- 连接管理 UI（§6.2）：AppContext 增 `transportConfig`（GET /api/transport），切换成功后立即用响应 config 更新全局状态；设置页「连接方式」分组（模式单选 Log / TCP / Windows 驱动 / Zebra，只显示当前模式参数，「测试连接」testOnly、「保存并应用」先测试后生效失败回滚）；DataPrint 顶部连接徽标 + 快速切换；状态栏 / 导航徽标显示 mode + 关键参数（TCP 192.168.1.50:9100 等）。
- 调试独立（§6.3）：独立开关（默认关）——开：「调试出图（单张）」走 render-image 下载 PNG、「下载调试图片 zip（N 张）」走 render-images（全部行），不建作业不发驱动，作业进度区提示；关：「打印测试 / 批量打印」正常作业 +「出图预览」即时预览。
- 删除（§3.2）：DataPrint / Settings 的 printMode 下拉与旧调试复选框、`Healthz.printMode` / `SubmitJobRequest.printMode` 类型。
- api client：新增 `getTransport` / `setTransport` / `testTransport` / `renderImages`；下载型端点统一 `fetchBlob`（Content-Disposition 文件名 + ErrorView 错误解析）。
- 测试 91 全绿（新增 27 个：draft 纯逻辑 / 连接切换交互 / 保留与调试按钮行为）；`pnpm build` / `pnpm lint` 通过。

## 迭代 15 打包（0.13.0，2026-08-10）

- 含迭代 15 前后端合并版：彻底删除矢量 ZPL（打印统一 Skia / Android 整版位图 ^GF）；连接管理 `GET/POST /api/transport`（单一连接、先测试后生效、失败回滚、持久化 connection.json）+ Web 设置页 / 数据与打印页连接切换 UI；调试独立（单张 PNG / 批量 zip，不建作业不发驱动）；DataPrint 会话保留（同标签页切视图不丢设置、标签页间不互通）；Log 模拟打印保存 PNG；AndroidHost 图片打印。
- 联调反馈修复：TCP 连接测试加固（IP 直连 + 本地监听开/关回归测试）；前端测试环境 storage 垫片（Node 26 下 jsdom 兼容，91 用例全绿）。
- `appsettings.json` 保留用户配置机制沿用；`%LOCALAPPDATA%\LabelFrame\connection.json` 保存用户连接配置。
- 产物 `LabelFrame-0.13.0.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 15 实测修复（连接测试严格化 + Log 输出可见，2026-08-10）

- 连接测试升级：Tcp / Zebra 均改为「连接 + `~HS` 主机状态探测」，无打印机响应判定失败（能连端口 ≠ 打印机），失败不切换、不持久化。
- Log 模拟打印：PNG 保存到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\`，作业视图新增 `printImageDir` / `printImageCount`，前端作业进度区显示目录与张数（此前用户找不到输出）。
- 新增回归测试：本地监听响应 `~HS` 判定成功、无响应判定失败（两向稳定）。

## 迭代 15 增强（设计器快捷操作说明，2026-08-10）

- 设计器画布顶部常驻核心快捷键提示条（`Ctrl+Z 撤销 · Ctrl+C/V 复制粘贴 · Delete 删除 · 中键平移 · Ctrl+滚轮缩放`），编辑模式随时可见（与预览模式提示同款视觉；预览时自动切换为预览提示）。
- 设计器工具栏新增「快捷键」按钮，弹出完整清单：编辑（撤销重做 / 删除 / 取消放置）、剪贴板（复制粘贴 / 导出导入设计 JSON）、画布（中键平移 / Ctrl+滚轮缩放 / Shift+Ctrl 多选 / 拖拽吸附 / 手柄缩放）三组。
- 测试 95 全绿（新增快捷键清单结构测试 4 个）。

## 迭代 15 打包（0.13.1，2026-08-10）

- 含迭代 15 前后端合并版 + 实测修复：连接测试升级为 `~HS` 打印机探测（能连端口≠打印机，无响应不切换）；Log 模拟打印目录随作业视图显示（`printImageDir` / `printImageCount`，前端进度区展示）；设计器快捷键提示条与「快捷键」弹窗（95 前端用例全绿）。
- 产物 `LabelFrame-0.13.1.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装；`appsettings.json` 保留机制沿用，连接配置持久化到 connection.json。
## 修复：本地 UI 打开地址规范化（2026-08-10）

- 当 `ListenUrl` 配置为 `0.0.0.0`（局域网访问）时，启动自动开浏览器与托盘「打开主界面」会跳到 `http://0.0.0.0:53960`；新增 `ToLocalUiUrl` 把通配监听地址（`0.0.0.0` / `*` / `+` / `[::]`）规范化为 `127.0.0.1`，本地界面始终可打开。
## 迭代 15 打包（0.13.2，2026-08-11）

- 前端 baseUrl 修复（附九定稿实施）：`getBaseUrl()` 无存储值时默认返回页面自身来源（`window.location.origin`）——PDA 远程访问不再发往自身回环 127.0.0.1；方案 B 自动纠正旧版保存的默认地址残留；新增 `settings.test.ts` 5 用例；`vite.config.ts` dev proxy（`/api` + `/healthz`）配套联调。前端 100 用例全绿。
- 后端（此前已合入）：本地 UI 打开地址规范化（`0.0.0.0` 监听时浏览器/托盘跳 127.0.0.1）、连接测试 `~HS` 探测、Log 模拟打印目录展示。
- 产物 `LabelFrame-0.13.2.msi`（2026-08-11）：可覆盖 0.12.x / 0.11.x 安装；`appsettings.json` 保留机制沿用。
## 迭代 16（服务端 / 客户端拆分，后端骨架，2026-08-11）

- 服务端（LabelFrame.Server）迁入集中能力：模板库（CRUD / 导入导出 / 预览）、作业提交支持 `templateName` 引用（pending 载荷附带模板 + 图片 base64）、调试出图（render-image / render-images，Skia）、设备日志接收与查询、Excel 导入、Web UI 静态托管（SPA fallback）；Server TFM 改 net10.0-windows（引用 Skia 渲染）。
- 客户端（WinHost）配合：`TemplateDto` 增 `Images`（base64），提交服务优先用内联图片否则按 Name 本地加载；路由 Worker 透传 Server 附带模板；`SqliteLogStore` 移至 Core.Logs 供两端共用；单机模式保留。
- 测试 147 全绿（Core 60 / Server 10 / Studio 25 / WinHost 52）；Server 新增 templateName 解析与模板不存在用例。
## 迭代 16/17（服务端 / 客户端拆分，0.14.0，2026-08-11）

- 前端（hermes，e161d81）：移除打印机连接 UI（连接方式 / 打印机分组、连接徽标与快速切换、transport API 与类型）；数据与打印新增目标设备选择（listDevices + targetDeviceId + templateName 提交），404/失败自动降级单机模式；JobView 适配 Server 作业视图；前端 105 用例全绿。
- 双 MSI 打包：`LabelFrame-Server-0.14.0.msi`（→ Program Files\LabelFrame\Server，服务端，默认 0.0.0.0:53961）与 `LabelFrame-Client-0.14.0.msi`（→ Program Files\LabelFrame\Client，打印客户端，默认 ServerUrl=127.0.0.1:53961）；两包 appsettings 保留机制沿用；打包脚本 `build-msi.ps1`（Client）与 `build-server-msi.ps1`（Server），文件清单 GUID 按包加盐避免冲突。
## 打包增强：卸载询问清除用户数据（0.14.0，2026-08-11）

- 两个 MSI 卸载时弹出确认对话框「清除用户数据（默认不勾选）」；勾选则删除本程序产生的数据：
  - Client：`%LOCALAPPDATA%\LabelFrame\` 下 jobs.db / templates.db / logs.db / host.log / connection.json / print 目录 + 安装目录 appsettings.json；
  - Server：`%LOCALAPPDATA%\LabelFrame\server\` 目录 + server.db + 安装目录 appsettings.json。
- 仅手动卸载触发（条件 `REMOVE=ALL AND NOT UPGRADINGPRODUCTCODE`），覆盖升级不会清数据；静默卸载不弹窗、默认保留。

## 迭代 18（决策与规格，进行中，2026-08-11）

- 架构修订（0.15.0）：服务端默认不提供界面（移除 web/dist 托管），客户端（WinHost 127.0.0.1:53960）托管完整 Web UI；模板 / 作业 / 设备投递仍以服务端为中心，作业走服务端队列。
- 服务端 Windows 服务部署（`LabelFrameServer`，LocalSystem）；数据目录默认改 `%ProgramData%\LabelFrame\server`；历史数据定期清理（作业默认保留 30 天、日志默认保留 90 天，可配置，非终态作业不删）。
- 客户端机器级 ServerUrl（WinHost `GET/POST /api/host/config` → `%ProgramData%\LabelFrame\Client\settings.json`）。
- 双 MSI 安装完成弹窗：Server（开机自启 / 立即运行，默认勾选）、Client（立即打开，默认勾选）；升级不触发。
- 规格与任务单：docs/ITERATION-18-SPEC.md；决策登记 docs/DESIGN.md #53-58；架构修订 docs/ARCHITECTURE-SPLIT.md。


## 迭代 18 后端实施（0.15.0，2026-08-11）

- Server 无头化：移除 Web UI 静态托管与测试页（/、/devices、/jobs），仅保留 /healthz 与 API；`GET /api/jobs` 支持 `?limit`（默认 100，上限 500）。
- Server Windows 服务：`builder.Host.UseWindowsService`（服务名 LabelFrameServer，LocalSystem）；控制台模式保留供开发；exe 图标改用 labelframe.ico。
- 数据目录改 `%ProgramData%\LabelFrame\server`（server.db / templates.db / logs.db；服务账户下 LOCALAPPDATA 不可靠）。
- 历史数据定期清理：`DataCleanupService`（启动 60s 后按周期执行，默认 24h）删除终态作业超 `JobRetentionDays`（默认 30 天）与日志超 `LogRetentionDays`（默认 90 天）；非终态作业不删；`ServerDb.DeleteTerminalJobsBeforeAsync` + `SqliteLogStore.DeleteBeforeAsync`。
- WinHost 机器级配置：`GET/POST /api/host/config`（serverUrl + deviceId/deviceName，仅回环可写），持久化 `%ProgramData%\LabelFrame\Client\settings.json`（缺失 / 损坏返回默认值；启动加载覆盖 ServerUrl）。
- WinHost 作业列表：`GET /api/jobs`（limit 默认 100 上限 500），JobView 扩展 CreatedAt / FailedItems / ErrorMessage / TargetDeviceId（本机作业为 null）。
- 双 MSI 0.15.0：Server 安装注册 Windows 服务 + 安装完成弹窗（开机自启 / 立即运行，默认勾选；按勾选 `sc config start= auto` / `net start`，升级不触发）；Client 安装完成弹窗（立即打开，默认勾选；启动客户端开界面）；Server 不再打包 web/dist；卸载清理路径含 ProgramData（Server server 目录 / Client settings.json）。
- 测试 156 全绿（Core 60 / Server 13 / WinHost 58 / Studio 25）；产物 `LabelFrame-Server-0.15.0.msi`（7.6MB）、`LabelFrame-Client-0.15.0.msi`（14.2MB）。


## 修复：Server 安装未注册 Windows 服务（0.15.1，2026-08-11）

- 根因：`ServiceInstall` 放在只有注册表 KeyPath 的独立组件，Windows Installer 拿不到服务二进制路径，服务创建被跳过（其余文件正常安装）。
- 修复：`ServiceInstall / ServiceControl` 移入 `LabelFrame.Server.exe` 所在组件（服务二进制 = 组件 KeyPath）；`generate-files.ps1` 新增 `-ServerServiceName / -ServerServiceDisplayName` 参数。
- 版本升至 0.15.1（MajorUpgrade 可覆盖已装的 0.15.0）；产物 `LabelFrame-Server-0.15.1.msi`（7.6MB）、`LabelFrame-Client-0.15.1.msi`（14.2MB）。
- 覆盖升级（0.15.0 → 0.15.1）不弹完成弹窗、不自动启动，服务注册为手动启动，可 `net start LabelFrameServer` 或服务管理器启动；全新安装仍弹完成弹窗（默认自启 + 立即运行）。


## 修复：安装完成弹窗的「自启 / 立即运行 / 立即打开」动作未触发（0.15.2，2026-08-11）

- 现象：0.15.1 全新安装后服务已注册，但 StartType=Manual 且从未启动（弹窗点确认后后续 InstallUISequence 动作不执行——最后一个对话框 EndDialog 后序列结束）。
- 修复：改为弹窗「确认」按钮点击时通过 `DoAction` 直接触发：Server（SetAutoStart → sc config start= auto；StartServiceNow → net start）、Client（LaunchClient）；移除 InstallUISequence 中的尾部 Custom 动作。
- 版本升至 0.15.2；产物 `LabelFrame-Server-0.15.2.msi`（7.6MB）、`LabelFrame-Client-0.15.2.msi`（14.1MB）。


## 简化：Server 服务安装改为“注册即自动 + 安装时启动”，完成弹窗仅提示（0.15.3，2026-08-11）

- 按用户反馈简化：移除 Server 完成弹窗的「开机自启 / 立即运行」勾选项与 `sc config / net start` 自定义动作；`ServiceInstall Start=auto` + `ServiceControl Start=install`（安装即自动 + 启动），完成弹窗仅提示“服务已注册并自动启动”。
- 实装验证：静默安装 0.15.3 后 `START_TYPE=AUTO_START`、服务 RUNNING、healthz OK。
- 双包版本对齐 0.15.3：`LabelFrame-Server-0.15.3.msi`（7.6MB）、`LabelFrame-Client-0.15.3.msi`（14.1MB，Client 交互不变：完成弹窗仍含「立即打开」勾选）。


## 迭代 18 前端合入与联调（0.15.3 客户端，2026-08-11）

- 前端（hermes，0668d03）：F1-F7 全部完成——双 base（serverApi / localApi）、机器级配置（/api/host/config 启动加载 + 保存即生效）、恢复连接方式（TransportPanel）与打印机分组、数据与打印本机设备默认选中 + 顶部连接徽标、作业历史页（limit=100，空态按模式）、Workbench/Designer/PdaLogs 跟随 serverMode 降级；前端测试 125 全绿、build/lint 通过。
- 后端复核：契约对齐（JobView.createdAt / HostConfig.serverUrl-deviceId-deviceName / TransportConfig / PrinterStatus 字段与后端一致）。
- 客户端 MSI 0.15.3 重新打包（含新前端 dist index-DfhhEvBH.js）；端到端冒烟通过：Client 注册在线 → 提交作业 Pending → Completed 2/2 → Log 模拟 PNG 落盘；页面引用新 bundle。


## 0.15.4：推送等效 + 客户端弹窗关闭修复 + 弹窗文字简化（2026-08-11）

- 服务端推送（长轮询通知）：`GET /api/devices/{deviceId}/jobs/notify?timeout=N`——作业入队立即返回 hasPending=true（等效推送），同时刷新设备心跳；客户端 `ServerRoutingWorker` 改为长轮询等待 → 立即领取，打印等待从最多 5s 轮询降为 <1s；网络异常回退间隔重试。
- 客户端安装完成弹窗无法关闭：`LaunchClient` 改为 `cmd /c start` 非阻塞启动（msiexec 不再等待 GUI 进程退出）。
- 弹窗文字去掉「（默认勾选）」「（默认不勾选）」括号说明。
- 测试 162 全绿（Server 17 / WinHost 60 / Core 60 / Studio 25）；产物 `LabelFrame-Server-0.15.4.msi`（7.6MB）、`LabelFrame-Client-0.15.4.msi`（14.1MB）。


## 迭代 19：Ubuntu 服务端部署 + 跨机验证（2026-08-11，进行中）

- Rendering / Server 多目标框架 `net10.0;net10.0-windows`：GDI 预览（LabelPreviewRenderer）仅 Windows（#if WINDOWS）；Server `UseWindowsService` / 应用图标 / WindowsServices 包仅 Windows；Linux 用 systemd。
- SkiaSharp Linux 原生库：新增 `SkiaSharp.NativeAssets.Linux`（net10.0）；发布产物含 `libSkiaSharp.so` / `libe_sqlite3.so`。
- Server 数据目录按平台默认：Windows `%ProgramData%\LabelFrame\server` / Linux `/var/lib/labelframe/server`；`LABELFRAME_SERVER_*` 覆盖。
- 交付：`scripts/publish-server-linux.ps1`（framework-dependent / self-contained，tar.gz 归档）、`scripts/deploy-server-ubuntu.sh`（用户/目录/systemd 自启/防火墙提示）、`packaging/ubuntu/labelframe-server.service`、`packaging/ubuntu/Dockerfile`。
- 测试 162 全绿；Windows Server MSI 打包回归正常；linux-x64 产物 6.7MB（归档）。
- 跨机验证（服务端 Linux + 客户端 Windows）待真机 / 容器执行：验证清单见 docs/ITERATION-19-SPEC.md §5。
