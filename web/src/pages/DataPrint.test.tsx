// @vitest-environment jsdom
// 迭代 15 §5.4：draft 保留（切 tab / 刷新 / 标签页隔离 / Excel 不保留）+ 调试开关下按钮行为与下载（单张 PNG / zip）
// 迭代 18 F5：双 base（serverApi / localApi 跟随 deviceMode）+ 本机设备默认选中（hostConfig.deviceId 匹配）+ 单机降级守门

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DeviceView, JobView, TemplatePackage } from '../lib/api/types'
import { ApiError } from '../lib/api/types'
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

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))

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

const DONE_JOB: JobView = {
  jobId: 'job-1',
  requestId: 'r-1',
  status: 'Completed',
  totalItems: 1,
  completedItems: 1,
  items: [{ index: 0, status: 'Completed' }],
}

/** Server 作业视图（迭代 16：无逐张 items，只有汇总字段）。 */
const DONE_JOB_SERVER: JobView = {
  jobId: 'job-1',
  requestId: 'r-1',
  status: 'Completed',
  totalItems: 1,
  completedItems: 1,
  targetDeviceId: 'device-1',
  deviceStatus: 'Online',
}

const DEVICES: DeviceView[] = [
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
  { deviceId: 'device-2', name: '仓库-2 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-10T23:00:00Z', status: 'Offline' },
]

/** 两台在线 + 一台离线：验证「本机设备优先于第一台在线」。 */
const THREE_DEVICES: DeviceView[] = [
  { deviceId: 'pc-a', name: 'A 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
  { deviceId: 'pc-b', name: 'B 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-11T01:00:00Z', status: 'Online' },
  { deviceId: 'pc-c', name: 'C 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-10T23:00:00Z', status: 'Offline' },
]

const OFFLINE_DEVICES: DeviceView[] = [
  { deviceId: 'device-1', name: '仓库-1 打印电脑', registeredAt: '2026-08-11T00:00:00Z', lastSeenAt: '2026-08-10T23:00:00Z', status: 'Offline' },
]

let clickSpy: ReturnType<typeof vi.spyOn>

/** 模拟 DataPrint 挂载在 AppProvider 下的切 tab 行为（provider 不卸载，页面卸载重挂）。 */
function Harness({ show }: { show: boolean }) {
  return (
    <AppProvider>
      <div>{show && <DataPrint />}</div>
    </AppProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:mock'), revokeObjectURL: vi.fn() })
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: '仓库-1 打印电脑' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  // 默认单机模式（旧 WinHost 无 /api/devices → 404）：隐藏设备选择、提交不带 targetDeviceId；业务 API 走 localApi
  mocks.server.listDevices.mockRejectedValue(new ApiError('HTTP_404', 'Not Found'))
  mocks.local.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.local.getTemplate.mockResolvedValue(PKG)
  mocks.local.submitJob.mockResolvedValue(DONE_JOB)
  mocks.local.getJob.mockResolvedValue(DONE_JOB)
  mocks.local.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01'], ['Y-02']] })
  mocks.local.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.local.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
  // 服务端模式各方法默认就绪（渲染后由用例覆盖 listDevices 的 mock）
  mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.server.submitJob.mockResolvedValue(DONE_JOB)
  mocks.server.getJob.mockResolvedValue(DONE_JOB)
  mocks.server.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01'], ['Y-02']] })
  mocks.server.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.server.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
})

afterEach(() => {
  vi.unstubAllGlobals()
  clickSpy.mockRestore()
  cleanup()
})

async function renderDataPrint() {
  render(<Harness show />)
  // 等模板与 testData 预填值出现
  await screen.findByDisplayValue('A-01')
}

describe('DataPrint 会话保留（迭代 15 §6.1）', () => {
  it('切 tab（页面卸载重挂）：模板、字段值、调试开关保留', async () => {
    const { rerender } = render(<Harness show />)
    await screen.findByDisplayValue('A-01')
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'B-02' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /调试模式/ }))
    await waitFor(() => expect((screen.getByRole('checkbox', { name: /调试模式/ }) as HTMLInputElement).checked).toBe(true))

    // 切走再切回
    rerender(<Harness show={false} />)
    rerender(<Harness show />)
    await waitFor(() => expect(screen.getByDisplayValue('B-02')).toBeTruthy())
    expect((screen.getByRole('checkbox', { name: /调试模式/ }) as HTMLInputElement).checked).toBe(true)
    // 模板仍是选中项（第一个下拉 = 模板选择）
    expect((screen.getAllByRole('combobox')[0] as HTMLSelectElement).value).toBe('库位标签')
  })

  it('刷新（sessionStorage 恢复）：字段值与调试开关保留', async () => {
    const { unmount } = render(<Harness show />)
    await screen.findByDisplayValue('A-01')
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'C-03' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /调试模式/ }))
    await waitFor(() => expect(window.sessionStorage.getItem('labelframe.printDraft')).toContain('C-03'))

    unmount()
    // 全新会话（模拟刷新页面）：草稿从 sessionStorage 恢复
    render(<Harness show />)
    await waitFor(() => expect(screen.getByDisplayValue('C-03')).toBeTruthy())
    expect((screen.getByRole('checkbox', { name: /调试模式/ }) as HTMLInputElement).checked).toBe(true)
  })

  it('草稿只用 sessionStorage，不用 localStorage（D5：避免跨标签页共享）', async () => {
    const { unmount } = render(<Harness show />)
    await screen.findByDisplayValue('A-01')
    fireEvent.change(screen.getByDisplayValue('A-01'), { target: { value: 'D-04' } })
    await waitFor(() => expect(window.sessionStorage.getItem('labelframe.printDraft')).toContain('D-04'))
    // 刷新后从 sessionStorage 恢复，localStorage 无草稿（标签页隔离的存储基础）
    expect(window.localStorage.getItem('labelframe.printDraft')).toBeNull()
    unmount()
  })

  it('Excel 导入数据与列映射不保留：切页后重新上传', async () => {
    const { rerender } = render(<Harness show />)
    // 导入 Excel → 映射弹窗
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')
    expect(screen.getByRole('button', { name: '重新映射（data.xlsx）' })).toBeTruthy()

    // 切走再切回：Excel 状态丢弃（无重新映射按钮、无弹窗）
    rerender(<Harness show={false} />)
    rerender(<Harness show />)
    await screen.findByDisplayValue('A-01')
    expect(screen.queryByRole('button', { name: /重新映射/ })).toBeNull()
    expect(screen.queryByText('列映射（2 行数据）')).toBeNull()
    // 模板与字段值仍在
    expect(screen.getByDisplayValue('A-01')).toBeTruthy()
  })
})

