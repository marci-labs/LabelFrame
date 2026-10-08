// @vitest-environment jsdom
// 帮助总界面测试（迭代 121 · #280）：
// - AC-01：五菜单卡片（是什么 / 能干什么）齐全、点击跳转对应 tab；designer / workbench 卡
//   「去做演示」深链回调携带正确 DemoId（跳转后自动开始由 App pendingDemo 机制承接，DemoRunner 侧另测）；
// - AC-05：help 域卡片 / 演示 / 弹窗词条与「✅ 文案定稿 v1」逐字一致（zh/en 双侧断言）；
//   拍板 8：jobs / settings / data 卡不渲染演示按钮（仅「进入」跳转）。
// 键集全量一致性归 locales.test.ts；server 构建不挂载守门在 Help.server.test.tsx。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen } from '@testing-library/react'
// 显式初始化 i18n 单例（Help 组件链无运行期 ../i18n 依赖，useTranslation 需已初始化的实例）
import '../i18n'
import { Help } from './Help'
import { HELP_CARDS } from '../lib/helpDemo'
import zhHelp from '../i18n/locales/zh-CN/help.json'
import enHelp from '../i18n/locales/en/help.json'

configure({ asyncUtilTimeout: 8000 })

// ---- 「✅ 文案定稿 v1」逐字底稿（定稿 json 字段镜像；help/demo/modal 三组行 6~26）----
const FINALIZED_HELP: readonly {
  key: string
  titleZh: string
  bodyZh: string
  titleEn: string
  bodyEn: string
  btnZh?: string
  btnEn?: string
}[] = [
  // help 卡片（行 6~10；按钮词 = 定稿组 1：深链卡「上手试一遍 / Try It Yourself」、跳转卡「进入 / Open」）
  { key: 'card.workbench', titleZh: '模板管理中心', bodyZh: '新建与导入模板；搜索、分组整理；从卡片直接进设计器或去打印。', btnZh: '上手试一遍', titleEn: 'Template Management', bodyEn: 'Create and import templates; organize them with search and groups; open the designer or print right from a card.', btnEn: 'Try It Yourself' },
  { key: 'card.designer', titleZh: '设计标签的画布', bodyZh: '拖入文本、条码、二维码拼出标签；给元素绑定打印字段；按真实打印比例预览效果。', btnZh: '上手试一遍', titleEn: 'The Label Design Canvas', bodyEn: 'Drag in text, barcode and QR code elements to compose a label; bind print fields; preview at real print scale.', btnEn: 'Try It Yourself' },
  { key: 'card.data', titleZh: '填数据打印页', bodyZh: '选模板生成填写表单；打印测试前先看图片预览；用 Excel 导入数据批量打印。', btnZh: '进入', titleEn: 'Fill Data & Print', bodyEn: 'Pick a template to get its fill-in form; preview the label image before a test print; batch print by importing Excel data.', btnEn: 'Open' },
  { key: 'card.jobs', titleZh: '打印记录查询', bodyZh: '查看最近 100 条打印记录；展开单条作业看逐张结果；打印失败时在这里找失败原因。', btnZh: '进入', titleEn: 'Print Job Records', bodyEn: 'Browse the last 100 print jobs; expand a job for per-label results; find failure reasons when a print goes wrong.', btnEn: 'Open' },
  { key: 'card.settings', titleZh: '本机设置中心', bodyZh: '配置服务端地址（多机共用模板）与打印机连接；查看打印机状态、发测试页；切换界面语言、更新客户端与插件。', btnZh: '进入', titleEn: 'Settings for This Machine', bodyEn: 'Set the server address (shared templates) and printers; check status, send a test page; update language, client and plugins.', btnEn: 'Open' },
  // designer 演示（行 11~16）
  { key: 'demo.designer.intro', titleZh: '亲手做一个标签', bodyZh: '接下来都是真实操作：拖控件、设属性、绑字段、命名保存；演示会创建「示例·基础标签」样例模板。', titleEn: 'Build One Yourself', bodyEn: 'All steps are real: drag widgets, set properties, bind fields, name and save; the demo creates "Sample · Basic Label".' },
  { key: 'demo.designer.draw', titleZh: '拖控件，拼版式', bodyZh: '从左侧控件栏把「文本」「条码」「二维码」拖进画布；移动时参考线自动吸附，Ctrl+Z 可撤销。', titleEn: 'Drag Widgets In', bodyEn: 'Drag "Text", "Barcode" and "QR code" from the left widget bar onto the canvas; smart guides snap as you move, Ctrl+Z to undo.' },
  { key: 'demo.designer.props', titleZh: '调整元素属性', bodyZh: '选中画布上的元素，右侧即可调字体、字高与条码参数。', titleEn: 'Tune the Properties', bodyEn: 'Select an element on the canvas and tune its font, font height and barcode parameters on the right.' },
  { key: 'demo.designer.fields', titleZh: '让内容跟着数据走', bodyZh: '来源选「字段填充」并填字段名（如品名），打印时自动填入数据；左侧「打印字段」列表自动生成。', titleEn: 'Make Content Follow Data', bodyEn: 'Pick "Field fill" and name the field (e.g. product name); data fills it at print time. The left "Print Fields" list is automatic.' },
  { key: 'demo.designer.name', titleZh: '命名并保存', bodyZh: '在顶部工具栏输入模板名称，点击「保存模板」；保存成功自动回到工作台，按名字即可找到它。', titleEn: 'Name It and Save', bodyEn: 'Type a name in the top toolbar and click "Save Template"; it returns to the Workbench, where you can find it by name.' },
  { key: 'demo.designer.outro', titleZh: '演示完成', bodyZh: '样例模板默认自动清理；勾选「保留样例」可留下练习。之后可从页头「功能演示」随时再来。', titleEn: 'Demo Complete', bodyEn: 'The sample is cleaned up by default; check "Keep sample" to keep practicing. Come back any time via "Demo" in the page header.' },
  // workbench 演示（行 17~22）
  { key: 'demo.workbench.intro', titleZh: '新建、导出、导入', bodyZh: '本演示全程真实操作：新建模板、导出成文件再导入回来，最后按名字找到它。', titleEn: 'Create, Export, Import', bodyEn: 'A real walk-through: create a template, export it to a file, import it back, then find it by name.' },
  { key: 'demo.workbench.new', titleZh: '新建一张模板', bodyZh: '点击「新建模板」进入设计器，演示随之继续，用示例元素拼出「示例·基础标签」。', titleEn: 'Create a New Template', bodyEn: 'Click "New Template" to open the designer; the demo follows and builds the "Sample · Basic Label" template with sample elements.' },
  { key: 'demo.workbench.export', titleZh: '把模板导出成文件', bodyZh: '在模板卡片点击「⋯」选择「导出」，下载 .lfpkg 模板包；下一步把它导入回来。', titleEn: 'Export It to a File', bodyEn: 'On the template card, open "…" and choose "Export" to download a .lfpkg package; you import it back in the next step.' },
  { key: 'demo.workbench.import', titleZh: '把文件导入回来', bodyZh: '点击「导入模板」选择刚下载的 .lfpkg 文件；导入后列表出现同名模板，内容与导出前一致。', titleEn: 'Import It Back', bodyEn: 'Click "Import Template" and pick the .lfpkg you just downloaded; the same template reappears in the list with contents intact.' },
  { key: 'demo.workbench.identify', titleZh: '按名字认模板', bodyZh: '模板以名字区分：列表按名称与分组排列，在搜索框输入名字即可快速定位。', titleEn: 'Know It by Name', bodyEn: 'Templates are distinguished by name: the list sorts by name and group; type a name in the search box to locate one quickly.' },
  { key: 'demo.workbench.outro', titleZh: '演示完成', bodyZh: '样例模板默认自动清理；勾选「保留样例」可留下练习。', titleEn: 'Demo Complete', bodyEn: 'The sample template is cleaned up by default; check "Keep sample" to keep practicing.' },
  // 数据与打印承接（行 23~24）
  { key: 'demo.data.form', titleZh: '字段变成了表单', bodyZh: '选示例模板后，刚才绑定的「品名」「编码」就在这里填写。', titleEn: 'Fields Become a Form', bodyEn: 'Pick the sample template; the "Product Name" and "Code" fields you just bound are filled in here.' },
  { key: 'demo.data.excel', titleZh: '字段也是 Excel 列', bodyZh: '点击「下载 Excel 模板」，一行填一条；导入时确认每列对应字段，批量打印（演示不出纸）。', titleEn: 'Fields Are Excel Columns', bodyEn: 'Click "Download Excel Template", one row per label; confirm each column\'s field on import to batch print (demo prints nothing).' },
]

