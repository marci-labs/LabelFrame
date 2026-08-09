// 设计器左侧栏：控件栏（点击放置 / 拖入画布）+ 契约字段（只读推导）+ 图层

import type { DesignElement } from '../../lib/design/types'
import { layerLabel } from '../../lib/design/types'
import { elementsByIds } from '../../lib/design/model'
import { Icon } from '../../components/Icon'

const PALETTE: { type: string; label: string; icon: 'text' | 'barcode' | 'qrcode' | 'rect' }[] = [
  { type: 'Text', label: '文本', icon: 'text' },
  { type: 'Barcode', label: '条码', icon: 'barcode' },
  { type: 'QrCode', label: '二维码', icon: 'qrcode' },
  { type: 'Rect', label: '矩形', icon: 'rect' },
]

export interface SidePanelProps {
  elements: DesignElement[]
  selected: string[]
  viewMode: 'fit' | 'preview'
  pendingType: string | null
  fields: string[]
  onPickType: (type: string) => void
  onSelect: (id: string, toggle?: boolean) => void
  onMoveLayer: (delta: number) => void
  onLayerTop: () => void
  onLayerBottom: () => void
  onDelete: (ids: string[]) => void
}

export function SidePanel(p: SidePanelProps) {
  const locked = p.viewMode === 'preview'
  const sel = elementsByIds(p.elements, p.selected)

  return (
    <aside className="designer-side">
      <section>
        <h3>
          控件栏
          <small>点击后在画布放置 / 拖入</small>
        </h3>
        <div className="palette">
          {PALETTE.map((it) => (
            <button
              key={it.type}
              className={'palette-btn' + (p.pendingType === it.type ? ' armed' : '')}
              draggable={!locked}
              onClick={() => {
                if (locked) {
                  return
                }
                p.onPickType(p.pendingType === it.type ? '' : it.type)
              }}
              onDragStart={(ev) => {
                ev.dataTransfer.setData('text/plain', it.type)
                ev.dataTransfer.effectAllowed = 'copy'
              }}
            >
              <PaletteIcon name={it.icon} />
              {it.label}
            </button>
          ))}
        </div>
        {p.pendingType && (
          <div className="pending-hint">
            {locked ? '预览中，先退出预览。' : `点击画布放置「${PALETTE.find((x) => x.type === p.pendingType)?.label}」（Esc 取消）`}
          </div>
        )}
      </section>

      <section>
        <h3>
          契约字段
          <small>自动推导</small>
        </h3>
        {locked ? (
          <div className="side-empty">预览中</div>
        ) : p.fields.length === 0 ? (
          <div className="side-empty">暂无字段</div>
        ) : (
          <ul className="field-list">
            {p.fields.map((k) => (
              <li key={k} className="mono">
                {k}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <h3>
          图层
          <small>点击选中 · Delete 删除</small>
        </h3>
        {locked ? (
          <div className="side-empty">预览中（退出后可编辑）</div>
        ) : (
          <ul className="layer-list">
            {p.elements.map((e, i) => (
              <li
                key={e.id}
                className={'layer-item' + (p.selected.includes(e.id) ? ' active' : '')}
                title={layerLabel(e)}
                onClick={(ev) => p.onSelect(e.id, ev.shiftKey || ev.ctrlKey)}
              >
                <span className="layer-idx mono">{i + 1}</span>
                <span className="layer-label">{layerLabel(e)}</span>
              </li>
            ))}
          </ul>
        )}
        <div className="layer-actions">
          <button className="btn sm" onClick={p.onLayerTop} disabled={locked || sel.length !== 1} title="置顶">
            <Icon name="layers" size={12} />
            置顶
          </button>
          <button className="btn sm" onClick={() => p.onMoveLayer(-1)} disabled={locked || sel.length !== 1} title="上移">
            上移
          </button>
          <button className="btn sm" onClick={() => p.onMoveLayer(1)} disabled={locked || sel.length !== 1} title="下移">
            下移
          </button>
          <button className="btn sm" onClick={p.onLayerBottom} disabled={locked || sel.length !== 1} title="置底">
            置底
          </button>
        </div>
      </section>
    </aside>
  )
}

function PaletteIcon({ name }: { name: 'text' | 'barcode' | 'qrcode' | 'rect' }) {
  switch (name) {
    case 'text':
      return (
        <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
          <path d="M5 7V5h14v2M12 5v14M9 19h6" />
        </svg>
      )
    case 'barcode':
      return (
        <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
          <path d="M4 7v10M7.5 7v10M11 7v10M14 7v10M17 7v10M20 7v10" />
        </svg>
      )
    case 'qrcode':
      return (
        <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
          <path d="M4 4h7v7H4zM13 4h7v4h-7zM4 13h4v7H4zM13 13h3v3h-3zM17 17h4v4h-4zM13 20h4M20 13v4" />
        </svg>
      )
    case 'rect':
      return <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="4" y="7" width="16" height="10" rx="1" /></svg>
  }
}