describe('调试开关与按钮语义（迭代 15 §6.3）', () => {
  it('调试关：打印测试提交作业，「出图预览」即时出图（不建作业）', async () => {
    await renderDataPrint()
    expect(screen.getByRole('button', { name: /打印测试（单张）/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: '出图预览' })).toBeTruthy()

    // 打印测试 → 正常作业
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    // 作业进度出现（jobId 保留链路）
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    expect(mocks.local.renderImage).not.toHaveBeenCalled()

    // 出图预览 → render-image 下载，不建作业
    fireEvent.click(screen.getByRole('button', { name: '出图预览' }))
    await waitFor(() => expect(mocks.local.renderImage).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
  })

  it('调试开：按钮文案联动、隐藏「出图预览」、打印测试改为 render-image 下载、不提交作业', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('checkbox', { name: /调试模式/ }))

    // 文案联动
    expect(screen.getByRole('button', { name: '调试出图（单张）' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: '出图预览' })).toBeNull()
    // 作业进度区提示调试模式
    expect(screen.getByText('调试模式：不提交作业，出图已下载。')).toBeTruthy()

    // 单张出图 → render-image 下载 PNG，不提交作业
    fireEvent.click(screen.getByRole('button', { name: '调试出图（单张）' }))
    await waitFor(() => {
      expect(mocks.local.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
  })

  it('调试开 + 批量：下载 zip（全部行），不提交作业', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('checkbox', { name: /调试模式/ }))
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')

    fireEvent.click(screen.getByRole('button', { name: '下载调试图片 zip（2 张）' }))
    await waitFor(() => {
      expect(mocks.local.renderImages).toHaveBeenCalledWith(
        expect.objectContaining({ labels: [{ data: { location: 'X-01' } }, { data: { location: 'Y-02' } }] }),
      )
    })
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('labels-debug.zip'))
  })

  it('调试关 + 批量：批量打印提交作业', async () => {
    await renderDataPrint()
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')

    fireEvent.click(screen.getByRole('button', { name: '批量打印 2 张' }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledWith(
        expect.objectContaining({ labels: [{ data: { location: 'X-01' } }, { data: { location: 'Y-02' } }] }),
      )
    })
    expect(mocks.local.renderImages).not.toHaveBeenCalled()
  })
})

