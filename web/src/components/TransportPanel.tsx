// 连接方式（迭代 15 §6.2 恢复，迭代 18 F3）：设置页完整面板 + DataPrint 顶部快速切换。
// 交互：先测试后生效（非 testOnly 后端先测试再切换持久化）；失败返回当前连接、前端全局状态不动。
// 全部走 localApi（本机 Client 127.0.0.1:53960 / 页面来源）。

import { useCallback, useEffect, useState } from 'react'
import { localApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { TransportApplyRequest, TransportMode, TransportParams, ZebraKind } from '../lib/api/types'
import { ALL_TRANSPORT_MODES, MODE_LABELS, ZEBRA_KIND_LABELS, defaultParams, formatTransport } from '../lib/transport'
import { useApp } from '../state/AppContext'
import { Icon } from './Icon'

/** 表单状态：当前候选模式 + 参数（只维护当前生效方式的参数，切换模式丢弃其它模式输入）。 */
function useTransportForm() {
  const { transportConfig } = useApp()
  const [mode, setMode] = useState<TransportMode>(transportConfig?.mode ?? 'Log')
  const [params, setParams] = useState<TransportParams>(() => defaultParams(transportConfig?.mode ?? 'Log', transportConfig))
  const [touched, setTouched] = useState(false)

  // 后端探测 / 连接测试刷新全局配置后，表单与当前生效连接同步（未手动编辑过时）；用户编辑后不再覆盖
  useEffect(() => {
    const cur = transportConfig?.mode ?? 'Log'
    if (!touched && mode !== cur) {
      setMode(cur)
      setParams(defaultParams(cur, transportConfig))
    }
  }, [transportConfig]) // eslint-disable-line react-hooks/exhaustive-deps

  const switchMode = (m: TransportMode) => {
    setTouched(true)
    setMode(m)
    setParams(defaultParams(m, transportConfig))
  }

  const setParam = useCallback((key: keyof TransportParams, value: string | number | undefined) => {
    setTouched(true)
    setParams((p) => ({ ...p, [key]: value }))
  }, [])

  /** 拼 POST /api/transport 请求体（参数平铺，空值省略）。 */
  const buildRequest = useCallback((): TransportApplyRequest => {
    const req: TransportApplyRequest = { mode }
    if (mode === 'Tcp') {
      req.tcpHost = params.tcpHost?.trim() || undefined
      req.tcpPort = params.tcpPort
    } else if (mode === 'WindowsDriver') {
      req.printerName = params.printerName?.trim() || undefined
    } else if (mode === 'Zebra') {
      req.zebraKind = params.zebraKind ?? 'Tcp'
      if (req.zebraKind === 'Usb') {
        req.zebraUsbName = params.zebraUsbName?.trim() || undefined
      } else if (req.zebraKind === 'Driver') {
        req.printerName = params.printerName?.trim() || undefined
      } else {
        req.tcpHost = params.tcpHost?.trim() || undefined
        req.tcpPort = params.tcpPort
      }
    }
    return req
  }, [mode, params])

  return { mode, params, switchMode, setParam, buildRequest }
}

/** 当前模式参数输入区（Log 无参数）。 */
export function TransportParamsEditor({
  mode,
  params,
  setParam,
}: {
  mode: TransportMode
  params: TransportParams
  setParam: (key: keyof TransportParams, value: string | number | undefined) => void
}) {
  if (mode === 'Log') {
    return <div className="hint">Log（模拟）：不连接打印机，作业渲染后保存 PNG 到本地目录，用于无打印机联调。</div>
  }
  if (mode === 'Tcp') {
    return (
      <>
        <label className="field" style={{ maxWidth: 240 }}>
          打印机 IP / 主机名
          <input
            className="input mono"
            value={params.tcpHost ?? ''}
            onChange={(ev) => setParam('tcpHost', ev.target.value)}
            placeholder="192.168.1.50"
            spellCheck={false}
          />
        </label>
        <label className="field" style={{ maxWidth: 120 }}>
          端口
          <input
            className="input mono"
            type="number"
            min={1}
            max={65535}
            value={params.tcpPort ?? 9100}
            onChange={(ev) => setParam('tcpPort', ev.target.value === '' ? undefined : Number(ev.target.value))}
          />
        </label>
      </>
    )
  }
  if (mode === 'WindowsDriver') {
    return (
      <label className="field" style={{ maxWidth: 340 }}>
        打印机名称
        <input
          className="input mono"
          value={params.printerName ?? ''}
          onChange={(ev) => setParam('printerName', ev.target.value)}
          placeholder="ZDesigner ZD421-203dpi ZPL"
          spellCheck={false}
        />
      </label>
    )
  }
  // Zebra
  const kind = params.zebraKind ?? 'Tcp'
  return (
    <>
      <label className="field" style={{ maxWidth: 220 }}>
        Zebra 连接方式
        <select className="input" value={kind} onChange={(ev) => setParam('zebraKind', ev.target.value as ZebraKind)}>
          {(Object.keys(ZEBRA_KIND_LABELS) as ZebraKind[]).map((k) => (
            <option key={k} value={k}>
              {ZEBRA_KIND_LABELS[k]}
            </option>
          ))}
        </select>
      </label>
      {kind === 'Tcp' && (
        <>
          <label className="field" style={{ maxWidth: 240 }}>
            打印机 IP / 主机名
            <input
              className="input mono"
              value={params.tcpHost ?? ''}
              onChange={(ev) => setParam('tcpHost', ev.target.value)}
              placeholder="192.168.1.50"
              spellCheck={false}
            />
          </label>
          <label className="field" style={{ maxWidth: 120 }}>
            端口
            <input
              className="input mono"
              type="number"
              min={1}
              max={65535}
              value={params.tcpPort ?? 9100}
              onChange={(ev) => setParam('tcpPort', ev.target.value === '' ? undefined : Number(ev.target.value))}
            />
          </label>
        </>
      )}
      {kind === 'Driver' && (
        <label className="field" style={{ maxWidth: 340 }}>
          打印机名称
          <input
            className="input mono"
            value={params.printerName ?? ''}
            onChange={(ev) => setParam('printerName', ev.target.value)}
            placeholder="ZDesigner ZD421-203dpi ZPL"
            spellCheck={false}
          />
        </label>
      )}
      {kind === 'Usb' && (
        <label className="field" style={{ maxWidth: 260 }}>
          USB 设备名
          <input
            className="input mono"
            value={params.zebraUsbName ?? ''}
            onChange={(ev) => setParam('zebraUsbName', ev.target.value)}
            placeholder="留空 = 自动发现第一台"
            spellCheck={false}
          />
        </label>
      )}
    </>
  )
}

/** 设置页完整面板：当前生效连接 + 模式单选 + 参数 + 测试连接 / 保存并应用。 */
export function TransportPanel() {
  const app = useApp()
  const form = useTransportForm()
  const [busy, setBusy] = useState<'test' | 'save' | null>(null)
  const [result, setResult] = useState<{ ok: boolean; msg: string } | null>(null)

  const run = async (testOnly: boolean) => {
    setBusy(testOnly ? 'test' : 'save')
    setResult(null)
    try {
      const r = testOnly ? await localApi.testTransport(form.buildRequest()) : await localApi.setTransport(form.buildRequest())
      if (r.ok) {
        // 切换成功后立即用响应 config 更新全局状态（不依赖 healthz 10s 轮询）
        app.applyTransportConfig(r.config)
        if (!testOnly) app.setStatus(`连接已切换：${r.message}`)
        setResult({ ok: true, msg: r.message })
      } else {
        // 200 + ok:false：测试失败不切换，config 仍是当前生效连接，全局状态不动
        setResult({ ok: false, msg: r.message })
      }
    } catch (err) {
      setResult({ ok: false, msg: err instanceof ApiError ? err.message : '保存连接失败。' })
    } finally {
      setBusy(null)
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span className="hint">当前生效连接</span>
        <span className={'badge ' + (app.connected ? 'ok' : '')}>
          {formatTransport(app.transportConfig) || app.transport || '未知'}
        </span>
        {!app.transportConfig && <span className="hint">（旧版客户端无连接管理端点，仅显示健康检查模式）</span>}
      </div>

      <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap' }}>
        {ALL_TRANSPORT_MODES.map((m) => (
          <label key={m} className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
            <input type="radio" name="transport-mode" checked={form.mode === m} onChange={() => form.switchMode(m)} />
            {MODE_LABELS[m]}
          </label>
        ))}
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <TransportParamsEditor mode={form.mode} params={form.params} setParam={form.setParam} />
      </div>

      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <button className="btn" onClick={() => void run(true)} disabled={busy !== null}>
          <Icon name="link" size={13} />
          {busy === 'test' ? '测试中…' : '测试连接'}
        </button>
        <button className="btn primary" onClick={() => void run(false)} disabled={busy !== null}>
          <Icon name="save" size={13} />
          {busy === 'save' ? '应用中…' : '保存并应用'}
        </button>
        {result && <span className={result.ok ? 'badge ok' : 'badge err'}>{result.msg}</span>}
      </div>

      <div className="hint">切换为先测试后生效：测试失败不切换并提示原因；保存后重启客户端仍保留该连接。</div>
    </div>
  )
}

/** DataPrint 顶部快速切换：当前连接徽标 + 模式下拉 + 参数内联 + 应用（测试+生效）。 */
export function TransportQuickSwitch() {
  const app = useApp()
  const form = useTransportForm()
  const [busy, setBusy] = useState(false)
  const [errMsg, setErrMsg] = useState<string | null>(null)

  const apply = async () => {
    setBusy(true)
    setErrMsg(null)
    try {
      const r = await localApi.setTransport(form.buildRequest())
      if (r.ok) {
        app.applyTransportConfig(r.config)
        app.setStatus(r.message)
      } else {
        setErrMsg(r.message)
      }
    } catch (err) {
      setErrMsg(err instanceof ApiError ? err.message : '切换连接失败。')
    } finally {
      setBusy(false)
    }
  }

  const modes = app.transportConfig?.availableModes ?? ALL_TRANSPORT_MODES
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
      <span className={'conn' + (app.connected ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
        <span className={'status-dot' + (app.connected ? ' on' : '')} />
        {formatTransport(app.transportConfig) || app.transport || '未连接'}
      </span>
      <select
        className="input"
        value={form.mode}
        onChange={(ev) => form.switchMode(ev.target.value as TransportMode)}
        title="快速切换连接方式（应用 = 测试后生效）"
      >
        {modes.map((m) => (
          <option key={m} value={m}>
            {MODE_LABELS[m]}
          </option>
        ))}
      </select>
      <TransportParamsEditor mode={form.mode} params={form.params} setParam={form.setParam} />
      <button className="btn sm" onClick={() => void apply()} disabled={busy}>
        <Icon name="link" size={12} />
        {busy ? '应用中…' : '应用'}
      </button>
      {errMsg && <span className="error-text" style={{ fontSize: 12 }}>{errMsg}</span>}
    </div>
  )
}
