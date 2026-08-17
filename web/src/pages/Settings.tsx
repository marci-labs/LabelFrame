// 设置页（迭代 18 F2-F4）：服务端地址（机器级配置，保存即生效）+ 连接方式（本机 Client）+ 打印机状态 / 测试打印
// 迭代 22 §2.3：新增「更新与安装包」卡片——列出服务端可用客户端安装包（下载指向 {serverBaseUrl}/api/client-packages/{file}）；单机模式提示需先连接服务端。

import { useCallback, useEffect, useState } from 'react'
import { clientPackageDownloadUrl, localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo, PrinterStatus } from '../lib/api/types'
import { formatSize } from '../lib/download'
import { formatTransport } from '../lib/transport'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'
import { TransportPanel } from '../components/TransportPanel'

export function Settings() {
  const app = useApp()
  const [url, setUrl] = useState(app.baseUrl)
  const [testing, setTesting] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testResult, setTestResult] = useState<{ ok: boolean; msg: string } | null>(null)
  const [saveResult, setSaveResult] = useState<{ ok: boolean; msg: string } | null>(null)
  const [printer, setPrinter] = useState<PrinterStatus | null>(null)
  const [printerLoading, setPrinterLoading] = useState(false)
  const [testPrinting, setTestPrinting] = useState(false)
  const [printResult, setPrintResult] = useState<string | null>(null)

  // 迭代 22 §2.3：更新与安装包——服务端可达时拉取安装包列表；不可达提示需先连接服务端
  const [packages, setPackages] = useState<ClientPackageInfo[] | null>(null)
  const [packagesError, setPackagesError] = useState<string | null>(null)

  useEffect(() => {
    if (!app.connected) {
      setPackages(null)
      setPackagesError(null)
      return
    }
    let on = true
    serverApi
      .listClientPackages()
      .then((list) => {
        if (on) {
          setPackages(list)
          setPackagesError(null)
        }
      })
      .catch((err) => {
        if (on) {
          setPackages([])
          setPackagesError(err instanceof ApiError ? err.message : '获取安装包列表失败。')
        }
      })
    return () => {
      on = false
    }
  }, [app.connected])

  const refreshPrinter = useCallback(async () => {
    setPrinterLoading(true)
    try {
      const s = await localApi.getPrinterStatus()
      setPrinter(s)
    } catch (err) {
      setPrinter(null)
      setPrintResult(err instanceof ApiError ? err.message : '获取打印机状态失败。')
    } finally {
      setPrinterLoading(false)
    }
  }, [])

  useEffect(() => {
    void refreshPrinter()
  }, [refreshPrinter])

  /** 测试连接：探测输入框地址的 /healthz，不保存不生效。 */
  const testConnection = async () => {
    setTesting(true)
    setTestResult(null)
    const ok = await app.checkUrl(url)
    setTestResult(
      ok
        ? { ok: true, msg: '连接成功：该地址可访问服务端。' }
        : { ok: false, msg: '连接失败：请确认服务端已启动，且地址格式正确（http://主机:端口）。' },
    )
    setTesting(false)
  }

  /** 保存：机器级配置持久化 + 立即生效（无需重启），旧客户端回退浏览器本地保存。 */
  const saveAddress = async () => {
    setSaving(true)
    setSaveResult(null)
    const ok = await app.changeBaseUrl(url)
    setSaveResult(
      ok
        ? { ok: true, msg: '已保存到本机配置并立即生效。' }
        : { ok: false, msg: '本机配置接口不可用，已使用浏览器本地保存。' },
    )
    setSaving(false)
  }

  const doTestPrint = async () => {
    setTestPrinting(true)
    setPrintResult(null)
    try {
      const r = await localApi.testPrinter()
      setPrintResult(`测试页已发送（${r.bytes} 字节）。请查看客户端日志确认打印。`)
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
          <small>服务端地址 / 本机连接与打印机</small>
        </div>
      </div>

      {/* 迭代 21+：内容容器与其他页面对齐——flex:1 + overflowY:auto，低屏高可滚动（此前被 .page overflow:hidden 裁剪，小分辨率看不到「打印机」卡片）；minWidth:0 防长文本（%ProgramData% 路径）撑破 */}
      <div style={{ flex: 1, minHeight: 0, minWidth: 0, overflowY: 'auto', padding: 16, display: 'flex', flexDirection: 'column', gap: 14, maxWidth: 640 }}>
        <section className="panel">
          <div className="panel-head">服务端地址</div>
          <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <label className="field">
              服务端地址
              <input className="input mono" value={url} onChange={(ev) => setUrl(ev.target.value)} placeholder="http://127.0.0.1:53961" spellCheck={false} />
            </label>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <button className="btn" onClick={() => void testConnection()} disabled={testing}>
                <Icon name="link" size={13} />
                {testing ? '测试中…' : '测试连接'}
              </button>
              <button className="btn primary" onClick={() => void saveAddress()} disabled={saving}>
                <Icon name="save" size={13} />
                {saving ? '保存中…' : '保存并生效'}
              </button>
              <span className={'conn' + (app.connected ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <span className={'status-dot' + (app.connected ? ' on' : '')} />
                {app.connected ? '服务端已连接' : '服务端未连接（单机模式可用）'}
              </span>
            </div>
            {testResult && (
              <div className={testResult.ok ? 'badge ok' : 'badge err'} style={{ alignSelf: 'flex-start' }}>
                {testResult.msg}
              </div>
            )}
            {saveResult && (
              <div className={saveResult.ok ? 'badge ok' : 'badge err'} style={{ alignSelf: 'flex-start' }}>
                {saveResult.msg}
              </div>
            )}
            <div className="hint">
              服务端为 LabelFrame Server（模板库 / 作业中心 / 设备投递，默认 127.0.0.1:53961）。
              地址保存在本机（%ProgramData%\\LabelFrame\\Client\\settings.json），保存后立即生效、重启保持；
              未安装 / 未启动服务端时自动降级为单机模式（本机 Client 直接打印）。
            </div>
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
              <div className="hint">{printerLoading ? '读取中…' : '未获取到状态（本机客户端可能不支持状态查询，或尚未连接）。'}</div>
            )}
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <button className="btn" onClick={() => void doTestPrint()} disabled={testPrinting}>
                <Icon name="test" size={13} />
                {testPrinting ? '发送中…' : '测试打印'}
              </button>
              {printResult && <span className={printResult.startsWith('测试页已发送') ? 'badge ok' : 'badge err'}>{printResult}</span>}
            </div>
            <div className="hint">
              测试打印发送一张测试页（条码 LABELFRAME-TEST）到本机当前连接。当前连接方式：
              {formatTransport(app.transportConfig) || app.transport || '未知'}（Log 模式无需打印机）。
            </div>
          </div>
        </section>

        <section className="panel">
          <div className="panel-head">
            更新与安装包
            <span className="spacer" style={{ flex: 1 }} />
            <span className={'conn' + (app.connected ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              <span className={'status-dot' + (app.connected ? ' on' : '')} />
              {app.connected ? '服务端已连接' : '单机模式'}
            </span>
          </div>
          <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {!app.connected ? (
              <div className="hint">
                当前未连接服务端（单机模式）。安装包由服务端统一分发，请先在上方「服务端地址」中连接服务端后查看可用安装包。
              </div>
            ) : packages === null ? (
              <div className="hint">加载安装包列表…</div>
            ) : packages.length === 0 ? (
              <div className="hint">
                {packagesError ? `获取安装包列表失败：${packagesError}` : '服务端暂无客户端安装包。可在服务端管理界面「客户端下载」页上传后，从此处下载更新。'}
              </div>
            ) : (
              <>
                <table className="table">
                  <thead>
                    <tr>
                      <th>文件名</th>
                      <th style={{ width: 110 }}>大小</th>
                      <th style={{ width: 140 }}>修改时间</th>
                      <th style={{ width: 90 }}></th>
                    </tr>
                  </thead>
                  <tbody>
                    {packages.map((p) => (
                      <tr key={p.fileName} style={{ cursor: 'default' }}>
                        <td className="mono" style={{ fontSize: 12, wordBreak: 'break-all' }}>
                          {p.fileName}
                        </td>
                        <td className="mono" style={{ fontSize: 12 }}>
                          {formatSize(p.sizeBytes)}
                        </td>
                        <td className="mono" style={{ fontSize: 12 }}>
                          {formatPackageTime(p.modifiedAt)}
                        </td>
                        <td>
                          <a className="btn sm" href={clientPackageDownloadUrl(p.fileName)} title={`从服务端下载 ${p.fileName}`}>
                            <Icon name="download" size={12} />
                            下载
                          </a>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <div className="hint">下载安装包后请自行运行安装（LabelFrame 客户端不自动升级）。安装包由服务端分发：{app.baseUrl}。</div>
              </>
            )}
          </div>
        </section>
      </div>
    </div>
  )
}

/** 修改时间：本地时间 MM-dd HH:mm。 */
function formatPackageTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}
