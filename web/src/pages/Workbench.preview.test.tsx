// @vitest-environment jsdom
// 迭代 46：工作台模板悬停预览——防抖触发（AC-01/05）、会话缓存命中不重复请求（AC-02）、
// 列表刷新后缓存失效并释放 blob URL（AC-03）、失败态优雅降级（AC-04）、双模式（服务端 / 单机降级）。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链）；URL.createObjectURL / revokeObjectURL 为 jsdom 缺失项，手动打桩。

import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import type { TemplateSummary } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { Workbench } from './Workbench'

const mocks = vi.hoisted(() => {
  let seq = 0
  return {
    server: {
      healthz: vi.fn(),
      listTemplates: vi.fn(),
      deleteTemplate: vi.fn(),
      exportTemplate: vi.fn(),
      importTemplate: vi.fn(),
      previewTemplate: vi.fn(),
    },
    local: {
      healthz: vi.fn(),
      listTemplates: vi.fn(),
      getHostConfig: vi.fn(),
      getTransport: vi.fn(),
      previewTemplate: vi.fn(),
    },
    createObjectURL: vi.fn(() => `blob:preview-${++seq}`),
    revokeObjectURL: vi.fn(),
    resetBlobSeq: () => {
      seq = 0
    },
  }
})

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

const TEMPLATES: TemplateSummary[] = [
  { name: 'Carton-Label-A', group: '华东仓', updatedAt: '2026-09-01T10:00:00Z' },
  { name: 'carton-label-b', group: '华东仓', updatedAt: '2026-09-02T10:00:00Z' },
  { name: 'Shelf-Tag', group: '华南仓', updatedAt: '2026-09-03T10:00:00Z' },
]

const png = () => new Blob(['png-bytes'], { type: 'image/png' })

function renderWorkbench() {
  return render(
    <AppProvider>
      <Workbench onOpenDesigner={() => {}} />
    </AppProvider>,
  )
}

function rowOf(name: string): HTMLTableRowElement {
  // 浮层标题也会显示模板名，用 td 选择器锁定列表行单元格
  return screen.getByText(name, { selector: 'td' }).closest('tr') as HTMLTableRowElement
}

const hover = (name: string) => fireEvent.mouseEnter(rowOf(name))
const leave = (name: string) => fireEvent.mouseLeave(rowOf(name))

/** 停满触发延迟（PREVIEW_HOVER_DELAY_MS = 400）。 */
const dwell = () =>
  act(() => {
    vi.advanceTimersByTime(400)
  })

/** 冲洗微任务链（探测 → 列表加载 → 预览 promise 等）。 */
async function flush() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0)
  })
}

beforeAll(() => {
  Object.defineProperty(URL, 'createObjectURL', { value: mocks.createObjectURL, configurable: true })
  Object.defineProperty(URL, 'revokeObjectURL', { value: mocks.revokeObjectURL, configurable: true })
})

beforeEach(() => {
  vi.clearAllMocks()
  vi.useFakeTimers()
  mocks.resetBlobSeq()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: 'PC' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.server.listTemplates.mockResolvedValue(TEMPLATES)
  mocks.local.listTemplates.mockResolvedValue(TEMPLATES)
  mocks.server.previewTemplate.mockResolvedValue({ blob: png(), filename: 'preview.png' })
  mocks.local.previewTemplate.mockResolvedValue({ blob: png(), filename: 'preview.png' })
})

afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

