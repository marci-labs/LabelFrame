// 设置页（迭代 18 F2-F4）：服务端地址（机器级配置，保存即生效）+ 连接方式（本机 Client）+ 打印机状态 / 测试打印
// 迭代 22 §2.3：新增「更新与安装包」卡片——列出服务端可用客户端安装包（下载指向 {serverBaseUrl}/api/client-packages/{file}）；单机模式提示需先连接服务端。
// 迭代 23 §5.6：新增「插件管理」卡片（置于「更新与安装包」之下）——服务端可用插件区（仅 valid 可安装，安装 = 下载 blob → 本机 WinHost multipart）+ 已安装插件区（始终渲染，徽标 + 卸载）。
// 迭代 45：内容容器改多列自适应网格（决策 B）——宽窗多列、窄窗单列，替代原固定 640 左对齐窄列。

import { useCallback, useEffect, useState } from 'react'
import { clientPackageDownloadUrl, localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo, InstalledPluginInfo, PluginPackageInfo, PrinterStatus, PrintSettings } from '../lib/api/types'
import { formatSize } from '../lib/download'
import { checkForUpdate } from '../lib/update'
import { formatTransport } from '../lib/transport'
import { pluginPackageTooLarge } from '../lib/pluginLimits'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { TransportPanel } from '../components/TransportPanel'

