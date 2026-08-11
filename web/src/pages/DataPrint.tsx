// 数据与打印：测试数据表单 / 打印测试 / Excel 导入映射 / 批量打印 / 作业进度与失败重试
// 迭代 15：会话草稿提升全局（模板 / 字段值 / 调试开关 / 作业进度保留；Excel 不保留）；
// 调试模式独立开关——开：打印按钮改为后端渲染出图下载（单张 PNG / 批量 zip），不建作业不发驱动。

import { useCallback, useEffect, useMemo, useState } from 'react'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { DeviceView, JobView, SubmitJobRequest, TemplatePackage, TemplateSummary } from '../lib/api/types'
import { formatTransport } from '../lib/transport'
import { downloadBlob } from '../lib/download'
import { fromBackendElements } from '../lib/design/convert'
import { deriveFields } from '../lib/design/fields'
import { findDuplicateKeys, isMappingComplete, rowToData, suggestMapping } from '../lib/excel/mapping'
import { useApp } from '../state/AppContext'
import { mergeDraftValues } from '../state/draft'
import { isServerUi } from '../lib/uiMode'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'

/** 设备在线状态中文标签。 */
const deviceStatusLabel = (s: string) => (s === 'Online' ? '在线' : '离线')

/** 离线原因（选择器置灰时显示上次心跳时间）。 */
function formatLastSeen(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

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

/** 作业轮询（1.5s，终端状态停止）；API 跟随模式（服务端 / 单机降级）。 */
function useJobPolling(jobId: string | null, biz: typeof serverApi) {
  const [job, setJob] = useState<JobView | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!jobId) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | null = null
    const tick = async () => {
      try {
        const j = await biz.getJob(jobId)
        if (stopped) return
        setJob(j)
        setError(null)
        if (!isTerminal(j.status)) timer = setTimeout(() => void tick(), 1500)
      } catch (err) {
        if (stopped) return
        setError(err instanceof ApiError ? err.message : '查询作业失败。')
        timer = setTimeout(() => void tick(), 2000)
      }
    }
    void tick()
    return () => {
      stopped = true
      if (timer) clearTimeout(timer)
    }
  }, [jobId, biz])

  const retry = useCallback(
    async (index: number): Promise<boolean> => {
      if (!jobId) return false
      try {
        const j = await biz.retryJobItem(jobId, index)
        setJob(j)
        return true
      } catch (err) {
        setError(err instanceof ApiError ? err.message : '重试失败。')
        return false
      }
    },
    [jobId, biz],
  )

  return { job, error, retry }
}

