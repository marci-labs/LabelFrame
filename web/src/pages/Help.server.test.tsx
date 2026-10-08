// @vitest-environment jsdom
// server 构建分支（VITE_UI_MODE=server）帮助体系不挂载 / 不渲染守门（迭代 121 · #280，AC-06）：
// 帮助 tab（SERVER_TABS 不含）、帮助索引页（main 渲染 !isServerUi 双保险）、页头「功能演示」入口
// （onRequestDemo 单点不下发）、Shell 级 DemoRunner 均以构建期开关裁剪——server 构建（web/dist-server）
// 不出现任何帮助体系内容。拆分独立文件与 Guide.server.test.tsx 同款双跑机制（vi.mock uiMode 注入分支）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, configure, render, screen } from '@testing-library/react'
import App from '../App'

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
  mocks.local.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

describe('server 构建：帮助体系整特性不挂载 / 不渲染（迭代 121 · #280，AC-06）', () => {
  it('主导航无「帮助」tab（SERVER_TABS 单点裁剪），首屏无帮助卡片网格', async () => {
    render(<App />)
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: '帮助' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Help' })).toBeNull()
    expect(document.querySelector('[data-help="cards"]')).toBeNull()
  })

  it('全页无「去做演示」深链按钮与「功能演示」页头入口（深链 / 入口双措辞均不出现）', async () => {
    render(<App />)
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: '上手试一遍' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Try It Yourself' })).toBeNull()
    expect(screen.queryByRole('button', { name: '功能演示' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Demo' })).toBeNull()
  })

  it('Shell 级演示引擎与中断残留询问不挂载（无 demo 层 DOM）', async () => {
    render(<App />)
    expect(await screen.findByText(`${window.location.origin} · 服务端管理界面`)).toBeTruthy()
    // 等过首见自动启动窗口（300ms）——帮助体系与引导同窗口裁剪，一并守门
    await new Promise((resolve) => setTimeout(resolve, 500))
    expect(document.querySelector('.demo-layer')).toBeNull()
    expect(document.querySelector('.guide-layer')).toBeNull()
  })
})
