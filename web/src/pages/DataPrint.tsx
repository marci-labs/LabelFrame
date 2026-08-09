// 数据与打印：测试数据表单 / 打印测试 / Excel 导入映射 / 批量打印 / 作业进度与失败重试

import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { JobView, TemplatePackage, TemplateSummary } from '../lib/api/types'
import { fromBackendElements } from '../lib/design/convert'
import { deriveFields } from '../lib/design/fields'
import { findDuplicateKeys, isMappingComplete, rowToData, suggestMapping } from '../lib/excel/mapping'
import { useApp } from '../state/AppContext'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'

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

/** 作业轮询（1.5s，终端状态停止）。 */
function useJobPolling(jobId: string | null) {
  const [job, setJob] = useState<JobView | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!jobId) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | null = null
    const tick = async () => {
      try {
        const j = await api.getJob(jobId)
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
  }, [jobId])

  const retry = useCallback(
    async (index: number): Promise<boolean> => {
      if (!jobId) return false
      try {
        const j = await api.retryJobItem(jobId, index)
        setJob(j)
        return true
      } catch (err) {
        setError(err instanceof ApiError ? err.message : '重试失败。')
        return false
      }
    },
    [jobId],
  )

  return { job, error, retry }
}

function JobPanel({ job, error, retry }: { job: JobView | null; error: string | null; retry: (i: number) => Promise<boolean> }) {
  const app = useApp()
  if (!job) {
    return (
      <div className="panel">
        <div className="panel-head">作业进度</div>
        <div className="panel-body">
          <div className="hint">提交打印后显示进度与逐张结果。</div>
          {error && <div className="error-text" style={{ marginTop: 6 }}>{error}</div>}
        </div>
      </div>
    )
  }
  const pct = job.totalItems > 0 ? Math.round((job.completedItems / job.totalItems) * 100) : 0
  const failed = job.items.filter((i) => i.status === 'Failed').length
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
              有 {failed} 张打印失败，可在下方表格中单独重试。
            </div>
          )}
        </div>
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
      </div>
    </div>
  )
}

export function DataPrint() {
  const app = useApp()
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [selectedName, setSelectedName] = useState('')
  const [pkg, setPkg] = useState<TemplatePackage | null>(null)
  const [loading, setLoading] = useState(false)
  const [values, setValues] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)

  const [mappingOpen, setMappingOpen] = useState(false)
  const [excel, setExcel] = useState<{ headers: string[]; rows: string[][]; file: string } | null>(null)
  const [mapping, setMapping] = useState<string[]>([])
  const [importing, setImporting] = useState(false)

  const [jobId, setJobId] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const { job, error: jobError, retry } = useJobPolling(jobId)

  useEffect(() => {
    void api
      .listTemplates()
      .then((list) => {
        setTemplates(list)
        if (list.length > 0 && !selectedName) setSelectedName(list[0].name)
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : '加载模板列表失败。'))
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!selectedName) return
    setLoading(true)
    setPkg(null)
    setError(null)
    void api
      .getTemplate(selectedName)
      .then((p) => {
        setPkg(p)
        setValues({ ...(p.testData ?? {}) })
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : '加载模板失败。'))
      .finally(() => setLoading(false))
  }, [selectedName])

  // 契约字段键：contract.fields 优先，空则从版式推导
  const fieldKeys = useMemo(() => {
    if (!pkg) return []
    const fromContract = (pkg.contract.fields ?? []).map((f) => f.key).filter(Boolean)
    if (fromContract.length > 0) return fromContract
    return deriveFields(fromBackendElements(pkg.layout.elements))
  }, [pkg])

  const submit = async (labels: { data: Record<string, string> }[]) => {
    if (!pkg) return
    setSubmitting(true)
    try {
      const j = await api.submitJob({
        requestId: crypto.randomUUID(),
        template: { contract: pkg.contract, layout: pkg.layout },
        labels,
      })
      setJobId(j.jobId)
      app.setStatus(`作业已提交（${labels.length} 张，ID ${j.jobId.slice(0, 8)}）。`)
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : '提交作业失败。')
    } finally {
      setSubmitting(false)
    }
  }

  const testPrint = () => {
    if (!pkg || fieldKeys.length === 0) {
      app.setStatus('当前模板没有字段，请先在设计器中绑定字段填充。')
      return
    }
    void submit([{ data: { ...values } }])
  }

  const pickExcel = async (file: File) => {
    setImporting(true)
    setError(null)
    try {
      const r = await api.importExcel(file)
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
    app.setStatus(`已按映射生成 ${labels.length} 张标签，提交批量打印…`)
    void submit(labels)
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          数据与打印
          <small>测试数据 / Excel 批量打印 / 打印测试</small>
        </div>
        <div className="spacer" />
        <select className="input" value={selectedName} onChange={(ev) => setSelectedName(ev.target.value)} style={{ minWidth: 180 }}>
          {templates.length === 0 && <option value="">（暂无模板）</option>}
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
                        onChange={(ev) => setValues((v) => ({ ...v, [k]: ev.target.value }))}
                      />
                    </label>
                  ))}
                  <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
                    <button className="btn primary" onClick={testPrint} disabled={submitting || !pkg}>
                      <Icon name="printer" size={13} />
                      {submitting ? '提交中…' : '打印测试（单张）'}
                    </button>
                    {excel && (
                      <button className="btn" onClick={() => setMappingOpen(true)} disabled={!excel}>
                        重新映射（{excel.file}）
                      </button>
                    )}
                  </div>
                  <div className="hint">打印测试提交 1 张标签，使用上方数据；默认后端 Log 传输，无需打印机。</div>
                </>
              )}
            </div>
          </div>
        </div>

        <JobPanel job={job} error={jobError} retry={retry} />
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
}: {
  headers: string[]
  rows: string[][]
  keys: string[]
  mapping: string[]
  setMapping: (m: string[]) => void
  onCancel: () => void
  onConfirm: () => void
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
          <button className="btn primary" onClick={onConfirm} disabled={!complete || dup.length > 0}>
            <Icon name="printer" size={13} />
            批量打印 {rows.length} 张
          </button>
        </>
      }
    >
      <div className="hint">
        每列映射到一个字段键（自动按列名匹配，可手工调整）。未映射的列不参与打印。
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
