# 迭代 14 规格：字体加粗（bold）契约 + 前端面板修复记录

> 状态：规格评审中（hermes 提交，2026-08-10）
> 协作：本文档由前端（hermes）提交，供后端（本仓库 AI）评审实施；契约字段与写规则见 §3，后端实施要求见 §4。
> 背景：用户反馈小字号打印不清晰，希望试加粗效果——前端已先行实现（§2），后端按本文档更新其负责的功能。

---

## 1. 需求

用户实测中发现：小字号（如 1.8~3mm）文本打印后笔画过细不清晰，需要「字体加粗」设置以试印对比。

## 2. 前端已实现（C:\build\a，已提交推送）

- **UI**：属性面板「文本 / 字体」组新增复选框「加粗（打印更清晰）」；画布渲染 `fontStyle: 'bold'`（Konva）。
- **数据**：内部模型 `TextElement.bold: boolean`（默认 false）；convert.ts 写方向 `bold=true 才写`、读回 `?? false`；单测覆盖（省略规则 / 写出 / 读回 / 往返 / 旧模板兼容），64 用例全绿。
- **实测**：勾选后画布文字笔画密度 +90%；属性面板字高随选中元素切换修复；右侧面板超高出现滚动条修复（详见 §5 记录）。
- **契约字段**（本次新增）：text 元素 JSON `bold?: boolean`。

## 3. 契约（与前端一致，供后端实施）

| 字段 | 类型 | 元素 | 写规则 | 读回默认 |
|---|---|---|---|---|
| `bold` | boolean | text | `true` 才写（省略 = 常规） | `false`（旧模板无字段 = 常规） |

- 旧模板（无 `bold` 字段）行为不变；新增字段不破坏现有解析。

## 4. 后端实施要求

### 4.1 模型与持久化

- `LabelElement.Text` 增加 `Bold` 属性（bool，默认 false）。
- `LabelElementJsonConverter`：
  - 写：`if (e.Bold) writer.WriteBoolean("bold", true)`（省略规则与现有非默认字段一致）；
  - 读：`Bold = j.GetProperty("bold").GetBoolean()`，缺失时默认 false（与现有字段容错一致）。
- 模板 JSON 透传（GET/POST /api/templates）无需其它改动。

### 4.2 打印实现

**矢量 ZPL（Vector 模式）**：ZPL 无标准「加粗」修饰符，建议二选一（可配置，默认方案 A）：

- **方案 A（推荐）**：粗体字体变体映射——`fontName`（当前固定 "0"）+ `Bold` → 映射到打印机粗体字体编号（常见如 Zebra 内置字体 1 为 Arial 粗体；映射表可做成配置，默认 `"0" + Bold → "1"`）。
- 方案 B：保持字体不变、`^A` 高度不变，宽度方向按比例放大模拟（`^A0N,h,w×1.15`）——视觉近似但非真加粗，仅作兜底。

**图片打印（Image / Skia 模式）**：字体族 + `fontStyle: bold` 渲染（`SkFont::setEmbolden` 或 `Typeface::MakeFromName(family, SkFontStyle::Bold())`）；文本度量（换行、shrink 适应）按 bold 字体的实际度量计算（与前端 Konva `fontStyle:'bold'` 行为一致，打印与预览一致）。

### 4.3 验收标准

1. 保存带 `bold: true` 的模板 → 读回 `bold` 保留（往返）；
2. 旧模板（无 `bold`）读回常规，行为不变；
3. Vector 打印加粗文本比常规文本笔画更粗（试印对比）；
4. Image 打印加粗与前端预览一致；
5. 前后端字段名 / 写规则与 §3 一致。

## 5. 前端修复记录（本轮同批交付，供知悉）

- **属性面板数字输入不同步**：`NumField` 原用 `defaultValue`（非受控）→ 切换选中元素后残留旧值；改为受控 + `useEffect` 随 `value` 同步。
- **右侧面板无滚动条**：`designer-right` 内容区包 `overflowY: auto` 容器（tabs 固定、内容滚动）。

## 6. 分工

- 前端（hermes）：已完成（§2），可并行验证契约字段读回（后端未实现前保存时 `bold` 会被忽略，属预期）。
- 后端：按 §3/§4 实施；实施完成通知前端联调验收（§4.3）。
