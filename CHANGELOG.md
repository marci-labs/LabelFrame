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