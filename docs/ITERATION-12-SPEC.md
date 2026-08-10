# 迭代 12：模板预览值持久化 + 图片打印实验（规格 v1）

> 状态：规格 v2（评审完成，双方结论已确认，待实施）
> 日期：2026-08-10
> 协作：本文档给前端（hermes）评审；评审通过后，前端做前端改动，后端（本仓库 AI）做后端改动，最后联调验收。

---

## 1. 背景与要解决的问题

1. **预览值丢失（Bug）**：编辑器里为「字段填充」控件设置的预览值（仅画布显示），保存后重开模板就没了。
   - 根因（已确认）：前端 `web/src/lib/design/convert.ts` 的 `toBackendElement()` 在字段填充模式只写 `sourceKey`，**没有把预览值写入任何后端字段**；`fromBackendElements()` 读回时预览值只从 `literal`（固定值）取，所以字段模式的预览值必然丢失。
2. **测试默认值（体验）**：模板的预览值应直接作为打印测试的初始默认值，避免每次手工填一堆字段。
   - 现状：模板已有顶层 `testData`（后端 `test_data_json`，Designer 有「测试数据」面板，DataPrint 已用 `pkg.testData` 预填表单）。但用户习惯在元素上填预览值，期望它自动成为默认值。
3. **打印效果与定位（实验）**：当前打印走 ZPL 矢量指令（文本用打印机内置字体、条码/二维码用打印机生成），与画布预览的渲染效果不一致，定位有偏差。希望**先试整版位图直传打印机**（所见即所得），评估效果后再决定后续方向。

---

## 2. 设计决策（建议，待评审确认）

- **D1 预览值单一事实来源**：元素 JSON 新增 `previewValue` 字段（text / barcode / qrcode 字段填充模式写入）。固定值模式仍用 `literal`，不写 `previewValue`。
- **D2 测试默认值自动生成**：保存模板时，后端从元素 `previewValue` 自动生成模板 `testData`（字段 key → 预览值），**预览值优先**（显式传入的 testData 先并入、再被预览值覆盖）。Designer 的「测试数据」手工面板建议移除或改为只读提示（见待确认 Q1）。
- **D3 图片打印为实验模式**：后端新增 `PrintMode`（`Vector` 默认 / `Image`），可被作业请求覆盖。`Image` 模式把整张标签渲染为 1bpp 位图，用 `^GF` 发给打印机；与预览渲染同源（GDI 文本 + ZXing 条码/二维码），保证所见即所得。

---

## 3. 前端改动清单（hermes 负责）

### 3.1 修复预览值持久化（必须）

文件：`web/src/lib/design/convert.ts`

- `BackendElement` 接口增加 `previewValue?: string`。
- `toBackendElement()`：
  - 字段填充模式且 `key` 非空、`text` 非空 → 写 `previewValue: text`（key 为空 = 未绑定字段，不持久化预览值，避免读回时模式推断改变，见边界 A）；
  - 固定值模式（`mode === 'literal'`）→ 不写 `previewValue`（保持现状，只写 `literal`）。
- `fromBackendElements()`：
  - text / barcode / qrcode 读取时，字段填充模式（`sourceKey` 存在）的 `text` 取 `previewValue ?? ''`；固定值模式仍取 `literal`。
  - 注意与现有 `mode` 推断兼容：`literal` 非空 → literal 模式；否则 `sourceKey` 非空 → field 模式；两者皆空 → literal。
- 同步更新 `web/src/lib/design/convert.test.ts`：新增 round-trip 用例（字段填充 + 预览值 → 保存 JSON → 读回一致；固定值行为不变）。

### 3.2 测试默认值来源统一（必须）

文件：`web/src/pages/Designer.tsx`

- `doSave()` 中 `testData` 不再手工维护：保存时不传 `testData`（或传空），由后端自动从元素 `previewValue` 生成（见 4.2）。
- 「测试数据」面板（Designer 右侧 tab）：建议移除，或改为只读提示「测试默认值由元素预览值自动生成，保存后生效」。
- 「测试数据」面板改为**只读预览列表**：实时从元素预览值推导 `key → 值`，标注「保存后作为打印测试 / PDA 测试默认值」；移除 Designer 的 `testData` state；API 层 `pkg.testData`（DataPrint 预填用）不动。

文件：`web/src/pages/DataPrint.tsx`（小改动）

- 已实现 `setValues({ ...(pkg.testData ?? {}) })` 预填；补一行提示文案：「已用模板预览值预填，可修改后打印」。

### 3.3 打印方式切换（做，评审已确认）

文件：`web/src/lib/api/types.ts`、`web/src/pages/DataPrint.tsx`

