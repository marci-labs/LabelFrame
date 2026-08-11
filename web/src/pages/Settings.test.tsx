// @vitest-environment jsdom
// 迭代 17：设置页 = 后端地址 + 连接测试（服务端前端；连接方式 / 打印机分组已迁至客户端本机）

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { AppProvider } from '../state/AppContext'
import { Settings } from './Settings'

const mocks = vi.hoisted(() => ({
  healthz: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({ api: mocks }))

function renderSettings() {
  return render(
    <AppProvider>
      <Settings />
    </AppProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // Server 的 healthz 无 transport 字段（服务端无传输概念）
  mocks.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
})

afterEach(() => {
  cleanup()
})

describe('设置页（迭代 17：服务端前端）', () => {
  it('只保留「后端连接」分组：后端地址 + 测试连接；无连接方式 / 打印机分组', () => {
    renderSettings()
    expect(screen.getByLabelText('后端地址')).toBeTruthy()
    expect(screen.getByRole('button', { name: /测试连接/ })).toBeTruthy()
    expect(screen.queryByText('连接方式')).toBeNull()
    expect(screen.queryByText('打印机')).toBeNull()
    // 无连接模式单选 / 传输参数
    expect(screen.queryByRole('radio')).toBeNull()
    expect(screen.queryByLabelText('打印机 IP / 主机名')).toBeNull()
    expect(screen.queryByRole('button', { name: /保存并应用/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /测试打印/ })).toBeNull()
  })

  it('测试连接成功：healthz 通过后显示「已连接」与成功提示', async () => {
    renderSettings()
    fireEvent.click(screen.getByRole('button', { name: /测试连接/ }))
    expect(await screen.findByText('连接成功。')).toBeTruthy()
    expect(screen.getByText('已连接')).toBeTruthy()
  })

  it('Server healthz 不返回 transport：状态灯仍显示已连接（无传输概念时只显示连接状态）', async () => {
    renderSettings()
    fireEvent.click(screen.getByRole('button', { name: /测试连接/ }))
    expect(await screen.findByText('已连接')).toBeTruthy()
  })

  it('测试连接失败（healthz 拒绝）：提示失败原因', async () => {
    mocks.healthz.mockRejectedValue(new Error('network down'))
    renderSettings()
    fireEvent.click(screen.getByRole('button', { name: /测试连接/ }))
    expect(await screen.findByText(/连接失败：请确认后端已启动/)).toBeTruthy()
    expect(screen.getByText('未连接')).toBeTruthy()
  })

  it('修改地址后测试连接：新地址持久化（localStorage）并重新探测', async () => {
    renderSettings()
    fireEvent.change(screen.getByLabelText('后端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    fireEvent.click(screen.getByRole('button', { name: /测试连接/ }))
    expect(await screen.findByText('连接成功。')).toBeTruthy()
    expect(window.localStorage.getItem('labelframe.baseUrl')).toBe('http://192.168.1.10:53961')
    expect(mocks.healthz).toHaveBeenCalledTimes(1)
  })
})
