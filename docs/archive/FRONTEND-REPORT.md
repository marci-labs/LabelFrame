# LabelFrame Web 前端交付汇报（hermes，2026-08-09）

> 本文是迭代 11 前端部分的交付汇报，由前端开发者 hermes 编写，**供审核员评审**。
> 开发依据：`docs/FRONTEND-SPEC.md`（V2 定稿，含两轮审阅意见与审核结论，全部采纳）。
> 交互移植依据：`prototypes/web-designer/`（已获用户验收，本迭代未修改该目录）。

## 1. 交付范围与结论

- 交付物：仓库新建 `web/` 目录（Vite 8 + React 19 + TypeScript strict + Konva 10），约 5300 行 TS/TSX/CSS，51 个 Vitest 单测全绿，`pnpm build` 通过，`web/dist` 由后端静态托管。
- 五页应用全部实现：工作台 / 设计器 / 数据与打印 / PDA 日志 / 设置。
- **验收清单 7 条全部通过真实后端（WinHost 53960）联调实测**（见 §5）。
- 联调中发现并修复 3 个前端缺陷、2 个后端问题（§6）；后端 2 处修改在 `src/LabelFrame.WinHost/Program.cs`（共 2 行），**尚未提交**，需主 agent 确认。

## 2. 交付物清单

```
web/
├── index.html / package.json / vite.config.ts / tsconfig*.json
└── src/
    ├── main.tsx / App.tsx            # 入口 + 应用框架（左导航 / 底部状态栏 / 日志抽屉）
    ├── styles.css                    # 设计系统（工业精密工具主题，CSS 变量）
    ├── state/                        # AppContext（连接 / 状态栏 / 日志）、共享类型
    ├── lib/
    │   ├── api/                      # 契约 DTO（types.ts）+ fetch 封装（client.ts，错误归一）
    │   ├── design/                   # 设计器纯逻辑层（全部有单测）
    │   │   ├── types.ts              # 元素模型（判别联合）+ 默认值 + 中文标签
    │   │   ├── geometry.ts           # mm↔px / 视口缩放 / DPI 换算
    │   │   ├── snapping.ts           # 智能参考线吸附（纯函数）
    │   │   ├── fields.ts             # 契约字段自动推导（决策 #37）
    │   │   ├── history.ts            # 撤销 / 重做快照栈
    │   │   ├── format.ts             # labelframe-web-design 导入导出
    │   │   ├── convert.ts            # ★ 内部模型 ↔ 后端版式契约（§7）
    │   │   └── barcode.ts            # JsBarcode / qrcode-generator canvas 渲染
    │   ├── excel/mapping.ts          # 列映射建议（忽略大小写/空白/下划线）
    │   └── settings.ts               # 后端地址持久化（localStorage）
    ├── pages/                        # Workbench / Designer / DataPrint / PdaLogs / Settings
    └── pages/designer/               # CanvasViewport（画布交互）/ ElementNode / PropsPanel / SidePanel
```

依赖：`konva` `react-konva` `jsbarcode` `qrcode-generator`；dev：`vitest` 等。无重型 UI 框架（按规格）。

## 3. 规格实现对照（功能规格 → 实现位置）

| 规格条目 | 实现 | 说明 |
|---|---|---|
| §1 技术栈 / pnpm / Vitest | `web/` 全局 | TS strict 手动开启（模板默认缺省） |
| §2 五 Tab state 导航 | `App.tsx` | 无路由库 |
| §3.1 设置页（地址 / 测试连接） | `pages/Settings.tsx` | 连接状态同步到底部状态栏 |
| §3.1 打印机状态 / 测试页 | `pages/Settings.tsx` | `GET /api/printer/status` + `POST /api/printer/test` |
| §3.2 跨域约定 | `api/client.ts` | fetch `mode: cors`，base 地址来自设置 |
| §3.3 工作台（列表 / 分组 / 新建 / 编辑 / 删除 / 导出 / 导入） | `pages/Workbench.tsx` | 双击打开设计器；导出走 Content-Disposition 文件名 |
| §3.4 设计器（画布 / 控件 / 属性 / 图层 / 字段 / 快捷键 / DPI 预览 / testData / 保存） | `pages/Designer.tsx` + `designer/*` | 交互逻辑逐条移植自原型 app.js（见 §4 交互对照） |
| §3.4 保存 = 同名覆盖 + 弹确认规则 | `Designer.tsx save()` | 按文档澄清：仅「保存名 ≠ 当前编辑名且已存在」时确认 |
| §3.4 contract 构造 | `convert.ts toContract` | 字段推导 + 新建 version="1" / 编辑沿用 |
| §3.5 数据与打印（testData 表单 / Excel 映射 / 批量打印 / 作业轮询 / 失败重试） | `pages/DataPrint.tsx` | 作业 1.5s 轮询至终态；失败项「重试」按钮 |
| §3.6 PDA 日志（5s 轮询 / 设备过滤） | `pages/PdaLogs.tsx` | 全量拉取（修复见 §6.3） |
| §5 验收清单 7 条 | 全部实测通过 | 见 §5 |