- `SubmitJobRequest`：
  - `template` 增加 `name?: string`（= 当前模板名，图片打印时后端取模板图片资源用；Vector 模式后端忽略，总是带上无害）；
  - 请求顶层增加 `printMode?: 'Vector' | 'Image'`。
- DataPrint 测试表单上方加「打印方式」下拉：选项「默认（服务端）/ 矢量 ZPL / 图片」；选「默认」时不发送 `printMode`（跟随服务端配置），选具体模式时发送。
- 契约统一为 `template.name`（顶层不再有 `templateName`，与后端 `TemplateDto` 演进一致）。

---

## 4. 后端改动清单（本仓库 AI 负责，评审通过后实施）

1. **元素模型**：`LabelElement` 增加 `string? PreviewValue`；`LabelElementJsonConverter.Write` 在 text / barcode / qrcode 且 `PreviewValue` 非空时输出 `previewValue`（读由默认反序列化器承接）。无需数据库迁移（layout 存 JSON 整块）。
2. **testData 自动派生（读-改-写，防清空旧值）**：`TemplateStore.SaveAsync` 保存前：以数据库现有 testData 为基底 → 并入显式 `TestData` → 再遍历 layout 元素（text / barcode / qrcode）取 `SourceKey` + `PreviewValue` 非空项覆盖 `testData[key]`（预览值优先）。旧模板（无 previewValue）即使前端不传 testData 也不会丢已有值，消除前后端上线时序风险。
3. **图片打印**：
   - `HostOptions` / `appsettings.json` 增加 `PrintMode`（`Vector` / `Image`，默认 `Vector`；环境变量 `LABELFRAME_PRINT_MODE`）。
   - `SubmitJobRequest` 增加可选 `TemplateName`（请求级，对应 `template.name`）、`PrintMode`；`TemplateDto` 增加 `Name` 属性；`JobSubmissionService` 解析模式（请求 > 配置）。`/healthz` 返回 `printMode` 默认值（供前端下拉显示）。
   - `LabelPreviewRenderer` 新增渲染 1bpp `LabelBitmap` 的方法（白底黑字，复用现有 GDI + ZXing 绘制，按 DPI 出图）。
   - 新增 `ImageZplEncoder`（或 `ZplEncoder` 扩展方法）：`^XA ^PW{..} ^LL{..} ^FO0,0 ^GFA{..} ^FS ^XZ`。
   - `JobSubmissionService`：`Image` 模式 = 加载模板图片（`TemplateName` 从 `TemplateStore` 取，取不到按无图渲染）→ 整版 1bpp → `^GF`；`Vector` 模式保持现状（含已有 `^PW`/`^LL`）。
4. **测试**：
   - 元素 `previewValue` 序列化 round-trip；
   - `SaveAsync` 自动生成 testData（预览值优先、覆盖显式值）；
   - `Image` 模式 ZPL 输出为整版 `^GF`（含 `^PW`/`^LL`）；
   - 回归：`Vector` 模式输出与现有一致；全部 `dotnet test` 通过。
5. **文档**：更新 `docs/DESIGN.md`（决策 #45）、`docs/ROADMAP.md`、`CHANGELOG.md`、`README.md`（PrintMode 配置说明）。

---

## 5. 接口契约变更

### 5.1 元素 JSON（layout.elements[]）

新增可选字段：

```json
{
  "type": "text",
  "sourceKey": "location",
  "previewValue": "A-01-02-03",
  "xMm": 5,
  "yMm": 5
}
```

- `previewValue`：仅字段填充模式写；固定值模式不写。
- 旧模板无此字段 → 前端按空处理，行为不变（向后兼容）。

### 5.2 POST /api/templates（保存）

- 请求结构不变；`testData` 仍可选。服务端保存时自动用元素预览值派生/覆盖 testData。
- 响应不变。

### 5.3 POST /api/jobs（提交打印作业）

请求体新增可选字段：

```json
{
  "requestId": "...",
  "template": { "name": "模板名", "contract": {...}, "layout": {...} },
  "printMode": "Image",
  "labels": [{ "data": {...} }]
}
```

- `template.name`（可选）：用于 `Image` 打印时取模板图片资源；`Vector` 模式可忽略。
- `printMode`（可选）：`Vector` / `Image`；缺省用服务端 `PrintMode` 配置。

### 5.4 GET /api/templates/{name}

- 元素 JSON 透传 `previewValue`，前端可读回。

---

## 6. 验收标准

