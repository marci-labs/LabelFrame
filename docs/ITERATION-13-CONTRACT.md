# 迭代 13：元素契约字段对照与渲染语义（供 Hermes 评估）

> 状态：契约评审中（2026-08-10，主 agent 整理，交前端 hermes 评估）
> 协作：本文件定义**前后端元素 JSON 契约字段**（前端 convert.ts ↔ 后端 C# 模型/转换器）与 **Skia 图片打印渲染语义**；Hermes 评估无异议后，后端实施 C# + Skia，前端实施 convert.ts 联动。

---

## 1. 背景与目标

迭代 12 / 0.11.6 已合入：`previewValue`、`heightMm`、`verticalAlign`。
本批补齐仍缺失的元素契约字段：**wrap / lineHeight / fitMode / fontFamily / qrEcc / qrMargin / displayValue / paddingH / paddingV**，使：
- 导入设计 JSON → 保存 → 重开，逐元素逐字段一致（往返不丢）；
- 图片打印（Skia）与前端预览渲染一致；
- 旧模板向后兼容。

## 2. 已确认决策（用户拍板）

- **A（垂直对齐默认值统一）**：后端 `VerticalAlign` 默认由 Top 改为 **Middle**（与前端一致）；旧模板无 `heightMm` 时渲染器框高兜底 = `max(字高 + 2×内边距, 10mm)`（与前端读回兜底一致）。写规则改为“**非 Middle 才写**”（Top/Bottom 写，Middle 省略）。
- **B（连渲染一起做）**：后端不仅补字段，Skia 渲染器按这些字段真实绘制（换行/行距/溢出处理/字体/QR 参数/条码文字/双边内边距）。
- **C（图片打印字体）**：图片打印用模板 `fontFamily`（Skia 渲染）；矢量 ZPL 仍用打印机内置字体（`fontName`），不受影响。

## 3. 字段对照总表

> 规则约定：**写方向非默认才写**（与现有 `heightMm`/省略风格一致）；读方向缺失字段回填前端/后端默认值。

### 3.1 通用（所有元素）

| 前端字段（默认值） | 后端 JSON | C# 属性 | 写规则 | 读回规则 |
|---|---|---|---|---|
| x | `xMm` | XMm | 总是 | 直接 |
| y | `yMm` | YMm | 总是 | 直接 |
| border（0） | `borderMm` | BorderMm | >0 才写 | ?? 0 |
| paddingH（1）/ paddingV（1） | `paddingH` / `paddingV`（新） | PaddingHMm / PaddingVMm（新，0=未设） | >0 才写（**双值**） | `paddingH ?? paddingMm`、`paddingV ?? paddingMm` |
| （兼容保留） | `paddingMm`（= max(padH,padV)，现状） | PaddingMm | 现状继续写 | 新字段缺失时兜底 |
| regionId / regionHAlign / regionVAlign | `regionId` / `regionHAlign` / `regionVAlign` | RegionId / RegionHAlign / RegionVAlign | 非空才写 | ?? null / 居中 |

### 3.2 文本 Text

| 前端字段（默认值） | 后端 JSON | C# 属性 | 写规则 | 读回规则 |
|---|---|---|---|---|
| key / text / mode | `sourceKey` / `literal` / `previewValue` | SourceKey / Literal / PreviewValue | 现状（固定值写 literal；字段填充写 sourceKey + previewValue） | 现状 |
| fontH | `fontHeightMm` | FontHeightMm | 总是 | 直接 |
| fontW | `fontWidthMm` | FontWidthMm | 总是 | ?? fontH |
| w | `widthMm` | WidthMm | >0 才写 | ?? 40 |
| h | `heightMm` | HeightMm | >0 才写 | ?? max(fontH + 2×max(pad), 10) |
| align（Left） | `textAlign` | TextAlign | != Left 才写 | ?? Left |
| valign（middle） | `verticalAlign` | VerticalAlign | **!= Middle 才写**（Top/Bottom） | ?? Middle |
| fontFamily（Microsoft YaHei） | `fontFamily`（新） | FontFamily（新） | != 默认才写 | ?? Microsoft YaHei |
| wrap（false） | `wrap`（新） | Wrap（新） | **true 才写** | ?? false |
| lineHeight（1.2） | `lineHeight`（新） | LineHeight（新） | != 1.2 才写 | ?? 1.2 |
| fitMode（shrink） | `fitMode`（新，`shrink`/`overflow`） | FitMode（新，Shrink/Overflow） | != shrink 才写 | ?? shrink |

