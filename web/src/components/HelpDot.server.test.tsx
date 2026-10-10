// @vitest-environment jsdom
// server 构建分支（VITE_UI_MODE=server）帮助体系不渲染守门（迭代 126 · #308 AC-07）：
// PropsPanel / Settings 的「?」文档点均以 !isServerUi 条件渲染（拍板③口径：server 可达路径
// 不渲染）——server 分支断言无「?」、无 data-help 属性；App 级无「帮助」导航入口。
// 拆分独立文件与 Guide.server.test.tsx 同款双跑机制（vi.mock uiMode 注入分支，uiMode.ts:3 注释明示）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, render, screen } from '@testing-library/react'
import App from '../App'
import { Settings } from '../pages/Settings'
import { PropsPanel } from '../pages/designer/PropsPanel'
import { defaultElement } from '../lib/design/types'
import type { TextElement } from '../lib/design/types'

configure({ asyncUtilTimeout: 8000 })

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
    listClientPackages: vi.fn(),
    uploadClientPackage: vi.fn(),
    deleteClientPackage: vi.fn(),
    listPdaPackages: vi.fn(),
    uploadPdaPackage: vi.fn(),
    deletePdaPackage: vi.fn(),
    listServerIpv4Candidates: vi.fn(),
    listPluginPackages: vi.fn(),
    uploadPluginPackage: vi.fn(),
    deletePluginPackage: vi.fn(),
    downloadPluginPackage: vi.fn(),
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
    listInstalledPlugins: vi.fn(),
    installPlugin: vi.fn(),
    uninstallPlugin: vi.fn(),
    getPrintSettings: vi.fn(),
    setPrintSettings: vi.fn(),
  },
  probeHealthz: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
  clientPackageDownloadUrl: (fileName: string) => `/api/client-packages/${encodeURIComponent(fileName)}`,
  pdaPackageDownloadUrl: (fileName: string) => `/api/pda-packages/${encodeURIComponent(fileName)}`,
  pluginPackageDownloadUrl: (fileName: string) => `/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))

vi.mock('../lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listTemplates.mockResolvedValue([])
  mocks.server.listDevices.mockResolvedValue([])
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPdaPackages.mockResolvedValue([])
  mocks.server.listServerIpv4Candidates.mockResolvedValue({ candidates: [] })
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', deviceName: 'PC-1' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.getPrinterStatus.mockResolvedValue({ isOnline: true, isPaperOut: false, isPaused: false, message: '' })
  mocks.local.listInstalledPlugins.mockResolvedValue([])
  mocks.local.getPrintSettings.mockResolvedValue({ batchEnabled: false, batchSize: 10, batchIntervalMs: 500 })
})

afterEach(() => {
  cleanup()
})

describe('server 构建：帮助体系不渲染（迭代 126 · #308 AC-07）', () => {
  it('App：导航无「帮助」入口（help tab 仅 CLIENT_TABS）', async () => {
    render(<App />)
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: '帮助' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Help' })).toBeNull()
  })

  it('PropsPanel：server 分支无「?」、无 data-help 锚点（!isServerUi 守门在调用点）', () => {
    const textEl = { ...defaultElement('Text', 't1') } as TextElement
    render(<PropsPanel elements={[textEl]} selected={['t1']} viewMode="fit" onChange={() => {}} onAlign={() => {}} onDelete={() => {}} />)
    expect(screen.getByText('文本')).toBeTruthy()
    expect(document.querySelector('[data-help]')).toBeNull()
    expect(screen.queryByRole('button', { name: '查看说明' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Learn more' })).toBeNull()
  })

  it('Settings：server 分支无「?」、无 data-help 锚点', async () => {
    const { AppProvider } = await import('../state/AppContext')
    render(
      <AppProvider>
        <Settings />
      </AppProvider>,
    )
    await screen.findByText('语言')
    expect(document.querySelector('[data-help]')).toBeNull()
    expect(screen.queryByRole('button', { name: '查看说明' })).toBeNull()
  })
})
