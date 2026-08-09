# Web 设计器原型（技术选型评估）

用于评估「Studio UI 层改用 Web 技术」的拖拽设计器原型。后端（WinHost / Core / Rendering / Server / AndroidHost）完全复用，原型通过 WinHost HTTP API 加载 / 保存模板与生成预览。

## 运行

1. 启动 WinHost（Log 模式即可）：
   ```powershell
   dotnet run --project src\LabelFrame.WinHost
   ```
2. 用浏览器打开本目录 `index.html`（直接双击即可，无需服务器）。
3. 顶部填 WinHost 地址（默认 `http://127.0.0.1:53960`）→「连接」→ 可「加载」已有模板、「保存」、「预览」。

> 说明：`konva.min.js` 已本地化，原型离线可打开；未连接 WinHost 时仍可本地拖拽设计（保存 / 预览需连接）。

## 功能对照（与 WPF 设计器 8D 对齐）

- 控件栏：文本 / 条码 / 二维码 / 图片 / 线 / 容器（点击后在画布放置，或直接拖入）。
- 画布：毫米标尺 + 网格；左键选中、8 手柄缩放（Konva Transformer）、Shift 多选、Delete 删除、中键平移、Ctrl+滚轮缩放（以鼠标为中心）。
- 容器：元素拖入容器自动居中；拖出解除。
- 属性面板：仅选中时显示；单选显示位置 / 尺寸 / 填充（字段 Key 或固定值）/ 字体 / 边框；多选显示对齐工具（左 / 水平居中 / 右 / 上 / 垂直居中 / 下）。
- 契约字段：由元素填充 Key 自动推导（左侧只读列表）。
- 保存：`POST /api/templates`（模板包契约与 WPF 设计器一致）；预览：`POST /api/templates/{name}/preview`（WinHost 渲染 PNG）。

## 文件

| 文件 | 说明 |
|---|---|
| `index.html` | 页面布局与样式 |
| `app.js` | 原型逻辑（Konva 画布 + WinHost API） |
| `konva.min.js` | Konva 9.3.18 本地副本 |

## 评估结论记录

见 `docs/DESIGN.md` 决策 #39（UI 技术选型评估）。原型用于对比 WPF 设计器观感与操作手感，确定是否将 Studio UI 层迁移到 Web 技术栈（Tauri 2 / Blazor Hybrid / 纯浏览器）。