function JobPanel({
  job,
  error,
  retry,
  debugMode,
  canRetry,
}: {
  job: JobView | null
  error: string | null
  retry: (i: number) => Promise<boolean>
  debugMode: boolean
  /** 迭代 20（G4）：server 构建无逐张 retry 端点——隐藏逐张失败重试表格（Server 作业本就无 items，强制隐藏兜底）。 */
  canRetry: boolean
}) {
  const app = useApp()
  if (!job) {
    return (
      <div className="panel">
        <div className="panel-head">作业进度</div>
        <div className="panel-body">
          {debugMode ? (
            <div className="hint">调试模式：不提交作业，出图已下载。</div>
          ) : (
            <div className="hint">提交打印后显示进度与逐张结果。</div>
          )}
          {error && <div className="error-text" style={{ marginTop: 6 }}>{error}</div>}
        </div>
      </div>
    )
  }
  const pct = job.totalItems > 0 ? Math.round((job.completedItems / job.totalItems) * 100) : 0
  const failed = job.items ? job.items.filter((i) => i.status === 'Failed').length : (job.failedItems ?? 0)
  return (
    <div className="panel">
      <div className="panel-head">
        作业进度
        <span className={'badge ' + (job.status === 'Completed' ? 'ok' : job.status === 'Failed' ? 'err' : job.status === 'Cancelled' ? 'neutral' : 'info')}>
          {jobLabel(job.status)}
        </span>
        <span className="spacer" style={{ flex: 1 }} />
        <span className="mono" style={{ color: 'var(--ink-3)', fontSize: 11 }}>ID {job.jobId.slice(0, 8)}</span>
      </div>
      <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        {debugMode && <div className="hint">调试模式：不提交作业，出图已下载。（以下为历史作业进度）</div>}
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4, fontSize: 12, color: 'var(--ink-2)' }}>
            <span>
              已完成 {job.completedItems} / {job.totalItems} 张
            </span>
            <span className="mono">{pct}%</span>
          </div>
          <div className="progress">
            <div className={failed > 0 ? 'fail' : job.status === 'Completed' ? 'done' : ''} style={{ width: pct + '%' }} />
          </div>
          {failed > 0 && (
            <div className="hint" style={{ marginTop: 6, color: 'var(--danger)' }}>
              有 {failed} 张打印失败。{job.items && canRetry ? '可在下方表格中单独重试。' : '详见作业状态与客户端回报的失败原因。'}
            </div>
          )}
        </div>
        {job.targetDeviceId && (
          <div className="hint">
            目标设备：{job.targetDeviceId}
            {job.deviceStatus ? `（${deviceStatusLabel(job.deviceStatus)}）` : ''}
          </div>
        )}
        {job.printImageDir && (
          <div className="hint" style={{ wordBreak: 'break-all' }}>
            模拟打印图片（Log）：{job.printImageDir}（{job.printImageCount ?? 0} 张）
          </div>
        )}
        {job.errorMessage && (
          <div className="hint" style={{ color: 'var(--danger)' }}>错误：{job.errorMessage}</div>
        )}
        {job.items && canRetry && (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 50 }}>#</th>
                <th style={{ width: 90 }}>状态</th>
                <th>失败原因</th>
                <th style={{ width: 90 }}></th>
              </tr>
            </thead>
            <tbody>
              {job.items.map((it) => (
                <tr key={it.index} style={{ cursor: 'default' }}>
                  <td className="mono">{it.index + 1}</td>
                  <td>
                    <span className={'badge ' + (it.status === 'Completed' ? 'ok' : it.status === 'Failed' ? 'err' : it.status === 'Cancelled' ? 'neutral' : 'info')}>
                      {jobLabel(it.status)}
                    </span>
                  </td>
                  <td style={{ color: 'var(--danger)', fontSize: 12 }}>{it.status === 'Failed' ? it.errorMessage || it.errorCode || '未知错误' : ''}</td>
                  <td>
                    {it.status === 'Failed' && (
                      <button
                        className="btn sm"
                        onClick={() => {
                          void retry(it.index).then((ok) => ok && app.setStatus(`已重试第 ${it.index + 1} 张。`))
                        }}
                      >
                        <Icon name="retry" size={12} />
                        重试
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {!job.items && (
          <div className="hint">（服务端作业无逐张明细，进度见上方进度条；失败原因见作业状态。）</div>
        )}
      </div>
    </div>
  )
}

export function DataPrint() {
  const app = useApp()
  const { printDraft } = app
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [pkg, setPkg] = useState<TemplatePackage | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Excel 导入数据与列映射：页面局部状态（迭代 15：切页 / 刷新即丢弃，重新上传）
  const [mappingOpen, setMappingOpen] = useState(false)
  const [excel, setExcel] = useState<{ headers: string[]; rows: string[][]; file: string } | null>(null)
  const [mapping, setMapping] = useState<string[]>([])
  const [importing, setImporting] = useState(false)

  const [submitting, setSubmitting] = useState(false)

  // 目标设备（迭代 17/18 F5）：GET /api/devices 成功 = 服务端模式（显示选择、提交带 targetDeviceId）；
  // 404 / 失败 = 单机 WinHost 降级（隐藏选择、提交不带 targetDeviceId）。
  // 迭代 18：默认选中本机设备（机器级配置 deviceId 匹配在线列表），未命中回退第一台在线。
  // 迭代 20（K2）：server 构建恒服务端模式——设备列表拉取成功即 'server'（失败也保持 'server'，无 standalone 分支）。
  const [deviceMode, setDeviceMode] = useState<'loading' | 'server' | 'standalone'>('loading')
  const [devices, setDevices] = useState<DeviceView[]>([])
  const [targetDeviceId, setTargetDeviceId] = useState('')

  /** 业务 API 跟随模式：服务端 = serverApi（模板 / 作业中心）；单机降级 = localApi（本机 WinHost 全套 API）。
   *  迭代 20：server 构建恒 serverApi（Server UI 由服务端托管，无本机 Client）。 */
  const biz = isServerUi ? serverApi : deviceMode === 'server' ? serverApi : localApi
  const { job, error: jobError, retry } = useJobPolling(printDraft.jobId, biz)

  useEffect(() => {
    let cancelled = false
    if (isServerUi) {
      // 迭代 20（K1/K2/Y2）：server 构建不探测本机（无 getHostConfig / getTransport）；
      // 进入页面拉取一次设备列表（无需轮询，提交前另有现拉校验）；
      // 默认目标优先级 = 用户点选（localStorage labelframe.defaultTargetDeviceId，须在线）> 第一台在线。
      serverApi
        .listDevices()
        .then((list) => {
          if (cancelled) return
          setDevices(list)
          setDeviceMode('server')
          const online = list.filter((d) => d.status === 'Online')
          const saved =
            app.defaultTargetDeviceId && online.some((d) => d.deviceId === app.defaultTargetDeviceId)
              ? app.defaultTargetDeviceId
              : ''
          setTargetDeviceId(saved || online[0]?.deviceId || '')
        })
        .catch((err) => {
          if (cancelled) return
          setDeviceMode('server')
          setError(err instanceof ApiError ? err.message : '加载设备列表失败。')
        })
      return () => {
        cancelled = true
      }
    }
    void Promise.all([serverApi.listDevices().catch(() => null), localApi.getHostConfig().catch(() => null)]).then(
      ([list, cfg]) => {
        if (cancelled) return
        if (list) {
          setDevices(list)
          setDeviceMode('server')
          const online = list.filter((d) => d.status === 'Online')
          // 本机设备优先（hostConfig.deviceId 匹配），未命中回退第一台在线（少点一次；全部离线时留空由用户选择）
          const mine = cfg ? online.find((d) => d.deviceId === cfg.deviceId) : undefined
          setTargetDeviceId(mine?.deviceId ?? online[0]?.deviceId ?? '')
        } else {
          // 单机模式：旧 WinHost 无 /api/devices（404），或服务端不可达——隐藏设备选择，正常提交
          setDeviceMode('standalone')
        }
      },
    )
    return () => {
      cancelled = true
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const selectedName = printDraft.selectedName
  const debugMode = printDraft.debugMode

  useEffect(() => {
    if (deviceMode === 'loading') return
    void biz
      .listTemplates()
      .then((list) => {
        setTemplates(list)
        if (list.length > 0 && !selectedName) app.setDraftSelected(list[0].name)
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : '加载模板列表失败。'))
  }, [deviceMode]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!selectedName) return
    setLoading(true)
    setPkg(null)
    setError(null)
    void biz
      .getTemplate(selectedName)
      .then((p) => setPkg(p))
      .catch((err) => setError(err instanceof ApiError ? err.message : '加载模板失败。'))
      .finally(() => setLoading(false))
  }, [selectedName, deviceMode]) // eslint-disable-line react-hooks/exhaustive-deps

  // 契约字段键：contract.fields 优先，空则从版式推导
  const fieldKeys = useMemo(() => {
    if (!pkg) return []
    const fromContract = (pkg.contract.fields ?? []).map((f) => f.key).filter(Boolean)
    if (fromContract.length > 0) return fromContract
    return deriveFields(fromBackendElements(pkg.layout.elements))
  }, [pkg])

  // 显示值 = { ...testData, ...用户 dirty 的 key }（按 key 存在性合并，用户清空不被顶回）
  const values = useMemo(() => {
    if (!pkg) return {}
    return mergeDraftValues(pkg.testData, printDraft.valuesByTemplate[pkg.name], printDraft.dirtyKeysByTemplate[pkg.name])
  }, [pkg, printDraft.valuesByTemplate, printDraft.dirtyKeysByTemplate])

  const setFieldValue = (key: string, value: string) => {
    if (pkg) app.setDraftValue(pkg.name, key, value)
  }

  /**
   * 拼提交请求：
   * - job（服务端模式）：templateName 引用服务端模板库 + targetDeviceId 定向投递（自包含 template 不携带）；
   * - job（单机降级）：自包含 template（旧 WinHost 兼容，无 templateName / targetDeviceId）；
   * - debug 出图：自包含 template（render-image 后端要求 contract + layout，不建作业）。
   */
  const buildRequest = useCallback(
    (labels: { data: Record<string, string> }[], kind: 'job' | 'debug'): SubmitJobRequest | null => {
      if (!pkg) return null
      if (kind === 'job' && deviceMode === 'server') {
        return {
          requestId: crypto.randomUUID(),
          templateName: pkg.name,
          targetDeviceId,
          labels,
        }
      }
      return {
        requestId: crypto.randomUUID(),
        template: { name: pkg.name, contract: pkg.contract, layout: pkg.layout },
        labels,
      }
    },
    [pkg, deviceMode, targetDeviceId],
  )

  const submit = async (labels: { data: Record<string, string> }[]) => {
    if (deviceMode === 'server' && !targetDeviceId) {
      app.setStatus('请先选择目标设备（作业投递到客户端打印）。')
      return
    }
    setSubmitting(true)
    try {
      // 迭代 20（K3，仅 server 构建）：提交时现拉 GET /api/devices 核对所选设备在线——
      // 不复用进入页面时的缓存列表（设备中途掉线后缓存校验形同虚设）；掉线提示并禁止提交、作业不排队。
      // client 构建保持现状（可选离线设备排队）。
      if (isServerUi) {
        try {
          const fresh = await serverApi.listDevices()
          const dev = fresh.find((d) => d.deviceId === targetDeviceId)
          if (!dev || dev.status !== 'Online') {
            setDevices(fresh)
            const msg = '所选设备已离线或不存在，无法提交（作业不会排队）。请重新选择在线设备。'
            setError(msg)
            app.setStatus(msg)
            return
          }
        } catch (err) {
          const msg = err instanceof ApiError ? err.message : '校验设备在线状态失败，无法提交。'
          setError(msg)
          app.setStatus(msg)
          return
        }
      }
      const req = buildRequest(labels, 'job')
      if (!req) return
      const j = await biz.submitJob(req)
      app.setDraftJobId(j.jobId)
      app.setStatus(`作业已提交（${labels.length} 张，ID ${j.jobId.slice(0, 8)}）。`)
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : '提交作业失败。'
      setError(msg)
      app.setStatus(msg)
    } finally {
      setSubmitting(false)
    }
  }

  const downloadDebug = async (labels: { data: Record<string, string> }[], batch: boolean) => {
    const req = buildRequest(labels, 'debug')
    if (!req) return
    setSubmitting(true)
    try {
      const { blob, filename } = batch ? await biz.renderImages(req) : await biz.renderImage(req)
      downloadBlob(blob, filename)
      app.setStatus(`调试图片已下载：${filename}`)
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : '出图失败。')
    } finally {
      setSubmitting(false)
    }
  }

  /** 调试关：打印测试（单张）提交正常作业。 */
  const testPrint = () => {
    if (!pkg || fieldKeys.length === 0) {
      app.setStatus('当前模板没有字段，请先在设计器中绑定字段填充。')
      return
    }
    void submit([{ data: { ...values } }])
  }

  /** 调试开：单张出图下载（后端渲染 PNG，不建作业不发驱动）。 */
  const debugSingle = () => {
    if (!pkg) return
    void downloadDebug([{ data: { ...values } }], false)
  }

  /** 调试关：出图预览（即时预览，不建作业）。 */
  const previewImage = () => {
    if (!pkg) return
    void downloadDebug([{ data: { ...values } }], false)
  }

  const pickExcel = async (file: File) => {
    setImporting(true)
    setError(null)
    try {
      const r = await biz.importExcel(file)
      if (r.headers.length === 0) {
        app.setStatus('Excel 未读取到表头（第一行作为表头）。')
        return
      }
      setExcel({ headers: r.headers, rows: r.rows, file: file.name })
      setMapping(suggestMapping(r.headers, fieldKeys))
      setMappingOpen(true)
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : 'Excel 解析失败。')
    } finally {
      setImporting(false)
    }
  }

  const confirmMapping = () => {
    if (!excel || !pkg) return
    const dup = findDuplicateKeys(mapping)
    if (dup.length > 0) {
      app.setStatus(`以下字段被多列映射：${dup.join('、')}，请调整。`)
      return
    }
    const labels = excel.rows.map((row) => ({ data: rowToData(excel.headers, row, mapping) }))
    setMappingOpen(false)
    if (debugMode) {
      app.setStatus(`正在渲染 ${labels.length} 张调试图片并打包下载…`)
      void downloadDebug(labels, true)
    } else {
      app.setStatus(`已按映射生成 ${labels.length} 张标签，提交批量打印…`)
      void submit(labels)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          数据与打印
          <small>测试数据 / Excel 批量打印 / 打印测试</small>
        </div>
        <div className="spacer" />
        <select className="input" value={selectedName} onChange={(ev) => app.setDraftSelected(ev.target.value)} style={{ minWidth: 180 }}>
          {templates.length === 0 && <option value="">（暂无模板）</option>}
          {selectedName && !templates.some((t) => t.name === selectedName) && <option value={selectedName}>{selectedName}</option>}
          {templates.map((t) => (
            <option key={t.name} value={t.name}>
              {t.name}
            </option>
          ))}
        </select>
        <button className="btn" onClick={() => document.getElementById('excelFile')?.click()} disabled={!pkg || importing || submitting}>
          <Icon name="upload" size={13} />
          Excel 导入
        </button>
        <input
          id="excelFile"
          type="file"
          accept=".xlsx"
          style={{ display: 'none' }}
          onChange={(ev) => {
            const f = ev.target.files?.[0]
            if (f) void pickExcel(f)
            ev.target.value = ''
          }}
        />
      </div>

      {/* 连接状态徽标（迭代 18 F5）：本机连接（Client 传输方式）与服务端连通（模板 / 作业中心）各自含义。
          迭代 20：server 构建隐藏（本机连接 = 打印机相关内容；服务端连通状态在底部状态栏已显示） */}
      {!isServerUi && (
        <div
          style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '6px 16px', borderBottom: '1px solid var(--line)', flexWrap: 'wrap' }}
          title="本机连接：LabelFrame Client 的打印机连接方式（数据来自本机）；服务端连通：模板库 / 作业队列所在 Server"
        >
          <span className="hint" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            本机连接
            <span className="badge">{formatTransport(app.transportConfig) || app.transport || '未知'}</span>
          </span>
          <span className="hint" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            服务端
            <span className={'conn' + (app.connected ? ' on' : ' off')} style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
              <span className={'status-dot' + (app.connected ? ' on' : '')} />
              {app.connected ? '已连接' : '未连接（单机模式可用）'}
            </span>
          </span>
        </div>
      )}

      {deviceMode === 'server' && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '6px 16px', borderBottom: '1px solid var(--line)', flexWrap: 'wrap' }}>
          <span className="hint">目标设备</span>
          <select
            className="input"
            aria-label="目标设备"
            value={targetDeviceId}
            onChange={(ev) => setTargetDeviceId(ev.target.value)}
            style={{ minWidth: 240 }}
            title={isServerUi ? '作业将投递到所选在线设备执行打印（仅在线设备可选）' : '作业将投递到所选客户端打印'}
          >
            {devices.length === 0 && <option value="">（暂无设备）</option>}
            {devices.length > 0 && !targetDeviceId && <option value="">（请选择设备）</option>}
            {devices.map((d) => (
              <option key={d.deviceId} value={d.deviceId} disabled={isServerUi && d.status !== 'Online'} title={isServerUi && d.status !== 'Online' ? `离线（上次心跳 ${formatLastSeen(d.lastSeenAt)}）` : undefined}>
                {d.name}（{deviceStatusLabel(d.status)}）
                {isServerUi && d.status !== 'Online' ? ` · 上次心跳 ${formatLastSeen(d.lastSeenAt)}` : ''}
              </option>
            ))}
          </select>
          {devices.length === 0 ? (
            <span className="badge warn">{isServerUi ? '暂无设备，请先在打印电脑安装并启动 LabelFrame Client' : '暂无在线客户端，请先在打印电脑安装并启动 LabelFrame Client'}</span>
          ) : targetDeviceId ? (
            <span className="hint">{isServerUi ? '仅在线设备可选；提交时将再次校验所选设备在线状态。' : '提交作业将投递到所选客户端打印；客户端离线时作业排队，上线后自动领取。'}</span>
          ) : (
            <span className="badge warn">{isServerUi ? '暂无在线设备，仅在线设备可选' : '暂无在线设备，请先启动打印电脑上的 LabelFrame Client（或选择离线设备排队等待）'}</span>
          )}
        </div>
      )}

      {error && (
        <div style={{ padding: '6px 16px', background: 'var(--danger-soft)', color: 'var(--danger)', fontSize: 12 }}>{error}</div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: 12, display: 'grid', gridTemplateColumns: 'minmax(0,1fr) minmax(0,1fr)', gap: 12, alignItems: 'start' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div className="panel">
            <div className="panel-head">
              测试数据
              <span className="hint" style={{ marginLeft: 6 }}>字段由版式自动推导（模板 {pkg ? `「${pkg.name}」` : ''}）</span>
            </div>
            <div className="panel-body" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {loading ? (
                <div className="hint">加载中…</div>
              ) : !pkg ? (
                <div className="hint">请先在左侧选择模板。</div>
              ) : fieldKeys.length === 0 ? (
                <div className="hint">该模板没有字段。请在设计器中为元素绑定「字段填充」后保存。</div>
              ) : (
                <>
                  {fieldKeys.map((k) => (
                    <label className="field" key={k}>
                      {k}
                      <input
                        className="input mono"
                        value={values[k] ?? ''}
                        placeholder={`字段 ${k} 的值（打印时使用）`}
                        onChange={(ev) => setFieldValue(k, ev.target.value)}
                      />
                    </label>
                  ))}
                  <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, marginTop: 2 }}>
                    <input type="checkbox" checked={debugMode} onChange={(ev) => app.setDraftDebug(ev.target.checked)} />
                    调试模式：只生成图片，不发送打印驱动（后端渲染）
                  </label>
                  <div style={{ display: 'flex', gap: 8, marginTop: 4, flexWrap: 'wrap' }}>
                    <button
                      className="btn primary"
                      onClick={debugMode ? debugSingle : testPrint}
                      disabled={submitting || !pkg || (deviceMode === 'server' && !targetDeviceId)}
                      title={
                        debugMode
                          ? '后端渲染当前表单为 PNG 下载，不发送打印驱动'
                          : deviceMode === 'server'
                            ? isServerUi
                              ? '提交 1 张标签作业到所选在线设备（由该设备客户端执行打印）'
                              : '提交 1 张标签作业到所选目标设备'
                            : '提交 1 张标签作业到本机打印'
                      }
                    >
                      <Icon name="printer" size={13} />
                      {submitting ? '处理中…' : debugMode ? '调试出图（单张）' : '打印测试（单张）'}
                    </button>
                    {!debugMode && (
                      <button className="btn" onClick={previewImage} disabled={submitting || !pkg} title="后端渲染当前表单为 PNG 下载（不建作业）">
                        <Icon name="preview" size={13} />
                        出图预览
                      </button>
                    )}
                    {excel && (
                      <button className="btn" onClick={() => setMappingOpen(true)} disabled={!excel}>
                        重新映射（{excel.file}）
                      </button>
                    )}
                  </div>
                  <div className="hint">
                    {debugMode
                      ? '调试模式：出图为后端渲染的实际打印位图（同一 Skia / DPI），不提交作业、不发送打印驱动。'
                      : deviceMode === 'server'
                        ? isServerUi
                          ? '已用模板预览值预填，可修改后打印；打印测试提交 1 张标签到所选在线设备（仅在线设备可选，由设备客户端执行打印）。'
                          : '已用模板预览值预填，可修改后打印；打印测试提交 1 张标签到所选目标设备（客户端离线时排队等待）。'
                        : '已用模板预览值预填，可修改后打印；单机模式：作业提交到本机 WinHost 打印（兼容旧版单机部署）。'}
                  </div>
                </>
              )}
            </div>
          </div>
        </div>

        <JobPanel job={job} error={jobError} retry={retry} debugMode={debugMode} canRetry={!isServerUi} />
      </div>

      {mappingOpen && excel && pkg && (
        <MappingModal
          headers={excel.headers}
          rows={excel.rows}
          keys={fieldKeys}
          mapping={mapping}
          setMapping={setMapping}
          onCancel={() => setMappingOpen(false)}
          onConfirm={confirmMapping}
          debugMode={debugMode}
        />
      )}
    </div>
  )
}

