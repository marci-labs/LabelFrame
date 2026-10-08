// 帮助演示——步骤定义纯数据 + 一次性样例模板 + 中断残留登记（迭代 121 · #280，决议 1/2/9 与拍板 3~9）。
// 架构口径（#280「📐 方案 v1」+ 拍板 11「可复用结构」）：演示本体在目标界面上下文运行（DemoRunner 挂
// Shell 级，App.tsx），本文件只承载纯数据与生命周期登记——帮助卡片数据同口径纯数据化（pages/Help.tsx），
// server 版后续换一组卡数据即可低成本接入（本轮 client-only，拍板/裁剪口径）。
//
// 存储对照（#280 方案 v1 已核实口径，同 guide.ts 先例）：样例残留登记 = 「跨刷新/重启检测残留」的登记型
// 数据，走 localStorage（键 `labelframe.demo.residue`）；DESIGN #51「禁 localStorage」适用范围是 DataPrint
// 会话草稿（需标签页隔离故 sessionStorage），语义相反不适用。登记名单只作「精确删除名单」防误删用户自建的
// 「示例·」前缀模板——残留事实以后端模板库为准（listTemplates 核对存在性），核对确认已不存在即移除登记。

import { deriveFieldInfos } from './design/fields'
import { toContract, toLayout } from './design/convert'
import type { BarcodeElement, DesignElement, QrCodeElement, TextElement } from './design/types'
import type { TemplatePackage } from './api/types'
import type { TabId } from '../state/types'

/** 演示标识（首波两场：designer 三问主线 + workbench 新建/导入/命名；jobs/settings/data 演示留 V3）。 */
export type DemoId = 'designer' | 'workbench'

/**
 * 真实操作达成条件（拍板 6：完成真实操作自动推进 +「下一步」手动兑底；null = 仅手动推进）。
 * - `attr`：观测 DOM 元素上的 data-demo 观测钩子属性（data-demo-count 计数 / data-demo-value 值）；
 * - `tab`：观测 Shell 当前 tab（跨页跟随，如 workbench 演示「新建」切设计器后继续）。
 */
export type DemoWaitFor =
  | { kind: 'attr'; selector: string; attr: 'data-demo-count' | 'data-demo-value'; min?: number; value?: string }
  | { kind: 'tab'; tab: TabId }

/** 演示步骤定义（顺序即播放顺序；文案逐字来源 = #280「✅ 文案定稿 v1」，help 域 key = `demo.*`）。 */
export interface DemoStep {
  /** 步骤所在 tab（DemoRunner 经注入的 switchTab 切页，继承 #149 设计器离开守卫语义）。 */
  tab: TabId
  /** 锚点完整选择器（data-demo 语义钩子或既有 data-guide 锚点复用）；null = 无锚点（全屏暗幕居中气泡）。 */
  anchor: string | null
  /** help 域 i18n 词条前缀（title / body 后缀）。 */
  key: string
  /** 真实操作达成条件；null = 仅手动「下一步」。 */
  waitFor: DemoWaitFor | null
}

/** 样例模板名（拍板 3 建议套餐；拍板 9 固定名覆盖——重复发起同名覆盖，登记名单幂等不累积）。 */
export const SAMPLE_TEMPLATE_NAME = '示例·基础标签'
/** 样例独立分组（拍板 3；分组由列表推导，清理后空组自然消失）。 */
export const SAMPLE_TEMPLATE_GROUP = '示例'

// 帮助卡片数据（拍板 11「帮助卡片数据纯数据化」）：五菜单卡「是什么 / 能干什么」索引，与 CLIENT_TABS
// 一一对应；`demo` 仅 designer / workbench 卡携带（拍板 8：其余卡不渲染演示按钮，V3 演示到位再露出）。
export interface HelpCard {
  /** 卡片标识（help 域词条前缀 card.<id> + data-help 测试钩子）。 */
  id: 'workbench' | 'designer' | 'data' | 'jobs' | 'settings'
  /** 跳转目标 tab。 */
  tab: TabId
  icon: 'workbench' | 'designer' | 'data' | 'history' | 'settings'
  /** 「去做演示」深链对应的演示（仅 designer / workbench 卡）。 */
  demo?: DemoId
}

export const HELP_CARDS: readonly HelpCard[] = [
  { id: 'workbench', tab: 'workbench', icon: 'workbench', demo: 'workbench' },
  { id: 'designer', tab: 'designer', icon: 'designer', demo: 'designer' },
  { id: 'data', tab: 'data', icon: 'data' },
  { id: 'jobs', tab: 'jobs', icon: 'history' },
  { id: 'settings', tab: 'settings', icon: 'settings' },
]

