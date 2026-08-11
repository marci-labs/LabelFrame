// @vitest-environment jsdom
// 迭代 20：client 构建菜单与状态栏——含 设置 / PDA 日志（不含 在线设备 / 设备日志）；
// 状态栏在服务端已连接时显示本机 IP（/api/host/config.ips，多 IP 逗号分隔，title 给全量）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
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
}))

vi.mock('./lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({
    serverUrl: 'http://127.0.0.1:53961',
    deviceId: 'PC-1',
    deviceName: 'PC-1',
    ips: ['192.168.1.5', '10.0.0.8'],
  })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.listTemplates.mockResolvedValue([])
  mocks.server.listTemplates.mockResolvedValue([])
})

afterEach(() => {
  cleanup()
})

describe('client 构建：菜单（迭代 20 裁剪守门）', () => {
  it('含 设置 / PDA 日志；不含 在线设备 / 设备日志', async () => {
    render(<App />)
    expect(await screen.findByRole('button', { name: '设置' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'PDA 日志' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '在线设备' })).toBeNull()
    expect(screen.queryByRole('button', { name: '设备日志' })).toBeNull()
  })
})

describe('client 构建：状态栏本机 IP（迭代 20 G3）', () => {
  it('服务端已连接时显示本机 IP（多 IP 逗号分隔全部）', async () => {
    render(<App />)
    // healthz 成功后 connected=true → IP 显示
    expect(await screen.findByText(/本机 IP：192\.168\.1\.5, 10\.0\.0\.8/)).toBeTruthy()
    // title 给全量
    const el = screen.getByText(/本机 IP：/)
    expect(el.getAttribute('title')).toBe('192.168.1.5, 10.0.0.8')
  })

  it('不显示「同源 · Server 管理界面」（server 构建专属）', async () => {
    render(<App />)
    expect(screen.queryByText(/同源（/)).toBeNull()
    expect(screen.queryByText(/Server 管理界面/)).toBeNull()
  })
})
