// 作业历史页（迭代 18 F6）：服务端 / 本机作业列表（GET /api/jobs?limit=100），终态 / 进行中徽标。
// 单机降级：指向本机时显示本机作业列表（localBase GET /api/jobs，后端 B10 新增）；空态文案按模式区分。
// 迭代 48（用户定稿建议组合）：自动轮询——存在进行中（非终态）作业时 1.5s 轮询，列表全终态即停；
// 页面隐藏时暂停，恢复可见立即拉取一次；轮询失败保留既有列表、2s 退避重试；手动「刷新」保留。

import { useCallback, useEffect, useRef, useState } from 'react'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { JobView } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import { isServerUi } from '../lib/uiMode'
import { Icon } from '../components/Icon'

const JOB_STATUS_LABEL: Record<string, string> = {
  Pending: '排队中',
  Printing: '打印中',
  Completed: '已完成',
  Failed: '失败',
  Suspended: '已挂起',
  Cancelled: '已取消',
  Claimed: '已领取',
  Expired: '已过期',
}

const jobLabel = (s: string) => JOB_STATUS_LABEL[s] ?? s
const isTerminal = (s: string) => s === 'Completed' || s === 'Failed' || s === 'Cancelled' || s === 'Expired'

/** 迭代 48：轮询节奏（对齐 DataPrint useJobPolling——1.5s 常规 / 2s 失败退避）。 */
const POLL_INTERVAL_MS = 1500
const POLL_ERROR_RETRY_MS = 2000

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
  /** 轮询定时器（存在进行中作业时续排；全终态即停）。 */
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  /** 最近一次成功列表（失败退避判断是否仍有进行中作业需要重试）。 */
  const jobsRef = useRef<JobView[] | null>(null)

  // 迭代 22 §2.1：作业历史可见性——客户端构建在服务端模式下传本机 deviceId（只看自己的作业）；
  // 服务端构建不传（看全部）；单机降级看本机历史本就不传。
  const deviceFilter = isServerUi || serverMode !== 'server' ? undefined : (app.hostDeviceId ?? undefined)

  const clearTimer = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }, [])

  const load = useCallback(
    async (opts?: { silent?: boolean }) => {
      // 周期轮询在页面隐藏时暂停（恢复可见由 visibilitychange 立即拉取，不走此分支）
      if (opts?.silent && document.hidden) return
      if (!opts?.silent) setLoading(true)
      /** 本轮结束后是否续排下一轮轮询。 */
      let keepPolling = false
      let failed = false
      try {
        const list = await biz.getJobs(100, deviceFilter)
        jobsRef.current = list
        setJobs(list)
        setError(null)
        keepPolling = list.some((j) => !isTerminal(j.status))
      } catch (err) {
        // 轮询失败不清空既有列表（瞬时错误只出横幅）；已知存在进行中作业则退避重试
        failed = true
        setJobs((prev) => prev ?? [])
        setError(err instanceof ApiError ? err.message : '获取作业历史失败。')
        keepPolling = jobsRef.current?.some((j) => !isTerminal(j.status)) ?? false
      } finally {
        if (!opts?.silent) setLoading(false)
      }
      clearTimer()
      if (keepPolling) {
        timerRef.current = setTimeout(() => void load({ silent: true }), failed ? POLL_ERROR_RETRY_MS : POLL_INTERVAL_MS)
      }
    },
    [biz, deviceFilter, clearTimer],
  )

  useEffect(() => {
    if (serverMode === 'unknown') return
    jobsRef.current = null
    void load()
    // 页面隐藏暂停轮询；恢复可见立即拉取一次（是否续排由结果决定）
    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        clearTimer()
        void load({ silent: true })
      }
    }
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      document.removeEventListener('visibilitychange', onVisibility)
      clearTimer()
    }
  }, [serverMode, load, clearTimer])

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          作业历史
          <small>最近 100 条作业（服务端队列 / 单机降级本机队列）；存在进行中作业时自动刷新</small>
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