function MappingModal({
  headers,
  rows,
  keys,
  mapping,
  setMapping,
  onCancel,
  onConfirm,
  debugMode,
}: {
  headers: string[]
  rows: string[][]
  keys: string[]
  mapping: string[]
  setMapping: (m: string[]) => void
  onCancel: () => void
  onConfirm: () => void
  debugMode: boolean
}) {
  const [suggested, setSuggested] = useState<string[]>([])
  useEffect(() => {
    setSuggested(suggestMapping(headers, keys))
  }, [headers, keys])
  const complete = isMappingComplete(mapping)
  const dup = findDuplicateKeys(mapping)

  return (
    <Modal
      title={`列映射（${rows.length} 行数据）`}
      onClose={onCancel}
      width={640}
      footer={
        <>
          <button className="btn" onClick={onCancel}>
            取消
          </button>
          <button
            className="btn sm"
            onClick={() => setMapping(suggested)}
            disabled={suggested.every((k) => !k)}
            title="按列名自动匹配（忽略大小写 / 空格）"
          >
            自动匹配
          </button>
          <button className="btn primary" onClick={onConfirm} disabled={!complete || dup.length > 0} title={debugMode ? '后端渲染全部行打包 zip 下载（不建作业）' : '提交批量打印作业'}>
            <Icon name={debugMode ? 'download' : 'printer'} size={13} />
            {debugMode ? `下载调试图片 zip（${rows.length} 张）` : `批量打印 ${rows.length} 张`}
          </button>
        </>
      }
    >
      <div className="hint">
        每列映射到一个字段键（自动按列名匹配，可手工调整）。未映射的列不参与打印。
        {debugMode && <span className="hint"> 调试模式：将渲染全部行打包 zip 下载，不提交作业。</span>}
        {dup.length > 0 && (
          <span className="error-text"> 重复映射：{dup.join('、')}。</span>
        )}
      </div>
      <table className="table">
        <thead>
          <tr>
            <th style={{ width: 44 }}>列</th>
            <th>Excel 列名</th>
            <th style={{ width: 200 }}>字段键</th>
            <th>示例值</th>
          </tr>
        </thead>
        <tbody>
          {headers.map((h, i) => (
            <tr key={i} style={{ cursor: 'default' }}>
              <td className="mono" style={{ color: 'var(--ink-3)' }}>{i + 1}</td>
              <td style={{ fontWeight: 600 }}>{h || `（空列 ${i + 1}）`}</td>
              <td>
                <select className="input" style={{ width: '100%' }} value={mapping[i] ?? ''} onChange={(ev) => setMapping(mapping.map((m, j) => (j === i ? ev.target.value : m)))}>
                  <option value="">— 不映射 —</option>
                  {keys.map((k) => (
                    <option key={k} value={k}>
                      {k}
                    </option>
                  ))}
                </select>
              </td>
              <td className="mono" style={{ color: 'var(--ink-2)', maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {rows[0]?.[i] ?? ''}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </Modal>
  )
}