### 3.3 二维码 QrCode

| 前端字段（默认值） | 后端 JSON | C# 属性 | 写规则 | 读回规则 |
|---|---|---|---|---|
| key / text / mode | sourceKey / literal / previewValue | 同上 | 现状 | 现状 |
| w/h（size= max(w,h)） | `sizeMm` | SizeMm | 现状 | 现状 |
| qrEcc（M） | `qrEcc`（新，L/M/Q/H） | QrEcc（新，枚举） | != M 才写 | ?? M |
| qrMargin（2） | `qrMargin`（新） | QrMargin（新） | != 2 才写 | ?? 2 |

### 3.4 条码 Barcode

| 前端字段（默认值） | 后端 JSON | C# 属性 | 写规则 | 读回规则 |
|---|---|---|---|---|
| key / text / mode | sourceKey / literal / previewValue | 同上 | 现状 | 现状 |
| h | `heightMm` | HeightMm | 现状 | 现状 |
| moduleWidth | `moduleWidth` | ModuleWidth | 现状 | 现状 |
| displayValue（true） | `displayValue`（新） | DisplayValue（新） | **false 才写** | ?? true |
| barcodeFormat（CODE128） | ——（仅支持 CODE128，不持久化） | —— | —— | 固定 CODE128 |

### 3.5 图片 / 线条 / 区域 / 矩形（现状不变）

- Image：`sourceKey`、`widthMm`、`heightMm`
- Line：`x2Mm`、`y2Mm`、`thicknessMm`
- Region/Rect：`id`、`widthMm`、`heightMm`

## 4. Skia 图片打印渲染语义

1. **字体**：`fontFamily` 决定 Typeface；含 CJK 时沿用系统回退匹配（常见中文字符匹配，避免生僻字开头只匹配小字体）。
2. **文本默认（wrap=false + fitMode=shrink）**：单行，超出框宽按比例缩小至最小 1.5mm（现状）。
3. **文本 wrap=false + fitMode=overflow**：单行、不缩小、按框裁剪（隐藏溢出）。
4. **文本 wrap=true**：按框宽自动换行，行距 = 1.2 × 字高（`lineHeight` 倍数）；若整体超出框高，整体缩小至能放下（最小 1.5mm），避免打印丢字。
5. **垂直对齐**：按 `verticalAlign`（Top/Middle/Bottom）在框内定位；水平对齐按 `textAlign`。
6. **内边距**：文本/条码/二维码内容区 = 元素框减去 `paddingH` / `paddingV`。
7. **二维码**：`qrEcc` / `qrMargin` 传入 ZXing（`QrCodeEncodingOptions`）。
8. **条码**：`displayValue=true` 时框内底部绘制数值文字（字号取框高比例，最小 1.5mm），条码占剩余高度；`displayValue=false` 仅条码。
9. **ZPL 矢量路径不变量**：以上新字段**不参与** ZPL 编码，矢量输出与现状一致。

## 5. 兼容性

- 旧模板无新字段 → 读回默认 → 行为 = 前端现状（无报错、无破坏）。
- `paddingMm` 保留：新字段缺失时用 `paddingMm` 兜底（旧模板不变）。
- 无数据库迁移（layout 整块 JSON）。
- 后端默认值变化（VerticalAlign Top→Middle）只影响**没有 verticalAlign 字段**的旧模板打印位置（顶部→居中，与预览对齐），这是用户已确认的全局行为变化（决策 A）。

## 6. 验收标准

1. 导入「物料标签设计_100×60_方案1_非表格样式.json」→ 保存 → 重开 → **逐元素逐字段与导入一致**（差异清单为空）。
2. 旧模板打开行为不变（读回默认、无报错）。
3. 图片打印与前端预览一致：垂直/水平对齐、字号（wrap 影响）、行距、QR 纠错与静区、条码底部文字、双边内边距。
4. 矢量 ZPL 输出与现状一致；`dotnet test` / `pnpm test` 全绿。

## 7. 分工与时序

