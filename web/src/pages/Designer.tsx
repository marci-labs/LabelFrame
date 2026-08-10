// 设计器（核心）：顶栏（模板名/分组/纸张/DPI/预览/保存）+ 左栏（控件/字段/图层）
// + 画布（移植原型交互）+ 右栏（属性 / 测试数据 Tab）。
// 状态：stateRef 为同步真相（事件回调内先更新再 setState），历史为快照式。

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { TemplatePackage } from '../lib/api/types'
import type { DesignerRequest } from '../state/types'
import { useApp } from '../state/AppContext'
import { CanvasViewport } from './designer/CanvasViewport'
import type { DesignState } from './designer/CanvasViewport'
import { SidePanel } from './designer/SidePanel'
import { PropsPanel } from './designer/PropsPanel'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
import type { DesignElement } from '../lib/design/types'
import { cloneElement, defaultElement } from '../lib/design/types'
import { deriveFields } from '../lib/design/fields'
import { createHistory } from '../lib/design/history'
import { r2 } from '../lib/design/geometry'
import { exportDesign, parseDesign } from '../lib/design/format'
import { fromBackendElements, toContract, toLayout } from '../lib/design/convert'
import { SHORTCUT_GROUPS } from './designer/shortcuts'

const snap = (s: DesignState) => JSON.stringify({ paperW: s.paperW, paperH: s.paperH, elements: s.elements })
const parse = (s: string): DesignState => JSON.parse(s) as DesignState

interface DesignerProps {
  request: DesignerRequest
  onClose: () => void
}