/** 按点路径取嵌套 JSON 词条（不存在返回 undefined）。 */
function resolve(pack: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>(
    (node, seg) => (node !== null && typeof node === 'object' ? (node as Record<string, unknown>)[seg] : undefined),
    pack,
  )
}

describe('文案资源（AC-05：定稿逐字 + zh/en 双侧断言）', () => {
  it('zh 标题 / 正文 / 按钮词与「✅ 文案定稿 v1」逐字一致', () => {
    for (const row of FINALIZED_HELP) {
      expect(resolve(zhHelp, `${row.key}.title`), row.key).toBe(row.titleZh)
      expect(resolve(zhHelp, `${row.key}.body`), row.key).toBe(row.bodyZh)
      if (row.btnZh) expect(resolve(zhHelp, `${row.key}.action`), row.key).toBe(row.btnZh)
    }
    // 弹窗三选按钮词（定稿行 25/26 按钮列逐字拆分）
    expect(resolve(zhHelp, 'residue.clean')).toBe('立即清理')
    expect(resolve(zhHelp, 'residue.keep')).toBe('保留')
    expect(resolve(zhHelp, 'residue.later')).toBe('暂不')
    // 演示收尾勾选词 = 定稿 outro 正文引用原串；页头入口词 = 拍板组 1「功能演示」
    expect(resolve(zhHelp, 'demo.keepSample')).toBe('保留样例')
    expect(resolve(zhHelp, 'entry.start')).toBe('功能演示')
  })

  it('en 标题 / 正文 / 按钮词与「✅ 文案定稿 v1」逐字一致（zh/en 对称防线）', () => {
    for (const row of FINALIZED_HELP) {
      expect(resolve(enHelp, `${row.key}.title`), row.key).toBe(row.titleEn)
      expect(resolve(enHelp, `${row.key}.body`), row.key).toBe(row.bodyEn)
      if (row.btnEn) expect(resolve(enHelp, `${row.key}.action`), row.key).toBe(row.btnEn)
    }
    expect(resolve(enHelp, 'residue.clean')).toBe('Clean Up Now')
    expect(resolve(enHelp, 'residue.keep')).toBe('Keep')
    expect(resolve(enHelp, 'residue.later')).toBe('Not Now')
    expect(resolve(enHelp, 'demo.keepSample')).toBe('Keep sample')
    expect(resolve(enHelp, 'entry.start')).toBe('Demo')
  })

  it('中断残留弹窗词条与「✅ 文案定稿 v1」行 25/26 逐字一致（扁平键；正文两段并列渲染）', () => {
    // 行 25（key = help.residue.title）：标题 + 事实正文
    expect(resolve(zhHelp, 'residue.title')).toBe('发现演示样例')
    expect(resolve(zhHelp, 'residue.body')).toBe('上次演示中断，留下了样例模板。')
    expect(resolve(enHelp, 'residue.title')).toBe('Sample Templates Found')
    expect(resolve(enHelp, 'residue.body')).toBe('The last demo was interrupted and left sample templates behind.')
    // 行 26（key = help.residue.body）：同标题 + 选项说明正文（实现落 bodyExtra 避免与行 25 撞键，逐字不改）
    expect(resolve(zhHelp, 'residue.bodyExtra')).toBe('可立即清理，或保留继续练习；选「暂不」下次再问。')
    expect(resolve(enHelp, 'residue.bodyExtra')).toBe('Clean them up now, keep them to practice, or choose "Not Now" to decide next time.')
  })
})

