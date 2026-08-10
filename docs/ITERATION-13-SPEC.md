# 迭代 13 规格：文本排版与二维码参数持久化（元素契约补齐）

> 状态：规格评审中（hermes 提交 bug 报告与契约建议，2026-08-10）
> 协作：本文档由前端（hermes）提交，供后端（本仓库 AI）评审实施；实施后前端联动 convert.ts 字段映射。
> 背景迭代 12（previewValue 持久化）已合入，本文档是同一类问题（元素 JSON 契约字段缺失）的**第二批次补齐**。

---

## 1. 问题（用户实测报告）

用户从设计器导出设计 JSON（`labelframe-web-design` 格式），在新环境导入（Ctrl+Shift+V）→ 保存 → 重新打开模板，**画布显示与保存时不一致：控件位置有偏移、字体大小变化**。

复现素材：`C:\work\Multiway\project\S00008\系统导入筹备\物料标签设计_100×60_方案1_非表格样式.json`（100×60，11 元素：1 二维码 + 10 文本）。

## 2. 复现证据（hermes 实测）

用前端 `parseDesign → toLayout（保存）→ fromBackendElements（读回）` 全链路复现，**11/11 元素全部受影响**：

```
QrCode(UniCode):     x: 44.99999999999999 → 45（浮点舍入，无碍）; qrEcc: "H" → "M"
Text(MaterialName):  h: 25 → 10; wrap: true → false; valign: "top" → "middle"; lineHeight: 1.3 → 1.2
Text(Specification): wrap: true → false; valign: "top" → "middle"
Text(MaterialCode):  wrap: true → false
Text(UniCode):       paddingV: 1 → 2
Text(WarehouseName): paddingV: 1 → 2
（其余元素：fontFamily undefined → "Microsoft YaHei" 等默认值，视觉一致，无碍）
```

**用户可见影响映射**：
- 「字体大小变了」：MaterialName 框高 25mm 保存后读回 10mm（`heightMm` 无字段，兜底 `max(fontH+2×padding, 10)`），长文本从 25mm 多行（wrap=true, valign=top）退化为 10mm 单行 → 缩小适应（shrink）字号剧变；Specification/MaterialCode 的 wrap 丢失同样触发单行溢出缩小。
- 「位置偏移」：valign top→middle 使文字在框内垂直位移；UniCode/WarehouseName 的 `paddingV: 1→2`（不对称 padding 经单值 `paddingMm` 取 max 合并）使内容下移 1mm。
- 二维码纠错级别 H→M（`qrEcc` 无字段），打印属性被改。

**后端存储侧确认**（`GET /api/templates/…` 实测）：text 元素仅有 `fontName/fontHeightMm/fontWidthMm/widthMm/textAlign/paddingMm/previewValue/sourceKey` 等；**无 heightMm / wrap / lineHeight / valign / fitMode / fontFamily**；qrcode 无 `qrEcc / qrMargin`。迭代 12 已加 `previewValue`，但文本排版与二维码参数仍属契约缺口（此前记于 FRONTEND-REPORT §7 决策 6「已知差距」）。

## 3. 根因

- 后端 `LabelElementJsonConverter.Write`（自定义）只输出其已知字段；`Read` 由默认反序列化器承接（未知属性忽略）。
- 前端 `convert.ts` 是严格镜像后端契约的忠实转换器：**写了后端不收（丢弃），不写则读回丢失**。前端单方面无法修复，必须后端扩展元素 JSON 契约。

## 4. 建议契约扩展（后端实施）

### 4.1 元素模型与 JSON 字段

| 元素 | 新增字段 | 类型 | 前端默认 | 说明 |
|---|---|---|---|---|
| text | `heightMm` | number? | — | 文本控件高度（后端 ZPL 打印不依赖它，仅前端布局；当前读回兜底 `max(fontH+2pad,10)` 是偏差来源） |
| text | `wrap` | bool? | false | 自动换行（`^FB` 行为的前端呈现） |
| text | `lineHeight` | number? | 1.2 | 行距系数 |
| text | `valign` | 'Top'/'Middle'/'Bottom'? | Middle | 垂直对齐 |
| text | `fitMode` | 'Shrink'/'None'? | Shrink | 缩小适应 / 不处理 |
| text | `fontFamily` | string? | — | 前端画布字体（打印用字体仍由宿主 fontName 决定，不影响 ZPL） |
| qrcode | `qrEcc` | 'L'/'M'/'Q'/'H'? | M | 纠错级别（打印属性，ZXing 渲染用） |
| qrcode | `qrMargin` | int? | 2 | 静区（模块数） |
| barcode | `displayValue` | bool? | true | 底部文字（顺带补齐，本次未触发） |
| 通用 | `paddingH` / `paddingV` | number? | 0 | 双边内边距（替代单值 `paddingMm` 的 max 近似；`paddingMm` 保留兼容，读回优先双边） |

- `LabelElement` 各子类增加对应属性；`LabelElementJsonConverter.Write` **有值即写**（与现有「非 0/非空才写」省略风格一致）；`Read` 由默认反序列化器承接（无需迁移，layout 整块 JSON 存储）。
- 旧模板（无新字段）读回取前端默认 → **行为与现状完全一致**（向后兼容）。

### 4.2 测试（后端）

- 新字段序列化 round-trip（text 全字段 + qrcode + barcode displayValue + paddingH/V）；
- 省略规则：默认值 / 空值不输出；
- 回归：现有 53+ 用例全绿，Vector 打印输出不变（heightMm 等不进 ZPL 编码路径）。

## 5. 前端联动（hermes，后端契约就绪后）

- `convert.ts`：`BackendElement` 加对应字段；`toBackendElement` 写方向（有值即写）；`fromBackendElements` 读回（`?? 前端默认`）；**读回规则改为 `heightMm ?? 兜底`、`wrap ?? false`、`valign ?? 'middle'`、`fitMode ?? 'shrink'`、`qrEcc ?? 'M'`、`paddingH/V ?? paddingMm`**。
- `convert.test.ts`：新增 text/qrcode 全字段往返用例 + 旧模板无字段兼容用例。
- 时序：前端映射可先行（后端未实现时写方向被忽略、读回默认，不破坏现状）；后端实现后自动完整。

## 6. 验收标准

1. 导入「物料标签设计_100×60_方案1_非表格样式.json」→ 保存 → 重开 → **逐元素逐字段与导入一致**（用 §2 复现脚本回归：差异清单为空）。
2. 旧模板（迭代 12 及更早）打开行为不变（读回默认，无报错）。
3. 矢量打印输出与现有一致（新字段不进 ZPL）；`dotnet test` / `pnpm test` 全绿。
4. 双向：设计器内修改 h/wrap/valign/qrEcc 等属性 → 保存 → 重开保留。

## 7. 协作流程

1. 本文档交后端评审；确认后后端实施第 4 节，hermes 实施第 5 节。
2. 联调验收按第 6 节；完成后更新 ROADMAP / CHANGELOG / DESIGN.md（决策记录）。
