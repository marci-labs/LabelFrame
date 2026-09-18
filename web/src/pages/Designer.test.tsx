// @vitest-environment jsdom
// 迭代 91（F-13 · #149）：Designer 加载 effect 与 Workbench 的 serverMode 守卫对齐——
// unknown 阶段（模式探测未完成）不以 localApi 误发模板详情请求；模式解析后按正确 base 正常加载（AC-04）。
// 画布为 react-konva 重组件（jsdom 无 2d context），以桩替换——本文件只验证加载链路守卫，不测画布交互。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { ApiError } from '../lib/api/types'
import type { TemplatePackage } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { Designer } from './Designer'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    getTemplate: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    getTemplate: vi.fn(),
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

function renderDesigner(name?: string) {
  return render(
    <AppProvider>
      <Designer request={name ? { kind: 'edit', name } : { kind: 'new' }} onClose={() => {}} />
    </AppProvider>,
  )
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
