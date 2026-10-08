// @vitest-environment jsdom
// 分界面交互演示引擎测试（迭代 121 · #280）：
// - 启动即创建「示例·基础标签」样例（biz.saveTemplate）并登记残留名单（决议 1 + 拍板 3/9）；
// - 步骤推进：「下一步」手动兑底 + waitFor 真实操作自动推进（data-demo 观测钩子 / Shell tab 到达）；
//   跨页步骤经 onSwitchTab 切页（拍板 4 跨页跟随）；
// - 样例生命周期三路径（AC-02/03）：完成默认清理（deleteTemplate + 清登记）/ 勾选保留（只清登记）/
//   跳过·Esc 中途退出（不清理、残留保留——下次进入询问）。
// jsdom 定位局限（getBoundingClientRect 全 0）：spotlight 断言归 Guide.test.tsx 先例，本文件聚焦状态机
// 与生命周期；真实定位与全链演示归 AC-01~03 浏览器自动化走查。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useState } from 'react'
import { AppProvider } from '../state/AppContext'
import { DemoRunner } from './DemoRunner'
import { DEMO_RESIDUE_KEY, SAMPLE_TEMPLATE_NAME, clearDemoResidue, getDemoResidue, registerDemoResidue } from '../lib/helpDemo'
import type { DemoId } from '../lib/helpDemo'
import type { TabId } from '../state/types'

configure({ asyncUtilTimeout: 8000 })

// ---- mock 口径与 Guide.test.tsx 一致（AppProvider 启动链不发真请求） ----
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

/** 外控 tab 注入口（验证跨页 waitFor：等价 App Shell 把 tab 切到别处的真实形态）。 */
let externalSetTab: ((t: TabId) => void) | null = null
const onFinish = vi.fn()

/** 最小壳：tab 状态随 onSwitchTab / 外控口演进（等价 App 的 switchTab 直通，无离开守卫）。 */
function Harness({ demoId }: { demoId: DemoId }) {
  const [tab, setTab] = useState<TabId>(demoId === 'designer' ? 'designer' : 'workbench')
  externalSetTab = setTab
  return (
    <AppProvider>
      <DemoRunner demoId={demoId} tab={tab} onSwitchTab={setTab} onFinish={onFinish} />
    </AppProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  externalSetTab = null
  window.localStorage.clear()
  window.sessionStorage.clear()
  // AppProvider 启动链（与 Guide.test.tsx 同款默认值）；服务端探测拒绝 → serverMode='standalone'——
  // biz 恒 localApi，样例生命周期断言不随探测时序漂移（探测异步解析会中途切换 biz 引用）
  mocks.server.healthz.mockRejectedValue(new Error('standalone test'))
  mocks.local.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({
    serverUrl: 'http://127.0.0.1:53961',
    deviceId: 'PC-1',
    deviceName: 'PC-1',
    ips: [],
  })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  // 演示生命周期关注点：样例创建 / 删除 / 模板库核对
  mocks.local.saveTemplate.mockResolvedValue(undefined)
  mocks.local.deleteTemplate.mockResolvedValue(undefined)
  mocks.local.listTemplates.mockResolvedValue([])
  mocks.server.saveTemplate.mockResolvedValue(undefined)
  mocks.server.deleteTemplate.mockResolvedValue(undefined)
  mocks.server.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
  clearDemoResidue()
})

/** 点「下一步」推进 n 步（手动兑底通道）。 */
async function clickNext(n: number) {
  for (let i = 0; i < n; i++) {
    fireEvent.click(await screen.findByRole('button', { name: '下一步' }))
  }
}

