// @vitest-environment jsdom
// 迭代 91（F-13 · #149）：Designer 加载 effect 与 Workbench 的 serverMode 守卫对齐——
// unknown 阶段（模式探测未完成）不以 localApi 误发模板详情请求；模式解析后按正确 base 正常加载（AC-04）。
// 画布为 react-konva 重组件（jsdom 无 2d context），以桩替换——本文件只验证加载链路守卫，不测画布交互。
// 迭代 92（F-02/F-03 · #150）：补页级保存链路（新建保存 / 同名覆盖确认 / 保存失败）与
// 未保存离开保护三选路径（AC-01~03，dirty = 历史栈有已提交更改，决议 a）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ApiError } from '../lib/api/types'
import type { TemplatePackage } from '../lib/api/types'
import { AppProvider, useApp } from '../state/AppContext'
import { Designer } from './Designer'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    getTemplate: vi.fn(),
    listTemplates: vi.fn(),
    saveTemplate: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    getTemplate: vi.fn(),
    listTemplates: vi.fn(),
    saveTemplate: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: vi.fn(async () => false),
}))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))
// 画布桩：本文件只验证加载链路（serverMode 守卫），konva 画布由专门交互测试与真机验收覆盖
vi.mock('./designer/CanvasViewport', () => ({
  CanvasViewport: () => null,
}))

const PKG: TemplatePackage = {
  name: '库位标签',
  group: '默认',
  contract: { name: 'contract-1', version: '1', fields: [] },
  layout: { name: 'layout-1', contractName: 'contract-1', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
  testData: {},
}

function renderDesigner(
  name?: string,
  opts: { onClose?: () => void; registerLeaveGuard?: (fn: ((leave: () => void) => void) | null) => void } = {},
) {
  return render(
    <AppProvider>
      {/* 状态栏消息探针：保存 / 守卫错误经 app.setStatus 呈现（真实界面在 Shell 状态栏，测试树内以探针捕获） */}
      <StatusProbe />
      <Designer
        request={name ? { kind: 'edit', name } : { kind: 'new' }}
        onClose={opts.onClose ?? (() => {})}
        registerLeaveGuard={opts.registerLeaveGuard}
      />
    </AppProvider>,
  )
}

function StatusProbe() {
  const app = useApp()
  return <div data-testid="status-msg">{app.statusMsg}</div>
}

/** healthz 挂起（可控解析）——保持 serverMode 停留 'unknown'，用于断言 unknown 阶段行为。 */
function pendingHealthz(): () => void {
  let resolve!: () => void
  mocks.server.healthz.mockImplementation(() => new Promise<void>((res) => (resolve = res)))
  return () => resolve()
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
})

afterEach(() => {
  cleanup()
})

describe('serverMode 守卫对齐 Workbench（迭代 91 F-13）', () => {
  it('AC-04：unknown 阶段（healthz 在途）不发模板详情请求；解析为 server 后以 serverApi 加载', async () => {
    const resolveHealthz = pendingHealthz()
    mocks.server.getTemplate.mockResolvedValue(PKG)
    renderDesigner('库位标签')

    // unknown 阶段：既不以 localApi 误发，也不以 serverApi 发（模式未定不发请求）
    await waitFor(() => expect(mocks.local.getHostConfig).toHaveBeenCalled())
    expect(mocks.server.getTemplate).not.toHaveBeenCalled()
    expect(mocks.local.getTemplate).not.toHaveBeenCalled()

    // 模式解析为 server → 以 serverApi 加载模板详情
    resolveHealthz()
    await waitFor(() => expect(mocks.server.getTemplate).toHaveBeenCalledWith('库位标签'))
    expect(mocks.local.getTemplate).not.toHaveBeenCalled()
    // 加载完成：顶栏模板名填充
    await waitFor(() => expect((screen.getByDisplayValue('库位标签') as HTMLInputElement).value).toBe('库位标签'), { timeout: 3000 })
  })

  it('AC-04 回退：解析为 standalone（healthz 失败 / 服务端不可达）后以 localApi 加载（单机降级）', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('unreachable'))
    mocks.local.getTemplate.mockResolvedValue(PKG)
    renderDesigner('库位标签')

    await waitFor(() => expect(mocks.local.getTemplate).toHaveBeenCalledWith('库位标签'), { timeout: 3000 })
    expect(mocks.server.getTemplate).not.toHaveBeenCalled()
    await waitFor(() => expect((screen.getByDisplayValue('库位标签') as HTMLInputElement).value).toBe('库位标签'), { timeout: 3000 })
  })

  it('新建模板不依赖模式解析：unknown 阶段即可用，且不发任何模板详情请求', async () => {
    pendingHealthz()
    renderDesigner()

    await act(async () => {
      await Promise.resolve()
    })
    // 新建路径立即初始化（分组默认值可见），本地零请求
    expect(screen.getByDisplayValue('默认')).toBeTruthy()
    expect(mocks.server.getTemplate).not.toHaveBeenCalled()
    expect(mocks.local.getTemplate).not.toHaveBeenCalled()
  })

  it('加载失败呈现中文原因（ApiError message），与 Workbench 同一错误通道', async () => {
    mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
    mocks.server.getTemplate.mockRejectedValue(new ApiError('TEMPLATE_NOT_FOUND', '模板「不存在的模板」不存在。'))
    renderDesigner('不存在的模板')
    await waitFor(() => expect(screen.getByText('模板「不存在的模板」不存在。')).toBeTruthy(), { timeout: 3000 })
  })
})

