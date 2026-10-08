// @vitest-environment jsdom
// 分步引导测试（迭代 119 · #276；en 逐字比对补强与归位 components/ 见迭代 120 · #278；
// 迭代 121 · #280：文案 v2 重写——FINALIZED 底稿与硬编码断言随「✅ 文案定稿 v1」同步替换，首见标记键升 v2）：
// - AC-04：步骤定义的 i18n key 与「✅ 文案定稿 v1」（#280）五条一一对应；zh 标题 / 正文逐字一致
//   （定稿比对锚点）、单步正文 ≤ 60 字（拍板 10：v2 口径放宽）；en 标题 / 正文 / 步骤按钮与定稿逐字一致
//   （#278 AC-02 双语对称防线）；键集全量一致性归 locales.test.ts 统一防线；
// - AC-01/02：首见自动启动、逐步推进（经 switchTab 切页）、跳过 / 完成 / Esc 均写首见标记、刷新不再自动出现；
// - AC-03：状态栏「使用引导」重看入口再放完整一轮，重放不改变已看状态。
// server 构建分支不渲染断言在 Guide.server.test.tsx（App.server.test.tsx 同款双跑拆分）。
// jsdom 定位局限：getBoundingClientRect 全 0（Popover.tsx:32 先例）——spotlight 挖洞矩形断言用伪造矩形，
// 真实定位正确性归 AC-01 浏览器自动化走查。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useState } from 'react'
import App from '../App'
import { Guide } from './Guide'
import { GUIDE_SEEN_KEY, GUIDE_SEEN_V1_KEY, GUIDE_STEPS, guideAnchorSelector, isGuideSeen, markGuideSeen } from '../lib/guide'
import type { TabId } from '../state/types'
import zhGuide from '../i18n/locales/zh-CN/guide.json'
import enGuide from '../i18n/locales/en/guide.json'

configure({ asyncUtilTimeout: 8000 })

// ---- 「✅ 文案定稿 v1」（#280）逐字底稿（定稿 json 字段镜像；zh 定稿比对 + zh≤60 字 + en 对称断言依据） ----
const FINALIZED: readonly {
  key: string
  titleZh: string
  bodyZh: string
  btnZh: string
  titleEn: string
  bodyEn: string
  btnEn: string
}[] = [
  { key: 'tour.workbench.new', titleZh: '一切从模板开始', bodyZh: '新建、导入、搜索、分组都在工作台；点击「新建模板」即可设计第一张标签。', btnZh: '下一步', titleEn: 'It All Starts with a Template', bodyEn: 'Create, import, search and group templates, all in the Workbench; click "New Template" to design your first label.', btnEn: 'Next' },
  { key: 'tour.designer.new', titleZh: '标签长什么样，这里定', bodyZh: '把文本、条码、二维码、矩形拖进画布拼出标签；宽 / 高在顶部工具栏设定，打印前可预览。', btnZh: '下一步', titleEn: 'Shape Your Label Here', bodyEn: 'Drag text, barcode, QR code and rectangle onto the canvas; set W / H in the top toolbar and preview before printing.', btnEn: 'Next' },
  { key: 'tour.dataprint.select', titleZh: '填数据，出标签', bodyZh: '「数据与打印」按模板字段生成填写表单：选模板、填数据、打印测试；暂无模板时先回工作台新建。', btnZh: '下一步', titleEn: 'Fill In, Print Out', bodyEn: 'Data & Print turns template fields into a form: pick one, fill in the data, print a test; if none, create one in the Workbench.', btnEn: 'Next' },
  { key: 'tour.dataprint.excel', titleZh: '一张表格打一批', bodyZh: '「下载 Excel 模板」按模板字段生成表格，一行填一条；导入时确认每列对应的字段。', btnZh: '下一步', titleEn: 'One Sheet, Many Labels', bodyEn: 'Click "Download Excel Template" to get a sheet from template fields, one row per label; confirm each column\'s field on import.', btnEn: 'Next' },
  { key: 'tour.workbench.nav', titleZh: '随时回来', bodyZh: '左侧导航切换页面，更多说明见「帮助」；要重看本引导，点击底部状态栏「使用引导」。', btnZh: '完成', titleEn: 'Come Back Anytime', bodyEn: 'Use the left navigation to switch pages and see "Help" for more; replay this tour from the bottom status bar.', btnEn: 'Done' },
]