**设计器交互对照原型**（`prototypes/web-designer/app.js`）：视口模型（适应窗口 / DPI 预览、stage 尺寸 = 逻辑 × 总缩放、平移 clamp 不越界）、毫米标尺 + 网格（标签边缘蓝色粗刻度）、智能参考线吸附（候选 = 画布/内容区/元素边缘与中心，网格兜底）、多选拖动跟随、8 手柄缩放（QR 保持正方形）、中键平移（document 级监听防粘滞）、Ctrl+滚轮缩放（以鼠标为中心）、撤销/重做快照栈、Ctrl+C/V 复制粘贴（偏移 5mm）、Ctrl+Shift+C/V 设计 JSON 导入导出、图层置顶/上移/下移/置底、字段填充「键名 + 预览值」、文本缩小适应/隐藏与自动换行遮罩、条码/二维码实时渲染（JsBarcode / qrcode-generator）、拖入画布用 clientX/Y 几何换算。

## 4. 自动化测试（51 个，`pnpm test` 全绿）

| 模块 | 用例数 | 覆盖 |
|---|---|---|
| `geometry` | 7 | mm↔px、DPI→点/预览缩放、画布尺寸、视口坐标换算、fitScale |
| `fields` | 4 | 字段推导（去重 / 顺序 / 图片线容器排除 / 未绑定） |
| `history` | 5 | 撤销重做往返、空栈、commit 清空 redo、容量上限、快照隔离 |
| `format` | 4 | 设计 JSON 导出结构、导入往返（重生成 id）、非法输入、纸张回退 |
| `snapping` | 5 | 网格兜底、内容区边缘、元素边缘、多目标取最近、参考线位置 |
| `convert` | 15 | ★ 后端契约双向转换（详见 §7） |
| `excel/mapping` | 7 | 列名归一、自动匹配、完成判定、重复映射、行→数据拼装 |

## 5. 联调实测记录（验收清单逐条，真实 WinHost）

启动 `dotnet run --project src/LabelFrame.WinHost`（默认 53960，Log 传输），浏览器访问 `http://127.0.0.1:53960/`（后端静态托管 `web/dist`）：

1. ✅ `pnpm build` 通过；`/` 返回 index.html（200 text/html）。
2. ✅ 工作台列出模板；新建 → 设计器 → 添加文本 → 属性 X 改 15 → 保存 → 返回工作台列表可见；新建重名保存弹「模板已存在」确认框。
3. ✅ 设计器：字段推导（location/sku）、图层 3 元素、Ctrl+Z 撤销（图层 4→3）、DPI 预览（203dpi 画布 800×480 = 100mm×4×2，仅标签范围，提示条显示）。
4. ✅ Excel：注入 10 行 xlsx → 映射模态（Location→location、SKU→sku 自动匹配，Qty 未映射）→ 批量打印 → 作业 10/10 Completed（轮询进度 100%）。
5. ✅ PDA 日志：`POST /api/logs`（PDA-001）后界面可见，设备过滤下拉可用。
6. ✅ 设置页：已连接（Log）、打印机「在线」、测试打印 94 字节已发送（后端日志确认 `LABELFRAME-TEST` ZPL）。
7. ✅ 全中文界面；无公司 / 业务线品牌字样。

数据链路（curl/Python 直测后端）：healthz ✓、模板 CRUD + 导出 528B .lfpkg + 导入 ✓、作业提交→Completed 2/2 ✓、日志 POST/GET ✓、Excel headers+10 行 ✓。

## 6. 联调发现并修复的问题

### 6.1 前端：设计器无限循环（严重，曾致页面卡死）
- 症状：进入设计器后浏览器无响应，后端日志显示 `GET /api/templates` 被反复请求。
- 根因：加载 effect 依赖 `[request, app]`，而 `app` 为 context 对象（每次渲染新引用）；effect 内 `app.setStatus` 又触发 context 更新 → 无限循环。
- 修复：effect 仅依赖 `[request]`（用稳定的 `setStatus` 闭包）；`AppProvider` value 加 `useMemo`；Shell 连接探测 effect 依赖稳定函数。

### 6.2 前端：Excel 映射「批量打印」按钮永远禁用
- 症状：存在无法映射的列（如 Qty 无对应字段）时，`isMappingComplete` 要求全列映射 → 按钮永远禁用。
- 修复：改为「至少一列映射即可提交」（未映射列不参与打印），并同步测试。

### 6.3 前端：PDA 日志轮询重复累积
- 症状：同一条日志每次轮询重复追加。
- 根因：后端 `ORDER BY id DESC` 返回最新在前，前端却取 `list[last]` 的时间作为 `since`（取到最旧），且后端 `time >= $since` 含等号 → 每次轮询几乎全量重放。
- 修复：前端改全量拉取（数据量小、5s 一次、上限 500 条），实测轮询多次稳定不重复。后端 `>=` 建议改 `>`（见 §8）。

