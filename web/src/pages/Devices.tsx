// 在线设备页（迭代 20，Server UI 专用）：GET /api/devices 每 5s 自动刷新，
// 列表显示 deviceId / 名称 / lastIp / 在线状态 / 最近心跳；点击设备设为「数据与打印」默认目标
// （localStorage labelframe.defaultTargetDeviceId，AppContext 共享状态，跨页联动）。

import { useEffect, useState } from 'react'
import { serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { DeviceView } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'

const POLL_MS = 5000

const deviceStatusLabel = (s: string) => (s === 'Online' ? '在线' : '离线')

/** 最近心跳：本地时间 MM-dd HH:mm:ss。 */
function formatTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

export function Devices() {
  const app = useApp()
  const [devices, setDevices] = useState<DeviceView[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [running, setRunning] = useState(true)

  useEffect(() => {
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | null = null

    const tick = async () => {
      try {
        // 全量轮询替换（在线窗口 30s + 2s 偏差，5s 轮询下最坏约 37s 翻转；不要求即时）
        const list = await serverApi.listDevices()
        if (!stopped) {
          setDevices(list)
          setError(null)
        }
      } catch (err) {
        if (!stopped) setError(err instanceof ApiError ? err.message : '获取设备列表失败。')
      } finally {
        if (!stopped) timer = setTimeout(() => void tick(), POLL_MS)
      }
    }

    void tick()
    return () => {
      stopped = true
      if (timer) clearTimeout(timer)
    }
  }, [running])

  /** 点击设备设为数据与打印默认目标（仅在线设备可设，避免与「仅在线可选」语义冲突）；页面内提示 + 状态栏/日志。 */
  const pick = (d: DeviceView) => {
    if (d.status !== 'Online') {
      const msg = `设备「${d.name || d.deviceId}」当前离线，无法设为默认目标。`
      setNotice(msg)
      app.setStatus(msg)
      return
    }
    const msg = `已将「${d.name || d.deviceId}」设为数据与打印默认目标。`
    setNotice(msg)
    app.setDefaultTargetDeviceId(d.deviceId)
    app.setStatus(msg)
  }

  const isDefault = (d: DeviceView) => app.defaultTargetDeviceId === d.deviceId

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          在线设备
          <small>设备目录（含 lastIp 与在线状态），点击设备设为数据与打印默认目标</small>
        </div>
        <div className="spacer" />
        <button
          className={'btn' + (running ? ' active' : '')}
          onClick={() => setRunning(!running)}
          title={running ? '暂停自动刷新' : '恢复自动刷新'}
        >
          <Icon name={running ? 'preview' : 'refresh'} size={13} />
          {running ? '自动刷新' : '已暂停'}
        </button>
        <button className="btn" onClick={() => void serverApi.listDevices().then(setDevices).catch((err) => setError(err instanceof ApiError ? err.message : '获取设备列表失败。'))} title="立即刷新">
          <Icon name="refresh" size={13} />
          刷新
        </button>
      </div>

      {error && (
        <div style={{ padding: '6px 16px', background: 'var(--danger-soft)', color: 'var(--danger)', fontSize: 12 }}>{error}</div>
      )}
      {notice && (
        <div style={{ padding: '6px 16px', background: 'var(--accent-soft)', color: 'var(--accent)', fontSize: 12 }}>{notice}</div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {devices.length === 0 ? (
          <div className="empty">
            <Icon name="grid" />
            <div className="empty-title">暂无设备</div>
            <div className="hint">
              客户端（LabelFrame Client）安装并连接服务端后，设备会出现在这里。
              <br />
              在线状态由心跳判定（30s 窗口），页面每 5s 自动刷新。
            </div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 210 }}>deviceId</th>
                <th>名称</th>
                <th style={{ width: 150 }}>lastIp</th>
                <th style={{ width: 90 }}>在线状态</th>
                <th style={{ width: 160 }}>最近心跳</th>
                <th style={{ width: 110 }}></th>
              </tr>
            </thead>
            <tbody>
              {devices.map((d) => (
                <tr
                  key={d.deviceId}
                  className={isDefault(d) ? 'selected' : undefined}
                  style={{ cursor: 'pointer' }}
                  onClick={() => pick(d)}
                  title={isDefault(d) ? '当前为数据与打印默认目标；点击其他设备可切换' : '点击设为数据与打印默认目标'}
                >
                  <td className="mono" style={{ fontSize: 12 }}>
                    {d.deviceId}
                  </td>
                  <td>
                    {isDefault(d) && (
                      <span className="badge info" style={{ marginRight: 6 }}>
                        默认
                      </span>
                    )}
                    {d.name || '—'}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {d.lastIp || '—'}
                  </td>
                  <td>
                    <span className={'badge ' + (d.status === 'Online' ? 'ok' : 'err')}>
                      <span className="status-dot" style={{ background: d.status === 'Online' ? 'var(--ok)' : 'var(--danger)' }} />
                      {deviceStatusLabel(d.status)}
                    </span>
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatTime(d.lastSeenAt)}
                  </td>
                  <td>
                    {d.status === 'Online' ? (
                      <button className="btn sm" onClick={(ev) => { ev.stopPropagation(); pick(d) }}>
                        <Icon name="link" size={12} />
                        设为默认
                      </button>
                    ) : (
                      <span className="hint" style={{ fontSize: 12 }}>—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
