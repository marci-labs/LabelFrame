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