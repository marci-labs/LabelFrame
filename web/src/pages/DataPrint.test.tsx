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
    excelTemplate: vi.fn(),
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
    excelTemplate: vi.fn(),
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
// 迭代 20：本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

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
  mocks.local.excelTemplate.mockResolvedValue({ blob: new Blob(['xlsx']), filename: 'excel-template.xlsx' })
  mocks.local.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.local.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
  // 服务端模式各方法默认就绪（渲染后由用例覆盖 listDevices 的 mock）
  mocks.server.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.server.getTemplate.mockResolvedValue(PKG)
  mocks.server.submitJob.mockResolvedValue(DONE_JOB)
  mocks.server.getJob.mockResolvedValue(DONE_JOB)
  mocks.server.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01'], ['Y-02']] })
  mocks.server.excelTemplate.mockResolvedValue({ blob: new Blob(['xlsx']), filename: 'excel-template.xlsx' })
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

describe('目标设备固定本机（迭代 22 决策 1A）', () => {
  /** 服务端模式：listDevices 成功 + 本机 hostConfig（deviceId / deviceName 可覆盖）。 */
  async function renderServerMode(devices: DeviceView[], host: { deviceId?: string; deviceName?: string } = { deviceId: 'device-1', deviceName: '仓库-1 打印电脑' }) {
    mocks.server.listDevices.mockResolvedValue(devices)
    mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', ...host })
    render(<Harness show />)
    await screen.findByDisplayValue('A-01')
    // 等本机目标标签出现（listDevices 异步 resolve）
    await waitFor(() => expect(screen.getByText(/^本机（/)).toBeTruthy())
  }

  it('本机已注册且在线：只显示「本机（{deviceName}）」标签，无设备选择器', async () => {
    await renderServerMode(DEVICES)
    expect(screen.getByText('本机（仓库-1 打印电脑）')).toBeTruthy()
    expect(screen.getByText(/本机已注册且在线：作业经服务端投递/)).toBeTruthy()
    // 客户端构建不再有设备选择器
    expect(screen.queryByLabelText('目标设备')).toBeNull()
  })

  it('本机在线提交：templateName + targetDeviceId=本机 deviceId 走 serverApi；localApi 不提交（双 base 守门）', async () => {
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

  it('本机设备离线：降级本机直连并提示原因；提交自包含 template 走 localApi', async () => {
    await renderServerMode(DEVICES, { deviceId: 'device-2', deviceName: '仓库-2 打印电脑' })
    expect(screen.getByText('本机（仓库-2 打印电脑）')).toBeTruthy()
    expect(screen.getByText(/本机设备当前离线：已降级为本机直连打印/)).toBeTruthy()

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

  it('本机未注册（deviceId 不在服务端列表）：降级本机直连并提示未注册', async () => {
    await renderServerMode(DEVICES, { deviceId: 'pc-x', deviceName: '未注册电脑' })
    expect(screen.getByText(/本机未注册到服务端：已降级为本机直连打印/)).toBeTruthy()
    // 提交走本机直连（localApi）
    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    await waitFor(() => expect(mocks.local.submitJob).toHaveBeenCalledTimes(1))
    expect(mocks.server.submitJob).not.toHaveBeenCalled()
  })

  it('旧客户端无 deviceId：降级本机直连并提示未注册', async () => {
    await renderServerMode(DEVICES, { deviceId: undefined, deviceName: undefined })
    expect(screen.getByText('本机（未知）')).toBeTruthy()
    expect(screen.getByText(/本机未注册到服务端：已降级为本机直连打印/)).toBeTruthy()
  })

  it('服务端模式无设备（空列表）：本机未注册降级直连，打印测试仍可用', async () => {
    await renderServerMode([])
    expect(screen.getByText(/本机未注册到服务端/)).toBeTruthy()
    expect((screen.getByRole('button', { name: /打印测试（单张）/ }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('Server 作业视图（无 items）：进度与目标设备可见，不渲染逐张表格', async () => {
    mocks.server.listDevices.mockResolvedValue(DEVICES)
    mocks.server.getJob.mockResolvedValue(DONE_JOB_SERVER)
    render(<Harness show />)
    await screen.findByDisplayValue('A-01')
    await waitFor(() => expect(screen.getByText(/^本机（/)).toBeTruthy())

    fireEvent.click(screen.getByRole('button', { name: /打印测试（单张）/ }))
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    expect(screen.getByText(/目标设备：device-1（在线）/)).toBeTruthy()
    // 无逐张表格，显示说明行
    expect(screen.getByText(/服务端作业无逐张明细/)).toBeTruthy()
  })

  it('单机降级（/api/devices 404）：无目标设备 UI，模板列表走 localApi，提交自包含 template 走 localApi（双 base 守门）', async () => {
    await renderDataPrint()
    expect(screen.queryByText(/^本机（/)).toBeNull()
    expect(screen.queryByLabelText('目标设备')).toBeNull()
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
})

describe('下载 Excel 模板（迭代 22 §2.1）', () => {
  it('点击「下载 Excel 模板」：按契约字段 + testData 生成请求并下载 xlsx', async () => {
    mocks.local.excelTemplate.mockResolvedValue({ blob: new Blob(['xlsx']), filename: '库位标签.xlsx' })
    await renderDataPrint()
    fireEvent.click(screen.getByRole('button', { name: /下载 Excel 模板/ }))
    await waitFor(() => {
      expect(mocks.local.excelTemplate).toHaveBeenCalledWith(
        [{ key: 'location', displayName: '库位' }],
        { location: 'A-01' },
      )
    })
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('库位标签.xlsx'))
  })

  it('模板无契约字段：按钮禁用', async () => {
    mocks.local.getTemplate.mockResolvedValue({
      ...PKG,
      contract: { ...PKG.contract, fields: [] },
      layout: { ...PKG.layout, elements: [] },
    })
    render(<Harness show />)
    // 无字段模板：测试数据区提示无字段，无示例值输入框；等模板加载后按钮禁用
    await waitFor(() => expect(screen.getByText(/该模板没有字段/)).toBeTruthy())
    expect((screen.getByRole('button', { name: /下载 Excel 模板/ }) as HTMLButtonElement).disabled).toBe(true)
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
