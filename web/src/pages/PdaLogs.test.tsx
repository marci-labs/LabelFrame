// @vitest-environment jsdom
// PDA 日志页（迭代 51，AC-02）：查询失败（非 2xx / 解析失败）显示明确错误条，不再静默「暂无日志」；自动刷新保持。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链）；单机降级模式（healthz 失败 → localApi）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen } from '@testing-library/react'
import { AppProvider } from '../state/AppContext'
import { PdaLogs } from './PdaLogs'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    getLogs: vi.fn(),
  },
  local: {
    getLogs: vi.fn(),
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: vi.fn(),
}))
// client 构建语义用例（本页客户端场景为主），显式注入 client 分支
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

function renderPdaLogs() {
  return render(
    <AppProvider>
      <PdaLogs />
    </AppProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  // 单机降级：healthz 探测失败 → serverMode = standalone → 业务 API 走 localApi（本机 WinHost）
  mocks.local.getHostConfig.mockResolvedValue({ serverUrl: '' })
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.server.healthz.mockRejectedValue(new Error('unreachable'))
})

afterEach(() => {
  vi.useRealTimers()
  cleanup()
})

describe('PDA 日志页：查询失败显错（AC-02）', () => {
  it('查询失败（非 2xx）→ 显示错误条，不静默', async () => {
    mocks.local.getLogs.mockRejectedValue(new Error('HTTP 500'))
    renderPdaLogs()
    // 错误条出现（非 ApiError 的兜底文案），不是只有「暂无日志」空态
    expect(await screen.findByText('获取日志失败。')).toBeTruthy()
  })

  it('解析失败（旧版客户端 /api/logs 命中 SPA 回退返回 HTML）→ 显示响应格式异常错误条', async () => {
    // 200 + HTML：fetch 封装按文本回退 → 非数组结果（迭代 51 前会静默当「暂无日志」）
    mocks.local.getLogs.mockResolvedValue('<!DOCTYPE html><html><body>index</body></html>')
    renderPdaLogs()
    expect(await screen.findByText(/响应格式异常/)).toBeTruthy()
  })

  it('成功路径：按行渲染日志表', async () => {
    mocks.local.getLogs.mockResolvedValue([
      { deviceId: 'pda-1', time: '2026-09-11T00:00:00Z', line: '第一行' },
      { deviceId: 'pda-1', time: '2026-09-11T00:00:01Z', line: '第二行' },
    ])
    renderPdaLogs()
    expect(await screen.findByText('第一行')).toBeTruthy()
    expect(screen.getByText('第二行')).toBeTruthy()
    // 设备号出现在设备下拉选项与表格设备列（至少表格一处）
    expect(screen.getAllByText('pda-1').length).toBeGreaterThanOrEqual(2)
  })
})

describe('PDA 日志页：失败后自动刷新保持', () => {
  /** 冲洗挂载链（本机配置 → healthz 探测 → 模式确定 → 首次拉取）与各 promise 微任务。 */
  async function flush() {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
      await vi.advanceTimersByTimeAsync(0)
    })
  }

  async function advance(ms: number) {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(ms)
    })
  }

  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('失败后 5s 继续重试；恢复后错误条消失', async () => {
    mocks.local.getLogs.mockRejectedValueOnce(new Error('network down'))
    mocks.local.getLogs.mockResolvedValue([
      { deviceId: 'pda-1', time: '2026-09-11T00:00:00Z', line: '恢复后的行' },
    ])

    renderPdaLogs()
    await flush()
    expect(mocks.local.getLogs).toHaveBeenCalledTimes(1)
    expect(screen.getByText('获取日志失败。')).toBeTruthy()

    // 失败后 5s 自动刷新保持：到点重试，恢复后错误条消失、日志显示
    await advance(5000)
    expect(mocks.local.getLogs).toHaveBeenCalledTimes(2)
    expect(screen.getByText('恢复后的行')).toBeTruthy()
    expect(screen.queryByText('获取日志失败。')).toBeNull()
  })
})