- 后端（本仓库 AI）：C# 模型属性 + `LabelElementJsonConverter` 读写 + Skia 渲染支持 + 测试 + 默认值统一（决策 A）。
- 前端（hermes）：`convert.ts` 的 `BackendElement` 加字段、`toBackendElement` 写方向、`fromBackendElements` 读回兜底；`convert.test.ts` 全字段往返 + 旧模板兼容用例。
- 时序：可并行；字段名与写/读规则**以本文件为准**。

## 8. 请 Hermes 重点评估

1. 字段命名与类型（wrap / lineHeight / fitMode / fontFamily / qrEcc / qrMargin / displayValue / paddingH / paddingV）。
2. 写规则：非默认才写；`wrap` true 才写；`verticalAlign` 非 Middle 才写；`displayValue` false 才写。
3. `fitMode` 取值：`shrink` / `overflow`（overflow = 隐藏/裁剪，不是“溢出显示”）。
4. `wrap=true` 渲染语义：换行后若超高允许**整体缩小**（避免打印丢字）——与前端当前“不缩小、超高超框裁剪”是否可接受，或前端最终以 2x 超采样后的效果为准。
5. 条码 `displayValue` 文字占位规则（底部文字、条码占剩余高度）。
6. 旧模板兜底框高 10mm + 默认 Middle 的全局行为变化（决策 A）确认。


---

## 附：Hermes 评估结论（2026-08-10）

> 前端 hermes 对本文档（契约 v1）评估后追加，供主 agent 知悉；**评估通过，前后端按 §7 分工并行开工**。本节保留为评估记录。

### 一、结论

**通过**。字段命名 / 类型 / 写规则 / 读回规则与前端 convert.ts 现有实现及迭代 13 规格完全对齐；决策 A（垂直对齐默认值统一为 Middle + 旧模板框高兜底 max(字高+2×pad,10)）解决了前后端默认值不一致，用户已拍板，无异议。

### 二、§8 重点评估逐条确认

1. **字段命名与类型** ✅ 无异议。前端实际默认值核对：paddingH/V=1、fitMode 类型恰为 `'shrink' | 'overflow'`、barcodeFormat 固定 CODE128、fontFamily='Microsoft YaHei'——与对照表完全一致。
2. **写规则** ✅ 无异议（wrap=true 才写、displayValue=false 才写、verticalAlign 非 Middle 才写、fitMode 非 shrink 才写）；现有 convert.ts 的 verticalAlign 写规则（valign≠middle 才写）已与决策 A 一致，无需改。
3. **fitMode 取值 shrink/overflow** ✅ 前端已是该枚举；overflow=隐藏裁剪语义一致（ElementNode 按 fitMode 分支渲染）。
4. **wrap=true 超高语义——接受「整体缩小」，但需补前端联动项**（见第三节）：打印不丢字优先；前端当前 wrap=true 超高是裁剪，需同步改为整体缩小（最小 1.5mm）以保证图片打印与预览一致。**该联动不在 §7 前端分工清单内，补充之**。
5. **条码 displayValue 占位** ✅ 可接受。前端 JsBarcode 自带底部文字并整体 fit 进框（条码+文字等比缩小），与 Skia「条码占剩余高度」为近似一致；联调对比微调，不阻塞。
6. **决策 A 全局行为变化** ✅ 用户已确认；前端读回默认 middle 与后端默认 Middle 统一后，旧模板打印（顶部→居中）为预期行为。

### 三、前端实施清单（hermes，与后端并行）

1. `convert.ts`：`BackendElement` 加 `wrap/lineHeight/fitMode/fontFamily/qrEcc/qrMargin/displayValue/paddingH/paddingV`；写方向按 §3 规则；读回 `?? 默认`（`paddingH ?? paddingMm`、`paddingV ?? paddingMm`）。
2. `convert.test.ts`：全字段往返 + 旧模板无字段兼容 + 省略规则用例（预期 57 → 70+ 用例）。
3. **ElementNode.tsx（补充项）**：`TextContent` 的 wrap=true 分支由「超高裁剪」改为「整体缩小至能放下（最小 1.5mm）」，与 §4.4 Skia 语义一致；同时保留 overflow 分支裁剪语义。
4. 验收：`pnpm test` / `pnpm build` 全绿；§6 验收 1 复现脚本（100×60 方案）差异清单为空。