/** 按点路径取嵌套 JSON 词条（不存在返回 undefined）。 */
function resolve(pack: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>(
    (node, seg) => (node !== null && typeof node === 'object' ? (node as Record<string, unknown>)[seg] : undefined),
    pack,
  )
}

describe('步骤定义（lib/guide.ts 纯数据，#276 方案 v1 步骤清单）', () => {
  it('5 步核心链：顺序 = 工作台 → 设计器 → 数据与打印（模板选择）→ Excel 导入 → 收尾（工作台导航）', () => {
    expect(GUIDE_STEPS.map((s) => s.tab)).toEqual(['workbench', 'designer', 'data', 'data', 'workbench'])
    expect(GUIDE_STEPS.map((s) => s.anchor)).toEqual([
      'workbench-new',
      'designer-empty-new',
      'dataprint-template-select',
      'dataprint-excel-import',
      'nav-main',
    ])
  })

  it('步骤 i18n key 与「文案定稿 v1」五条一一对应（同序）', () => {
    expect(GUIDE_STEPS.map((s) => s.key)).toEqual(FINALIZED.map((s) => s.key))
  })

  it('锚点选择器 = data-guide 语义属性查询', () => {
    expect(guideAnchorSelector(GUIDE_STEPS[0])).toBe('[data-guide="workbench-new"]')
  })
})

describe('文案资源（AC-04：定稿逐字 + zh≤60 字 + 双语真实存在）', () => {
  it('zh 标题 / 正文与「✅ 文案定稿 v1」（#280 v2）逐字一致，单步正文 ≤ 60 字（拍板 10 放宽）', () => {
    for (const step of FINALIZED) {
      expect(resolve(zhGuide, `${step.key}.title`)).toBe(step.titleZh)
      const body = resolve(zhGuide, `${step.key}.body`)
      expect(body).toBe(step.bodyZh)
      expect((body as string).length).toBeLessThanOrEqual(60)
    }
  })

  it('en 标题 / 正文 / 步骤按钮与「✅ 文案定稿 v1」逐字一致（#278 AC-02：zh/en 对称防线）', () => {
    FINALIZED.forEach((step, i) => {
      expect(resolve(enGuide, `${step.key}.title`)).toBe(step.titleEn)
      expect(resolve(enGuide, `${step.key}.body`)).toBe(step.bodyEn)
      // 步骤主按钮为共享词条：前四步 btn.next（下一步 / Next）、末步 btn.done（完成 / Done）
      const btnKey = i === FINALIZED.length - 1 ? 'btn.done' : 'btn.next'
      expect(resolve(zhGuide, btnKey)).toBe(step.btnZh)
      expect(resolve(enGuide, btnKey)).toBe(step.btnEn)
    })
  })

  it('步骤引用的 key 在 en 语言包真实存在（标题 / 正文非空字符串）', () => {
    for (const step of FINALIZED) {
      for (const suffix of ['title', 'body']) {
        const value = resolve(enGuide, `${step.key}.${suffix}`)
        expect(typeof value).toBe('string')
        expect(value as string).toBeTruthy()
      }
    }
  })

  it('气泡按钮与「使用引导」入口词条 zh/en 均非空（btn.* / entry.title）', () => {
    for (const key of ['btn.next', 'btn.prev', 'btn.skip', 'btn.done', 'entry.title']) {
      expect(resolve(zhGuide, key)).toBeTruthy()
      expect(resolve(enGuide, key)).toBeTruthy()
    }
  })
})

describe('首见标记（localStorage，决策 #170 ③④）', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('无标记 = 未看过；写入后已看（键 labelframe.guide.client.v2 值 done）且重复写入幂等', () => {
    expect(isGuideSeen()).toBe(false)
    markGuideSeen()
    expect(isGuideSeen()).toBe(true)
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
    markGuideSeen()
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
  })

  it('键升 v2（#280 决议 3）：仅持有 v1 遗留键的存量已看用户判定为未看过（自动重放一次的判定基座）；v1 键不迁移不删除', () => {
    window.localStorage.setItem(GUIDE_SEEN_V1_KEY, 'done')
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBeNull()
    expect(isGuideSeen()).toBe(false)
    markGuideSeen()
    expect(window.localStorage.getItem(GUIDE_SEEN_V1_KEY)).toBe('done')
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
    expect(isGuideSeen()).toBe(true)
  })
})