// ---------- 迭代 92（#150 F-03）：页级保存链路 + 未保存离开保护（单机模式，biz = localApi） ----------

/** 单机模式默认桩：服务端不可达 → serverMode=standalone；本机 API 就绪（沿用 DataPrint.test.tsx 范式）。 */
function useStandaloneStubs() {
  beforeEach(() => {
    mocks.server.healthz.mockRejectedValue(new Error('unreachable'))
    mocks.local.getTemplate.mockResolvedValue(PKG)
    mocks.local.listTemplates.mockResolvedValue([])
    mocks.local.saveTemplate.mockResolvedValue(undefined)
  })
}

/** 等新建初始化完成（宽 100mm 输入出现 = state 就绪，保存按钮可用）。 */
async function waitForNewReady() {
  await screen.findByDisplayValue('100', undefined, { timeout: 3000 })
}

/** 等编辑模式加载完成（模板名 + 宽 70mm 输入出现）。 */
async function waitForEditLoaded() {
  await screen.findByDisplayValue('库位标签', undefined, { timeout: 3000 })
  await screen.findByDisplayValue('70', undefined, { timeout: 3000 })
}

/** 提交一步历史更改（改纸张宽）——决议 a：undoCount > 0 即 dirty。 */
function makeDirty() {
  fireEvent.change(screen.getByDisplayValue('70'), { target: { value: '80' } })
  expect(screen.getByDisplayValue('80')).toBeTruthy()
}

