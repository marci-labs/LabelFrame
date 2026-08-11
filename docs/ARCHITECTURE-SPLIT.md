# LabelFrame 服务端 / 客户端拆分设计

> 状态：设计已确认（2026-08-11，用户拍板 6 项决策），待实施
> 协作：本文档定义拆分后的职责边界、跨端契约、部署形态与迁移路径；实施按 ROADMAP 迭代排期（建议迭代 16 / 17）。
> 背景：PDA 联调延后（暂无蓝牙 / IP 打印机，打印以 PC + USB 为主）；当前单机 WinHost = 服务 + 打印 + Web UI 一体，拆分为「服务端集中部署」与「客户端打印部署」两个安装包。

---

## 1. 目标

- 多台带打印机的 PC 共用一个服务端：业务人员在服务端浏览器设计模板、提交作业、看进度；客户端只负责接打印机并执行打印。
- 服务端不依赖打印机；客户端不托管 Web UI。
- 保留单机能力（Server + Client 同机）作为旧 0.13.x 的迁移路径。

## 2. 已确认决策（用户拍板，2026-08-11）

1. **Web UI 全放服务端**（工作台 / 设计器 / 数据与打印 / 日志 / 设置，不含打印机连接）；客户端只留托盘 + 本机小页面。
2. **打印机连接配置在客户端本机配置**（托盘 / 本机小页面；不跨端下发，服务端只显示设备在线与回报的打印机状态）。
3. **调试出图在服务端渲染**（Skia 同源，浏览器直接下载单张 PNG / 批量 zip）；**最终打印仍以客户端渲染为准**（与打印机 DPI / 传输一致）。
4. **保留单机模式**：Server + Client 同机安装兼容（Client 默认指向 127.0.0.1:53961）。
5. **作业提交改为 `templateName + labels`**（模板库在服务端；客户端领取时服务端附带模板数据）；自包含模板接口保留兼容。
6. **双安装包、同版本号**：`LabelFrame-Server-x.y.z.msi` / `LabelFrame-Client-x.y.z.msi`。

## 3. 职责边界

### 3.1 服务端（LabelFrame.Server）
- **模板中心**：模板库（SQLite）、CRUD / 导入导出 / 契约 / 浏览器设计器数据。
- **作业中心**（SQLite）：提交（templateName + labels）、进度查询、失败重试、集中作业列表。
- **设备与投递**：设备注册 / 心跳 / 目录、定向投递（pending 队列）、结果回报（沿用迭代 3 模型）。
- **Web UI 静态托管**：完整前端（工作台 / 设计器 / 数据与打印 / 日志 / 设置——打印机连接项移除，迁至客户端）。
- **调试出图**：引用 LabelFrame.Rendering（Skia），`render-image`（单张 PNG）/ `render-images`（批量 zip）。
- **Excel 导入、日志接收**（客户端 / PDA 日志回传与集中查看，从 WinHost 迁入）。
- 部署：Windows 服务 / 控制台；无打印机依赖；默认端口 53961。

### 3.2 客户端（LabelFrame.Client，演进 WinHost）
- **打印执行**：本机打印机连接（WindowsDriver / TCP / Zebra / Log）、Skia 渲染 → `ZplImageEncoder`（^GF）、打印 Worker。
- **作业领取**：轮询 Server `GET /api/devices/{deviceId}/jobs/pending` → 本地渲染打印 → 回报 `result`；领取响应附带模板数据（contract + layout + testData + images）。
- **本地能力**：打印机状态 / 测试页、连接方式配置（本机小页面 + 托盘）、本机日志。
- **可选**：本机直连 `POST /api/jobs`（单机兼容，无 Server 时局域网直连打印）。
- 部署：托盘程序（开机自启）；不托管 Web UI、不自动开浏览器。

## 4. 跨端契约（公共契约变更，以本文档为准）

| 接口 | 说明 |
|---|---|
| `POST /api/devices` | 设备注册 / 心跳（沿用） |
| `GET /api/devices/{deviceId}/jobs/pending` | 领取作业；**响应附带模板**（contract + layout + testData + images）与 labels |
| `POST /api/devices/{deviceId}/jobs/{jobId}/result` | 结果回报（成功 / 失败 + 原因）（沿用） |
| `POST /api/jobs` | 作业提交：`{ requestId, templateName, labels[], targetDeviceId? }`（新增 templateName 引用；自包含模板保留兼容） |
| `GET/POST/DELETE /api/templates...` | 模板库（从 WinHost 迁入 Server） |
| `POST /api/logs` | 客户端 / PDA 日志回传（沿用） |
| `POST /api/print/render-image` / `render-images` | 服务端调试出图（从 WinHost 迁入） |
| 客户端连接配置 | 仅本机 API（127.0.0.1），不跨端下发 |

## 5. 部署形态与版本

- 双 MSI：`LabelFrame-Server-x.y.z.msi` / `LabelFrame-Client-x.y.z.msi`，同版本号（首个拆分版建议 0.14.0）。
- 前置：.NET 10 Desktop Runtime（与现状一致）。
- 服务端：Windows 服务（自启，可选）或控制台；客户端：托盘程序、开机自启（沿用现有托盘实现）。
- `appsettings.json` 保留机制、`connection.json` 连接持久化沿用。

## 6. 单机模式与迁移

