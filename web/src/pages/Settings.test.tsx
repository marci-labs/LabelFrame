// @vitest-environment jsdom
// 迭代 15 §5.4：连接切换交互（测试连接 testOnly / 保存并应用先测试后生效 / 失败回滚提示）

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AppProvider } from '../state/AppContext'
import { ApiError } from '../lib/api/types'
import { Settings } from './Settings'

const mocks = vi.hoisted(() => ({
  healthz: vi.fn(),
  getTransport: vi.fn(),
  setTransport: vi.fn(),
  testTransport: vi.fn(),
  printerStatus: vi.fn(),
  printerTest: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({ api: mocks }))

const LOG_CONFIG = { mode: 'Log', params: {}, availableModes: ['Log', 'Tcp', 'WindowsDriver', 'Zebra'] }
const TCP_CONFIG = { mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 }, availableModes: ['Log', 'Tcp', 'WindowsDriver', 'Zebra'] }

function renderSettings() {
  return render(
    <AppProvider>
      <Settings />
    </AppProvider>,
  )
}

/** 连接方式分组里的「测试连接」（页面有两个同名按钮，第一个属于「后端连接」分组）。 */
function transportTestButton() {
  return screen.getAllByRole('button', { name: '测试连接' })[1]
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.healthz.mockResolvedValue({ service: 'LabelFrame.WinHost', status: 'ok', transport: 'Log' })
  mocks.getTransport.mockResolvedValue(LOG_CONFIG)
  mocks.printerStatus.mockResolvedValue({ isOnline: true, isPaperOut: false, isPaused: false, message: '' })
  mocks.printerTest.mockResolvedValue({ sent: true, bytes: 1024 })
})

afterEach(() => {
  cleanup()
})

describe('连接方式面板', () => {
  it('模式单选（Log / TCP / Windows驱动 / Zebra）且只显示当前模式参数', () => {
    renderSettings()
    expect((screen.getByRole('radio', { name: 'Log（模拟）' }) as HTMLInputElement).checked).toBe(true)
    expect((screen.getByRole('radio', { name: 'TCP' }) as HTMLInputElement).checked).toBe(false)
    expect((screen.getByRole('radio', { name: 'Windows 驱动' }) as HTMLInputElement).checked).toBe(false)
    expect((screen.getByRole('radio', { name: 'Zebra' }) as HTMLInputElement).checked).toBe(false)
    // Log 无参数输入区
    expect(screen.queryByLabelText('打印机 IP / 主机名')).toBeNull()
    // 切换 TCP 后出现 IP / 端口
    fireEvent.click(screen.getByRole('radio', { name: 'TCP' }))
    expect(screen.getByLabelText('打印机 IP / 主机名')).toBeTruthy()
    expect(screen.getByLabelText('端口')).toBeTruthy()
    // 切换 Windows 驱动后只显示打印机名称
    fireEvent.click(screen.getByRole('radio', { name: 'Windows 驱动' }))
    expect(screen.queryByLabelText('打印机 IP / 主机名')).toBeNull()
    expect(screen.getByLabelText('打印机名称')).toBeTruthy()
  })

  it('测试连接（testOnly）：发送候选参数，成功后显示后端 message 且不调用 setTransport（不保存不切换）', async () => {
    mocks.testTransport.mockResolvedValue({
      ok: true,
      message: '连接测试成功（未切换）。',
      config: LOG_CONFIG, // testOnly 成功也返回当前生效连接
    })
    renderSettings()
    fireEvent.click(screen.getByRole('radio', { name: 'TCP' }))
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '192.168.1.50' } })
    fireEvent.change(screen.getByLabelText('端口'), { target: { value: '9100' } })

    fireEvent.click(transportTestButton())
    await waitFor(() => {
      // 走 testTransport（真实 client 内部会补 testOnly: true），不调用 setTransport
      expect(mocks.testTransport).toHaveBeenCalledWith(
        expect.objectContaining({ mode: 'Tcp', tcpHost: '192.168.1.50', tcpPort: 9100 }),
      )
    })
    expect(await screen.findByText('连接测试成功（未切换）。')).toBeTruthy()
    expect(mocks.setTransport).not.toHaveBeenCalled()
  })

  it('保存并应用成功：setTransport 不带 testOnly，全局状态立即用响应 config 更新（不依赖 healthz 轮询）', async () => {
    mocks.setTransport.mockResolvedValue({ ok: true, message: '已切换为 TCP（192.168.1.50:9100）。', config: TCP_CONFIG })
    renderSettings()
    fireEvent.click(screen.getByRole('radio', { name: 'TCP' }))
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '192.168.1.50' } })
    fireEvent.change(screen.getByLabelText('端口'), { target: { value: '9100' } })

    fireEvent.click(screen.getByRole('button', { name: '保存并应用' }))
    await waitFor(() => {
      expect(mocks.setTransport).toHaveBeenCalledWith(expect.not.objectContaining({ testOnly: true }))
    })
    // 成功 message + 徽标立即更新为新连接
    expect(await screen.findByText('已切换为 TCP（192.168.1.50:9100）。')).toBeTruthy()
    expect(screen.getByText('TCP 192.168.1.50:9100')).toBeTruthy()
  })

  it('保存失败回滚：展示后端失败 message，当前生效连接保持原样', async () => {
    mocks.setTransport
      .mockResolvedValueOnce({ ok: true, message: '已切换为 TCP（192.168.1.50:9100）。', config: TCP_CONFIG })
      .mockResolvedValueOnce({
        ok: false,
        message: '连接测试失败：无法连接 192.168.1.50:9100',
        config: TCP_CONFIG, // 失败返回当前生效连接
      })
    renderSettings()
    fireEvent.click(screen.getByRole('radio', { name: 'TCP' }))
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '192.168.1.50' } })
    fireEvent.click(screen.getByRole('button', { name: '保存并应用' }))
    // 第一次保存成功 → 徽标更新为 TCP
    expect(await screen.findByText('TCP 192.168.1.50:9100')).toBeTruthy()

    // 改成错误 IP 再保存 → 失败
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '10.0.0.9' } })
    fireEvent.click(screen.getByRole('button', { name: '保存并应用' }))
    expect(await screen.findByText('连接测试失败：无法连接 192.168.1.50:9100')).toBeTruthy()
    // 徽标仍是原生效连接（回滚，前端全局状态不动）
    expect(screen.getByText('TCP 192.168.1.50:9100')).toBeTruthy()
  })

  it('400 ErrorView（参数校验失败）展示错误 message', async () => {
    mocks.setTransport.mockRejectedValue(new ApiError('LF_TRANSPORT_INVALID', '参数校验失败：缺少打印机 IP。', 'tcpHost'))
    renderSettings()
    fireEvent.click(screen.getByRole('radio', { name: 'TCP' }))
    fireEvent.click(screen.getByRole('button', { name: '保存并应用' }))
    expect(await screen.findByText('参数校验失败：缺少打印机 IP。')).toBeTruthy()
  })
})
