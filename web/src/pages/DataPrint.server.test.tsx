// @vitest-environment jsdom
// 迭代 20：DataPrint server 构建（VITE_UI_MODE=server）——在线设备选择器（必选、仅在线可选、离线置灰显示
// 上次心跳）；默认目标优先级 = 用户点选（localStorage labelframe.defaultTargetDeviceId，须在线）> 第一台在线；
// 提交时现拉 GET /api/devices 校验在线（K3，掉线提示并禁止提交、作业不排队，不复用缓存列表）；
// 隐藏打印机连接徽标与逐张失败重试表格（G4）；业务 API 恒 serverApi（无 standalone 分支）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DeviceView, JobView, TemplatePackage } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { DataPrint } from './DataPrint'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listDevices: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    importExcel: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    listDevices: vi.fn(),
    listTemplates: vi.fn(),
    getTemplate: vi.fn(),
    submitJob: vi.fn(),
    getJob: vi.fn(),
    retryJobItem: vi.fn(),
    importExcel: vi.fn(),
    renderImage: vi.fn(),
    renderImages: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
    setHostConfig: vi.fn(),
    setTransport: vi.fn(),
    testTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    testPrinter: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: vi.fn(),
}))

vi.mock('../lib/uiMode', () => ({ UI_MODE: 'server', isServerUi: true }))