export function Designer({ request, onClose }: DesignerProps) {
  const app = useApp()
  const [state, setState] = useState<DesignState | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [viewMode, setViewMode] = useState<'fit' | 'preview'>('fit')
  const [dpi, setDpi] = useState(203)
  const [zoom, setZoom] = useState(1)
  const [gridOn, setGridOn] = useState(true)
  const [pendingType, setPendingType] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [group, setGroup] = useState('默认')
  const [rightTab, setRightTab] = useState<'props' | 'data'>('props')
  const [saving, setSaving] = useState(false)
  const [confirmOverwrite, setConfirmOverwrite] = useState(false)
  const [shortcutsOpen, setShortcutsOpen] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)

  const stateRef = useRef<DesignState | null>(null)
  const historyRef = useRef<ReturnType<typeof createHistory<DesignState>> | null>(null)
  const initialNameRef = useRef('')
  const contractNameRef = useRef('')
  const contractVersionRef = useRef('1')
  const selectedRef = useRef<string[]>([])

  const commit = useCallback((next: DesignState) => {
    stateRef.current = next
    if (historyRef.current) historyRef.current = historyRef.current.commit(next)
    setState(next)
  }, [])

  const applyElements = useCallback(
    (updater: (els: DesignElement[]) => DesignElement[]) => {
      const prev = stateRef.current
      if (!prev) return
      const next = { ...prev, elements: updater(prev.elements) }
      stateRef.current = next
      setState(next)
    },
    [],
  )

  const commitNow = useCallback(() => {
    if (historyRef.current && stateRef.current) {
      historyRef.current = historyRef.current.commit(stateRef.current)
    }
  }, [])

  // ---------- 加载 ----------
  // 注意：effect 依赖只用 request —— app 为 context 对象，每次渲染新引用，
  // 若作为依赖会与 setStatus（触发 context 更新）形成无限循环。
  useEffect(() => {
    let cancelled = false
    const status = app.setStatus
    const init = (s: DesignState, pkg?: TemplatePackage) => {
      if (cancelled) return
      stateRef.current = s
      historyRef.current = createHistory(s, snap, parse)
      setState(s)
      setSelected([])
      selectedRef.current = []
      setZoom(1)
      setViewMode('fit')
      if (pkg) {
        setName(pkg.name)
        setGroup(pkg.group || '默认')
        initialNameRef.current = pkg.name
        contractNameRef.current = pkg.contract?.name ?? pkg.name
        contractVersionRef.current = pkg.contract?.version ?? '1'
      }
    }

    if (request.kind === 'new') {
      init({ paperW: 100, paperH: 60, elements: [] })
      status('新建模板：控件栏添加元素，保存后返回工作台。')
      return
    }
    let alive = true
    void api
      .getTemplate(request.name!)
      .then((pkg) => {
        if (!alive) return
        init(
          {
            paperW: pkg.layout?.widthMm || 100,
            paperH: pkg.layout?.heightMm || 60,
            elements: fromBackendElements(pkg.layout?.elements ?? []),
          },
          pkg,
        )
        status(`已打开模板「${pkg.name}」。`)
      })
      .catch((err) => {
        if (!alive) return
        setLoadError(err instanceof ApiError ? err.message : '加载模板失败。')
      })
    return () => {
      alive = false
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [request])

  // ---------- 选择 ----------
  const handleSelect = useCallback((ids: string[], toggle?: boolean) => {
    setSelected((prev) => {
      const next = toggle ? ids.filter((id) => !prev.includes(id)).concat(prev.filter((id) => !ids.includes(id))) : ids
      selectedRef.current = next
      return next
    })
  }, [])

  // ---------- 元素操作 ----------
  const addElementAt = useCallback(
    (type: string, xMm: number, yMm: number) => {
      const s = stateRef.current
      if (!s) return
      const e = defaultElement(type as 'Text' | 'Barcode' | 'QrCode' | 'Rect') as DesignElement
      e.x = Math.max(0, Math.min(s.paperW - 2, r2(xMm)))
      e.y = Math.max(0, Math.min(s.paperH - 2, r2(yMm)))
      const next = { ...s, elements: [...s.elements, e] }
      commit(next)
      setSelected([e.id])
      selectedRef.current = [e.id]
      setPendingType(null)
      app.setStatus(`已添加「${type === 'Barcode' ? '条码' : type === 'QrCode' ? '二维码' : type === 'Rect' ? '矩形' : '文本'}」。`)
    },
    [app, commit],
  )

  const changeElement = useCallback(
    (id: string, patch: Partial<DesignElement>) => {
      const s = stateRef.current
      if (!s) return
      commit({ ...s, elements: s.elements.map((e) => (e.id === id ? ({ ...e, ...patch } as DesignElement) : e)) })
    },
    [commit],
  )

  const deleteElements = useCallback(
    (ids: string[]) => {
      const s = stateRef.current
      if (!s || ids.length === 0) return
      const next = { ...s, elements: s.elements.filter((e) => !ids.includes(e.id)) }
      commit(next)
      setSelected([])
      selectedRef.current = []
      app.setStatus(`已删除 ${ids.length} 个元素。`)
    },
    [app, commit],
  )

  const alignSelected = useCallback(
    (align: 'left' | 'centerH' | 'right' | 'top' | 'centerV' | 'bottom') => {
      const s = stateRef.current
      if (!s) return
      const sel = s.elements.filter((e) => selectedRef.current.includes(e.id) && e.type !== 'Region')
      if (sel.length < 2) {
        app.setStatus('对齐需要至少 2 个元素（容器除外）。')
        return
      }
      const left = Math.min(...sel.map((e) => e.x))
      const right = Math.max(...sel.map((e) => e.x + e.w))
      const top = Math.min(...sel.map((e) => e.y))
      const bottom = Math.max(...sel.map((e) => e.y + e.h))
      const ids = new Set(sel.map((e) => e.id))
      const next = {
        ...s,
        elements: s.elements.map((e) => {
          if (!ids.has(e.id)) return e
          const out: DesignElement = { ...e }
          delete out.regionId
          switch (align) {
            case 'left':
              out.x = left
              break
            case 'centerH':
              out.x = left + (right - left - e.w) / 2
              break
            case 'right':
              out.x = right - e.w
              break
            case 'top':
              out.y = top
              break
            case 'centerV':
              out.y = top + (bottom - top - e.h) / 2
              break
            case 'bottom':
              out.y = bottom - e.h
              break
          }
          return out
        }),
      }
      commit(next)
      app.setStatus(`已对齐 ${sel.length} 个元素。`)
    },
    [app, commit],
  )

  // ---------- 图层 ----------
  const moveLayer = useCallback(
    (delta: number) => {
      const s = stateRef.current
      if (!s || selectedRef.current.length !== 1) {
        app.setStatus('请先单选一个元素再调整层级。')
        return
      }
      const idx = s.elements.findIndex((e) => e.id === selectedRef.current[0])
      const ni = idx + delta
      if (idx < 0 || ni < 0 || ni >= s.elements.length) return
      const els = [...s.elements]
      ;[els[idx], els[ni]] = [els[ni], els[idx]]
      commit({ ...s, elements: els })
    },
    [app, commit],
  )

  const layerToTop = useCallback(() => {
    const s = stateRef.current
    if (!s || selectedRef.current.length !== 1) return
    const id = selectedRef.current[0]
    const e = s.elements.find((x) => x.id === id)
    if (!e) return
    commit({ ...s, elements: [...s.elements.filter((x) => x.id !== id), e] })
  }, [commit])

  const layerToBottom = useCallback(() => {
    const s = stateRef.current
    if (!s || selectedRef.current.length !== 1) return
    const id = selectedRef.current[0]
    const e = s.elements.find((x) => x.id === id)
    if (!e) return
    commit({ ...s, elements: [e, ...s.elements.filter((x) => x.id !== id)] })
  }, [commit])

  // ---------- 撤销 / 重做 / 复制粘贴 ----------
  const undo = useCallback(() => {
    const h = historyRef.current
    if (!h) return
    const next = h.undo()
    if (!next) {
      app.setStatus('没有可撤销的操作。')
      return
    }
    historyRef.current = next
    stateRef.current = next.data
    setState(next.data)
    setSelected([])
    selectedRef.current = []
    app.setStatus('已撤销。')
  }, [app])

  const redo = useCallback(() => {
    const h = historyRef.current
    if (!h) return
    const next = h.redo()
    if (!next) {
      app.setStatus('没有可恢复的操作。')
      return
    }
    historyRef.current = next
    stateRef.current = next.data
    setState(next.data)
    setSelected([])
    selectedRef.current = []
    app.setStatus('已恢复。')
  }, [app])

  const clipboardRef = useRef<DesignElement[]>([])

  const copySelected = useCallback(() => {
    const s = stateRef.current
    if (!s) return
    const items = s.elements.filter((e) => selectedRef.current.includes(e.id))
    if (!items.length) return
    clipboardRef.current = items.map((e) => cloneElement(e))
    app.setStatus(`已复制 ${clipboardRef.current.length} 个元素。`)
  }, [app])

  const pasteClipboard = useCallback(() => {
    const s = stateRef.current
    if (!s || clipboardRef.current.length === 0) return
    const copies = clipboardRef.current.map((e) => {
      const c = cloneElement(e)
      c.x = Math.max(0, Math.min(s.paperW - c.w - 1, c.x + 5))
      c.y = Math.max(0, Math.min(s.paperH - c.h - 1, c.y + 5))
      return c
    })
    commit({ ...s, elements: [...s.elements, ...copies] })
    const ids = copies.map((c) => c.id)
    setSelected(ids)
    selectedRef.current = ids
    app.setStatus(`已粘贴 ${copies.length} 个元素（偏移 5mm）。`)
  }, [app, commit])

  // ---------- 设计 JSON 导入 / 导出（剪贴板，prompt 兜底） ----------
  const doExportDesign = useCallback(async () => {
    const s = stateRef.current
    if (!s) return
    const text = exportDesign(s.paperW, s.paperH, s.elements)
    if (navigator.clipboard?.writeText) {
      try {
        await navigator.clipboard.writeText(text)
        app.setStatus(`设计已复制到剪贴板（${s.elements.length} 个元素），可用「导入设计」恢复。`)
        return
      } catch {
        // 走 prompt 兜底
      }
    }
    const input = window.prompt('复制以下设计代码（Ctrl+C），可用「导入设计」恢复：', text)
    if (input !== null) app.setStatus('已生成设计代码。')
  }, [app])

  const doImportDesign = useCallback(async () => {
    let text = ''
    if (navigator.clipboard?.readText) {
      try {
        text = await navigator.clipboard.readText()
      } catch {
        text = ''
      }
    }
    if (!text) text = window.prompt('请粘贴设计代码：') ?? ''
    if (!text) return
    try {
      const d = parseDesign(text)
      commit({ paperW: d.paperW, paperH: d.paperH, elements: d.elements })
      setSelected([])
      selectedRef.current = []
      setPendingType(null)
      app.setStatus(`已从剪贴板导入设计（${d.elements.length} 个元素）。`)
    } catch (err) {
      app.setStatus('导入失败：' + (err instanceof Error ? err.message : '未知错误'))
    }
  }, [app, commit])

  // ---------- 快捷键 ----------
  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      const s = stateRef.current
      if (!s) return
      const tag = ev.target instanceof HTMLElement ? ev.target.tagName : ''
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return
      const ctrl = ev.ctrlKey || ev.metaKey
      if (viewMode === 'preview') return

      if (ctrl && ev.shiftKey && ev.key.toLowerCase() === 'c') {
        ev.preventDefault()
        void doExportDesign()
        return
      }
      if (ctrl && ev.shiftKey && ev.key.toLowerCase() === 'v') {
        ev.preventDefault()
        void doImportDesign()
        return
      }
      if (ctrl && !ev.shiftKey && ev.key.toLowerCase() === 'z') {
        ev.preventDefault()
        undo()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'y') {
        ev.preventDefault()
        redo()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'c') {
        ev.preventDefault()
        copySelected()
        return
      }
      if (ctrl && ev.key.toLowerCase() === 'v') {
        ev.preventDefault()
        pasteClipboard()
        return
      }
      if (ev.key === 'Delete' || ev.key === 'Backspace') {
        if (selectedRef.current.length) {
          ev.preventDefault()
          deleteElements(selectedRef.current)
        }
        return
      }
      if (ev.key === 'Escape' && pendingType) {
        setPendingType(null)
        app.setStatus('已取消放置。')
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [viewMode, pendingType, app, doExportDesign, doImportDesign, undo, redo, copySelected, pasteClipboard, deleteElements])

  // ---------- 预览 ----------
  const togglePreview = useCallback(() => {
    if (viewMode === 'preview') {
      setViewMode('fit')
      setZoom(1)
      setPendingType(null)
      app.setStatus('已退出预览，画布适应窗口。')
    } else {
      setViewMode('preview')
      setZoom(1)
      setSelected([])
      selectedRef.current = []
      app.setStatus(`打印预览：${dpi} dpi（1mm ≈ ${Math.round(dpi / 25.4)} 点）；网格 / 标尺已隐藏，画布已锁定；可中键平移 / Ctrl+滚轮缩放。`)
    }
  }, [app, dpi, viewMode])

  // ---------- 保存 ----------
  const doSave = useCallback(
    async (finalName: string) => {
      const s = stateRef.current
      if (!s) return
      setSaving(true)
      try {
        const fields = deriveFields(s.elements)
        const version = request.kind === 'new' ? '1' : contractVersionRef.current
        const contractName = request.kind === 'new' ? finalName : contractNameRef.current
        const pkg: TemplatePackage = {
          name: finalName,
          group: group.trim() || '默认',
          contract: toContract(contractName, version, fields),
          // 迭代 12：不传 testData——由后端从元素 previewValue 自动派生（读-改-写，旧值不丢）
          layout: toLayout(finalName, contractName, version, s.paperW, s.paperH, s.elements),
        }
        await api.saveTemplate(pkg)
        app.setStatus(`模板「${finalName}」已保存。`)
        onClose()
      } catch (err) {
        app.setStatus(err instanceof ApiError ? err.message : '保存失败。')
      } finally {
        setSaving(false)
      }
    },
    [app, group, onClose, request.kind],
  )

  const save = useCallback(() => {
    const trimmed = name.trim()
    if (!trimmed) {
      app.setStatus('请先填写模板名称。')
      return
    }
    const isNew = request.kind === 'new'
    const renamed = !isNew && trimmed !== initialNameRef.current
    if (isNew || renamed) {
      void api
        .listTemplates()
        .then((list) => {
          if (list.some((t) => t.name === trimmed)) setConfirmOverwrite(true)
          else void doSave(trimmed)
        })
        .catch(() => void doSave(trimmed))
    } else {
      void doSave(trimmed)
    }
  }, [app, doSave, name, request.kind])

  const fields = useMemo(() => (state ? deriveFields(state.elements) : []), [state])

  // 测试默认值只读预览：与后端 SaveAsync 派生语义一致（遍历元素，后出现覆盖先出现）
  const previewDefaults = useMemo(() => {
    if (!state) return null
    const m = new Map<string, string>()
    for (const e of state.elements) {
      if ('mode' in e && e.mode === 'field' && e.key && e.text) m.set(e.key, e.text)
    }
    return m
  }, [state])

  // ---------- 渲染 ----------
  return (
    <div className="page designer-page">
      <div className="designer-toolbar">
        <button className="btn ghost" onClick={onClose} title="返回工作台">
          <Icon name="back" size={14} />
        </button>
        <input className="input" style={{ width: 150 }} value={name} onChange={(ev) => setName(ev.target.value)} placeholder="模板名称" title="模板名称" />
        <input className="input" style={{ width: 100 }} value={group} onChange={(ev) => setGroup(ev.target.value)} placeholder="分组" title="分组" />
        <span className="toolbar-sep" />
        <label className="toolbar-label">
          宽
          <input
            className="input num"
            type="number"
            style={{ width: 56 }}
            value={state?.paperW ?? ''}
            min={5}
            max={300}
            onChange={(ev) => {
              const v = parseFloat(ev.target.value)
              const s = stateRef.current
              if (s && !isNaN(v) && v > 0) commit({ ...s, paperW: Math.min(300, v) })
            }}
          />
          mm
        </label>
        <label className="toolbar-label">
          高
          <input
            className="input num"
            type="number"
            style={{ width: 56 }}
            value={state?.paperH ?? ''}
            min={5}
            max={300}
            onChange={(ev) => {
              const v = parseFloat(ev.target.value)
              const s = stateRef.current
              if (s && !isNaN(v) && v > 0) commit({ ...s, paperH: Math.min(300, v) })
            }}
          />
          mm
        </label>
        <span className="toolbar-sep" />
        <select className="input" value={dpi} onChange={(ev) => setDpi(parseInt(ev.target.value, 10))} title="打印 DPI">
          <option value={203}>203 dpi</option>
          <option value={300}>300 dpi</option>
        </select>
        <button className={'btn' + (viewMode === 'preview' ? ' active' : '')} onClick={togglePreview} title="按所选 DPI 以真实打印比例显示">
          <Icon name="preview" size={13} />
          {viewMode === 'preview' ? '退出预览' : '预览打印效果'}
        </button>
        <label className="toolbar-label" title="显示毫米网格">
          <input type="checkbox" checked={gridOn} onChange={(ev) => setGridOn(ev.target.checked)} />
          网格
        </label>
        <span className="toolbar-sep" />
        <span className="mono zoom-label" title="内容缩放（Ctrl+滚轮）">
          {Math.round(zoom * 100)}%
        </span>
        <span className="spacer" style={{ flex: 1 }} />
        <button className="btn ghost" onClick={() => setShortcutsOpen(true)} title="快捷键与画布操作说明">
          <Icon name="keyboard" size={13} />
          快捷键
        </button>
        <button className="btn" onClick={() => void doExportDesign()} title="Ctrl+Shift+C">
          <Icon name="clipboard" size={13} />
          导出设计
        </button>
        <button className="btn" onClick={() => void doImportDesign()} title="Ctrl+Shift+V">
          <Icon name="upload" size={13} />
          导入设计
        </button>
        <button className="btn primary" onClick={save} disabled={saving || !state}>
          <Icon name="save" size={13} />
          {saving ? '保存中…' : '保存模板'}
        </button>
      </div>

      {loadError && (
        <div style={{ padding: '6px 16px', background: 'var(--danger-soft)', color: 'var(--danger)', fontSize: 12, display: 'flex', alignItems: 'center', gap: 10 }}>
          {loadError}
          <button className="btn sm" onClick={onClose}>
            返回工作台
          </button>
        </div>
      )}

      {state && (
        <div className="designer-body">
          <SidePanel
            elements={state.elements}
            selected={selected}
            viewMode={viewMode}
            pendingType={pendingType}
            fields={fields}
            onPickType={(t) => setPendingType(t)}
            onSelect={(id, toggle) => handleSelect([id], toggle)}
            onMoveLayer={moveLayer}
            onLayerTop={layerToTop}
            onLayerBottom={layerToBottom}
            onDelete={deleteElements}
          />
          <CanvasViewport
            state={state}
            selected={selected}
            viewMode={viewMode}
            dpi={dpi}
            zoom={zoom}
            gridOn={gridOn}
            pendingType={pendingType}
            onSelect={handleSelect}
            onAddElement={addElementAt}
            onUpdateElements={applyElements}
            onCommit={commitNow}
            onZoomChange={setZoom}
          />
          <aside className="designer-right">
            <div className="right-tabs">
              <button className={'right-tab' + (rightTab === 'props' ? ' active' : '')} onClick={() => setRightTab('props')}>
                属性
              </button>
              <button className={'right-tab' + (rightTab === 'data' ? ' active' : '')} onClick={() => setRightTab('data')}>
                测试默认值
              </button>
            </div>
            {rightTab === 'props' ? (
              <div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>
                <PropsPanel
                  elements={state.elements}
                  selected={selected}
                  viewMode={viewMode}
                  onChange={changeElement}
                  onAlign={alignSelected}
                  onDelete={deleteElements}
                />
              </div>
            ) : (
              <div style={{ flex: 1, overflowY: 'auto', minHeight: 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
                <div className="group">
                  <div className="group-title">测试默认值（由元素预览值自动生成）</div>
                  {!previewDefaults || previewDefaults.size === 0 ? (
                    <div className="hint">暂无默认值。为「字段填充」控件设置预览值后，保存时自动生成测试默认值。</div>
                  ) : (
                    [...previewDefaults.entries()].map(([k, v]) => (
                      <div className="field" key={k} style={{ marginTop: 6 }}>
                        <span className="mono" style={{ minWidth: 90 }}>{k}</span>
                        <span className="mono" style={{ color: 'var(--text-2)', wordBreak: 'break-all' }}>{v}</span>
                      </div>
                    ))
                  )}
                  <div className="hint" style={{ marginTop: 8 }}>
                    测试默认值由元素预览值自动生成，保存后作为打印测试 / PDA 测试默认值；保存后生效。
                  </div>
                </div>
              </div>
            )}
          </aside>
        </div>
      )}

      {shortcutsOpen && (
        <Modal
          title="快捷操作"
          onClose={() => setShortcutsOpen(false)}
          width={520}
          footer={
            <button className="btn primary" onClick={() => setShortcutsOpen(false)}>
              知道了
            </button>
          }
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {SHORTCUT_GROUPS.map((g) => (
              <div key={g.title}>
                <div className="group-title" style={{ marginBottom: 4 }}>{g.title}</div>
                <table className="table">
                  <tbody>
                    {g.items.map((item) => (
                      <tr key={item.desc} style={{ cursor: 'default' }}>
                        <td style={{ width: 240, whiteSpace: 'nowrap' }}>
                          {item.keys.map((k) => (
                            <kbd key={k} className="kbd">{k}</kbd>
                          ))}
                        </td>
                        <td style={{ fontSize: 12 }}>{item.desc}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}
          </div>
        </Modal>
      )}

      {confirmOverwrite && (
        <Modal
          title="模板已存在"
          onClose={() => setConfirmOverwrite(false)}
          footer={
            <>
              <button className="btn" onClick={() => setConfirmOverwrite(false)}>
                取消
              </button>
              <button className="btn primary" onClick={() => { setConfirmOverwrite(false); void doSave(name.trim()) }}>
                覆盖保存
              </button>
            </>
          }
        >
          <p>
            已存在同名模板「<b>{name.trim()}</b>」，保存将覆盖原模板。确定继续吗？
          </p>
        </Modal>
      )}
    </div>
  )
}
