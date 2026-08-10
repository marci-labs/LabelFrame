// 设置页：后端地址 / 连接方式（迭代 15）/ 打印机状态 / 测试打印

import { useCallback, useEffect, useState } from 'react'
import { api } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { PrinterStatus } from '../lib/api/types'
import { formatTransport } from '../lib/transport'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'
import { TransportPanel } from '../components/TransportPanel'

export function Settings() {
  const app = useApp()
  const [url, setUrl] = useState(app.baseUrl)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ ok: boolean; msg: string } | null>(null)
  const [printer, setPrinter] = useState<PrinterStatus | null>(null)
  const [printerLoading, setPrinterLoading] = useState(false)
  const [testPrinting, setTestPrinting] = useState(false)
  const [printResult, setPrintResult] = useState<string | null>(null)

  const refreshPrinter = useCallback(async () => {
    setPrinterLoading(true)
    try {
      const s = await api.printerStatus()
      setPrinter(s)
    } catch (err) {
      setPrinter(null)
      setPrintResult(err instanceof ApiError ? err.message : '获取打印机状态失败。')
    } finally {
      setPrinterLoading(false)
    }
  }, [])

  const testConnection = useCallback(async () => {
    setTesting(true)
    setTestResult(null)
    app.changeBaseUrl(url)
    const ok = await app.checkConnection()
    setTestResult(
      ok
        ? { ok: true, msg: `连接成功 · 服务：${formatTransport(app.transportConfig) || app.transport || '正常'}` }
        : { ok: false, msg: '连接失败：请确认后端已启动，且地址格式正确（http://主机:端口）。' },
    )
    setTesting(false)
    if (ok) {
      void refreshPrinter()
    }
  }, [url, app, refreshPrinter])

  useEffect(() => {
    if (app.connected) {
      void refreshPrinter()
    }
  }, [app.connected, refreshPrinter])

  const doTestPrint = async () => {
    setTestPrinting(true)
    setPrintResult(null)
    try {
      const r = await api.printerTest()
      setPrintResult(`测试页已发送（${r.bytes} 字节）。请查看后端日志确认打印。`)
      void refreshPrinter()
    } catch (err) {
      setPrintResult(err instanceof ApiError ? err.message : '发送测试页失败。')
    } finally {
      setTestPrinting(false)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          设置
          <small>连接与打印机</small>
        </div>
      </div>

      <div style={{ padding: 16, display: 'flex', flexDirection: 'column', gap: 14, maxWidth: 640 }}>
        <section className="panel">
          <div className="panel-head">后端连接</div>
          <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <label className="field">
              后端地址
              <input className="input mono" value={url} onChange={(ev) => setUrl(ev.target.value)} placeholder="http://127.0.0.1:53960" spellCheck={false} />
            </label>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <button className="btn primary" onClick={() => void testConnection()} disabled={testing}>
                <Icon name="link" size={13} />
                {testing ? '测试中…' : '测试连接'}
              </button>
              <span className={'conn' + (app.connected ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <span className={'status-dot' + (app.connected ? ' on' : '')} />
                {app.connected ? '已连接' : '未连接'}
              </span>
            </div>
            {testResult && (
              <div className={testResult.ok ? 'badge ok' : 'badge err'} style={{ alignSelf: 'flex-start' }}>
                {testResult.msg}
              </div>
            )}
            <div className="hint">单机模式：后端为 LabelFrame WinHost 单机服务，默认 127.0.0.1:53960。地址可指向其它机器（需可访问）。</div>
          </div>
        </section>

        <section className="panel">
          <div className="panel-head">连接方式</div>
          <div className="panel-body">
            <TransportPanel />
          </div>
        </section>

        <section className="panel">
          <div className="panel-head">
            打印机
            <span className="spacer" style={{ flex: 1 }} />
            <button className="btn sm" onClick={() => void refreshPrinter()} disabled={printerLoading}>
              <Icon name="refresh" size={12} />
              刷新
            </button>
          </div>
          <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {printer ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                  <span className={'badge ' + (printer.isOnline ? 'ok' : 'err')}>
                    <span className="status-dot" style={{ background: printer.isOnline ? 'var(--ok)' : 'var(--danger)' }} />
                    {printer.isOnline ? '在线' : '离线'}
                  </span>
                  {printer.isPaperOut && <span className="badge warn">缺纸</span>}
                  {printer.isPaused && <span className="badge warn">已暂停</span>}
                </div>
                <div className="hint">{printer.message || '（无附加信息）'}</div>
              </div>
            ) : (
              <div className="hint">{printerLoading ? '读取中…' : '未获取到状态（后端可能不支持状态查询，或尚未连接）。'}</div>
            )}
            <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <button className="btn" onClick={() => void doTestPrint()} disabled={testPrinting || !app.connected}>
                <Icon name="test" size={13} />
                {testPrinting ? '发送中…' : '测试打印'}
              </button>
              {printResult && <span className={printResult.startsWith('测试页已发送') ? 'badge ok' : 'badge err'}>{printResult}</span>}
            </div>
            <div className="hint">
              测试打印发送一张测试页（条码 LABELFRAME-TEST）。当前传输模式：
              {formatTransport(app.transportConfig) || app.transport || '未知'}（Log 模式无需打印机）。
            </div>
          </div>
        </section>
      </div>
    </div>
  )
}
