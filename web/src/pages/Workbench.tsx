// 工作台：模板列表（分组过滤）/ 新建 / 编辑 / 删除 / 导出 / 导入

import { useCallback, useEffect, useState } from 'react'
import { api } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { TemplateSummary } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import type { DesignerRequest } from '../state/types'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'

export function Workbench({ onOpenDesigner }: { onOpenDesigner: (req: DesignerRequest) => void }) {
  const app = useApp()
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [groups, setGroups] = useState<string[]>([])
  const [group, setGroup] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<TemplateSummary | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const list = await api.listTemplates()
      setTemplates(list)
      setGroups([...new Set(list.map((t) => t.group))].sort())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '加载模板列表失败。')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const filtered = group ? templates.filter((t) => t.group === group) : templates

  const doDelete = async () => {
    if (!deleting) return
    setBusy('delete')
    try {
      await api.deleteTemplate(deleting.name)
      app.setStatus(`已删除模板「${deleting.name}」。`)
      setDeleting(null)
      void load()
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : '删除失败。')
    } finally {
      setBusy(null)
    }
  }

  const doExport = async (t: TemplateSummary) => {
    setBusy(t.name)
    try {
      const { blob, filename } = await api.exportTemplate(t.name)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
      app.setStatus(`已导出模板「${t.name}」。`)
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : '导出失败。')
    } finally {
      setBusy(null)
    }
  }

  const doImport = async (file: File) => {
    setBusy('import')
    try {
      const name = await api.importTemplate(file)
      app.setStatus(`已导入模板「${name}」。`)
      void load()
    } catch (err) {
      app.setStatus(err instanceof ApiError ? err.message : '导入失败（文件可能不是有效的 .lfpkg 模板包）。')
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          工作台
          <small>模板管理</small>
        </div>
        <div className="spacer" />
        <select className="input" value={group} onChange={(ev) => setGroup(ev.target.value)} title="按分组过滤">
          <option value="">全部分组</option>
          {groups.map((g) => (
            <option key={g} value={g}>
              {g}
            </option>
          ))}
        </select>
        <button className="btn" onClick={() => document.getElementById('importFile')?.click()} disabled={busy !== null}>
          <Icon name="upload" size={13} />
          导入 .lfpkg
        </button>
        <input
          id="importFile"
          type="file"
          accept=".lfpkg,application/zip"
          style={{ display: 'none' }}
          onChange={(ev) => {
            const f = ev.target.files?.[0]
            if (f) void doImport(f)
            ev.target.value = ''
          }}
        />
        <button className="btn primary" onClick={() => onOpenDesigner({ kind: 'new' })}>
          <Icon name="plus" size={13} />
          新建模板
        </button>
      </div>

      {error && (
        <div style={{ padding: '6px 16px', background: 'var(--danger-soft)', color: 'var(--danger)', fontSize: 12 }}>{error}</div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {loading ? (
          <div className="empty">
            <Icon name="refresh" />
            <div className="empty-title">加载中…</div>
          </div>
        ) : templates.length === 0 ? (
          <div className="empty">
            <Icon name="workbench" />
            <div className="empty-title">还没有模板</div>
            <div className="hint">点击「新建模板」开始设计第一张标签，或导入已有的 .lfpkg 模板包。</div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 60 }}>#</th>
                <th>模板名称</th>
                <th style={{ width: 160 }}>分组</th>
                <th style={{ width: 190 }}>更新时间</th>
                <th style={{ width: 240 }} className="actions">操作</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((t, i) => (
                <tr key={t.name} onDoubleClick={() => onOpenDesigner({ kind: 'edit', name: t.name })} title="双击打开设计器">
                  <td className="mono" style={{ color: 'var(--ink-3)' }}>{i + 1}</td>
                  <td style={{ fontWeight: 600 }}>{t.name}</td>
                  <td>
                    <span className="badge neutral">{t.group}</span>
                  </td>
                  <td className="mono" style={{ color: 'var(--ink-2)' }}>
                    {new Date(t.updatedAt).toLocaleString('zh-CN', { hour12: false })}
                  </td>
                  <td>
                    <div className="actions">
                      <button className="btn sm" onClick={() => onOpenDesigner({ kind: 'edit', name: t.name })}>
                        <Icon name="edit" size={12} />
                        编辑
                      </button>
                      <button className="btn sm" onClick={() => void doExport(t)} disabled={busy !== null}>
                        <Icon name="download" size={12} />
                        导出
                      </button>
                      <button className="btn sm danger" onClick={() => setDeleting(t)}>
                        <Icon name="trash" size={12} />
                        删除
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {deleting && (
        <Modal
          title="删除模板"
          onClose={() => setDeleting(null)}
          footer={
            <>
              <button className="btn" onClick={() => setDeleting(null)}>
                取消
              </button>
              <button className="btn danger" onClick={() => void doDelete()} disabled={busy === 'delete'}>
                <Icon name="trash" size={13} />
                确认删除
              </button>
            </>
          }
        >
          <p>
            确定删除模板「<b>{deleting.name}</b>」吗？该操作不可恢复。
          </p>
        </Modal>
      )}
    </div>
  )
}
