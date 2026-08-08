# LabelFrame

面向仓库场景的标签打印框架：模板契约 + 打印服务 + 设备宿主（PC / PDA）。

## 愿景

**方便仓库完成标签打印，提高办公效率。**

围绕这个愿景：
- **方便** = 少步骤、就近打印、零学习成本、少故障、少求人；
- **效率** = 打印快、能批量、不重复劳动、不出错、融进业务动作。

## 当前状态

- 迭代 0（奠基）：文档体系 + 解决方案骨架。
- 迭代 1（契约与 ZPL）：契约 / 版式模型、数据校验、ZPL 编码器（文本 / Code128 / 图片占位）、日志传输，单元测试全绿。
- 迭代 2（WinHost 打印闭环）：作业队列（SQLite 持久化 / 幂等 / 挂起恢复取消）、本地 HTTP API、打印 Worker、GDI 中文栅格化、TCP9100 / Windows 驱动 / Zebra SDK 传输，.NET 10（本迭代；真实设备验收待执行）。

详见 [docs/ROADMAP.md](docs/ROADMAP.md)。

## 组成

| 项目 | 说明 |
|---|---|
| `LabelFrame.Core` | 契约 / 版式模型、数据校验、ZPL 编码、作业队列（迭代 1 起实现） |
| `LabelFrame.Server` | 轻量服务端：设备注册、作业定向投递、测试入口（迭代 3 起实现） |
| `LabelFrame.WinHost` | Windows 打印宿主（迭代 2 起实现） |
| `LabelFrame.AndroidHost` | Android / PDA 打印宿主（迭代 5 起实现） |


## 快速验证（迭代 2）

无需打印机即可验证 WinHost 打印闭环（日志传输模拟打印机）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\demo-winhost.ps1
```

脚本会：构建 → 启动 WinHost（127.0.0.1:53999，日志传输）→ 提交一个含中文的库位码作业（2 张）→ 展示生成的 ZPL。

接真实 Zebra 打印机（USB / IP / 驱动）时，设置环境变量后运行 WinHost：

```powershell
$env:LABELFRAME_TRANSPORT = "Zebra"        # Zebra SDK；或 Tcp / WindowsDriver / Log
$env:LABELFRAME_TCP_HOST = "192.168.1.50"  # Zebra TCP 模式
$env:LABELFRAME_PRINTER = "ZDesigner ZD421-203dpi ZPL"  # Zebra Driver / WindowsDriver 模式
$env:LABELFRAME_DB = "C:\LabelFrame\jobs.db"
dotnet run --project src\LabelFrame.WinHost
```

本地 API（默认 127.0.0.1:53911，演示脚本用 53999）：
- `POST /api/jobs`：提交作业（requestId + 自包含模板 + labels[]），返回 jobId。
- `GET /api/jobs/{jobId}`：进度与逐张状态。
- `POST /api/jobs/{jobId}/suspend|resume|cancel`：挂起 / 恢复 / 取消。
## 文档

- [docs/DESIGN.md](docs/DESIGN.md) —— 架构设计与决策记录
- [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) —— 需求：场景、底线、能力、边界、成功衡量
- [docs/ROADMAP.md](docs/ROADMAP.md) —— 迭代计划与状态
- [AGENTS.md](AGENTS.md) —— 给 AI 协作的常驻约束