describe('Guide 组件行为', () => {
  const onFinish = vi.fn()

  /** 挂引导组件的最小壳：tab 状态随 onSwitchTab 演进（等价 App 的 switchTab 直通）。 */
  function Harness() {
    const [tab, setTab] = useState<TabId>('workbench')
    return <Guide open tab={tab} onSwitchTab={setTab} onFinish={onFinish} />
  }

  /** 手工注入五步锚点元素（脱离 App 的组件级测试；jsdom 矩形全 0 走居中降级形态）。 */
  function installAnchors() {
    for (const step of GUIDE_STEPS) {
      const el = document.createElement('button')
      el.setAttribute('data-guide', step.anchor)
      document.body.appendChild(el)
    }
  }

  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    installAnchors()
  })

  afterEach(() => {
    cleanup()
    document.querySelectorAll('[data-guide]').forEach((el) => el.remove())
  })

  it('首步渲染：定稿标题 / 正文、跳过与下一步按钮齐全；首步无上一步', async () => {
    render(<Harness />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    expect(screen.getByText('新建、导入、搜索、分组都在工作台；点击「新建模板」即可设计第一张标签。')).toBeTruthy()
    expect(screen.getByRole('button', { name: '跳过' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '下一步' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '上一步' })).toBeNull()
  })

  it('spotlight：锚点矩形可用时渲染挖洞（外扩 6px）；jsdom 零矩形降级为全屏暗幕', async () => {
    // a) 零矩形（jsdom 默认）：无挖洞、有暗幕（Popover.tsx:32 同款测试局限）
    render(<Harness />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    await waitFor(() => expect(document.querySelector('.guide-veil')).toBeTruthy())
    expect(document.querySelector('.guide-spot')).toBeNull()
    cleanup()

    // b) 伪造矩形 100,100,120×40：挖洞外扩 SPOT_PAD=6 → 94,94,132×52
    const anchor = document.querySelector('[data-guide="workbench-new"]') as HTMLElement
    anchor.getBoundingClientRect = () =>
      ({ x: 100, y: 100, width: 120, height: 40, top: 100, left: 100, right: 220, bottom: 140, toJSON: () => ({}) }) as unknown as DOMRect
    render(<Harness />)
    const spot = (await waitFor(() => document.querySelector('.guide-spot') as HTMLElement)) as HTMLElement
    expect(spot.style.left).toBe('94px')
    expect(spot.style.top).toBe('94px')
    expect(spot.style.width).toBe('132px')
    expect(spot.style.height).toBe('52px')
    expect(document.querySelector('.guide-veil')).toBeNull()
  })

  it('下一步推进（经 onSwitchTab 切页）并显示对应步骤；上一步回退', async () => {
    render(<Harness />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('标签长什么样，这里定')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '上一步' }))
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
  })

  it('末步按钮变「完成」，点击触发 onFinish', async () => {
    render(<Harness />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    for (let i = 0; i < 4; i++) fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('随时回来')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '完成' }))
    expect(onFinish).toHaveBeenCalledTimes(1)
  })

  it('跳过触发 onFinish', async () => {
    render(<Harness />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '跳过' }))
    expect(onFinish).toHaveBeenCalledTimes(1)
  })

  it('Esc = 跳过本轮（触发 onFinish，决策 #170 ⑨）', async () => {
    render(<Harness />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onFinish).toHaveBeenCalledTimes(1)
  })
})

// ---- client 构建集成（App 壳）——mock 口径与 App.client.test.tsx 一致 ----
const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    saveTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    importExcel: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getLogs: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    saveTemplate: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
    importExcel: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    getJobs: vi.fn(),
    listDevices: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getLogs: vi.fn(),
    getTransport: vi.fn(),
    setTransport: vi.fn(),
    testTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    testPrinter: vi.fn(),
    getHostConfig: vi.fn(),
    setHostConfig: vi.fn(),
  },
  probeHealthz: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
}))

vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

