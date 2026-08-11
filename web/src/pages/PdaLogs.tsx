// PDA 日志页：设备 / 时间 / 内容，每 5 秒轮询
// 迭代 18：业务 API 跟随模式——服务端 = serverApi（日志中心）；单机降级 = localApi（本机 WinHost 日志）。

import { useEffect, useState } from 'react'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { LogEntry } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'

const POLL_MS = 5000

export function PdaLogs() {
  const { serverMode } = useApp()
  /** 业务 API 跟随模式（unknown 时不轮询，待探测完成）。 */
  const biz = serverMode === 'server' ? serverApi : localApi
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [devices, setDevices] = useState<string[]>([])
  const [filter, setFilter] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [running, setRunning] = useState(true)

  useEffect(() => {
    if (serverMode === 'unknown') return
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | null = null

    const tick = async () => {
      try {
        // 全量拉取（后端返回 id DESC 最新在前，上限 500 条）；
        // 数据量小、5 秒一次，比 since 增量更可靠（避免时间戳边界重复/遗漏）
        const list = await biz.getLogs(filter || undefined)
        if (!stopped) {
          setLogs(list)
          setError(null)
        }
      } catch (err) {
        if (!stopped) setError(err instanceof ApiError ? err.message : '获取日志失败。')
      } finally {
        if (!stopped) timer = setTimeout(() => void tick(), POLL_MS)
      }
    }

    void tick()
    return () => {
      stopped = true
      if (timer) clearTimeout(timer)
    }
  }, [filter, running, serverMode, biz]) // eslint-disable-line react-hooks/exhaustive-deps

  // 设备下拉：从当前日志推导
  useEffect(() => {
    setDevices((prev) => {
      const all = new Set([...prev, ...logs.map((l) => l.deviceId)])
      return [...all].sort()
    })
  }, [logs])

  const clear = () => {
    setLogs([])
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          PDA 日志
          <small>PDA 打印测试日志回传，实时查看分析</small>
        </div>
        <div className="spacer" />
        <select className="input" value={filter} onChange={(ev) => { setFilter(ev.target.value); setLogs([]) }}>
          <option value="">全部设备</option>
          {devices.map((d) => (
            <option key={d} value={d}>
              {d}
            </option>
          ))}
        </select>
        <button className={'btn' + (running ? ' active' : '')} onClick={() => setRunning(!running)} title={running ? '暂停自动刷新' : '恢复自动刷新'}>
          <Icon name={running ? 'preview' : 'refresh'} size={13} />
          {running ? '自动刷新' : '已暂停'}
        </button>
        <button className="btn" onClick={clear}>
          <Icon name="clear" size={13} />
          清空列表
        </button>
      </div>

      {error && (
        <div style={{ padding: '6px 16px', background: 'var(--danger-soft)', color: 'var(--danger)', fontSize: 12 }}>{error}</div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {logs.length === 0 ? (
          <div className="empty">
            <Icon name="logs" />
            <div className="empty-title">暂无日志</div>
            <div className="hint">
              PDA 端打印测试后日志会回传显示于此。
              <br />
              可手动验证：POST /api/logs {'{ deviceId, lines }'}
            </div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 150 }}>设备</th>
                <th style={{ width: 170 }}>时间</th>
                <th>内容</th>
              </tr>
            </thead>
            <tbody>
              {logs.map((l, i) => (
                <tr key={i} style={{ cursor: 'default' }}>
                  <td className="mono">{l.deviceId}</td>
                  <td className="mono">{new Date(l.time).toLocaleString('zh-CN', { hour12: false })}</td>
                  <td style={{ whiteSpace: 'pre-wrap' }}>{l.line}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