const PKG: TemplatePackage = {
  name: '库位标签',
  group: '默认',
  contract: {
    name: 'contract-1',
    version: '1',
    fields: [{ key: 'location', displayName: '库位', isRequired: true, type: 'Text' }],
  },
  layout: { name: 'layout-1', contractName: 'contract-1', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
  testData: { location: 'A-01' },
}

/** Server 作业视图（无逐张 items 的汇总形状）。 */
const DONE_JOB_SERVER: JobView = {
  jobId: 'job-1',
  requestId: 'r-1',
  status: 'Completed',
  totalItems: 2,
  completedItems: 1,
  failedItems: 1,
  targetDeviceId: 'device-1',
  deviceStatus: 'Online',
}

/** 含逐张 items 的作业视图（client 形状；server 构建下即使返回也不渲染重试表格——G4 强制隐藏）。 */
const DONE_JOB_WITH_ITEMS: JobView = {
  ...DONE_JOB_SERVER,
  items: [
    { index: 0, status: 'Completed' },
    { index: 1, status: 'Failed', errorMessage: '打印失败' },
  ],
}

const DEVICES: DeviceView[] = [
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online', lastIp: '192.168.1.5' },
  { deviceId: 'device-2', name: '仓库-2 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-10T23:00:00Z', status: 'Offline', lastIp: '192.168.1.6' },
]

const DEFAULT_TARGET_KEY = 'labelframe.defaultTargetDeviceId'

function Harness() {
  return (
    <AppProvider>
      <DataPrint />
    </AppProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listDevices.mockResolvedValue(DEVICES)
  mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.server.submitJob.mockResolvedValue(DONE_JOB_SERVER)
  mocks.server.getJob.mockResolvedValue(DONE_JOB_SERVER)
  mocks.server.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01']] })
  mocks.server.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.server.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
  // localApi 全量 mock（AppProvider 启动链在 server 构建下不应调用，保险提供）
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '仓库-1 打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
})

afterEach(() => {
  cleanup()
})

async function renderDataPrint() {
  render(<Harness />)
  await screen.findByDisplayValue('A-01')
  await waitFor(() => expect(screen.getByLabelText('目标设备')).toBeTruthy())
}

describe('DataPrint server 构建：在线设备选择器', () => {
  it('仅在线设备可选：离线设备 option 置灰并显示上次心跳原因', async () => {
    await renderDataPrint()
    const select = screen.getByLabelText('目标设备') as HTMLSelectElement
    expect(select.value).toBe('device-1') // 第一台在线默认选中
    const offlineOpt = screen.getByRole('option', { name: /仓库-2 打印电脑/ }) as HTMLOptionElement
    expect(offlineOpt.disabled).toBe(true)
    expect(offlineOpt.textContent).toContain('上次心跳')
    const onlineOpt = screen.getByRole('option', { name: /仓库-1 打印电脑/ }) as HTMLOptionElement
    expect(onlineOpt.disabled).toBe(false)
    // 提示文案：仅在线可选 + 提交前再校验
    expect(screen.getByText('仅在线设备可选；提交时将再次校验所选设备在线状态。')).toBeTruthy()
  })

  it('默认目标优先级：用户点选（localStorage，在线）> 第一台在线', async () => {
    // device-2 在线、device-1 离线：用户点选 device-2 → 选中 device-2（而非第一台 device-1）
    mocks.server.listDevices.mockResolvedValue([
      { ...DEVICES[1], status: 'Online' },
      { ...DEVICES[0], status: 'Offline' },
    ])
    window.localStorage.setItem(DEFAULT_TARGET_KEY, 'device-2')
    await renderDataPrint()
    expect((screen.getByLabelText('目标设备') as HTMLSelectElement).value).toBe('device-2')
  })

  it('用户点选的设备离线：回退第一台在线', async () => {
    window.localStorage.setItem(DEFAULT_TARGET_KEY, 'device-2') // 离线
    await renderDataPrint()
    expect((screen.getByLabelText('目标设备') as HTMLSelectElement).value).toBe('device-1')
  })

  it('不显示「本机连接」打印机徽标（server 构建无打印机相关内容）', async () => {
    await renderDataPrint()
    expect(screen.queryByText('本机连接')).toBeNull()
    expect(screen.queryByText('LOG')).toBeNull()
  })
})

describe('DataPrint server 构建：提交前现拉校验（K3）', () => {
  it('提交时现拉设备列表：所选设备在线 → 正常提交（listDevices 第 2 次调用 = 提交时现拉）', async () => {
    await renderDataPrint()
    expect(mocks.server.listDevices).toHaveBeenCalledTimes(1) // 仅进入页面拉取一次

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => expect(mocks.server.submitJob).toHaveBeenCalledTimes(1))
    expect(mocks.server.listDevices).toHaveBeenCalledTimes(2)
    const req = mocks.server.submitJob.mock.calls[0][0]
    expect(req).toMatchObject({ templateName: '库位标签', targetDeviceId: 'device-1' })
    expect(req.template).toBeUndefined()
    // 双 base 守门：server 构建恒 serverApi，localApi 不提交
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
  })

  it('所选设备提交时已离线：禁止提交、作业不排队（不复用进入页面时的缓存列表）', async () => {
    await renderDataPrint()
    // 进入页面时 device-1 在线（缓存），提交时现拉已离线
    mocks.server.listDevices.mockResolvedValue([
      { ...DEVICES[0], status: 'Offline' },
      { ...DEVICES[1] },
    ])
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('所选设备已离线或不存在，无法提交（作业不会排队）。请重新选择在线设备。')).toBeTruthy()
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
    // 选择器数据随现拉结果刷新（device-1 置灰）
    const opt = screen.getByRole('option', { name: /仓库-1 打印电脑/ }) as HTMLOptionElement
    expect(opt.disabled).toBe(true)
  })

  it('现拉校验失败（网络错误）：提示并禁止提交', async () => {
    await renderDataPrint()
    mocks.server.listDevices.mockRejectedValue(new Error('network down'))
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText(/校验设备在线状态失败/)).toBeTruthy()
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })
})

describe('DataPrint server 构建：隐藏逐张失败重试表格（G4）', () => {
  it('作业返回 items 也不渲染逐张表格与重试按钮，失败提示不带「下方表格重试」', async () => {
    mocks.server.getJob.mockResolvedValue(DONE_JOB_WITH_ITEMS)
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('已完成 1 / 2 张')).toBeTruthy()
    expect(screen.getByText(/有 1 张打印失败/)).toBeTruthy()
    // G4：server 构建强制隐藏逐张表格 / 重试按钮
    expect(screen.queryByRole('button', { name: /重试/ })).toBeNull()
    expect(screen.queryByText(/可在下方表格中单独重试/)).toBeNull()
    // 失败原因提示走汇总文案
    expect(screen.getByText(/详见作业状态与客户端回报的失败原因/)).toBeTruthy()
  })
})