describe('client 构建：首见自动启动与退出路径（AC-01/02）', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    window.sessionStorage.clear()
    mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
    mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
    mocks.local.getHostConfig.mockResolvedValue({
      serverUrl: 'http://127.0.0.1:53961',
      deviceId: 'PC-1',
      deviceName: 'PC-1',
      ips: [],
    })
    mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
    mocks.local.listTemplates.mockResolvedValue([])
    mocks.server.listTemplates.mockResolvedValue([])
    // 引导第 3 步切到「数据与打印」页——probeRoute 拉设备列表（App.client.test 未挂载该页故无此默认）
    mocks.server.listDevices.mockResolvedValue([])
    mocks.local.listDevices.mockResolvedValue([])
  })

  afterEach(() => {
    cleanup()
  })

  it('首见（无标记）挂载后自动启动引导；点「跳过」写入首见标记并关闭', async () => {
    render(<App />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    expect(document.querySelector('.guide-layer')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '跳过' }))
    await waitFor(() => expect(document.querySelector('.guide-layer')).toBeNull())
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
  })

  it('已完成：重挂载（等价刷新 / 重启）不再自动出现', async () => {
    markGuideSeen()
    render(<App />)
    // 等过首见自动启动窗口（300ms）确认未出现——非异步时序假绿
    await new Promise((resolve) => setTimeout(resolve, 500))
    expect(document.querySelector('.guide-layer')).toBeNull()
    expect(screen.queryByText('一切从模板开始')).toBeNull()
  })

  it('键升 v2（AC-04，#280 决议 3）：仅持 v1 键的存量已看用户再次首见自动重放一次，完成后写 v2 不再自动出现', async () => {
    window.localStorage.setItem(GUIDE_SEEN_V1_KEY, 'done')
    render(<App />)
    // v1 已看判定为未看过 → 延迟自动启动 v2 文案导览
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '跳过' }))
    await waitFor(() => expect(document.querySelector('.guide-layer')).toBeNull())
    // 完成写 v2 键（v1 键遗留不清理）；再次重挂载不再自动出现
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
    expect(window.localStorage.getItem(GUIDE_SEEN_V1_KEY)).toBe('done')
  })

  it('完成路径：逐步走完 5 步（经 switchTab 切页，锚点页面真实渲染）写首见标记', async () => {
    render(<App />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('标签长什么样，这里定')).toBeTruthy()
    expect(document.querySelector('[data-guide="designer-empty-new"]')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('填数据，出标签')).toBeTruthy()
    expect(document.querySelector('[data-guide="dataprint-template-select"]')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('一张表格打一批')).toBeTruthy()
    expect(document.querySelector('[data-guide="dataprint-excel-import"]')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('随时回来')).toBeTruthy()
    expect(document.querySelector('[data-guide="nav-main"]')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '完成' }))
    await waitFor(() => expect(document.querySelector('.guide-layer')).toBeNull())
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
  })

  it('Esc = 跳过并写标记（决策 #170 ⑨）', async () => {
    render(<App />)
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(document.querySelector('.guide-layer')).toBeNull())
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
  })
})

describe('client 构建：「使用引导」重看入口（AC-03）', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    window.sessionStorage.clear()
    mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
    mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', ips: [] })
    mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
    mocks.local.listTemplates.mockResolvedValue([])
    mocks.server.listTemplates.mockResolvedValue([])
    mocks.server.listDevices.mockResolvedValue([])
    mocks.local.listDevices.mockResolvedValue([])
  })

  afterEach(() => {
    cleanup()
  })

  it('已看过用户点状态栏「使用引导」再放完整一轮；重放完成不改变已看状态', async () => {
    markGuideSeen()
    render(<App />)
    await new Promise((resolve) => setTimeout(resolve, 500))
    expect(document.querySelector('.guide-layer')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '使用引导' }))
    expect(await screen.findByText('一切从模板开始')).toBeTruthy()
    for (let i = 0; i < 4; i++) fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('随时回来')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '完成' }))
    await waitFor(() => expect(document.querySelector('.guide-layer')).toBeNull())
    expect(window.localStorage.getItem(GUIDE_SEEN_KEY)).toBe('done')
  })

  it('状态栏「使用引导」入口常驻（aria-label 与 title 均为「使用引导」）', async () => {
    render(<App />)
    const btn = (await screen.findByRole('button', { name: '使用引导' })) as HTMLButtonElement
    expect(btn.getAttribute('title')).toBe('使用引导')
  })
})