1. 编辑器为字段填充控件设置预览值 → 保存 → 重开模板 → 预览值仍在，画布显示一致（问题 1 修复）。
2. 新建打印作业 → 表单自动预填模板预览值，可直接点打印（问题 2 满足）。
3. 图片打印：`PrintMode=Image`（配置或前端切换）→ 打印输出为整版位图；Log 传输可见 ZPL 为 `^GF` 整图；真机输出定位与预览一致（问题 3 实验可评估）。
4. 回归：矢量打印输出不变（`^PW`/`^LL` + 元素指令）；`dotnet test` 全绿；前端 `pnpm build` 通过。

---

## 7. 待确认问题（已确认，2026-08-10）

- **Q1**：✅ 是——测试默认值完全由元素预览值生成并覆盖；Designer 手工「测试数据」面板改为只读预览列表。
- **Q2**：✅ 做——前端打印方式下拉（「默认 / 矢量 ZPL / 图片」），验收标准 3 的「前端切换」路径需要。
- **Q3**：✅ 支持——通过 `template.name` 取服务端模板图片资源；无 name 时按无图渲染。
- **Q4**：✅ 沿用作业 DPI 配置（203dpi 默认），前端无额外选项。

---

## 8. 协作流程

1. 本文档交 Hermes 评审（重点：3.1 / 3.2 / 3.3 与 5 的契约）。
2. 评审与审核结论已确认（见「附：Hermes 评审意见」与「附二：本仓库 AI 审核意见」）；前端实现第 3 节（含随附的 ElementNode 选中修复），后端实现第 4 节。
3. 联调验收按第 6 节执行；完成后更新 ROADMAP / CHANGELOG。

---

## 附：Hermes 评审意见（2026-08-10）

> 本节由前端开发者 hermes 对迭代 12 规格评审时追加，**供审核者评审**。
> 审核结论请回复后，hermes 按结论执行；本节保留作为评审记录，不视为规格正文。

### 一、总体评价

方向正确：3 项改动全部落在既有架构上（convert.ts 两个函数 + Designer 面板 + DataPrint 下拉），改动量约 100~150 行；向后兼容设计到位（旧模板无 `previewValue` 行为不变）。发现 1 处契约内部不一致、2 个边界问题、1 个联调时序风险，另确认 1 个已修复的既有缺陷（见第五节）。

### 二、3.1 预览值持久化（必须）——方案正确，补 1 个边界条件

- 内部模型恰好适配：字段填充模式的 `e.text` 即预览值，改 `toBackendElement` / `fromBackendElements` 即可。
- **边界 A（建议补充）**：字段填充模式但 key 为空（未绑定字段）时，按规格会写 `sourceKey=''` + `previewValue='预览值'`；但读回时 mode 推断（literal 空 + sourceKey 空 → literal 模式）会丢失预览值且改变模式。建议 `toBackendElement` 仅在 `mode==='field' && key 非空 && text 非空` 时写 `previewValue`（未绑定字段的预览值无意义，画布显示「未绑定字段」占位）。
- 测试：已核对现有 15 个 convert 用例断言不会被破坏；新增 2 用例：① field+key+text 往返保留预览值（并给现有「文本往返」用例补 `text` 断言）；② literal 模式断言 `previewValue` 为 `undefined`（防回归）。

### 三、3.2 测试默认值（必须）——1 个联调时序风险 + 面板建议

- **时序风险**：当前后端 `TemplateStore.SaveAsync` 对缺失 testData 存 `{}`（`dto.TestData ?? new()`）。若前端先上线「不传 testData」而后端 4.2 未就绪，**旧模板的显式 testData 会被清空**。建议前端改动与后端 4.2 同批联调上线，或在 4.2 就绪前前端仍传 testData。
- **面板建议**：推荐「移除手工输入，改为只读预览列表」——实时从元素预览值推导 `key → 值`，标注「保存后作为打印测试 / PDA 测试默认值」。比完全移除好（用户可见保存后将生效的值）；成本低（复用 deriveFields + elements 遍历）。Designer 的 `testData` state 随面板移除删除；API 层 `pkg.testData`（DataPrint 预填用）不动。

### 四、3.3 打印方式（可选→建议做）——**契约内部不一致，需统一**

- §3.3 写 `templateName?: string`（请求**顶层**），§5.3 写 `template: { name, ... }`（**template 内**）——两处不一致。
- 建议统一为 **`template.name`**：与现有后端 `TemplateDto(Contract, Layout)` 演进最自然（加 `Name` 属性）；前端 `SubmitJobRequest.template` 加 `name`，提交时填 `pkg.name`。
- 前端下拉建议做（Q2）：测试表单上方「打印方式：矢量 ZPL / 图片」，提交带 `printMode`；`template.name` 总是带上（Vector 模式后端忽略，无害）。
- 向后兼容 ✓：旧后端忽略未知 JSON 字段，前端可先行。