- 单机 = Server + Client 同机安装（Client 默认 `ServerUrl=http://127.0.0.1:53961`）。
- 旧 0.13.x 单机用户迁移：安装 Server + Client 两包；把 WinHost 数据目录（`%LOCALAPPDATA%\LabelFrame` 下 templates.db / jobs.db / logs.db）复制到 Server 数据目录；提供迁移说明（必要时给脚本）。
- 旧 Web UI 的打印机连接设置迁移到客户端本机小页面。

## 7. 实施迭代规划（建议）

- **迭代 16（拆分骨架，后端为主）**：Server 增模板库 / 作业持久化 / Web UI 静态托管 / 调试出图 / Excel / 日志（从 WinHost 迁入）；Client 默认路由领取模式（ServerUrl 必填）；pending 响应附带模板；双项目构建与单元测试全绿。
- **迭代 17（联调 + 打包）**：Web UI 指向 Server（前端 baseUrl 默认 Server、移除打印机连接项）；双 MSI 打包；单机迁移说明；端到端验收（服务端浏览器设计 → 提交 → 客户端 USB 打印 → 结果回服务端）。
- 完成定义：双包可独立安装部署；单机可迁移；`dotnet test` / `pnpm test` 全绿。

## 8. 不在范围 / 风险

- PDA（延后，AndroidHost 暂不改；跨端契约变更保留其兼容性）。
- 多语言、云部署、服务端高可用 / 负载均衡。
- 风险：跨端契约变更影响面较大（模板 / 作业 / 渲染归属），需在迭代 16 先定接口并用测试锁定；Web UI 拆分后打印机连接入口迁移到客户端本机页面（UX 变化需在客户端小页面补体验）。


## 迭代 16 后端实施记录（2026-08-11）

- Server：模板库（Core.Templates 复用）、`templateName` 提交（pending 附带模板 + 图片 base64）、render-image / render-images（Skia）、/api/logs（Core.Logs）、/api/import/excel、Web UI 静态托管；TFM net10.0-windows。
- Client（WinHost）：TemplateDto.Images（base64）+ JobSubmissionService 内联图片优先 + 路由 Worker 透传模板；单机模式不变。
- 跨端契约落地：`POST /api/jobs` 支持 `templateName`；`pending` 响应 TemplateDto 含 `Name` / `Images`（base64）；`POST /api/templates...` / `/api/logs` / `/api/print/render-*` / `/api/import/excel` 归属服务端。
- 测试 147 全绿。


---

## 迭代 17 前端任务单（hermes 实施，2026-08-11）

> 背景：迭代 16 后端骨架已完成——服务端具备模板库 / 作业（templateName + targetDeviceId）/ 调试出图 / 日志 / Excel / Web UI 静态托管；pending 附带模板与图片。前端从「单机 WinHost 前端」调整为「服务端前端」，并兼容单机过渡。

### 1. API client 与类型（web/src/lib/api）
- `types.ts`：
  - `SubmitJobRequest` 增加 `templateName?: string`、`targetDeviceId?: string`（自包含 `template` 保留兼容；服务端模式优先 `templateName`）。
  - `Healthz.transport?` 保持可选（Server 不返回；无传输概念时状态灯显示「已连接」即可）。
  - 新增 `DeviceView` 类型（deviceId / name / status）。
  - transport 相关类型（TransportConfig / TransportApplyRequest / TransportResult）在 UI 移除后可一并清理。
- `client.ts`：
  - 新增 `listDevices()`（GET /api/devices）。
  - `submitJob` 支持 `templateName` / `targetDeviceId`。
  - 删除或停用 `getTransport` / `setTransport` / `testTransport`（连接配置迁至客户端本机）。
  - 模板 / 调试出图 / 日志 / Excel API 路径不变（Server 同路径）。
- 兼容降级：`listDevices()` 404 / 失败时按「单机模式」处理（隐藏设备选择、提交不带 `targetDeviceId`），保证过渡期仍可用单机 WinHost 托管。

### 2. 移除打印机连接相关 UI（迁至客户端本机）
- 设置页：删除「连接方式」分组（模式单选 / 参数 / 测试连接 / 保存并应用）与「打印机」分组（状态 / 测试打印）；保留「后端地址」与连接测试（连 Server）。
- 数据与打印页：删除顶部连接徽标与快速切换（AppContext.transportConfig 相关）。
- AppContext：删除 `transportConfig` / transport 状态与相关轮询（healthz 仍用于连接探测）；`formatTransport` 等工具删除。
- 测试：Settings.test.tsx / DataPrint.test.tsx 中连接切换、徽标、回滚等用例删除或改写；新增「后端地址指向 Server」「目标设备选择」用例。

### 3. 数据与打印新增「目标设备 / 客户端」选择
- 页面加载时拉取设备列表（GET /api/devices），下拉选择目标设备（显示设备名 + 在线状态）；提交作业带 `targetDeviceId`。
- 无设备时提示「暂无在线客户端，请先在打印电脑安装并启动 LabelFrame Client」。
- 单机降级：设备接口不可用时隐藏设备选择、正常提交（不带 targetDeviceId）。

### 4. 验收
- Server 托管 Web UI 下：模板设计 / 保存、数据与打印选择设备 → 提交 → 进度可见；无打印机连接 UI。
- 兼容：指向单机 WinHost（无 /api/devices）时页面仍可用（隐藏设备选择、正常提交）。
- `pnpm test` / `pnpm build` / `pnpm lint` 全绿。

### 5. 备注
- 前端构建产物不变（由 Server 静态托管）；hermes 完成后 push，后端合入后打双 MSI（迭代 17 打包）。
