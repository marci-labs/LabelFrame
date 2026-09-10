// 工作台：模板管理（搜索 / 分组过滤 / 新建 / 编辑 / 删除 / 导出 / 导入）
// 迭代 18：业务 API 跟随模式——服务端 = serverApi（模板中心）；单机降级 = localApi（本机 WinHost 模板库）。
// 迭代 45：模板名搜索（子串、大小写不敏感，与分组过滤叠加生效）。
// 迭代 46：预览缩略图（列表加载后按需拉取全部预览，会话内缓存随列表刷新失效；点击居中灯箱放大）。
// 迭代 48：整体信息架构重构（用户定稿方案 A）——表格形态收敛为卡片网格（缩略图主视觉），
//   自适应多列卡片，名称 / 分组 / 日期 / 操作收于卡片下部；既有能力全部保留。

import { useCallback, useEffect, useState } from 'react'
import { localApi, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { TemplateSummary } from '../lib/api/types'
import { useApp } from '../state/AppContext'
import type { DesignerRequest } from '../state/types'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import { useTemplatePreviewCache } from './useTemplatePreview'
import type { TemplatePreviewEntry } from './useTemplatePreview'
import { TemplatePreviewModal } from './WorkbenchPreview'

/** 预览缩略图呈现（迭代 48 卡片形态）：加载中骨架 / 失败占位 / 成功图片（点击放大）。 */
function PreviewThumb({
  name,
  entry,
  onEnlarge,
}: {
  name: string
  entry: TemplatePreviewEntry | undefined
  onEnlarge: (name: string) => void
}) {
  if (!entry || entry.status === 'loading') {
    return <div className="preview-thumb-skel card" title="正在生成预览…" />
  }
  if (entry.status === 'error') {
    return (
      <div className="preview-thumb-err card" title={`预览不可用：${entry.message}`}>
        <Icon name="alert" size={12} />
      </div>
    )
  }
  return (
    <img
      className="preview-thumb card"
      src={entry.url}
      alt={`模板「${name}」缩略图`}
      title="点击放大预览"
      onClick={() => onEnlarge(name)}
    />
  )
}

export function Workbench({ onOpenDesigner }: { onOpenDesigner: (req: DesignerRequest) => void }) {
  const app = useApp()
  const { serverMode } = app
  /** 业务 API 跟随模式（unknown 时不拉取，待探测完成）。 */
  const biz = serverMode === 'server' ? serverApi : localApi
  const [templates, setTemplates] = useState<TemplateSummary[]>([])
  const [groups, setGroups] = useState<string[]>([])
  const [group, setGroup] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<TemplateSummary | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  // 预览缩略图（迭代 46 修订）：缓存随 biz 模式切换后的重新 load 失效
  const preview = useTemplatePreviewCache(biz.previewTemplate)
  /** 当前放大查看的模板名（居中灯箱）。 */
  const [enlarged, setEnlarged] = useState<string | null>(null)
  useEffect(() => {
    if (!enlarged) return
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') setEnlarged(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [enlarged])

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const list = await biz.listTemplates()
      setTemplates(list)
      setGroups([...new Set(list.map((t) => t.group))].sort())
      // 列表刷新 = 新周期：模板可能已修改，预览缓存整体失效（释放 blob URL）后按需重拉全部
      preview.invalidate()
      for (const t of list) {
        preview.ensure(t.name)
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '加载模板列表失败。')
    } finally {
      setLoading(false)
    }
  }, [biz, preview])

  useEffect(() => {
    if (serverMode === 'unknown') return
    void load()
  }, [serverMode, load])

  // 名称搜索（子串、大小写不敏感）与分组过滤叠加：两者都为空时即完整列表
  const keyword = search.trim().toLowerCase()
  const filtered = templates.filter((t) => {
    if (group && t.group !== group) return false
    if (keyword && !t.name.toLowerCase().includes(keyword)) return false
    return true
  })

  const doDelete = async () => {
    if (!deleting) return
    setBusy('delete')
    try {
      await biz.deleteTemplate(deleting.name)
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
      const { blob, filename } = await biz.exportTemplate(t.name)
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
      const name = await biz.importTemplate(file)
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
        <div style={{ position: 'relative', display: 'inline-flex', alignItems: 'center' }}>
          <Icon name="search" size={13} style={{ position: 'absolute', left: 7, color: 'var(--ink-3)', pointerEvents: 'none' }} />
          <input
            className="input"
            value={search}
            onChange={(ev) => setSearch(ev.target.value)}
            placeholder="搜索模板名称"
            title="按模板名称搜索（子串匹配，不分大小写）"
            spellCheck={false}
            style={{ width: 170, paddingLeft: 26 }}
          />
        </div>
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
        {loading || serverMode === 'unknown' ? (
          <div className="empty">
            <Icon name="refresh" />
            <div className="empty-title">{serverMode === 'unknown' ? '正在探测连接…' : '加载中…'}</div>
          </div>
        ) : templates.length === 0 ? (
          <div className="empty">
            <Icon name="workbench" />
            <div className="empty-title">还没有模板</div>
            <div className="hint">点击「新建模板」开始设计第一张标签，或导入已有的 .lfpkg 模板包。</div>
          </div>
        ) : filtered.length === 0 ? (
          <div className="empty">
            <Icon name="search" />
            <div className="empty-title">没有匹配的模板</div>
            <div className="hint">当前搜索词或分组下没有模板，请调整关键词或分组后重试。</div>
          </div>
        ) : (
          <div className="wb-grid">
            {filtered.map((t) => (
              <div key={t.name} className="wb-card" onDoubleClick={() => onOpenDesigner({ kind: 'edit', name: t.name })} title="双击打开设计器">
                <div className="wb-card-thumb">
                  <PreviewThumb name={t.name} entry={preview.get(t.name)} onEnlarge={setEnlarged} />
                </div>
                <div className="wb-card-body">
                  <div className="wb-card-name" title={t.name}>{t.name}</div>
                  <div className="wb-card-meta">
                    <span className="badge neutral">{t.group}</span>
                    <span className="mono" title={new Date(t.updatedAt).toLocaleString('zh-CN', { hour12: false })}>
                      {new Date(t.updatedAt).toLocaleDateString('zh-CN')}
                    </span>
                  </div>
                </div>
                <div className="wb-card-foot">
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
              </div>
            ))}
          </div>
        )}
      </div>

      {enlarged && (() => {
        const entry = preview.get(enlarged)
        return entry ? <TemplatePreviewModal name={enlarged} entry={entry} onClose={() => setEnlarged(null)} /> : null
      })()}

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