describe('页级保存链路（迭代 92 F-03）', () => {
  useStandaloneStubs()

  it('新建保存成功：查重通过后以最终名保存（契约名 = 模板名），提示并回工作台', async () => {
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '新模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    const pkg = mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage
    expect(pkg.name).toBe('新模板')
    expect(pkg.group).toBe('默认')
    // 新建路径：契约名 / 版本由最终名派生（toContract / toLayout 组装）
    expect(pkg.contract?.name).toBe('新模板')
    expect(pkg.contract?.version).toBe('1')
    expect(pkg.layout?.widthMm).toBe(100)
    // 既有行为零回归：保存成功回工作台 + 状态栏提示
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(screen.getByTestId('status-msg').textContent).toContain('模板「新模板」已保存')
  })

  it('同名覆盖确认 → 取消：不保存、停留设计器', async () => {
    mocks.local.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-09-18' }])
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '库位标签' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    expect(await screen.findByText('模板已存在', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    await waitFor(() => expect(screen.queryByText('模板已存在')).toBeNull())
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('同名覆盖确认 → 确认：覆盖保存并回工作台', async () => {
    mocks.local.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-09-18' }])
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '库位标签' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    expect(await screen.findByText('模板已存在', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '覆盖保存' }))

    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    expect((mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage).name).toBe('库位标签')
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
  })

  it('保存失败提示：呈现中文原因，停留设计器不回工作台', async () => {
    mocks.local.saveTemplate.mockRejectedValueOnce(new ApiError('LF_TPL_SAVE_FAILED', '模板保存失败：名称重复。'))
    const onClose = vi.fn()
    renderDesigner(undefined, { onClose })
    await waitForNewReady()
    fireEvent.change(screen.getByPlaceholderText('模板名称'), { target: { value: '新模板' } })
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))

    await waitFor(() => expect(screen.getByTestId('status-msg').textContent).toContain('模板保存失败：名称重复'), { timeout: 3000 })
    expect(onClose).not.toHaveBeenCalled()
    // 按钮复位（非「保存中…」），可重试
    expect(screen.getByRole('button', { name: '保存模板' })).toBeTruthy()
  })
})

describe('未保存离开保护三选（迭代 92 F-02，决议 a：dirty = 历史栈有已提交更改）', () => {
  useStandaloneStubs()

  it('AC-01「继续编辑」：dirty 后点返回弹三选，选择后留在设计器（不保存不离开）', async () => {
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    expect(onClose).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '继续编辑' }))
    await waitFor(() => expect(screen.queryByText('未保存的更改')).toBeNull())
    expect(onClose).not.toHaveBeenCalled()
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
    // 编辑未丢：宽仍为改后的 80
    expect(screen.getByDisplayValue('80')).toBeTruthy()
  })

  it('AC-01「放弃更改」：不保存直接回工作台', async () => {
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '放弃更改' }))

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
  })

  it('AC-01「保存并离开」：走既有保存链路，成功后离开（保存成功自然复位 dirty——组件随离开卸载）', async () => {
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '保存并离开' }))

    await waitFor(() => expect(mocks.local.saveTemplate).toHaveBeenCalledTimes(1))
    expect((mocks.local.saveTemplate.mock.calls[0][0] as TemplatePackage).name).toBe('库位标签')
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
    expect(screen.getByTestId('status-msg').textContent).toContain('模板「库位标签」已保存')
  })

  it('AC-01 导航切 tab 路径：经 registerLeaveGuard 注册的守卫挂起切换，「放弃更改」后执行挂起的切换', async () => {
    let guard: ((leave: () => void) => void) | null = null
    renderDesigner('库位标签', { registerLeaveGuard: (fn) => (guard = fn) })
    await waitForEditLoaded()
    makeDirty()
    expect(guard).toBeTruthy()

    // 模拟 Shell switchTab：把挂起的 setTab 交给守卫
    const pendingSwitch = vi.fn()
    act(() => guard!(pendingSwitch))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    expect(pendingSwitch).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '放弃更改' }))
    await waitFor(() => expect(pendingSwitch).toHaveBeenCalledTimes(1))
    expect(mocks.local.saveTemplate).not.toHaveBeenCalled()
  })

  it('AC-02 无编辑（无已提交更改）点返回：直接离开不弹窗（编辑已加载 / 新建两路径）', async () => {
    const onClose = vi.fn()
    const view = renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('未保存的更改')).toBeNull()
    view.unmount()

    const onClose2 = vi.fn()
    renderDesigner(undefined, { onClose: onClose2 })
    await waitForNewReady()
    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(onClose2).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('未保存的更改')).toBeNull()
  })

  it('AC-03「保存并离开」遇保存失败：停留设计器显示错误、不丢编辑；挂起动作作废后普通保存仍可正常离开', async () => {
    mocks.local.saveTemplate.mockRejectedValueOnce(new ApiError('LF_TPL_SAVE_FAILED', '模板保存失败：服务端写入失败。'))
    const onClose = vi.fn()
    renderDesigner('库位标签', { onClose })
    await waitForEditLoaded()
    makeDirty()

    fireEvent.click(screen.getByTitle('返回工作台'))
    expect(await screen.findByText('未保存的更改', undefined, { timeout: 3000 })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '保存并离开' }))

    await waitFor(() => expect(screen.getByTestId('status-msg').textContent).toContain('模板保存失败：服务端写入失败'), { timeout: 3000 })
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.queryByText('未保存的更改')).toBeNull()
    // 不丢编辑：宽仍为 80，重试普通保存成功后按既有行为回工作台
    expect(screen.getByDisplayValue('80')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '保存模板' }))
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1))
  })
})