describe('目标设备 / 客户端选择（迭代 17/18 F5）', () => {
  /** 服务端模式：listDevices 成功返回设备列表。 */
  async function renderServerMode(devices: DeviceView[]) {
    mocks.server.listDevices.mockResolvedValue(devices)
    render(<Harness show />)
    await screen.findByDisplayValue('A-01')
    // 等设备下拉出现（listDevices 异步 resolve）
    await waitFor(() => expect(screen.getByLabelText('目标设备')).toBeTruthy())
  }

  it('服务端模式：显示设备下拉（设备名 + 在线状态），默认选中本机设备（hostConfig.deviceId 匹配）', async () => {
    await renderServerMode(DEVICES)
    const select = screen.getByLabelText('目标设备') as HTMLSelectElement
    // 本机 deviceId=device-1 在线 → 选中本机
    expect(select.value).toBe('device-1')
    expect(screen.getByText('仓库-1 打印电脑（在线）')).toBeTruthy()
    expect(screen.getByText('仓库-2 打印电脑（离线）')).toBeTruthy()
  })

  it('多台在线：本机设备优先（deviceId=pc-b → 选中 pc-b，而非第一台 pc-a）', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'pc-b', deviceName: 'B 打印电脑' })
    await renderServerMode(THREE_DEVICES)
    expect((screen.getByLabelText('目标设备') as HTMLSelectElement).value).toBe('pc-b')
  })

  it('本机设备未命中（不在列表）：回退第一台在线设备', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'pc-x', deviceName: 'X' })
    await renderServerMode(THREE_DEVICES)
    expect((screen.getByLabelText('目标设备') as HTMLSelectElement).value).toBe('pc-a')
  })

  it('本机设备是离线设备：回退第一台在线设备', async () => {
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-2', deviceName: '仓库-2 打印电脑' })
    await renderServerMode(DEVICES)
    expect((screen.getByLabelText('目标设备') as HTMLSelectElement).value).toBe('device-1')
  })

  it('服务端模式提交：templateName + targetDeviceId 走 serverApi；localApi 不提交（双 base 守门）', async () => {
    await renderServerMode(DEVICES)
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.server.submitJob).toHaveBeenCalledTimes(1)
    })
    const req = mocks.server.submitJob.mock.calls[0][0]
    expect(req).toMatchObject({ templateName: '库位标签', targetDeviceId: 'device-1', labels: [{ data: { location: 'A-01' } }] })
    expect(req.template).toBeUndefined()
    expect(mocks.local.submitJob).not.toHaveBeenCalled()
  })

  it('服务端模式手动切换目标设备后提交：使用所选设备 ID', async () => {
    await renderServerMode(DEVICES)
    fireEvent.change(screen.getByLabelText('目标设备'), { target: { value: 'device-2' } })
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.server.submitJob).toHaveBeenCalledWith(expect.objectContaining({ targetDeviceId: 'device-2' }))
    })
  })

  it('无设备：提示「暂无在线客户端…」且打印测试禁用', async () => {
    await renderServerMode([])
    expect(screen.getByText('暂无在线客户端，请先在打印电脑安装并启动 LabelFrame Client')).toBeTruthy()
    expect((screen.getByRole('button', { name: /打印测试（单张）/ }) as HTMLButtonElement).disabled).toBe(true)
    // 调试出图不需要目标设备，仍可用
    expect((screen.getByRole('button', { name: '出图预览' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('全部离线：不默认选中，提示选择设备；选中离线设备后可提交（排队等待）', async () => {
    await renderServerMode(OFFLINE_DEVICES)
    const select = screen.getByLabelText('目标设备') as HTMLSelectElement
    expect(select.value).toBe('')
    expect(screen.getByText(/暂无在线设备/)).toBeTruthy()
    expect((screen.getByRole('button', { name: /打印测试（单张）/ }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.change(select, { target: { value: 'device-1' } })
    expect((screen.getByRole('button', { name: /打印测试（单张）/ }) as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.server.submitJob).toHaveBeenCalledWith(expect.objectContaining({ targetDeviceId: 'device-1' }))
    })
  })

  it('单机降级（/api/devices 404）：无设备选择 UI，模板列表走 localApi，提交自包含 template 走 localApi（双 base 守门）', async () => {
    await renderDataPrint()
    expect(screen.queryByLabelText('目标设备')).toBeNull()
    expect(screen.queryByText(/暂无在线客户端/)).toBeNull()
    // 模板列表来自本机（standalone → localApi）
    expect(mocks.local.listTemplates).toHaveBeenCalled()
    expect(mocks.server.listTemplates).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => {
      expect(mocks.local.submitJob).toHaveBeenCalledTimes(1)
    })
    const req = mocks.local.submitJob.mock.calls[0][0]
    expect(req.templateName).toBeUndefined()
    expect(req.targetDeviceId).toBeUndefined()
    expect(req.template).toMatchObject({ name: '库位标签', contract: PKG.contract, layout: PKG.layout })
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })

  it('Server 作业视图（无 items）：进度与目标设备可见，不渲染逐张表格', async () => {
    mocks.server.listDevices.mockResolvedValue(DEVICES)
    mocks.server.getJob.mockResolvedValue(DONE_JOB_SERVER)
    render(<Harness show />)
    await screen.findByDisplayValue('A-01')
    await waitFor(() => expect(screen.getByLabelText('目标设备')).toBeTruthy())

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    expect(screen.getByText(/目标设备：device-1（在线）/)).toBeTruthy()
    // 无逐张表格，显示说明行
    expect(screen.getByText(/服务端作业无逐张明细/)).toBeTruthy()
  })
})

describe('连接状态徽标（迭代 18 F5）', () => {
  it('显示本机连接方式与服务端连通状态（图例区分）', async () => {
    await renderDataPrint()
    expect(screen.getByText('本机连接')).toBeTruthy()
    expect(screen.getByText('服务端')).toBeTruthy()
    // 本机连接徽标：来自 localApi.getTransport（Log 模式）
    expect(screen.getByText('LOG')).toBeTruthy()
    // 服务端连通（healthz 成功）→ 已连接
    expect(screen.getByText('已连接')).toBeTruthy()
  })

  it('服务端不可达（healthz 失败）：徽标显示「未连接（单机模式可用）」', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    await renderDataPrint()
    expect(await screen.findByText('未连接（单机模式可用）')).toBeTruthy()
  })
})
