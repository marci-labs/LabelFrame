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
- 迭代 2（WinHost 打印闭环）：作业队列（SQLite 持久化 / 幂等 / 挂起恢复取消）、本地 HTTP API、打印 Worker、GDI 中文栅格化、TCP9100 / Windows 驱动 / Zebra SDK 传输，.NET 10。
- 迭代 3（Server 路由）：设备注册 / 心跳 / 目录、作业定向投递（宿主轮询领取）、结果回报、集中查询、测试入口。
- 迭代 4（模板管理 + 预览）：模板包 zip 导入导出、SQLite 模板库（按分组）、设计期 PNG 预览（ZXing 条码 / 二维码）。
- 迭代 6（P1 收尾）：失败项单独重打、打印机测试页 / 在线状态（本迭代；真实设备字段联调待执行）。
- 迭代 5（PDA 宿主）：AndroidHost（前台服务 / 开机自启 / 本地 HTTP / IP 9100 / Server 轮询 / 中文栅格化）已编译打包（真机验收待执行）。
- 迭代 7（Studio 模板工具 V1）：WPF 可视化界面管理模板、导入 `.lfpkg`、选模板预览并测试打印。
- 迭代 8（Studio 版式编排 V2）：画布拖拽编排元素、属性面板、契约字段编辑、保存与预览。
- 迭代 8B（Studio 版式增强）：字段键 / 显示名可编辑（引用联动）、新元素默认下排、文字对齐 / padding / 边框、区域（格子）布局。
- 迭代 8C（Studio 界面重构）：作业工作台 + 独立模板设计器（控件栏拖拽 / 毫米网格画布 / 区域拖矩形与元素入格居中 / 填充固定值或字段 / 本地实时预览 / 底部状态与日志栏）（本迭代；界面验收待执行）。
- 迭代 8D（设计器交互重做：容器控件 / 设计测试分离 / 字段自动推导 / 标尺对齐 / 框选多选手柄）：已完成（界面验收待执行）。
- 迭代 9（Excel 数据导入）：已完成（选 .xlsx → 列映射 → 批量打印）。
- 迭代 8E/8F（Web 设计器原型 v2/v3）：视口缩放 / 条码二维码实时渲染 / 智能参考线 / 文本溢出 / 画布留白标尺 / 真实比例 1mm=8点；`prototypes/web-designer/`，用于 UI 技术选型评估。
- 迭代 9（Excel 导入）/ 迭代 10（MSI 安装包）：计划中。

详见 [docs/ROADMAP.md](docs/ROADMAP.md)。

## 组成

| 项目 | 说明 |
|---|---|
| `LabelFrame.Core` | 契约 / 版式模型、数据校验、ZPL 编码、作业队列（迭代 1 起实现） |
| `LabelFrame.Server` | 轻量服务端：设备注册、作业定向投递、测试入口（迭代 3 起实现） |
| `LabelFrame.WinHost` | Windows 打印宿主（迭代 2 起实现） |
| `LabelFrame.Studio` | Windows 模板工具：管理 / 导入导出 / 预览 / 测试打印（迭代 7 起实现） |
| `LabelFrame.AndroidHost` | Android / PDA 打印宿主（迭代 5 起实现） |


## 安装包（迭代 10）

一键构建 MSI（需已安装 WiX Toolset v7 与 node / pnpm）：

```powershell
# 先构建前端产物
cd web; pnpm install; pnpm build; cd ..
# 一键打包（联网发布 self-contained + WiX 构建）
.\scripts\build-msi.ps1
```

产物：`artifacts\LabelFrame-0.11.0.msi`（约 10MB，framework-dependent，含 WinHost + Web UI + 应用图标）。

前置要求：目标机需安装 **.NET 10 Desktop Runtime**（下载：https://dotnet.microsoft.com/download/dotnet/10.0）；安装 MSI 时若缺失会弹出可点击的官方下载链接（不自动安装），启动时也会提示。

干净电脑使用：安装 MSI → 桌面 / 开始菜单「LabelFrame」→ 双击图标 → 自动启动服务并打开浏览器（http://127.0.0.1:53960）→ 直接模板编辑与打印测试。系统托盘出现 L 图标：双击打开界面、右键可退出。

辅助脚本：
- `scripts\generate-icon.ps1`：生成应用图标（双色 L 型，assets\labelframe.ico）。
- `scripts\create-signing-cert.ps1`：生成自签名代码签名证书（openssl + .NET 重封装）；`scripts\build-msi.ps1 -Sign` 签名 MSI（需本机有 signtool，或用环境变量 SIGNFILE 指定）。正式分发建议购买商业代码签名证书。
- `scripts\cleanup-residue.ps1`：管理员运行，清理历史安装残留（旧目录 / 快捷方式 / 注册表 / 数据目录）。


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

本地 API（默认 127.0.0.1:53960，演示脚本用 53999）：
- `POST /api/jobs`：提交作业（requestId + 自包含模板 + labels[]），返回 jobId。
- `GET /api/jobs/{jobId}`：进度与逐张状态。
- `POST /api/jobs/{jobId}/suspend|resume|cancel`：挂起 / 恢复 / 取消。

## Studio 使用（迭代 7）

```powershell
# 先构建并启动 WinHost（或让 Studio 一键启动）
dotnet run --project src\LabelFrame.WinHost
# 再启动 Studio
dotnet run --project src\LabelFrame.Studio
```

Studio 连接 WinHost（默认 127.0.0.1:53960）后：按分组浏览模板 → 导入 `.lfpkg` → 选模板填数据 → 刷新预览 → 打印测试（走 WinHost 当前传输：Log / TCP / Zebra / 驱动）。
## 文档

- [docs/DESIGN.md](docs/DESIGN.md) —— 架构设计与决策记录
- [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) —— 需求：场景、底线、能力、边界、成功衡量
- [docs/ROADMAP.md](docs/ROADMAP.md) —— 迭代计划与状态
- [AGENTS.md](AGENTS.md) —— 给 AI 协作的常驻约束