describe('卡片数据（拍板 8/11：纯数据化 + 演示深链仅两卡）', () => {
  it('五卡与 CLIENT_TABS 五菜单一一对应；仅 designer / workbench 卡带演示深链', () => {
    expect(HELP_CARDS.map((c) => c.id)).toEqual(['workbench', 'designer', 'data', 'jobs', 'settings'])
    expect(HELP_CARDS.map((c) => c.tab)).toEqual(['workbench', 'designer', 'data', 'jobs', 'settings'])
    expect(HELP_CARDS.filter((c) => c.demo !== undefined).map((c) => c.id)).toEqual(['workbench', 'designer'])
  })
})

describe('Help 页渲染与交互（AC-01）', () => {
  afterEach(() => {
    cleanup()
  })

  function renderHelp() {
    const onOpenTab = vi.fn()
    const onStartDemo = vi.fn()
    render(<Help onOpenTab={onOpenTab} onStartDemo={onStartDemo} />)
    return { onOpenTab, onStartDemo }
  }

  it('五张卡片齐全（标题 = zh 定稿逐字）；页题复用导航词条「帮助」', () => {
    renderHelp()
    expect(screen.getByText('帮助')).toBeTruthy()
    for (const card of HELP_CARDS) {
      expect(screen.getByText(zhTitle(card.id))).toBeTruthy()
    }
  })

  it('designer / workbench 卡渲染「上手试一遍」深链按钮并回调正确 DemoId', () => {
    const { onStartDemo } = renderHelp()
    const buttons = screen.getAllByRole('button', { name: '上手试一遍' })
    expect(buttons.length).toBe(2)
    fireEvent.click(buttons[0])
    expect(onStartDemo).toHaveBeenCalledWith('workbench')
    fireEvent.click(buttons[1])
    expect(onStartDemo).toHaveBeenCalledWith('designer')
  })

  it('data / jobs / settings 卡仅「进入」跳转按钮，不渲染演示按钮（拍板 8）', () => {
    const { onStartDemo } = renderHelp()
    expect(screen.getAllByRole('button', { name: '进入' }).length).toBe(3)
    fireEvent.click(screen.getAllByRole('button', { name: '进入' })[0])
    expect(onStartDemo).not.toHaveBeenCalled()
  })

  it('点击卡片主体跳转对应界面（onOpenTab 携带 tab）；深链按钮点击不冒泡跳转', () => {
    const { onOpenTab } = renderHelp()
    fireEvent.click(screen.getByText('填数据打印页'))
    expect(onOpenTab).toHaveBeenCalledWith('data')
    // 深链按钮 stopPropagation：不重复触发卡片级跳转
    fireEvent.click(screen.getAllByRole('button', { name: '上手试一遍' })[0])
    expect(onOpenTab).not.toHaveBeenCalledWith('workbench')
  })
})

/** 卡片标题（zh 定稿；与 FINALIZED_HELP 对应行同源）。 */
function zhTitle(id: string): string {
  const row = FINALIZED_HELP.find((r) => r.key === `card.${id}`)
  return row?.titleZh ?? ''
}
