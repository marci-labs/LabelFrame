// @vitest-environment jsdom
// 帮助演示纯数据与样例生命周期单测（迭代 121 · #280）：
// - 步骤定义：两场演示的顺序 / tab / 锚点 / i18n key 与「✅ 文案定稿 v1」demo 行一一对应；
//   AC-02/03 主线覆盖（designer 三问 + 数据页承接；workbench 新建 / 导入 / 命名）；
// - 样例模板包：拍板 3 建议套餐（固定名「示例·基础标签」＋独立分组「示例」＋三控件双字段）；
// - 残留登记：注册 / 读取 / 清除幂等与损坏容错（决议 1 中断残留路径的数据基座）；
// - 模板库变更广播事件（演示进程直接写库 → Workbench 列表刷新）。

import { beforeEach, describe, expect, it } from 'vitest'
import {
  DEMO_RESIDUE_KEY,
  DEMO_STEPS,
  SAMPLE_TEMPLATE_GROUP,
  SAMPLE_TEMPLATE_NAME,
  TEMPLATES_CHANGED_EVENT,
  clearDemoResidue,
  getDemoResidue,
  notifyTemplatesChanged,
  registerDemoResidue,
  sampleTemplatePackage,
} from './helpDemo'
import zhHelp from '../i18n/locales/zh-CN/help.json'
import enHelp from '../i18n/locales/en/help.json'

/** 按点路径取嵌套 JSON 词条（不存在返回 undefined）。 */
function resolve(pack: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>(
    (node, seg) => (node !== null && typeof node === 'object' ? (node as Record<string, unknown>)[seg] : undefined),
    pack,
  )
}

describe('演示步骤定义（lib/helpDemo.ts 纯数据，#280 方案步骤 4~6）', () => {
  it('designer 演示 8 步：三问主线（绘制 / 字段 / 命名）→ 数据与打印承接 → 收尾（AC-02）', () => {
    const steps = DEMO_STEPS.designer
    expect(steps.map((s) => s.key)).toEqual([
      'demo.designer.intro',
      'demo.designer.draw',
      'demo.designer.props',
      'demo.designer.fields',
      'demo.designer.name',
      'demo.data.form',
      'demo.data.excel',
      'demo.designer.outro',
    ])
    // 跨页段（数据页）排在「命名保存」之后：保存成功自然复位 dirty，避开 #149 离开守卫挂起切页
    expect(steps.map((s) => s.tab)).toEqual([
      'designer',
      'designer',
      'designer',
      'designer',
      'designer',
      'data',
      'data',
      'data',
    ])
  })

  it('workbench 演示 6 步：新建（跨页跟随）→ 导出 → 导入 → 按名识别 → 收尾（AC-03，拍板 4/5）', () => {
    const steps = DEMO_STEPS.workbench
    expect(steps.map((s) => s.key)).toEqual([
      'demo.workbench.intro',
      'demo.workbench.new',
      'demo.workbench.export',
      'demo.workbench.import',
      'demo.workbench.identify',
      'demo.workbench.outro',
    ])
    expect(steps.map((s) => s.tab)).toEqual(['workbench', 'workbench', 'workbench', 'workbench', 'workbench', 'workbench'])
    // 拍板 4：「新建」步骤 waitFor 观测切到设计器（演示跨页跟随），步骤本体留在 workbench 等真实点击
    expect(steps[1].waitFor).toEqual({ kind: 'tab', tab: 'designer' })
  })

  it('真实操作自动推进条件（拍板 6）：绘制=画布元素≥1、字段=打印字段≥1、表单=样例已选中', () => {
    expect(DEMO_STEPS.designer[1].waitFor).toEqual({
      kind: 'attr',
      selector: '[data-demo="designer-canvas"]',
      attr: 'data-demo-count',
      min: 1,
    })
    expect(DEMO_STEPS.designer[3].waitFor).toEqual({
      kind: 'attr',
      selector: '[data-demo="designer-fields"]',
      attr: 'data-demo-count',
      min: 1,
    })
    expect(DEMO_STEPS.designer[5].waitFor).toEqual({
      kind: 'attr',
      selector: '[data-demo="dataprint-template-select"]',
      attr: 'data-demo-value',
      value: SAMPLE_TEMPLATE_NAME,
    })
  })

  it('步骤 i18n key 在 zh/en help 语言包真实存在（标题 / 正文非空字符串）', () => {
    for (const demo of [DEMO_STEPS.designer, DEMO_STEPS.workbench]) {
      for (const step of demo) {
        for (const suffix of ['title', 'body']) {
          for (const [lang, pack] of [
            ['zh', zhHelp],
            ['en', enHelp],
          ] as const) {
            const value = resolve(pack, `${step.key}.${suffix}`)
            expect(typeof value, `${step.key}.${suffix} (${lang})`).toBe('string')
            expect(value as string, `${step.key}.${suffix} (${lang})`).toBeTruthy()
          }
        }
      }
    }
  })

  it('收尾步（outro）无锚点无等待——气泡居中、仅手动完成（收尾即清理决策点）', () => {
    expect(DEMO_STEPS.designer[DEMO_STEPS.designer.length - 1]).toMatchObject({ anchor: null, waitFor: null })
    expect(DEMO_STEPS.workbench[DEMO_STEPS.workbench.length - 1]).toMatchObject({ anchor: null, waitFor: null })
  })
})