export function Settings() {
  const app = useApp()
  const [url, setUrl] = useState(app.baseUrl)
  // 迭代 73（#108 决议 1）：连接配置低频收纳——「连接方式」默认折叠为当前连接摘要一行，点击展开完整编辑区
  const [transportOpen, setTransportOpen] = useState(false)
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

  // 迭代 23 §5.6：插件管理——服务端可用插件区（四态：加载中 / 空 / 错误[含旧 Server 404] / 单机模式）
  const [pluginPackages, setPluginPackages] = useState<PluginPackageInfo[] | null>(null)
  const [pluginPackagesError, setPluginPackagesError] = useState<string | null>(null)
  const [pluginPackagesOldServer, setPluginPackagesOldServer] = useState(false)
  // 已安装插件区（始终渲染，不依赖服务端；旧 WinHost 404 → 版本提示）
  const [installedPlugins, setInstalledPlugins] = useState<InstalledPluginInfo[] | null>(null)
  const [installedPluginsError, setInstalledPluginsError] = useState<string | null>(null)
  const [installedOldWinHost, setInstalledOldWinHost] = useState(false)
  const [installedPluginsLoading, setInstalledPluginsLoading] = useState(false)
  // 行内操作态与提示
  const [installing, setInstalling] = useState<string | null>(null)
  const [uninstalling, setUninstalling] = useState<string | null>(null)
  const [pluginNotice, setPluginNotice] = useState<string | null>(null)
  const [pluginError, setPluginError] = useState<string | null>(null)
  // 迭代 93（#151 F-04）：插件覆盖安装 / 卸载确认改自研 Modal（复用工作台删除确认模式），替代原生 confirm 弹窗
  const [confirmOverwrite, setConfirmOverwrite] = useState<PluginPackageInfo | null>(null)
  const [confirmUninstall, setConfirmUninstall] = useState<InstalledPluginInfo | null>(null)

  // 迭代 24 §4.4：打印批次——WinHost /api/host/print-settings（用户级持久化，保存即生效）；旧 WinHost 404 → 版本提示
  const [printSettings, setPrintSettings] = useState<PrintSettings | null>(null)
  const [printSettingsError, setPrintSettingsError] = useState<string | null>(null)
  const [printSettingsOldWinHost, setPrintSettingsOldWinHost] = useState(false)
  const [batchSaving, setBatchSaving] = useState(false)
  const [batchSaveResult, setBatchSaveResult] = useState<{ ok: boolean; msg: string } | null>(null)

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

  // 可用插件区：服务端可达时拉取（与「更新与安装包」同构；404 = 旧 Server 无此端点）
  useEffect(() => {
    if (!app.connected) {
      setPluginPackages(null)
      setPluginPackagesError(null)
      setPluginPackagesOldServer(false)
      return
    }
    let on = true
    serverApi
      .listPluginPackages()
      .then((list) => {
        if (on) {
          setPluginPackages(list)
          setPluginPackagesError(null)
          setPluginPackagesOldServer(false)
        }
      })
      .catch((err) => {
        if (on) {
          setPluginPackages([])
          setPluginPackagesError(err instanceof ApiError ? err.message : '获取可用插件列表失败。')
          setPluginPackagesOldServer(err instanceof ApiError && err.code === 'HTTP_404')
        }
      })
    return () => {
      on = false
    }
  }, [app.connected])

  // 已安装插件区：挂载即拉（单机模式下也可查看 / 卸载）；安装 / 卸载成功后刷新
  const refreshInstalledPlugins = useCallback(async () => {
    setInstalledPluginsLoading(true)
    try {
      setInstalledPlugins(await localApi.listInstalledPlugins())
      setInstalledPluginsError(null)
      setInstalledOldWinHost(false)
    } catch (err) {
      setInstalledPlugins([])
      setInstalledPluginsError(err instanceof ApiError ? err.message : '获取已安装插件列表失败。')
      setInstalledOldWinHost(err instanceof ApiError && err.code === 'HTTP_404')
    } finally {
      setInstalledPluginsLoading(false)
    }
  }, [])

  useEffect(() => {
    void refreshInstalledPlugins()
  }, [refreshInstalledPlugins])

  // 打印批次：挂载即拉（WinHost 专属端点；404 = 旧客户端无此端点 → 版本提示，不渲染表单）
  useEffect(() => {
    let on = true
    localApi
      .getPrintSettings()
      .then((s) => {
        if (on) {
          setPrintSettings(s)
          setPrintSettingsError(null)
          setPrintSettingsOldWinHost(false)
        }
      })
      .catch((err) => {
        if (on) {
          setPrintSettings(null)
          setPrintSettingsError(err instanceof ApiError ? err.message : '获取批次设置失败。')
          setPrintSettingsOldWinHost(err instanceof ApiError && err.code === 'HTTP_404')
        }
      })
    return () => {
      on = false
    }
  }, [])

  /** 安装：下载 blob → 保留原始文件名 multipart 提交本机 WinHost → 提示重启生效 + 刷新已安装列表。
   *  覆盖安装（已安装同 pluginId）先经自研 Modal 确认——确认后带 overwrite 直装。 */
  const installPlugin = async (p: PluginPackageInfo, opts?: { overwrite?: boolean }) => {
    setPluginError(null)
    setPluginNotice(null)
    // 覆盖安装确认（已安装同 pluginId；已安装列表加载失败则不判重，后端覆盖语义兜底）
    if (!opts?.overwrite && p.pluginId) {
      const existing = installedPlugins?.find((i) => i.pluginId === p.pluginId)
      if (existing) {
        setConfirmOverwrite(p)
        return
      }
    }
    setInstalling(p.fileName)
    try {
      const { blob, filename } = await serverApi.downloadPluginPackage(p.fileName)
      const file = new File([blob], filename)
      const res = await localApi.installPlugin(file)
      setPluginNotice(res.message) // 后端 message 已含「重启客户端后生效」
      void refreshInstalledPlugins()
    } catch (err) {
      setPluginError(err instanceof ApiError ? err.message : '安装失败。')
    } finally {
      setInstalling(null)
    }
  }

  /** 卸载：本机 WinHost 删目录 → 提示重启生效 + 刷新（确认由自研 Modal 承担）。 */
  const uninstallPlugin = async (plugin: InstalledPluginInfo) => {
    setUninstalling(plugin.pluginId)
    setPluginError(null)
    setPluginNotice(null)
    try {
      const res = await localApi.uninstallPlugin(plugin.pluginId)
      setPluginNotice(res.message) // 后端 message 已含「重启客户端后生效」
      void refreshInstalledPlugins()
    } catch (err) {
      setPluginError(err instanceof ApiError ? err.message : '卸载失败。')
    } finally {
      setUninstalling(null)
    }
  }

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
        : { ok: false, msg: '当前客户端版本较旧：地址已保存在本浏览器中（建议升级客户端）。' },
    )
    setSaving(false)
  }

  /** 保存批次设置：POST /api/host/print-settings 持久化并立即生效（无需重启）；失败展示后端中文 message。 */
  const savePrintSettings = async () => {
    if (!printSettings) return
    setBatchSaving(true)
    setBatchSaveResult(null)
    try {
      await localApi.setPrintSettings(printSettings)
      setBatchSaveResult({ ok: true, msg: '已保存并立即生效。' })
    } catch (err) {
      setBatchSaveResult({ ok: false, msg: err instanceof ApiError ? err.message : '保存批次设置失败。' })
    } finally {
      setBatchSaving(false)
    }
  }

  const doTestPrint = async () => {
    setTestPrinting(true)
    setPrintResult(null)
    try {
      await localApi.testPrinter()
      setPrintResult('测试页已发送，请确认打印机是否出纸。')
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
      {/* 迭代 45（决策定稿 B）：布局随可用宽度自适应——多列网格（每列 ≥560px，列数随宽度自动增减），窄窗自动回退单列、不破版 */}
      <div style={{ flex: 1, minHeight: 0, minWidth: 0, overflowY: 'auto', padding: 16, display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(560px, 1fr))', gap: 14, alignItems: 'start' }}>
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
              服务端保存全部模板与打印记录，地址通常由安装程序自动配置；如需更换，请与管理人员确认后再修改。
              保存后立即生效、重启保持；未连接服务端时仍可在本机直接打印（单机模式）。
            </div>
          </div>
        </section>

        <section className="panel">
          {/* 迭代 73（#108 决议 1）：默认折叠——面板头即当前连接摘要（Describe / displayText 数据源），点击整行展开编辑区 */}
          <div
            className="panel-head"
            style={{ cursor: 'pointer' }}
            onClick={() => setTransportOpen((v) => !v)}
            title={transportOpen ? '收起连接编辑区' : '展开以测试或切换打印机连接'}
          >
            连接方式
            <span className="hint" style={{ marginLeft: 6 }}>当前</span>
            <span
              className={'badge ' + (app.connected ? 'ok' : '')}
              style={{ maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {formatTransport(app.transportConfig) || app.transport || '未知'}
            </span>
            <span className="spacer" style={{ flex: 1 }} />
            <span className="hint">{transportOpen ? '收起' : '展开'}</span>
            <Icon
              name="back"
              size={13}
              style={{ transform: transportOpen ? 'rotate(-90deg)' : 'rotate(90deg)', transition: 'transform 0.15s' }}
            />
          </div>
          {transportOpen && (
            <div className="panel-body">
              <TransportPanel />
            </div>
          )}
        </section>

        <section className="panel">
          <div className="panel-head">打印批次</div>
          <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {printSettingsOldWinHost ? (
              <div className="hint">当前客户端版本不支持批次作业。</div>
            ) : printSettings === null ? (
              <div className="hint">{printSettingsError ?? '加载批次设置…'}</div>
            ) : (
              <>
                <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                  <input
                    type="checkbox"
                    checked={printSettings.batchEnabled}
                    onChange={(ev) => setPrintSettings({ ...printSettings, batchEnabled: ev.target.checked })}
                  />
                  开启批次作业
                </label>
                <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
                  <label className="field" style={{ maxWidth: 150 }}>
                    每批次打印数量
                    <input
                      className="input mono"
                      type="number"
                      min={1}
                      value={printSettings.batchSize}
                      disabled={!printSettings.batchEnabled}
                      onChange={(ev) => setPrintSettings({ ...printSettings, batchSize: Number(ev.target.value) })}
                    />
                  </label>
                  <label className="field" style={{ maxWidth: 190 }}>
                    批次打印间隔（毫秒）
                    <input
                      className="input mono"
                      type="number"
                      min={0}
                      value={printSettings.batchIntervalMs}
                      disabled={!printSettings.batchEnabled}
                      onChange={(ev) => setPrintSettings({ ...printSettings, batchIntervalMs: Number(ev.target.value) })}
                    />
                  </label>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                  <button className="btn primary" onClick={() => void savePrintSettings()} disabled={batchSaving}>
                    <Icon name="save" size={13} />
                    {batchSaving ? '保存中…' : '保存'}
                  </button>
                  {batchSaveResult && (
                    <span className={batchSaveResult.ok ? 'badge ok' : 'badge err'}>{batchSaveResult.msg}</span>
                  )}
                </div>
                <div className="hint">
                  开启后，大批量作业将每 {printSettings.batchSize} 张一批发送到打印机，批与批之间间隔 {printSettings.batchIntervalMs} 毫秒。
                </div>
              </>
            )}
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
              <div className="hint">{printerLoading ? '读取中…' : '未获取到打印机状态：请确认打印机已开机并连接本机。'}</div>
            )}
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <button className="btn" onClick={() => void doTestPrint()} disabled={testPrinting}>
                <Icon name="test" size={13} />
                {testPrinting ? '发送中…' : '测试打印'}
              </button>
              {printResult && <span className={printResult.startsWith('测试页已发送') ? 'badge ok' : 'badge err'}>{printResult}</span>}
            </div>
            <div className="hint">
              测试打印会发送一张测试页（内容为测试条码）到当前连接的打印机。当前连接方式：
              {formatTransport(app.transportConfig) || app.transport || '未知'}（模拟打印无需打印机）。
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
            {/* 迭代 64（决策 #126）：「检查更新」结论——本机版本 × 服务端 client-packages 最高包版本；unknown 不显示（无误导） */}
            {(() => {
              const update = checkForUpdate(app.hostVersion, (packages ?? []).map((p) => p.fileName))
              if (update.kind === 'unknown') {
                return null
              }
              if (update.kind === 'update-available') {
                return (
                  <div
                    data-testid="update-available"
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 4,
                      padding: '8px 12px',
                      borderRadius: 6,
                      background: 'rgba(255, 159, 67, 0.12)',
                      border: '1px solid rgba(255, 159, 67, 0.4)',
                    }}
                  >
                    <span style={{ fontWeight: 600 }}>
                      <Icon name="alert" size={13} /> 发现新版本 {update.latestVersion}（本机 {update.localVersion}）
                    </span>
                    <span className="hint">
                      请在下方列表下载新版安装包并运行安装（客户端不会自动升级），或请管理人员协助升级。
                    </span>
                  </div>
                )
              }
              return (
                <div data-testid="update-uptodate" className="badge ok" style={{ alignSelf: 'flex-start' }}>
                  已是最新（本机 {update.localVersion}）
                </div>
              )
            })()}
            {!app.connected ? (
              <div className="hint">
                当前未连接服务端（单机模式）。安装包由服务端统一分发，请先在上方「服务端地址」中连接服务端后查看可用安装包。
              </div>
            ) : packages === null ? (
              <div className="hint">加载安装包列表…</div>
            ) : packages.length === 0 ? (
              <div className="hint">
                {packagesError ? `获取安装包列表失败：${packagesError}` : '服务端暂无客户端安装包。可在服务端管理界面「下载中心」页上传后，从此处下载更新。'}
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
                <div className="hint">下载后运行安装包完成升级（客户端不会自动升级）。安装包来自服务端：{app.baseUrl}。</div>
              </>
            )}
          </div>
        </section>

        <section className="panel">
          <div className="panel-head">
            插件管理
            <span className="spacer" style={{ flex: 1 }} />
            <button className="btn sm" onClick={() => void refreshInstalledPlugins()} disabled={installedPluginsLoading}>
              <Icon name="refresh" size={12} />
              刷新
            </button>
          </div>
          <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {pluginError && (
              <div className="badge err" style={{ alignSelf: 'flex-start' }}>
                {pluginError}
              </div>
            )}
            {pluginNotice && (
              <div className="badge ok" style={{ alignSelf: 'flex-start' }}>
                {pluginNotice}
              </div>
            )}

            <div style={{ fontSize: 13, fontWeight: 600 }}>服务端可用插件</div>
            {!app.connected ? (
              <div className="hint">当前未连接服务端，处于单机模式。插件由服务端统一分发，请连接服务端后查看可用插件。</div>
            ) : pluginPackages === null ? (
              <div className="hint">加载可用插件列表…</div>
            ) : pluginPackages.length === 0 ? (
              <div className="hint">
                {pluginPackagesOldServer
                  ? '服务端版本较旧，暂不支持插件管理；请先将服务端升级到新版本。'
                  : pluginPackagesError
                    ? `获取可用插件列表失败：${pluginPackagesError}`
                    : '服务端暂无可用插件。请先在服务端管理界面「插件管理」页上传插件包，再从此处安装。'}
              </div>
            ) : (
              <table className="table">
                <thead>
                  <tr>
                    <th>插件</th>
                    <th style={{ width: 90 }}>版本</th>
                    <th style={{ width: 90 }}>大小</th>
                    <th style={{ width: 190 }}>状态</th>
                    <th style={{ width: 100 }}></th>
                  </tr>
                </thead>
                <tbody>
                  {pluginPackages.map((p) => (
                    <tr key={p.fileName} style={{ cursor: 'default' }}>
                      <td>
                        <div>{p.name ?? '—'}</div>
                        <div className="mono" style={{ fontSize: 11, color: 'var(--muted)' }}>
                          {p.pluginId ?? p.fileName}
                        </div>
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {p.version ?? '—'}
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {formatSize(p.sizeBytes)}
                      </td>
                      <td>
                        {p.valid ? (
                          <span className="badge ok">有效</span>
                        ) : (
                          <>
                            <span className="badge err">无效</span>{' '}
                            <span style={{ fontSize: 12, color: 'var(--danger)' }}>{p.invalidReason ?? '解析失败'}</span>
                          </>
                        )}
                      </td>
                      <td>
                        {(() => {
                          const tooLarge = pluginPackageTooLarge(p.sizeBytes)
                          return (
                            <button
                              className="btn sm"
                              onClick={() => void installPlugin(p)}
                              disabled={installing === p.fileName || !p.valid || tooLarge !== null}
                              title={
                                tooLarge ?? (!p.valid ? (p.invalidReason ?? '插件包无效') : '下载并安装到本机客户端（重启后生效）')
                              }
                            >
                              <Icon name="download" size={12} />
                              {installing === p.fileName ? '安装中…' : '安装'}
                            </button>
                          )
                        })()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            <div style={{ fontSize: 13, fontWeight: 600, marginTop: 4 }}>已安装插件</div>
            {installedPluginsError ? (
              <div className="hint">
                {installedOldWinHost ? '当前客户端版本不支持插件管理。' : `获取已安装插件列表失败：${installedPluginsError}`}
              </div>
            ) : installedPlugins === null ? (
              <div className="hint">加载已安装插件…</div>
            ) : installedPlugins.length === 0 ? (
              <div className="hint">尚未安装插件。可从上方列表选择插件安装。</div>
            ) : (
              <table className="table">
                <thead>
                  <tr>
                    <th>插件</th>
                    <th style={{ width: 90 }}>版本</th>
                    <th style={{ width: 190 }}>状态</th>
                    <th style={{ width: 100 }}></th>
                  </tr>
                </thead>
                <tbody>
                  {installedPlugins.map((pl) => (
                    <tr key={pl.pluginId + (pl.packageDir ?? '')} style={{ cursor: 'default' }}>
                      <td>
                        <div>{pl.name}</div>
                        <div className="mono" style={{ fontSize: 11, color: 'var(--muted)' }}>
                          {pl.pluginId}
                        </div>
                      </td>
                      <td className="mono" style={{ fontSize: 12 }}>
                        {pl.version === '?' ? '—' : pl.version}
                      </td>
                      <td>
                        {pl.source === 'manual' ? (
                          <span className="badge">手动放置</span>
                        ) : pl.loaded ? (
                          <span className="badge ok">已加载</span>
                        ) : pl.loadError ? (
                          <>
                            <span className="badge err">加载失败</span>{' '}
                            <span style={{ fontSize: 12, color: 'var(--danger)' }}>{pl.loadError}</span>
                          </>
                        ) : (
                          <span className="badge warn">待重启生效</span>
                        )}
                      </td>
                      <td>
                        {pl.source === 'package' && (
                          <button
                            className="btn sm danger"
                            onClick={() => setConfirmUninstall(pl)}
                            disabled={uninstalling === pl.pluginId}
                            title="卸载该插件（重启客户端后生效）"
                          >
                            <Icon name="trash" size={12} />
                            {uninstalling === pl.pluginId ? '卸载中…' : '卸载'}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <div className="hint">安装 / 卸载后需重启客户端生效。</div>
          </div>
        </section>
      </div>

      {confirmOverwrite && (() => {
        const existing = installedPlugins?.find((i) => i.pluginId === confirmOverwrite.pluginId)
        return (
          <Modal
            title="覆盖安装插件"
            onClose={() => setConfirmOverwrite(null)}
            footer={
              <>
                <button className="btn" onClick={() => setConfirmOverwrite(null)}>
                  取消
                </button>
                <button
                  className="btn primary"
                  onClick={() => {
                    const p = confirmOverwrite
                    setConfirmOverwrite(null)
                    void installPlugin(p, { overwrite: true })
                  }}
                >
                  <Icon name="download" size={13} />
                  确认覆盖安装
                </button>
              </>
            }
          >
            <p>
              已安装「{existing ? `${existing.name} ${existing.version}` : '同 ID 插件'}」。将覆盖为「{confirmOverwrite.name ?? confirmOverwrite.fileName} {confirmOverwrite.version ?? '?'}」，重启客户端后生效。确认覆盖安装？
            </p>
          </Modal>
        )
      })()}

      {confirmUninstall && (
        <Modal
          title="卸载插件"
          onClose={() => setConfirmUninstall(null)}
          footer={
            <>
              <button className="btn" onClick={() => setConfirmUninstall(null)}>
                取消
              </button>
              <button
                className="btn danger"
                onClick={() => {
                  const pl = confirmUninstall
                  setConfirmUninstall(null)
                  void uninstallPlugin(pl)
                }}
              >
                <Icon name="trash" size={13} />
                确认卸载
              </button>
            </>
          }
        >
          <p>
            确认卸载插件「<b>{confirmUninstall.name} {confirmUninstall.version}</b>」？卸载后重启客户端生效。
          </p>
        </Modal>
      )}
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
