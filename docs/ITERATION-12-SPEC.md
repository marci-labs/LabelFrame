# 迭代 12：模板预览值持久化 + 图片打印实验（规格 v1）

> 状态：规格评审中（待 Hermes 评审 / 用户确认）
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
  - 字段填充模式（`mode === 'field'`）且 `text` 非空 → 写 `previewValue: text`；
  - 固定值模式（`mode === 'literal'`）→ 不写 `previewValue`（保持现状，只写 `literal`）。
- `fromBackendElements()`：
  - text / barcode / qrcode 读取时，字段填充模式（`sourceKey` 存在）的 `text` 取 `previewValue ?? ''`；固定值模式仍取 `literal`。
  - 注意与现有 `mode` 推断兼容：`literal` 非空 → literal 模式；否则 `sourceKey` 非空 → field 模式；两者皆空 → literal。
- 同步更新 `web/src/lib/design/convert.test.ts`：新增 round-trip 用例（字段填充 + 预览值 → 保存 JSON → 读回一致；固定值行为不变）。

### 3.2 测试默认值来源统一（必须）

文件：`web/src/pages/Designer.tsx`

- `doSave()` 中 `testData` 不再手工维护：保存时不传 `testData`（或传空），由后端自动从元素 `previewValue` 生成（见 4.2）。
- 「测试数据」面板（Designer 右侧 tab）：建议移除，或改为只读提示「测试默认值由元素预览值自动生成，保存后生效」。
- 若保留面板输入框，必须加说明：保存时会被元素预览值覆盖（待确认 Q1 决定）。

文件：`web/src/pages/DataPrint.tsx`（小改动）

- 已实现 `setValues({ ...(pkg.testData ?? {}) })` 预填；补一行提示文案：「已用模板预览值预填，可修改后打印」。

### 3.3 打印方式切换（可选，待确认 Q2）

文件：`web/src/lib/api/types.ts`、`web/src/pages/DataPrint.tsx`

- `SubmitJobRequest` 增加：
  - `templateName?: string`（= 当前模板名，图片打印时后端取模板图片资源用）；
  - `printMode?: 'Vector' | 'Image'`。
- DataPrint 测试表单上方加「打印方式」下拉（矢量 ZPL / 图片），提交作业时带上。
- 不做也不阻塞：后端默认按 `appsettings.json` 的 `PrintMode`，前端零改动也能通过改配置试图片打印。

---

## 4. 后端改动清单（本仓库 AI 负责，评审通过后实施）

1. **元素模型**：`LabelElement` 增加 `string? PreviewValue`；`LabelElementJsonConverter.Write` 在 text / barcode / qrcode 且 `PreviewValue` 非空时输出 `previewValue`（读由默认反序列化器承接）。无需数据库迁移（layout 存 JSON 整块）。
2. **testData 自动派生**：`TemplateStore.SaveAsync` 保存前：先并入显式 `TestData`，再遍历 layout 元素（text / barcode / qrcode）取 `SourceKey` + `PreviewValue` 非空项覆盖 `testData[key]`。
3. **图片打印**：
   - `HostOptions` / `appsettings.json` 增加 `PrintMode`（`Vector` / `Image`，默认 `Vector`；环境变量 `LABELFRAME_PRINT_MODE`）。
   - `SubmitJobRequest` 增加可选 `TemplateName`、`PrintMode`；`JobSubmissionService` 解析模式（请求 > 配置）。
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

## 7. 待确认问题（用户 / Hermes 评审时答复）

- **Q1**：测试默认值是否完全由元素预览值生成并覆盖？（推荐：是，移除 Designer 手工「测试数据」面板；PDA 测试数据也以模板预览值为准）
- **Q2**：打印方式切换是否本期做前端下拉？（推荐：后端先支持配置 + 请求参数，前端下拉可选，不阻塞）
- **Q3**：图片打印是否必须支持模板图片（logo）？（推荐：支持，通过 `template.name` 取服务端图片资源）
- **Q4**：图片打印分辨率先用 203dpi（与当前一致）？是否需要 300dpi 选项？（推荐：沿用作业 DPI 配置，不做额外选项）

---

## 8. 协作流程

1. 本文档交 Hermes 评审（重点：3.1 / 3.2 / 3.3 与 5 的契约）。
2. 双方确认无误后：前端实现第 3 节，后端实现第 4 节。
3. 联调验收按第 6 节执行；完成后更新 ROADMAP / CHANGELOG。