// 锚点约定：data-demo 语义属性为演示专属钩子；`[data-guide=…]` 复用迭代 119 既有锚点（#280 方案步骤 6）。
// data-help 属性为页头「功能演示」入口钩子（Workbench / Designer 页头，拍板 2 措辞）。
export const DEMO_STEPS: Record<DemoId, readonly DemoStep[]> = {
  // designer 三问主线（AC-02）：绘制（拖控件成标签）→ 属性 → 字段作用 → 命名保存（保存成功自动回工作台，
  // waitFor 观测 tab 到达）→ 数据与打印（字段进填写表单 + Excel 列映射，承接「字段有什么用」，方案步骤 6）→ 收尾。
  // 跨页段排在「命名保存」之后：保存成功自然复位 dirty，避开 #149 离开守卫挂起切页（方案 risks 预判）。
  designer: [
    { tab: 'designer', anchor: '[data-demo="designer-toolbar"]', key: 'demo.designer.intro', waitFor: null },
    { tab: 'designer', anchor: '[data-demo="designer-palette"]', key: 'demo.designer.draw', waitFor: { kind: 'attr', selector: '[data-demo="designer-canvas"]', attr: 'data-demo-count', min: 1 } },
    { tab: 'designer', anchor: '[data-demo="designer-props"]', key: 'demo.designer.props', waitFor: null },
    { tab: 'designer', anchor: '[data-demo="designer-fields"]', key: 'demo.designer.fields', waitFor: { kind: 'attr', selector: '[data-demo="designer-fields"]', attr: 'data-demo-count', min: 1 } },
    { tab: 'designer', anchor: '[data-demo="designer-name"]', key: 'demo.designer.name', waitFor: { kind: 'tab', tab: 'workbench' } },
    { tab: 'data', anchor: '[data-demo="dataprint-template-select"]', key: 'demo.data.form', waitFor: { kind: 'attr', selector: '[data-demo="dataprint-template-select"]', attr: 'data-demo-value', value: SAMPLE_TEMPLATE_NAME } },
    { tab: 'data', anchor: '[data-guide="dataprint-excel-import"]', key: 'demo.data.excel', waitFor: null },
    { tab: 'data', anchor: null, key: 'demo.designer.outro', waitFor: null },
  ],
  // workbench 主线（AC-03，拍板 4 跨页跟随 + 拍板 5 导出→导入闭环）：新建（真实点击切设计器，演示跟随）→
  // 回工作台导出样例 → 用户亲手导入（演示代码不代选文件——浏览器 file input 手势硬边界）→ 按名识别 → 收尾。
  workbench: [
    { tab: 'workbench', anchor: '[data-help="workbench-help"]', key: 'demo.workbench.intro', waitFor: null },
    { tab: 'workbench', anchor: '[data-guide="workbench-new"]', key: 'demo.workbench.new', waitFor: { kind: 'tab', tab: 'designer' } },
    { tab: 'workbench', anchor: '[data-demo="wb-card-more"][data-demo-name="sample"]', key: 'demo.workbench.export', waitFor: null },
    { tab: 'workbench', anchor: '[data-demo="workbench-import"]', key: 'demo.workbench.import', waitFor: null },
    { tab: 'workbench', anchor: '[data-demo="workbench-search"]', key: 'demo.workbench.identify', waitFor: null },
    { tab: 'workbench', anchor: null, key: 'demo.workbench.outro', waitFor: null },
  ],
}

// 样例元素（拍板 3：文本:品名 / 条码:编码 / 二维码:编码——覆盖三控件与双字段 Excel 映射讲解）。
// id 固定字面量：样例为整体 saveTemplate 覆盖写，无需 uid 随机性，保证每次发起内容一致（拍板 9 幂等）。
const SAMPLE_ELEMENTS: (TextElement | BarcodeElement | QrCodeElement)[] = [
  {
    id: 'demo-sample-text', type: 'Text', x: 4, y: 3, w: 52, h: 10, border: 0,
    fontH: 5, fontW: 5, fontFamily: 'Microsoft YaHei', bold: false, wrap: false, lineHeight: 1.2,
    valign: 'middle', mode: 'field', key: '品名', displayName: '品名', text: '示例商品',
    align: 'Left', paddingH: 1, paddingV: 1, fitMode: 'shrink',
  },
  {
    id: 'demo-sample-barcode', type: 'Barcode', x: 4, y: 16, w: 34, h: 16, border: 0,
    mode: 'field', key: '编码', displayName: '编码', text: 'LF-0001',
    paddingH: 1, paddingV: 1, barcodeFormat: 'CODE128', displayValue: true, moduleWidth: 1,
  },
  {
    id: 'demo-sample-qrcode', type: 'QrCode', x: 42, y: 16, w: 14, h: 14, border: 0,
    mode: 'field', key: '编码', displayName: '编码', text: 'LF-0001',
    paddingH: 1, paddingV: 1, qrEcc: 'M', qrMargin: 2,
  },
] satisfies DesignElement[]