describe('工作台模板悬停预览（迭代 46）', () => {
  it('AC-01：悬停防抖后拉取 preview 并浮层显示（加载中 → 图片）；单机降级走本机 API', async () => {
    renderWorkbench()
    await flush()
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()

    let resolvePng!: (v: { blob: Blob; filename: string }) => void
    mocks.server.previewTemplate.mockImplementation(() => new Promise((res) => (resolvePng = res)))

    // 触发前 1ms 尚未请求；停满 400ms 才拉取
    hover('Carton-Label-A')
    act(() => {
      vi.advanceTimersByTime(399)
    })
    expect(mocks.server.previewTemplate).not.toHaveBeenCalled()
    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(1)
    expect(mocks.server.previewTemplate).toHaveBeenCalledWith('Carton-Label-A')
    expect(screen.getByText('正在生成预览…')).toBeTruthy()

    await act(async () => {
      resolvePng({ blob: png(), filename: 'a.png' })
    })
    await flush()
    const img = screen.getByAltText('模板「Carton-Label-A」预览') as HTMLImageElement
    expect(img.getAttribute('src')).toBe('blob:preview-1')

    // 离开行 → 浮层关闭
    leave('Carton-Label-A')
    expect(screen.queryByAltText('模板「Carton-Label-A」预览')).toBeNull()
  })

  it('AC-01：单机降级（服务端不可达）时预览走本机 WinHost API', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('offline'))
    renderWorkbench()
    await flush()
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()

    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect(mocks.local.previewTemplate).toHaveBeenCalledTimes(1)
    expect(mocks.local.previewTemplate).toHaveBeenCalledWith('Carton-Label-A')
    expect(mocks.server.previewTemplate).not.toHaveBeenCalled()
    expect((screen.getByAltText('模板「Carton-Label-A」预览') as HTMLImageElement).getAttribute('src')).toBe('blob:preview-1')
  })

  it('AC-02：同一模板再次悬停命中会话缓存，不重复请求、不重复建 blob URL', async () => {
    renderWorkbench()
    await flush()

    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(1)

    // 离开后再悬停（列表未刷新）：浮层直接显示缓存图
    leave('Carton-Label-A')
    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(1)
    expect(mocks.createObjectURL).toHaveBeenCalledTimes(1)
    expect((screen.getByAltText('模板「Carton-Label-A」预览') as HTMLImageElement).getAttribute('src')).toBe('blob:preview-1')
  })

  it('AC-03：列表刷新（删除触发重新 load）后缓存失效——释放旧 blob URL 并重新拉取', async () => {
    renderWorkbench()
    await flush()

    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect((screen.getByAltText('模板「Carton-Label-A」预览') as HTMLImageElement).getAttribute('src')).toBe('blob:preview-1')
    leave('Carton-Label-A')

    // 删除另一模板 → doDelete 成功后重新 load = 新列表周期
    mocks.server.deleteTemplate.mockResolvedValue(undefined)
    fireEvent.click(within(rowOf('Shelf-Tag')).getByText('删除'))
    fireEvent.click(screen.getByText('确认删除'))
    await flush()
    expect(mocks.server.deleteTemplate).toHaveBeenCalledWith('Shelf-Tag')
    expect(mocks.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')

    // 再悬停同一模板：重新请求、新 blob URL
    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(2)
    expect((screen.getByAltText('模板「Carton-Label-A」预览') as HTMLImageElement).getAttribute('src')).toBe('blob:preview-2')
  })

  it('AC-04：preview 请求失败——浮层占位提示，不影响列表；失败结果同周期内缓存不重复请求', async () => {
    renderWorkbench()
    await flush()

    mocks.server.previewTemplate.mockRejectedValue(new Error('请求失败（HTTP 404）。'))
    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect(screen.getByText('预览不可用')).toBeTruthy()
    expect(screen.getByText('请求失败（HTTP 404）。')).toBeTruthy()
    // 列表与其余功能不受影响
    expect(rowOf('Carton-Label-A')).toBeTruthy()
    expect(rowOf('Shelf-Tag')).toBeTruthy()

    // 失败也进会话缓存：同周期再次悬停不重复请求
    leave('Carton-Label-A')
    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(1)
    expect(screen.getByText('预览不可用')).toBeTruthy()
  })

  it('AC-05：鼠标快速扫过多行不产生请求风暴；停留足够时长才触发一次', async () => {
    renderWorkbench()
    await flush()

    hover('Carton-Label-A')
    act(() => {
      vi.advanceTimersByTime(200)
    })
    leave('Carton-Label-A')
    hover('carton-label-b')
    act(() => {
      vi.advanceTimersByTime(200)
    })
    leave('carton-label-b')
    hover('Shelf-Tag')
    act(() => {
      vi.advanceTimersByTime(200)
    })
    leave('Shelf-Tag')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000)
    })
    expect(mocks.server.previewTemplate).not.toHaveBeenCalled()

    hover('Carton-Label-A')
    await dwell()
    await flush()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(1)
    expect(mocks.server.previewTemplate).toHaveBeenCalledWith('Carton-Label-A')
  })
})
