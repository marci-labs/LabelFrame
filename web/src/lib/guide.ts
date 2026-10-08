// 新用户首次使用引导——步骤定义纯数据 + 首见标记读写（迭代 119 · #276，决策 #170）。
// 拍板口径（#276 方案 v1 用户拍板）：①分步气泡 spotlight ②自研实现 ③localStorage 每浏览器首见一次
// ④键名带版本号 ⑤重看入口在状态栏 meta 区「日志」旁 ⑧跳过即已看 ⑨Esc = 跳过并写标记。
//
// 存储对照（#276 方案 v1 已核实口径）：首见标记是「每浏览器一次」的偏好型数据（需跨标签页共享、持久），
// 与决策 #164 ④语言偏好（labelframe.locale）、默认目标设备（AppContext labelframe.defaultTargetDeviceId）
// 同类先例；DESIGN #51「禁 localStorage」的适用范围是 DataPrint 会话草稿（printDraft 需标签页隔离故
// sessionStorage），语义相反不适用本标记；不新增后端 API（#57 机器级 settings.json 路线不出轮）。

import type { TabId } from '../state/types'

/** 首见标记 localStorage 键（值 'done'；带版本号——引导改版换键即整体重放，决策 #170 ④）。
 * 迭代 121（#280）：文案重写为「介绍功能＋什么场景用」口径，键升级 v2——只持有 v1 键的存量已看用户
 * 判定为未看过、自动重放一次（v1 键不迁移不删除，读判定只认 v2，完成写 v2 后不再自动出现）。 */
export const GUIDE_SEEN_KEY = 'labelframe.guide.client.v2'

/** v1 遗留键（迭代 119）：仅 Guide.test.tsx 重放断言使用——运行期不读不写不清理（遗留无害）。 */
export const GUIDE_SEEN_V1_KEY = 'labelframe.guide.client.v1'

/** 引导步骤定义（顺序即播放顺序；V1 五步覆盖核心链，#276 方案 v1 步骤清单拍板）。 */
export interface GuideStep {
  /** 步骤所在 tab（App Shell 经 switchTab 切换，继承设计器离开守卫语义）。 */
  tab: TabId
  /** 锚点元素的 data-guide 属性值（组件挂载点见 Workbench / DataPrint / App）。 */
  anchor: string
  /** guide 域 i18n 词条前缀（文案逐字来源 = #276「✅ 文案定稿 v1」，title / body 后缀）。 */
  key: string
}

export const GUIDE_STEPS: readonly GuideStep[] = [
  { tab: 'workbench', anchor: 'workbench-new', key: 'tour.workbench.new' },
  { tab: 'designer', anchor: 'designer-empty-new', key: 'tour.designer.new' },
  { tab: 'data', anchor: 'dataprint-template-select', key: 'tour.dataprint.select' },
  { tab: 'data', anchor: 'dataprint-excel-import', key: 'tour.dataprint.excel' },
  { tab: 'workbench', anchor: 'nav-main', key: 'tour.workbench.nav' },
]

/** 步骤锚点的 DOM 查询选择器（data-guide 语义属性，与既有 class 解耦）。 */
export function guideAnchorSelector(step: GuideStep): string {
  return `[data-guide="${step.anchor}"]`
}

function getLocalStorage(): Storage | null {
  try {
    // 显式 window.localStorage + typeof 守卫：Node 26 实验性全局 localStorage 会遮蔽 jsdom 注入版
    // （lib/settings.ts:4 已知坑）；隐私模式等存取异常按未看过降级（i18n/index.ts readSavedLocale 先例）
    return typeof window !== 'undefined' ? window.localStorage : null
  } catch {
    return null
  }
}

/** 是否已看过引导（无标记 = 首次；存储异常容错视为未看过——引导每次出现，可接受降级）。 */
export function isGuideSeen(): boolean {
  const storage = getLocalStorage()
  return storage ? storage.getItem(GUIDE_SEEN_KEY) !== null : false
}

/** 写入首见标记（完成 / 跳过 / Esc 退出时调用；「使用引导」重放路径重复写入幂等）。 */
export function markGuideSeen(): void {
  const storage = getLocalStorage()
  if (storage) storage.setItem(GUIDE_SEEN_KEY, 'done')
}