### 6.4 后端：multipart 端点 500（联调发现，已改 Program.cs 2 行，未提交）
- 症状：`POST /api/import/excel` 与 `POST /api/templates/import` 必 500。
- 根因：.NET 8+ 对 `IFormFile` 端点自动附加 antiforgery 元数据，但服务未配置对应中间件。
- 修复：两个端点追加 `.DisableAntiforgery()`（本地工具服务无 cookie 认证，antiforgery 无意义；曾试 `UseAntiforgery()` 方案会要求 token 导致 400，已弃用）。

## 7. 契约实现决策（审核重点）

`web/src/lib/design/convert.ts` 实现设计器内部模型 ↔ 后端 `layout.elements` 双向转换，写方向**严格镜像 `LabelElementJsonConverter.Write` 的省略规则**（padding/border 仅 >0 写、textAlign 非 Left 写、literal 非空写、widthMm >0 写等），并有 15 个往返单测守门。关键决策：

| # | 决策 | 理由 / 依据 |
|---|---|---|
| 1 | `fontName` 保存固定写 `"0"` | 后端 FontName 直接进 ZPL `^A{fontName}`，写字体名会生成非法 ZPL；Studio 默认即 "0"；前端字体选择仅影响画布预览 |
| 2 | 矩形 → `region` 元素（无 Rect 类型） | 与原型「矩形控件保存映射 region」一致；读回时用启发式「是否被其它元素 regionId 锚定」区分容器/矩形 |
| 3 | `paddingH/paddingV` → `paddingMm = max(两值)` | 后端契约只有单值 PaddingMm（决策 #33）；对称时精确，不对称时取大值保证内容不贴边 |
| 4 | line 的 `x2Mm/y2Mm` = 绝对坐标（起点+长度） | ZPL 编码器 `|X2-X1|` 算宽高；读回还原为长度 |
| 5 | `moduleWidth` 保存时 int 化 | 后端为 `int`（默认 2） |
| 6 | **契约缺失字段的丢失**：文本 `h/wrap/lineHeight/valign/fitMode/fontFamily`、条码 `w/displayValue`、二维码 `qrEcc/qrMargin` 后端 JSON 无对应字段 | 保存后重新加载回到默认值；**打印效果以 ZPL 编码器能力为准**（文本单行、`^FB` 块宽）。这是后端契约（决策 #33/40）与原型前端增强（8E/8F）的已知差距，非前端缺陷；如需要保真，属跨迭代契约扩展，建议记入 DESIGN.md 未决问题 |
| 7 | 文本高度读回 = `fontHeightMm + 2×paddingMm`，下限 10mm | 后端无高度字段，按打印时内容盒高度近似 |
| 8 | 作业提交 `template` 快照 = 当前模板的 `contract + layout`（不含 name/group） | 与后端 `SubmitJobRequest` 一致；`requestId` 前端 `crypto.randomUUID()` |
| 9 | contract 顶层：新建 `version="1"`、`fields` 由推导生成（displayName=Key、非必填、type=Text）；编辑沿用原值 | 规格 §3.4 澄清版 |
| 10 | testData 随设计器保存提交；数据与打印页表单仅用于本次打印不持久化 | 规格未要求保存，最小实现 |

## 8. 遗留事项与建议

1. **文档更正**：`docs/FRONTEND-SPEC.md` §4 引用「详见 `prototypes/web-designer/app.js` 的 `toElementJson` / `parseElement`」——原型 8F 纯前端化时已移除这两个函数，实际转换实现在 `web/src/lib/design/convert.ts`（依据后端 `LabelElementJsonConverter` 实现）。建议更新文档引用。
2. **后端建议**：`SqliteLogStore.QueryAsync` 的 `time >= $since` 对增量轮询含边界重复，建议改 `>`（前端已改用全量轮询，不阻塞）。
3. **提交**：前端 `web/`（全新目录）与后端 2 行修复均**未提交**；建议分两个 commit（`feat: 迭代 11 Web 前端` / `fix: WinHost multipart 端点禁用 antiforgery`）。`web/dist` 已被 .gitignore 排除，交付时由 `pnpm build` 生成。
4. **测试数据残留**：本机 `%LOCALAPPDATA%\LabelFrame\` 数据库含联调数据（模板「库位标签」、PDA-001 日志、测试作业），如需干净环境可删除。
5. **迭代文档**：CHANGELOG / ROADMAP 迭代 11 状态由主 agent 在整体完成时统一更新（本汇报可作为前端部分依据）。
6. **截图**：浏览器实测截图未随本文附上（自动化会话无法截图）；审核员可启动后端后按 §5 步骤复核。