/** 组装「示例·基础标签」模板包（纯数据；经既有 biz.saveTemplate 落库，方案 §C「需建数据的环节自动创建」）。 */
export function sampleTemplatePackage(): TemplatePackage {
  const fields = deriveFieldInfos(SAMPLE_ELEMENTS)
  return {
    name: SAMPLE_TEMPLATE_NAME,
    group: SAMPLE_TEMPLATE_GROUP,
    contract: toContract(SAMPLE_TEMPLATE_NAME, '1', fields),
    // 迭代 12 口径：不传 testData——由后端从元素 previewValue 自动派生
    layout: toLayout(SAMPLE_TEMPLATE_NAME, SAMPLE_TEMPLATE_NAME, '1', 60, 40, SAMPLE_ELEMENTS),
  }
}

// ---- 演示样例残留登记（localStorage；方案 §C「中途退出残留下次进入时询问」） ----

export const DEMO_RESIDUE_KEY = 'labelframe.demo.residue'

export interface DemoResidue {
  /** 中断的演示标识。 */
  demoId: DemoId
  /** 演示创建的模板名精确清单（删除名单；防误删用户自建同名前缀模板）。 */
  names: string[]
  /** 登记时间戳（ms）。 */
  at: number
}

function getLocalStorage(): Storage | null {
  try {
    // 显式 window.localStorage + typeof 守卫（guide.ts 同款：Node 26 遮蔽坑、隐私模式降级）
    return typeof window !== 'undefined' ? window.localStorage : null
  } catch {
    return null
  }
}

/** 登记「演示已创建、未收尾」的样例名单（演示启动创建样例成功后调用；重复登记幂等覆盖）。 */
export function registerDemoResidue(demoId: DemoId, names: string[]): void {
  const storage = getLocalStorage()
  if (!storage) return
  const record: DemoResidue = { demoId, names: [...names], at: Date.now() }
  storage.setItem(DEMO_RESIDUE_KEY, JSON.stringify(record))
}

/** 读取残留登记（无登记 / 内容损坏返回 null）。 */
export function getDemoResidue(): DemoResidue | null {
  const storage = getLocalStorage()
  if (!storage) return null
  const raw = storage.getItem(DEMO_RESIDUE_KEY)
  if (!raw) return null
  try {
    const parsed = JSON.parse(raw) as Partial<DemoResidue>
    if (parsed.demoId !== 'designer' && parsed.demoId !== 'workbench') return null
    if (!Array.isArray(parsed.names) || parsed.names.length === 0) return null
    return { demoId: parsed.demoId, names: parsed.names.filter((n): n is string => typeof n === 'string' && n.length > 0), at: typeof parsed.at === 'number' ? parsed.at : 0 }
  } catch {
    return null
  }
}

/** 移除残留登记（收尾清理成功 / 勾选保留 / 核对确认模板已不存在时调用）。 */
export function clearDemoResidue(): void {
  const storage = getLocalStorage()
  if (storage) storage.removeItem(DEMO_RESIDUE_KEY)
}

// ---- 模板库变更通知（演示进程直接写库，列表页缓存需失效刷新） ----

/** window 事件名：演示创建 / 清理样例后广播，Workbench 列表监听刷新（无全局状态总线的最小解耦）。 */
export const TEMPLATES_CHANGED_EVENT = 'labelframe:templates-changed'

/** 广播模板库变更（DemoRunner 在样例创建成功 / 收尾清理后调用；仅 client 构建存在演示进程）。 */
export function notifyTemplatesChanged(): void {
  if (typeof window === 'undefined') return
  window.dispatchEvent(new Event(TEMPLATES_CHANGED_EVENT))
}
