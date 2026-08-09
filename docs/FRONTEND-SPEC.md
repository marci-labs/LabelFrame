# LabelFrame Web 前端规格（V2 · 交付 hermes 开发）

> 本文档是 LabelFrame 单机模式 Web 前端的完整开发规格。目标读者：前端开发者（hermes）。开发期间可随时对照现有交互原型 `prototypes/web-designer/`（功能已获用户认可，但**不要修改该目录**，它将被业务用于截图沟通）。
> 后端（LabelFrame.WinHost 演进为单机服务）由主 agent 并行开发，本文「API 契约」为双方对齐的接口，如遇不一致以后端实际实现为准并同步更新本文档。

## 1. 项目位置与技术栈

- 位置：仓库新建 `web/` 目录（与 `prototypes/` 并列，互不影响）。
- 技术栈：**Vite + React + TypeScript**。
  - 构建：`npm create vite@latest`（React + TS 模板），`vite build` 产物输出 `web/dist`（后端静态托管该目录，路径与后端约定为 `web/dist`）。
  - 渲染：画布使用 **Konva + react-konva**（与原型一致，便于交互移植）。
  - 条码 / 二维码：**JsBarcode + qrcode-generator**（与原型一致；库文件可复制自 `prototypes/web-designer/` 或 npm 安装）。
  - HTTP：`fetch` 即可，不强制引入 axios。
  - 样式：轻量自写 CSS（参考原型视觉：左侧控件栏 + 字段 + 图层，中间画布，右侧属性面板，底部状态 + 日志，顶部工具栏）；不引入重型 UI 框架。

## 2. 页面与路由

单页应用，左侧主导航 Tab 切换（无需路由库，用 state 即可）：

1. **工作台（模板管理）**
2. **设计器（模板编辑）**
3. **数据与打印（Excel 导入 / 批量打印 / 打印测试）**
4. **PDA 日志**
5. **设置（连接）**

## 3. 功能规格

### 3.1 连接设置（设置 Tab）
- 后端地址输入框，默认 `http://127.0.0.1:53960`。
- 「测试连接」按钮：调用 `GET /healthz`，成功显示传输模式。
- 连接状态显示在底部状态栏（已连接 / 未连接）。

### 3.2 工作台（模板管理）
- 模板列表：`GET /api/templates`（返回 `[{ name, group, updatedAt }]`），按分组显示（可下拉过滤）。
- 按钮：新建（跳到设计器，空模板）、编辑（打开设计器）、删除（确认后 `DELETE /api/templates/{name}`）、导出（`GET /api/templates/{name}/export` 下载 .lfpkg）、导入（`POST /api/templates/import` 上传 .lfpkg）。
- 双击模板打开设计器。

### 3.3 设计器（核心，移植原型交互）
- 打开模板：`GET /api/templates/{name}` → `{ name, group, contract, layout, testData }`。
- 画布：毫米标尺 + 网格；左键选中 / 拖拽（智能参考线 + 1mm 网格吸附）；8 手柄缩放；Shift 多选；Delete 删除；中键平移；Ctrl+滚轮缩放。
- 控件栏：文本 / 条码 / 二维码 / 矩形（点击放置 / 拖入画布）。
- 属性面板（选中才显示）：位置 / 尺寸（X / Y / 宽 / 高）、填充（固定值 或 字段填充=键名称 + 预览值）、边框 / 上下左右内边距、文本（字体 / 字高 / 行间距 / 水平 / 垂直对齐 / 自动换行 / 单行溢出）、条码（码制 / 底部文字 / 模块宽）、二维码（纠错 / 边距）、矩形（边框）。
- 图层列表：显示全部控件（固定值显示内容；字段填充显示「(键名) 预览值」；条码 / 二维码带类型前缀），支持置顶 / 上移 / 下移 / 置底、点击选中、Delete 删除。
- 字段列表：由字段填充元素的键名称自动推导（只读）。
- 快捷键：Ctrl+C/V 复制粘贴、Ctrl+Z/Y 撤销恢复、Ctrl+Shift+C 导出设计 JSON、Ctrl+Shift+V 导入设计 JSON（`labelframe-web-design` 格式，字段：`{ format, version, paperW, paperH, elements[] }`；与原型导出格式一致）。
- 测试数据（右侧面板）：按契约字段生成表单（键名称 + 值），供打印测试与保存 `testData`。
- **DPI 打印预览**：顶部 DPI 选择（203 / 300）+「预览打印效果」按钮；预览 = 仅显示标签宽高范围（无标尺 / 网格 / 留白），隐藏并锁定编辑，可中键平移 / Ctrl+滚轮缩放，再点退出。
- **保存**：`POST /api/templates`（带 testData，见 API 契约）；保存后返回工作台。
- 纸张宽高输入在顶部；新建时默认 100×60。