describe('designer 演示（demoId=designer）', () => {
  it('启动即创建样例（saveTemplate 携带固定名与「示例」分组）并登记残留；首步渲染 intro 定稿标题', async () => {
    render(<Harness demoId="designer" />)
    expect(await screen.findByText('亲手做一个标签')).toBeTruthy()
    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalled())
    expect(mocks.local.saveTemplate.mock.calls[0][0]).toMatchObject({ name: SAMPLE_TEMPLATE_NAME, group: '示例' })
    expect(getDemoResidue()).toMatchObject({ demoId: 'designer', names: [SAMPLE_TEMPLATE_NAME] })
    // 跳过按钮常在（拍板 6：跳过 / Esc 退出通道）
    expect(screen.getByRole('button', { name: '跳过' })).toBeTruthy()
  })

  it('「下一步」手动兑底推进（intro → draw）', async () => {
    render(<Harness demoId="designer" />)
    expect(await screen.findByText('亲手做一个标签')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('拖控件，拼版式')).toBeTruthy()
  })

  it('waitFor 真实操作自动推进（拍板 6）：画布元素计数 ≥1 即越过「拖控件」步', async () => {
    render(<Harness demoId="designer" />)
    expect(await screen.findByText('亲手做一个标签')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('拖控件，拼版式')).toBeTruthy()
    // 模拟真实操作达成：画布容器观测钩子计数置 1（CanvasViewport data-demo-count 的真实形态）
    const canvas = document.createElement('div')
    canvas.setAttribute('data-demo', 'designer-canvas')
    canvas.setAttribute('data-demo-count', '1')
    document.body.appendChild(canvas)
    expect(await screen.findByText('调整元素属性')).toBeTruthy()
    canvas.remove()
  })

  it('waitFor tab 到达（拍板 4 跨页跟随）：「命名并保存」步切工作台后自动进入数据页步并驱动切页', async () => {
    render(<Harness demoId="designer" />)
    expect(await screen.findByText('亲手做一个标签')).toBeTruthy()
    // 手动兑底推进到「命名并保存」（第 5 步；途中 fields 步的打印字段等待同样可被手动越过）
    await clickNext(4)
    expect(await screen.findByText('命名并保存')).toBeTruthy()
    // 模拟保存成功自动回工作台（Designer 普通保存 onClose 的既有行为）
    externalSetTab?.('workbench')
    // waitFor 达成自动推进 → data.form 步驱动 onSwitchTab('data') → Harness 切页
    expect(await screen.findByText('字段变成了表单')).toBeTruthy()
  })

  it('收尾默认清理（决议 1）：完成 → deleteTemplate（样例名）+ 清除残留 + onFinish', async () => {
    registerDemoResidue('designer', [SAMPLE_TEMPLATE_NAME])
    render(<Harness demoId="designer" />)
    await clickNext(7)
    expect(await screen.findByText('演示完成')).toBeTruthy()
    // 收尾勾选词（定稿 outro 正文引用原串）默认不勾选
    const keep = screen.getByRole('checkbox') as HTMLInputElement
    expect(keep.checked).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: '完成' }))
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1))
    expect(mocks.local.deleteTemplate).toHaveBeenCalledWith(SAMPLE_TEMPLATE_NAME)
    expect(getDemoResidue()).toBeNull()
  })

  it('勾选「保留样例」（决议 1）：完成 → 不删除样例、只清残留登记', async () => {
    registerDemoResidue('designer', [SAMPLE_TEMPLATE_NAME])
    render(<Harness demoId="designer" />)
    await clickNext(7)
    expect(await screen.findByText('演示完成')).toBeTruthy()
    fireEvent.click(screen.getByRole('checkbox'))
    fireEvent.click(screen.getByRole('button', { name: '完成' }))
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1))
    expect(mocks.local.deleteTemplate).not.toHaveBeenCalled()
    expect(getDemoResidue()).toBeNull()
  })

  it('跳过中途退出（决议 1）：不清理、残留保留、onFinish 触发', async () => {
    render(<Harness demoId="designer" />)
    expect(await screen.findByText('亲手做一个标签')).toBeTruthy()
    await waitFor(() => expect(getDemoResidue()).toMatchObject({ names: [SAMPLE_TEMPLATE_NAME] }))
    fireEvent.click(screen.getByRole('button', { name: '跳过' }))
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1))
    expect(mocks.local.deleteTemplate).not.toHaveBeenCalled()
    expect(getDemoResidue()).toMatchObject({ demoId: 'designer', names: [SAMPLE_TEMPLATE_NAME] })
  })

  it('Esc = 中途退出（同跳过语义，不即时清理）', async () => {
    render(<Harness demoId="designer" />)
    expect(await screen.findByText('亲手做一个标签')).toBeTruthy()
    await waitFor(() => expect(getDemoResidue()).toMatchObject({ names: [SAMPLE_TEMPLATE_NAME] }))
    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1))
    expect(mocks.local.deleteTemplate).not.toHaveBeenCalled()
    expect(window.localStorage.getItem(DEMO_RESIDUE_KEY)).not.toBeNull()
  })
})

describe('workbench 演示（demoId=workbench）', () => {
  it('首步渲染 intro 定稿标题；「下一步」兑底推进到导出步（跨页 waitFor 语义已在 designer 场验证）', async () => {
    render(<Harness demoId="workbench" />)
    expect(await screen.findByText('新建、导出、导入')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('新建一张模板')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '下一步' }))
    expect(await screen.findByText('把模板导出成文件')).toBeTruthy()
  })

  it('清理失败但模板库已无样例 → 视为已清理（核对口径），残留清除', async () => {
    registerDemoResidue('workbench', [SAMPLE_TEMPLATE_NAME])
    mocks.local.deleteTemplate.mockRejectedValue(new Error('gone'))
    mocks.local.listTemplates.mockResolvedValue([])
    render(<Harness demoId="workbench" />)
    await clickNext(5)
    expect(await screen.findByText('演示完成')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '完成' }))
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1))
    expect(getDemoResidue()).toBeNull()
  })
})
