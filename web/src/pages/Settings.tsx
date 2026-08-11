// 设置页：后端地址 + 连接测试
// 迭代 17：服务端前端——「连接方式」「打印机」分组迁至客户端本机（托盘 / 本机小页面），此处仅保留后端连接。

import { useState } from 'react'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'

export function Settings() {
  const app = useApp()
  const [url, setUrl] = useState(app.baseUrl)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ ok: boolean; msg: string } | null>(null)

  const testConnection = async () => {
    setTesting(true)
    setTestResult(null)
    app.changeBaseUrl(url)
    const ok = await app.checkConnection()
    setTestResult(ok ? { ok: true, msg: '连接成功。' } : { ok: false, msg: '连接失败：请确认后端已启动，且地址格式正确（http://主机:端口）。' })
    setTesting(false)
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          设置
          <small>后端连接</small>
        </div>
      </div>

      <div style={{ padding: 16, display: 'flex', flexDirection: 'column', gap: 14, maxWidth: 640 }}>
        <section className="panel">
          <div className="panel-head">后端连接</div>
          <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <label className="field">
              后端地址
              <input className="input mono" value={url} onChange={(ev) => setUrl(ev.target.value)} placeholder="http://127.0.0.1:53961" spellCheck={false} />
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
            <div className="hint">
              服务端模式：后端为 LabelFrame Server（模板 / 作业 / 设备投递，默认 127.0.0.1:53961）。
              指向单机 LabelFrame WinHost（127.0.0.1:53960）时自动降级为单机模式（无设备选择，本机直接打印）。
            </div>
          </div>
        </section>
      </div>
    </div>
  )
}
