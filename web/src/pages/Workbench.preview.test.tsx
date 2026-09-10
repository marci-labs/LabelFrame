// @vitest-environment jsdom
// 迭代 46（2026-09-10 修订：预览列内嵌缩略图）——列表加载后按需拉取全部预览（AC-01）、
// 会话缓存命中不重复请求（AC-02）、列表刷新后缓存失效并释放 blob URL（AC-03）、
// 失败态优雅降级（AC-04）、点击缩略图放大且放大不重复请求、遮罩 / Esc 关闭（AC-05）、双模式（服务端 / 单机降级）。
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
  // 放大浮层标题也含模板名，用 td 选择器锁定列表行单元格
  return screen.getByText(name, { selector: 'td' }).closest('tr') as HTMLTableRowElement
}

/** 缩略图（单元格内小图，与放大图以 alt 区分）。 */
const thumbOf = (name: string) => screen.getByAltText(`模板「${name}」缩略图`) as HTMLImageElement
/** 放大图（浮层内大图）。 */
const enlargedOf = (name: string) => screen.getByAltText(`模板「${name}」预览`) as HTMLImageElement

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

describe('工作台模板预览列（迭代 46 修订：内嵌缩略图）', () => {
  it('AC-01：列表加载即为全部模板拉取预览——加载中骨架 → 缩略图渲染（blob URL）', async () => {
    const pending: Array<(v: { blob: Blob; filename: string }) => void> = []
    mocks.server.previewTemplate.mockImplementation(
      () => new Promise((res) => pending.push(res)),
    )
    renderWorkbench()
    await flush()
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()

    // 全部模板立即进入加载态（骨架占位，各一次请求）
    expect(screen.getAllByTitle('正在生成预览…').length).toBe(3)
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(3)

    await act(async () => {
      for (const res of pending) res({ blob: png(), filename: 'a.png' })
    })
    await flush()
    expect(thumbOf('Carton-Label-A').getAttribute('src')).toBe('blob:preview-1')
    expect(thumbOf('carton-label-b').getAttribute('src')).toBe('blob:preview-2')
    expect(thumbOf('Shelf-Tag').getAttribute('src')).toBe('blob:preview-3')
  })

  it('AC-01：单机降级（服务端不可达）时预览走本机 WinHost API', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('offline'))
    renderWorkbench()
    await flush()
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()

    expect(mocks.local.previewTemplate).toHaveBeenCalledTimes(3)
    expect(mocks.server.previewTemplate).not.toHaveBeenCalled()
    expect(thumbOf('Carton-Label-A').getAttribute('src')).toBe('blob:preview-1')
  })

  it('AC-02：分组过滤切换（列表未刷新）命中会话缓存——不重复请求、不重复建 blob URL', async () => {
    renderWorkbench()
    await flush()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(3)
    expect(mocks.createObjectURL).toHaveBeenCalledTimes(3)

    // 切到「华东仓」：Shelf-Tag 行隐藏，其余缩略图来自缓存
    fireEvent.change(screen.getByTitle('按分组过滤'), { target: { value: '华东仓' } })
    await flush()
    expect(screen.queryByText('Shelf-Tag')).toBeNull()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(3)
    expect(mocks.createObjectURL).toHaveBeenCalledTimes(3)
    expect(thumbOf('Carton-Label-A').getAttribute('src')).toBe('blob:preview-1')

    // 切回全部分组：缓存依旧命中
    fireEvent.change(screen.getByTitle('按分组过滤'), { target: { value: '' } })
    await flush()
    expect(thumbOf('Shelf-Tag').getAttribute('src')).toBe('blob:preview-3')
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(3)
  })

  it('AC-03：列表刷新（删除触发重新 load）后缓存失效——释放全部旧 blob URL 并对剩余模板重新拉取', async () => {
    renderWorkbench()
    await flush()
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(3)

    // 删除一个模板 → 服务端列表少一项 → 重新 load = 新列表周期
    mocks.server.deleteTemplate.mockResolvedValue(undefined)
    mocks.server.listTemplates.mockResolvedValue(TEMPLATES.filter((t) => t.name !== 'Shelf-Tag'))
    fireEvent.click(within(rowOf('Shelf-Tag')).getByText('删除'))
    fireEvent.click(screen.getByText('确认删除'))
    await flush()
    expect(mocks.server.deleteTemplate).toHaveBeenCalledWith('Shelf-Tag')

    // 旧周期 3 个 blob URL 全部释放；新周期对剩余 2 个模板重新拉取
    expect(mocks.revokeObjectURL).toHaveBeenCalledTimes(3)
    expect(mocks.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')
    expect(mocks.revokeObjectURL).toHaveBeenCalledWith('blob:preview-3')
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(5)
    expect(thumbOf('Carton-Label-A').getAttribute('src')).toBe('blob:preview-4')
  })

  it('AC-04：preview 失败——单元格占位提示（title 携带原因），列表不受影响，同周期不重复请求', async () => {
    mocks.server.previewTemplate.mockRejectedValue(new Error('请求失败（HTTP 404）。'))
    renderWorkbench()
    await flush()

    const errs = screen.getAllByTitle(/^预览不可用：/)
    expect(errs.length).toBe(3)
    expect(errs[0].getAttribute('title')).toBe('预览不可用：请求失败（HTTP 404）。')
    // 列表与其余功能不受影响
    expect(rowOf('Carton-Label-A')).toBeTruthy()
    expect(rowOf('Shelf-Tag')).toBeTruthy()
    // 失败也进会话缓存：无重试风暴（每模板仍只请求一次）
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(3)
  })

  it('AC-05：点击缩略图放大——浮层显示大图且不再发请求；遮罩点击 / Esc 关闭', async () => {
    renderWorkbench()
    await flush()

    fireEvent.click(thumbOf('Carton-Label-A'))
    await flush()
    // 放大图与缩略图同源（缓存命中，无新请求）
    expect(enlargedOf('Carton-Label-A').getAttribute('src')).toBe('blob:preview-1')
    expect(mocks.server.previewTemplate).toHaveBeenCalledTimes(3)
    expect(screen.getByText('按模板测试数据渲染')).toBeTruthy()

    // 点击遮罩关闭
    fireEvent.click(document.querySelector('.preview-overlay') as HTMLElement)
    expect(screen.queryByAltText('模板「Carton-Label-A」预览')).toBeNull()

    // 重新放大，Esc 关闭
    fireEvent.click(thumbOf('Shelf-Tag'))
    expect(enlargedOf('Shelf-Tag').getAttribute('src')).toBe('blob:preview-3')
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByAltText('模板「Shelf-Tag」预览')).toBeNull()
  })
})
