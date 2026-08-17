// @vitest-environment jsdom
// 迭代 20：server 构建（VITE_UI_MODE=server）菜单裁剪——含 在线设备 / 设备日志，移除 设置 与一切
// 打印机相关入口（PDA 日志 为 client 版命名）；状态栏显示服务端地址（页面 origin /「同源」）与 UI 模式；
// K2 守门：跳过 localApi 探测（getHostConfig / getTransport 不被调用）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import App from './App'

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

vi.mock('./lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
  clientPackageDownloadUrl: (fileName: string) => `/api/client-packages/${encodeURIComponent(fileName)}`,
  pluginPackageDownloadUrl: (fileName: string) => `/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))

vi.mock('./lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listTemplates.mockResolvedValue([])
  mocks.server.listDevices.mockResolvedValue([
    { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online', lastIp: '192.168.1.5' },
  ])
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.local.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

describe('server 构建：菜单裁剪（迭代 20 §2.2 / Y5）', () => {
  it('含 在线设备 / 设备日志 / 客户端下载 / 插件管理 / 工作台 / 设计器 / 数据与打印 / 作业历史；不含 设置 / PDA 日志', async () => {
    render(<App />)
    expect(await screen.findByRole('button', { name: '在线设备' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '设备日志' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '工作台' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '设计器' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '数据与打印' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '作业历史' })).toBeTruthy()
    // 迭代 22 §2.3：Server UI「客户端下载」页入口
    expect(screen.getByRole('button', { name: '客户端下载' })).toBeTruthy()
    // 迭代 23 §5.4：Server UI「插件管理」页入口（与「客户端下载」并列）
    expect(screen.getByRole('button', { name: '插件管理' })).toBeTruthy()
    // 设置页与 PDA 日志（client 版命名）不存在
    expect(screen.queryByRole('button', { name: '设置' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'PDA 日志' })).toBeNull()
  })
})

describe('server 构建：K2 跳过 localApi 探测', () => {
  it('AppProvider 启动不调 getHostConfig / getTransport，healthz 正常探测', async () => {
    render(<App />)
    await waitFor(() => expect(mocks.server.healthz).toHaveBeenCalled())
    expect(mocks.local.getHostConfig).not.toHaveBeenCalled()
    expect(mocks.local.getTransport).not.toHaveBeenCalled()
  })
})

describe('server 构建：状态栏（服务端地址 + UI 模式，无打印机内容）', () => {
  it('显示 同源（页面 origin）· Server 管理界面', async () => {
    render(<App />)
    expect(await screen.findByText(`同源（${window.location.origin}）· Server 管理界面`)).toBeTruthy()
    // 无本机 IP 显示（server 构建不读 /api/host/config）
    expect(screen.queryByText(/本机 IP：/)).toBeNull()
  })
})

describe('server 构建：在线设备页入口', () => {
  it('点击「在线设备」tab 打开设备列表（GET /api/devices）', async () => {
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: '在线设备' }))
    expect(await screen.findByText('device-1')).toBeTruthy()
    expect(screen.getByText('192.168.1.5')).toBeTruthy()
  })
})

describe('server 构建：客户端下载页入口（迭代 22 §2.3）', () => {
  it('点击「客户端下载」tab：列表 / 上传 / 刷新按钮齐全（GET /api/client-packages）', async () => {
    mocks.server.listClientPackages.mockResolvedValue([
      { fileName: 'LabelFrame.Client-0.18.0.msi', sizeBytes: 2 * 1024 * 1024, modifiedAt: '2026-08-17T10:00:00Z', url: '/api/client-packages/LabelFrame.Client-0.18.0.msi' },
    ])
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: '客户端下载' }))
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('2.0 MB')).toBeTruthy()
    expect(screen.getByRole('button', { name: '上传安装包' })).toBeTruthy()
    // 下载链接（server 构建同源相对路径）
    const link = screen.getByRole('link', { name: /下载/ })
    expect(link.getAttribute('href')).toBe('/api/client-packages/LabelFrame.Client-0.18.0.msi')
  })
})

describe('server 构建：插件管理页入口（迭代 23 §5.4）', () => {
  it('点击「插件管理」tab：列表 / 上传 / 刷新按钮齐全（GET /api/plugin-packages；invalid 红标）', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([
      { fileName: 'sample-1.0.0.lfplugin', pluginId: 'sample', name: '示例插件', version: '1.0.0', sizeBytes: 2048, modifiedAt: '2026-08-17T10:00:00Z', valid: true },
      { fileName: 'broken.lfplugin', sizeBytes: 1024, modifiedAt: '2026-08-17T09:00:00Z', valid: false, invalidReason: 'manifest 缺少 pluginId' },
    ])
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: '插件管理' }))
    expect(await screen.findByText('示例插件')).toBeTruthy()
    expect(screen.getByText('sample')).toBeTruthy()
    expect(screen.getByText('无效')).toBeTruthy()
    expect(screen.getByRole('button', { name: '上传插件包' })).toBeTruthy()
    // 下载链接（server 构建同源相对路径；两行各一个）
    const links = screen.getAllByRole('link', { name: /下载/ })
    expect(links[0].getAttribute('href')).toBe('/api/plugin-packages/sample-1.0.0.lfplugin')
    expect(links[1].getAttribute('href')).toBe('/api/plugin-packages/broken.lfplugin')
  })
})