### 五、既有缺陷确认并修复：画布无法选中控件（用户报告，已属实）

- **症状**：设计器画布点击任何控件（加载的 / 拖入的 / Ctrl+Shift+V 导入的）都无法选中；图层列表点击选中正常（DOM 路径）。
- **根因**：`web/src/pages/designer/ElementNode.tsx` 移植时把元素**外框 Rect 错误设置 `listening={false}`**（6 处：Text / Barcode / QrCode / Rect / Image / Region）。Konva 命中检测中，Group 的可命中区域 = 子节点 union；内容节点（文字 / 条码图）本就 `listening=false`（与原型一致），外框再禁用后元素在 hit 图上**无命中区域** → 画布点击永远落空。原型的设计是：外框 Rect 默认 listening（命中区域），点击元素任意位置命中 Rect 后冒泡到 Group（`name=element`）完成选中 / 拖拽 / 手柄缩放。
- **修复（已完成并实测验证）**：移除 6 处外框 Rect 的 `listening={false}`；内容节点保持 `listening=false`（与原型逐行对照一致）。实测：点击「70*50 物料标签」的二维码元素 → 图层高亮 + 属性面板显示「二维码 UniCode」；新放置元素走同一渲染路径。
- 注：该缺陷已在前端工作区修复（`web/` 未提交），属迭代 11 遗留，建议随迭代 12 一并提交。

### 六、待确认问题建议

| 问题 | 建议 |
|---|---|
| Q1 测试默认值完全由预览值生成 | ✅ 是（移除手工面板 → 只读预览列表） |
| Q2 前端打印方式下拉 | ✅ 做（验收标准 3 的「前端切换」路径需要） |
| Q3 图片打印支持模板图片 | ✅ 支持（template.name 成本低） |
| Q4 分辨率 | ✅ 沿用作业 DPI 配置，前端无额外改动 |

### 七、验收标准补充（前端侧）

- 验收 1 前端自测路径：convert 单测 + 手动（设预览值 → 保存 → 重开）；重开后 testData 变化属后端 4.2 验收。
- 验收 3 的 `^GF` 整图确认属后端联调；前端只验证下拉与请求参数传递。
- 验收 4 回归：前端 `pnpm build` + `pnpm test`（51 用例 + 新增 previewValue 用例）。

---

### 附二：本仓库 AI 审核意见（2026-08-10）

> 审核结论：**总体接受**，正文已按以下意见修订为 v2；本节保留为审核记录。

1. **边界 A（3.1）——接受**：仅 `mode === 'field' && key 非空 && text 非空` 时写 `previewValue`，避免未绑定字段（key 空）读回时模式推断改变、预览值丢失；未绑定字段预览值无意义。已写入 3.1。
2. **时序风险（3.2 / 4.2）——接受，后端解法更强**：不依赖「同批上线 / 前端仍传 testData」；后端 `TemplateStore.SaveAsync` 改为**读-改-写**（数据库现有 testData 为基底 → 并入显式传入 → 预览值派生覆盖），旧模板显式 testData 永不因前端不传而清空。已写入 4.2。
3. **契约不一致（3.3 / 5.3）——接受，统一为 `template.name`**：后端 `TemplateDto` 增加 `Name`；请求顶层不再有 `templateName`。已写入 3.3 / 4.3。
4. **面板建议（3.2）——接受**：手工「测试数据」面板改为只读预览列表（实时由元素预览值推导），移除 Designer `testData` state；API 层 `pkg.testData`（DataPrint 预填用）不动。
5. **打印方式下拉（3.3）——接受并补充**：下拉三态「默认（服务端）/ 矢量 ZPL / 图片」，选「默认」不发送 `printMode`；后端 `/healthz` 返回 `printMode` 默认值供前端显示（避免前端固定值覆盖服务端配置）。
6. **既有缺陷（画布无法选中）——属实，接受**：已核对 HEAD（e44f4ca）确在 6 处外框 Rect 写 `listening={false}`（Text/Barcode/QrCode/Rect/Image/Region），工作区修复（移除 6 处）与 Konva 命中检测原理一致（内容节点已 `listening=false`，外框是唯一命中区域）。**随迭代 12 前端改动一并提交**（`web/src/pages/designer/ElementNode.tsx`，已在共享工作区、未提交）。
7. **Q1–Q4——全部确认**：Q1 是（预览值生成 + 只读列表）；Q2 做（下拉）；Q3 支持（`template.name` 取图，无 name 按无图渲染）；Q4 沿用作业 DPI。
8. **验收补充（七）——接受**；补充：Image 打印无 `template.name` 时按无图渲染（文本 / 条码 / 二维码不受影响），验收时覆盖该路径。