describe('样例模板包（拍板 3 建议套餐 + 拍板 9 固定名）', () => {
  it('固定名「示例·基础标签」+ 独立分组「示例」，同名重复组装内容一致（幂等覆盖前提）', () => {
    expect(SAMPLE_TEMPLATE_NAME).toBe('示例·基础标签')
    expect(SAMPLE_TEMPLATE_GROUP).toBe('示例')
    const a = sampleTemplatePackage()
    const b = sampleTemplatePackage()
    expect(a).toEqual(b)
  })

  it('三控件双字段（文本:品名 / 条码:编码 / 二维码:编码），契约字段由版式推导', () => {
    const pkg = sampleTemplatePackage()
    expect(pkg.name).toBe(SAMPLE_TEMPLATE_NAME)
    expect(pkg.group).toBe(SAMPLE_TEMPLATE_GROUP)
    const elementTypes = pkg.layout.elements.map((e) => e.type)
    // 版式元素为后端 JSON 形态（小写类型）
    expect(elementTypes).toEqual(['text', 'barcode', 'qrcode'])
    // 契约字段 = 去重后的填充字段（品名 + 编码，双字段 Excel 映射讲解的落点）
    expect(pkg.contract.fields.map((f) => f.key)).toEqual(['品名', '编码'])
  })
})

describe('演示残留登记（localStorage，决议 1 中断残留路径）', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('注册后可读取（名单精确复制）；清除后为空；重复注册幂等覆盖（拍板 9）', () => {
    expect(getDemoResidue()).toBeNull()
    registerDemoResidue('designer', [SAMPLE_TEMPLATE_NAME])
    expect(getDemoResidue()).toMatchObject({ demoId: 'designer', names: [SAMPLE_TEMPLATE_NAME] })
    registerDemoResidue('workbench', [SAMPLE_TEMPLATE_NAME])
    expect(getDemoResidue()).toMatchObject({ demoId: 'workbench', names: [SAMPLE_TEMPLATE_NAME] })
    clearDemoResidue()
    expect(getDemoResidue()).toBeNull()
    expect(window.localStorage.getItem(DEMO_RESIDUE_KEY)).toBeNull()
  })

  it('登记内容损坏 / 形态非法按无残留处理（不抛错）', () => {
    for (const raw of ['not-json', JSON.stringify({ demoId: 'x', names: ['a'] }), JSON.stringify({ demoId: 'designer', names: [] }), JSON.stringify({ demoId: 'designer', names: ['a', 3] })]) {
      window.localStorage.setItem(DEMO_RESIDUE_KEY, raw)
      const record = getDemoResidue()
      if (record) expect(record.names.every((n) => typeof n === 'string' && n.length > 0)).toBe(true)
    }
  })
})

describe('模板库变更广播（演示进程直接写库 → 列表页刷新）', () => {
  it('notifyTemplatesChanged 派发 window 事件（Workbench 监听刷新样例卡）', () => {
    let fired = 0
    const on = () => {
      fired += 1
    }
    window.addEventListener(TEMPLATES_CHANGED_EVENT, on)
    notifyTemplatesChanged()
    window.removeEventListener(TEMPLATES_CHANGED_EVENT, on)
    notifyTemplatesChanged()
    expect(fired).toBe(1)
  })
})
