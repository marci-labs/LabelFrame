// @vitest-environment jsdom
// 迭代 15 §5.4：draft 保留（切 tab / 刷新 / 标签页隔离 / Excel 不保留）+ 调试开关下按钮行为与下载（单张 PNG / zip）

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { JobView, TemplatePackage } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { DataPrint } from './DataPrint'

const mocks = vi.hoisted(() => ({
  healthz: vi.fn(),
  getTransport: vi.fn(),
  setTransport: vi.fn(),
  testTransport: vi.fn(),
  listTemplates: vi.fn(),
  getTemplate: vi.fn(),
  submitJob: vi.fn(),
  getJob: vi.fn(),
  retryJobItem: vi.fn(),
  importExcel: vi.fn(),
  renderImage: vi.fn(),
  renderImages: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({ api: mocks }))

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
  mocks.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok', transport: 'Log' })
  mocks.getTransport.mockResolvedValue({ mode: 'Log', params: {}, availableModes: ['Log', 'Tcp', 'WindowsDriver', 'Zebra'] })
  mocks.listTemplates.mockResolvedValue([{ name: '库位标签', group: '默认', updatedAt: '2026-08-10' }])
  mocks.getTemplate.mockResolvedValue(PKG)
  mocks.submitJob.mockResolvedValue(DONE_JOB)
  mocks.getJob.mockResolvedValue(DONE_JOB)
  mocks.importExcel.mockResolvedValue({ headers: ['Location'], rows: [['X-01'], ['Y-02']] })
  mocks.renderImage.mockResolvedValue({ blob: new Blob(['png']), filename: 'label-1.png' })
  mocks.renderImages.mockResolvedValue({ blob: new Blob(['zip']), filename: 'labels-debug.zip' })
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
    expect(screen.getByRole('button', { name: /重新映射（data\.xlsx）/ })).toBeTruthy()

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
      expect(mocks.submitJob).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    // 作业进度出现（jobId 保留链路）
    expect(await screen.findByText('已完成 1 / 1 张')).toBeTruthy()
    expect(mocks.renderImage).not.toHaveBeenCalled()

    // 出图预览 → render-image 下载，不建作业
    fireEvent.click(screen.getByRole('button', { name: '出图预览' }))
    await waitFor(() => expect(mocks.renderImage).toHaveBeenCalledTimes(1))
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
      expect(mocks.renderImage).toHaveBeenCalledWith(expect.objectContaining({ labels: [{ data: { location: 'A-01' } }] }))
    })
    expect(mocks.submitJob).not.toHaveBeenCalled()
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('label-1.png'))
  })

  it('调试开 + 批量：下载 zip（全部行），不提交作业', async () => {
    await renderDataPrint()
    fireEvent.click(screen.getByRole('checkbox', { name: /调试模式/ }))
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')

    fireEvent.click(screen.getByRole('button', { name: '下载调试图片 zip（2 张）' }))
    await waitFor(() => {
      expect(mocks.renderImages).toHaveBeenCalledWith(
        expect.objectContaining({ labels: [{ data: { location: 'X-01' } }, { data: { location: 'Y-02' } }] }),
      )
    })
    expect(mocks.submitJob).not.toHaveBeenCalled()
    await waitFor(() => expect(clickSpy.mock.instances[0]?.download).toBe('labels-debug.zip'))
  })

  it('调试关 + 批量：批量打印提交作业', async () => {
    await renderDataPrint()
    fireEvent.change(document.getElementById('excelFile')!, { target: { files: [new File(['x'], 'data.xlsx')] } })
    await screen.findByText('列映射（2 行数据）')

    fireEvent.click(screen.getByRole('button', { name: '批量打印 2 张' }))
    await waitFor(() => {
      expect(mocks.submitJob).toHaveBeenCalledWith(
        expect.objectContaining({ labels: [{ data: { location: 'X-01' } }, { data: { location: 'Y-02' } }] }),
      )
    })
    expect(mocks.renderImages).not.toHaveBeenCalled()
  })
})

describe('DataPrint 顶部连接徽标与快速切换（迭代 15 §6.2）', () => {
  it('显示当前连接徽标与模式下拉、应用按钮', async () => {
    await renderDataPrint()
    // 快速切换（title 定位模式下拉）
    const quickSelect = screen.getByTitle('快速切换连接方式（应用 = 测试后生效）') as HTMLSelectElement
    expect(quickSelect.value).toBe('Log')
    expect(screen.getByRole('button', { name: /应用/ })).toBeTruthy()
  })

  it('快速切换失败：提示错误且全局状态不动（回滚）', async () => {
    mocks.setTransport.mockResolvedValue({
      ok: false,
      message: '连接测试失败：无法连接 10.0.0.9:9100',
      config: { mode: 'Log', params: {} },
    })
    await renderDataPrint()
    const quickSelect = screen.getByTitle('快速切换连接方式（应用 = 测试后生效）') as HTMLSelectElement
    fireEvent.change(quickSelect, { target: { value: 'Tcp' } })
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '10.0.0.9' } })
    fireEvent.click(screen.getByRole('button', { name: /应用/ }))
    expect(await screen.findByText('连接测试失败：无法连接 10.0.0.9:9100')).toBeTruthy()
    expect(mocks.setTransport).toHaveBeenCalledWith(expect.objectContaining({ mode: 'Tcp', tcpHost: '10.0.0.9' }))
  })
})
