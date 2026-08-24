# 迭代 13 规格：文本排版与二维码参数持久化（元素契约补齐）

> 状态：已完成并归档（2026-08-10，前后端已实施，用户验收待执行）
> 协作：本文档由前端（hermes）提交，后端评审实施；实施后前端已联动 convert.ts 字段映射。
> 背景：迭代 12（previewValue）与 0.11.6（heightMm / verticalAlign）已合入；本文档覆盖**仍缺失**的第二批元素契约字段。

---

## 1. 问题（用户实测报告）

用户从设计器导出设计 JSON（`labelframe-web-design` 格式）→ 新环境导入 → 保存 → 重新打开，**画布显示与保存时不一致：位置偏移、字体大小变化**。

复现素材：`C:\work\Multiway\project\S00008\系统导入筹备\物料标签设计_100×60_方案1_非表格样式.json`（100×60，11 元素）。

## 2. 复现证据（hermes 实测，基于当前最新代码基线）

用 `parseDesign → toLayout（保存）→ fromBackendElements（读回）` 全链路复现，**11/11 元素仍存在差异**（差异字段收敛后）：

```
Text(MaterialName):  wrap: true → false; lineHeight: 1.3 → 1.2
Text(Specification): wrap: true → false
Text(MaterialCode):  wrap: true → false
QrCode(UniCode):     qrEcc: "H" → "M"
Text(UniCode):       paddingV: 1 → 2
Text(WarehouseName): paddingV: 1 → 2
（其余为 fontFamily undefined → "Microsoft YaHei" 等默认值回填，视觉一致，无碍）
```

**用户可见影响**：
- 「字体大小变了」：MaterialName 长文本 `wrap=true` 保存后读回 `wrap=false` → 单行溢出 → 缩小适应（shrink）字号剧变（Specification / MaterialCode 同理）。
- 「位置偏移」：UniCode / WarehouseName 的 `paddingH:2 / paddingV:1` 不对称，经后端单值 `paddingMm` 取 max 合并 → 读回 2/2 → 内容下移 1mm。
- 二维码纠错级别 H→M（打印属性被改，`qrEcc` 无字段）。

## 3. 现状核对（已实现 vs 仍缺失）

### 3.1 已实现（0.11.6，本次问题中已解决的）

| 字段 | 实现 | 实测确认 |
|---|---|---|
| text `heightMm` | `HeightMm > 0` 才写；读回 `heightMm ?? 兜底(max(fontH+2pad,10))` | ✅ 部署后端（127.0.0.1:53960）探测：POST heightMm=25 → 读回 25 |
| text `verticalAlign` | 枚举 Top/Middle/Bottom，**非 Top 才写**（Top 默认兼容旧模板） | ✅ 部署前端 JS 已含写入逻辑 |

→ 「MaterialName 框高 25→10」**已修复**（代码层面）；用户环境若仍复现，先核对部署版本与重新保存（旧模板由旧版前端保存时无 heightMm）。

### 3.2 仍缺失（本文档建议契约扩展）

| 元素 | 字段 | 类型 | 前端默认 | 影响 |
|---|---|---|---|---|
| text | `wrap` | bool? | false | 自动换行；长文本丢失 → 单行溢出缩小（**用户报告主因之一**） |
| text | `lineHeight` | number? | 1.2 | 行距；多行文本排版变化 |
| text | `fitMode` | 'Shrink'/'None'? | Shrink | 溢出处理方式 |
| text | `fontFamily` | string? | — | 前端画布字体（打印仍由 fontName 决定，不影响 ZPL） |
| qrcode | `qrEcc` | 'L'/'M'/'Q'/'H'? | M | 纠错级别（ZXing 渲染参数） |
| qrcode | `qrMargin` | int? | 2 | 静区模块数 |
| barcode | `displayValue` | bool? | true | 底部文字（顺带补齐） |
| 通用 | `paddingH` / `paddingV` | number? | 0 | 双边内边距（替代单值 `paddingMm` 的 max 近似；`paddingMm` 保留，读回优先双边） |

- `LabelElement` 各子类加属性；`LabelElementJsonConverter.Write` **非默认值才写**（与 heightMm/verticalAlign 现有省略风格一致）；`Read` 由默认反序列化器承接；无数据库迁移（layout 整块 JSON）。
- 旧模板无新字段 → 读回前端默认 → 行为与现状一致（向后兼容）。

## 4. 待确认（审核者 / 主 agent）

1. **默认值一致性**：后端 `verticalAlign` 默认 **Top**（0.11.6），前端内部默认 **middle**、读回 `|| 'middle'`。后果：旧模板（无字段）前端预览 middle、后端打印 Top → 打印与预览不一致；新建模板 valign=middle 不写字段 → 后端按 Top 打印。建议统一语义（如后端默认改 Middle + 旧模板读回显式 Top，或前端读回默认 top），并补一条「打印与预览一致」的联调验证。
2. `wrap` 写规则：建议 `true` 才写（默认 false，与现有省略风格一致）。
3. 部署核查：用户环境前端 JS 已含 heightMm/verticalAlign/previewValue（新版构建），后端探测支持 heightMm；但用户保存的模板无 heightMm 字段（保存时机/版本差异），建议用户升级部署后用「最新前端重新保存」验证 3.1 已修复项。

## 5. 前端联动（hermes，后端契约就绪后）

- `convert.ts`：`BackendElement` 加 `wrap/lineHeight/fitMode/fontFamily/qrEcc/qrMargin/displayValue/paddingH/paddingV`；写方向非默认即写；读回 `?? 前端默认`（`paddingH ?? paddingMm`、`paddingV ?? paddingMm`）。
- `convert.test.ts`：text/qrcode/barcode 全字段往返 + 旧模板无字段兼容用例（当前 57 用例 + 新增）。
- 时序：前端映射可先行（后端未实现时写方向被忽略、读回默认，不破坏现状）。

## 6. 验收标准

1. 导入「物料标签设计_100×60_方案1_非表格样式.json」→ 保存 → 重开 → **逐元素逐字段与导入一致**（§2 复现脚本回归：差异清单为空）。
2. 旧模板打开行为不变（读回默认，无报错）。
3. 矢量打印输出与现有一致（新字段不进 ZPL 编码路径）；`dotnet test` / `pnpm test` 全绿。
4. 打印与预览垂直对齐一致（§4.1 默认值统一后联调验证）。

## 7. 协作流程

1. 本文档交后端评审；确认后后端实施 §3.2 与 §4.1，hermes 实施 §5。
2. 联调验收按 §6；完成后更新 ROADMAP / CHANGELOG / DESIGN.md（决策记录）。
