// @vitest-environment jsdom
// 迭代 45：工作台模板名搜索——子串匹配、大小写不敏感，与分组过滤叠加生效；清空恢复完整列表。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { TemplateSummary } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { Workbench } from './Workbench'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    deleteTemplate: vi.fn(),
    exportTemplate: vi.fn(),
    importTemplate: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listTemplates: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))
// 本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

// 大小写混合命名 + 两组分组，覆盖搜索与分组叠加
const TEMPLATES: TemplateSummary[] = [
  { name: 'Carton-Label-A', group: '华东仓', updatedAt: '2026-09-01T10:00:00Z' },
  { name: 'carton-label-b', group: '华东仓', updatedAt: '2026-09-02T10:00:00Z' },
  { name: 'Shelf-Tag', group: '华南仓', updatedAt: '2026-09-03T10:00:00Z' },
]

function renderWorkbench() {
  return render(
    <AppProvider>
      <Workbench onOpenDesigner={() => {}} />
    </AppProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: 'PC' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.server.listTemplates.mockResolvedValue(TEMPLATES)
  mocks.local.listTemplates.mockResolvedValue(TEMPLATES)
})

afterEach(() => {
  cleanup()
})

describe('工作台模板名搜索（迭代 45）', () => {
  it('名称搜索：子串匹配、大小写不敏感，清空后恢复完整列表', async () => {
    renderWorkbench()
    expect(await screen.findByText('Carton-Label-A')).toBeTruthy()
    expect(screen.getByText('carton-label-b')).toBeTruthy()
    expect(screen.getByText('Shelf-Tag')).toBeTruthy()

    // 大写关键词命中小写命名（子串、不区分大小写）
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: 'CARTON' } })
    await waitFor(() => expect(screen.queryByText('Shelf-Tag')).toBeNull())
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()
    expect(screen.getByText('carton-label-b')).toBeTruthy()

    // 清空搜索 → 恢复完整列表
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: '' } })
    await waitFor(() => expect(screen.getByText('Shelf-Tag')).toBeTruthy())
    expect(screen.getByText('Carton-Label-A')).toBeTruthy()
  })

  it('搜索与分组过滤叠加生效：交集为准，双清空恢复完整列表', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    // 分组 = 华东仓：仅两个 carton 模板
    fireEvent.change(screen.getByTitle('按分组过滤'), { target: { value: '华东仓' } })
    await waitFor(() => expect(screen.queryByText('Shelf-Tag')).toBeNull())

    // 叠加搜索 shelf：华东仓内无匹配 → 空态提示
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: 'shelf' } })
    expect(await screen.findByText('没有匹配的模板')).toBeTruthy()

    // 清空搜索 → 回到分组过滤结果
    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: '' } })
    expect(await screen.findByText('Carton-Label-A')).toBeTruthy()
    expect(screen.queryByText('Shelf-Tag')).toBeNull()

    // 再清空分组 → 完整列表
    fireEvent.change(screen.getByTitle('按分组过滤'), { target: { value: '' } })
    await waitFor(() => expect(screen.getByText('Shelf-Tag')).toBeTruthy())
  })

  it('搜索词前后空白不影响匹配', async () => {
    renderWorkbench()
    expect(await screen.findByText('Shelf-Tag')).toBeTruthy()

    fireEvent.change(screen.getByPlaceholderText('搜索模板名称'), { target: { value: '  shelf  ' } })
    await waitFor(() => expect(screen.queryByText('Carton-Label-A')).toBeNull())
    expect(screen.getByText('Shelf-Tag')).toBeTruthy()
  })
})