> 交互细节一律以 `prototypes/web-designer/` 现有实现为准（已获用户验收）。移植时优先复用其逻辑（可整体翻译为 TS 模块）。

### 3.4 数据与打印
- 当前模板的测试数据表单（同设计器 testData；打印测试单张用此数据）。
- **Excel 导入**：选模板 → 上传 `.xlsx` → `POST /api/import/excel`（multipart file）→ 返回 `{ headers, rows }` → 弹映射界面（每列 → 字段键，自动按列名匹配，可手工调整）→ 批量打印 `POST /api/jobs`（labels 一次提交多张）→ 显示作业进度与失败原因（轮询 `GET /api/jobs/{id}`）。
- 打印测试：`POST /api/jobs` 单张（requestId + 模板 + 1 条数据），轮询进度；默认后端 Log 传输（无需打印机）。

### 3.5 PDA 日志
- 列表：`GET /api/logs?deviceId=`（可空），显示设备 / 时间 / 内容；自动轮询（每 5 秒）。
- 清空：`DELETE /api/logs`（可选；无则忽略）。
- 用途：PDA 打印测试后日志回传，PC 上直接查看分析。

## 4. API 契约（后端实现，前端据此对接）

Base：后端地址（默认 `http://127.0.0.1:53960`）。JSON 一律 camelCase。

| 方法 | 路径 | 请求 | 响应 |
|---|---|---|---|
| GET | /healthz | - | `{ service, status, transport }` |
| GET | /api/templates?group= | - | `[{ name, group, updatedAt }]` |
| POST | /api/templates | `{ name, group, contract, layout, testData? }` | `{ name, group }` |
| GET | /api/templates/{name} | - | `{ name, group, contract, layout, testData }` |
| DELETE | /api/templates/{name} | - | 204 |
| GET | /api/templates/{name}/export | - | `.lfpkg` zip 下载 |
| POST | /api/templates/import | multipart `file` | 模板名（文本） |
| POST | /api/import/excel | multipart `file` | `{ headers: string[], rows: string[][] }` |
| POST | /api/jobs | `{ requestId, template: { contract, layout }, labels: [{ data }] }` | `{ jobId, requestId, status, totalItems, completedItems, items[] }` |
| GET | /api/jobs/{jobId} | - | 同上（轮询） |
| POST | /api/jobs/{jobId}/suspend\|resume\|cancel | - | 作业视图 |
| POST | /api/jobs/{jobId}/items/{index}/retry | - | 作业视图 |
| GET | /api/printer/status | - | `{ isOnline, isPaperOut, isPaused, message }` |
| POST | /api/printer/test | - | `{ sent, bytes }` |
| POST | /api/logs | `{ deviceId, lines: string[] }` | 201 |
| GET | /api/logs?deviceId=&since= | - | `[{ deviceId, time, line }]` |

契约 / 版式元素 JSON（`contract` / `layout`）：
- `contract`：`{ name, version, fields: [{ key, displayName, isRequired, type }] }`
- `layout`：`{ name, contractName, contractVersion, widthMm, heightMm, elements: [...] }`
- 元素数组由 `LabelElementJsonConverter` 按 `type` 判别：`text` / `barcode` / `qrcode` / `image` / `line` / `region`；字段 camelCase（`xMm`, `yMm`, `sourceKey`, `literal`, `fontHeightMm`, `widthMm`, `textAlign`, `paddingMm`, `borderMm`, `regionId` 等）。详见 `prototypes/web-designer/app.js` 的 `toElementJson` / `parseElement`（原型已实现往返）。

## 5. 验收清单

1. `npm run build` 通过；`web/dist` 可由后端静态托管直接访问。
2. 连接后端后：工作台列出模板；新建 → 设计器 → 添加 4 类控件 → 属性编辑 → 保存 → 工作台可见。
3. 设计器交互（拖拽 / 吸附 / 缩放 / 多选 / 删除 / 撤销恢复 / 复制粘贴 / 图层 / 字段推导 / DPI 预览）与原型一致。
4. Excel：上传 10 行 xlsx → 映射 → 批量提交成功 → 轮询显示进度。
5. PDA 日志：手动 POST 测试数据后界面可见。
6. 全中文界面；无公司 / 业务线品牌字样。

## 6. 协作约定

- 前端只依赖本文 API 契约；后端开发期间接口可能有小调整，遇到 404 / 字段不符时先按契约实现，联调阶段统一修。
- 提交信息用 Conventional Commits（中文），不推 tag。