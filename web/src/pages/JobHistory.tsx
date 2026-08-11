// 作业历史页（迭代 18 F6）：服务端 / 本机作业列表（GET /api/jobs?limit=100），刷新按钮，终态 / 进行中徽标。
// 单机降级：指向本机时显示本机作业列表（localBase GET /api/jobs，后端 B10 新增）；空态文案按模式区分。

import { useCallback, useEffect, useState } from 'react'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { JobView } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'

const JOB_STATUS_LABEL: Record<string, string> = {
  Pending: '排队中',
  Printing: '打印中',
  Completed: '已完成',
  Failed: '失败',
  Suspended: '已挂起',
  Cancelled: '已取消',
  Claimed: '已领取',
}

const jobLabel = (s: string) => JOB_STATUS_LABEL[s] ?? s
const isTerminal = (s: string) => s === 'Completed' || s === 'Failed' || s === 'Cancelled'

/** 时间列：本地时间 MM-dd HH:mm:ss。 */
function formatTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

export function JobHistory() {
  const app = useApp()
  const { serverMode } = app
  // 业务 API 跟随模式：服务端 = serverApi；单机降级 = localApi（本机 WinHost 作业列表）
  const biz = serverMode === 'server' ? serverApi : localApi
  const [jobs, setJobs] = useState<JobView[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const list = await biz.getJobs(100)
      setJobs(list)
    } catch (err) {
      setJobs([])
      setError(err instanceof ApiError ? err.message : '获取作业历史失败。')
    } finally {
      setLoading(false)
    }
  }, [biz])

  useEffect(() => {
    if (serverMode === 'unknown') return
    void load()
  }, [serverMode, load])

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          作业历史
          <small>最近 100 条作业（服务端队列 / 单机降级本机队列）</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => void load()} disabled={loading || serverMode === 'unknown'} title="重新拉取作业列表">
          <Icon name="refresh" size={13} />
          {loading ? '刷新中…' : '刷新'}
        </button>
      </div>

      {error && (
        <div style={{ padding: '6px 16px', background: 'var(--danger-soft)', color: 'var(--danger)', fontSize: 12 }}>{error}</div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {serverMode === 'unknown' ? (
          <div className="empty">
            <Icon name="data" />
            <div className="empty-title">正在探测连接…</div>
            <div className="hint">正在确认服务端连通性（单机模式将显示本机作业列表）。</div>
          </div>
        ) : !jobs || jobs.length === 0 ? (
          <div className="empty">
            <Icon name="data" />
            <div className="empty-title">暂无历史作业</div>
            <div className="hint">
              {serverMode === 'server'
                ? '终态作业默认保留 30 天，由服务端自动清理。'
                : '本机作业不自动清理。'}
            </div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 150 }}>时间</th>
                <th style={{ width: 120 }}>requestId</th>
                <th style={{ width: 100 }}>jobId</th>
                <th style={{ width: 140 }}>目标设备</th>
                <th style={{ width: 90 }}>状态</th>
                <th style={{ width: 110 }}>完成-失败</th>
                <th>失败原因</th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((j) => (
                <tr key={j.jobId} style={{ cursor: 'default' }}>
                  <td className="mono">{formatTime(j.createdAt)}</td>
                  <td className="mono" style={{ fontSize: 12 }} title={j.requestId}>
                    {j.requestId.slice(0, 8)}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }} title={j.jobId}>
                    {j.jobId.slice(0, 8)}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {j.targetDeviceId ?? '本机'}
                  </td>
                  <td>
                    <span className={'badge ' + (j.status === 'Completed' ? 'ok' : j.status === 'Failed' ? 'err' : isTerminal(j.status) ? 'neutral' : 'info')}>
                      {jobLabel(j.status)}
                    </span>
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {j.completedItems}/{j.totalItems}
                    {(j.failedItems ?? 0) > 0 && (
                      <span style={{ color: 'var(--danger)' }}>（失败 {(j.failedItems ?? 0)}）</span>
                    )}
                  </td>
                  <td style={{ color: 'var(--danger)', fontSize: 12 }}>{j.errorMessage ?? ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
