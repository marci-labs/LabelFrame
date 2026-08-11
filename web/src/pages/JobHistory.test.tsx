// @vitest-environment jsdom
// 迭代 18 F6：作业历史页——服务端 / 单机降级列表、空态按模式文案、刷新、徽标区分。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { JobView } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { JobHistory } from './JobHistory'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    getJobs: vi.fn(),
  },
  local: {
    healthz: vi.fn(),
    getJobs: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({ serverApi: mocks.server, localApi: mocks.local, setServerBaseUrl: vi.fn() }))

const JOBS: JobView[] = [
  {
    jobId: 'job-aaa-1',
    requestId: 'req-aaa-1',
    status: 'Completed',
    totalItems: 3,
    completedItems: 3,
    targetDeviceId: 'device-1',
    createdAt: '2026-08-11T10:30:00Z',
  },
  {
    jobId: 'job-bbb-2',
    requestId: 'req-bbb-2',
    status: 'Failed',
    totalItems: 2,
    completedItems: 1,
    failedItems: 1,
    errorMessage: '打印机缺纸',
    targetDeviceId: 'device-2',
    createdAt: '2026-08-11T09:00:00Z',
  },
  {
    jobId: 'job-ccc-3',
    requestId: 'req-ccc-3',
    status: 'Printing',
    totalItems: 5,
    completedItems: 2,
    createdAt: '2026-08-11T08:00:00Z',
  },
]

function renderJobHistory() {
  return render(
    <AppProvider>
      <JobHistory />
    </AppProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: 'http://127.0.0.1:53961', deviceId: 'device-1', deviceName: 'PC' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.server.getJobs.mockResolvedValue(JOBS)
  mocks.local.getJobs.mockResolvedValue(JOBS)
})

afterEach(() => {
  cleanup()
})

describe('作业历史页（迭代 18 F6）', () => {
  it('服务端模式：列表渲染（时间 / requestId / jobId / 目标设备 / 状态 / 完成-失败 / 失败原因），走 serverApi', async () => {
    renderJobHistory()
    expect(await screen.findByText('已完成')).toBeTruthy()
    // 三行作业都渲染
    expect(screen.getByText('失败')).toBeTruthy()
    expect(screen.getByText('打印中')).toBeTruthy()
    // 目标设备列：有值显示设备 ID，无值显示「本机」
    expect(screen.getByText('device-1')).toBeTruthy()
    expect(screen.getByText('本机')).toBeTruthy()
    // 完成-失败张数与失败原因
    expect(screen.getByText(/3\/3/)).toBeTruthy()
    expect(screen.getByText('打印机缺纸')).toBeTruthy()
    // 双 base 守门：服务端模式走 serverApi
    expect(mocks.server.getJobs).toHaveBeenCalledWith(100)
    expect(mocks.local.getJobs).not.toHaveBeenCalled()
  })

  it('状态徽标区分终态 / 进行中', async () => {
    renderJobHistory()
    await screen.findByText('已完成')
    const completed = screen.getByText('已完成').closest('span')
    const printing = screen.getByText('打印中').closest('span')
    expect(completed?.className).toContain('ok')
    expect(printing?.className).toContain('info')
  })

  it('刷新按钮：重新拉取列表', async () => {
    renderJobHistory()
    await screen.findByText('已完成')
    mocks.server.getJobs.mockClear()
    fireEvent.click(screen.getByRole('button', { name: /刷新/ }))
    await waitFor(() => expect(mocks.server.getJobs).toHaveBeenCalledTimes(1))
  })

  it('空态（服务端模式）：提示保留期文案', async () => {
    mocks.server.getJobs.mockResolvedValue([])
    renderJobHistory()
    expect(await screen.findByText('暂无历史作业')).toBeTruthy()
    expect(screen.getByText('终态作业默认保留 30 天，由服务端自动清理。')).toBeTruthy()
  })

  it('单机降级（healthz 失败 → standalone）：列表走 localApi，空态文案为本机不自动清理', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    renderJobHistory()
    // 等 serverMode 探测完成 → standalone → localApi.getJobs
    await waitFor(() => expect(mocks.local.getJobs).toHaveBeenCalledWith(100))
    expect(mocks.server.getJobs).not.toHaveBeenCalled()
    expect(await screen.findByText('已完成')).toBeTruthy()
  })

  it('单机降级空态：本机作业不自动清理文案', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    mocks.local.getJobs.mockResolvedValue([])
    renderJobHistory()
    expect(await screen.findByText('暂无历史作业')).toBeTruthy()
    expect(screen.getByText('本机作业不自动清理。')).toBeTruthy()
  })

  it('加载失败：显示错误信息', async () => {
    mocks.server.getJobs.mockRejectedValue(new Error('network down'))
    renderJobHistory()
    expect(await screen.findByText(/获取作业历史失败/)).toBeTruthy()
  })
})
