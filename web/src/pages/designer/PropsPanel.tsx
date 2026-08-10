// 属性面板：选中元素才显示；多选显示对齐操作；文本 / 条码 / 二维码 / 矩形等分组属性

import { useEffect, useState } from 'react'
import type { DesignElement } from '../../lib/design/types'
import { typeLabel } from '../../lib/design/types'
import { elementsByIds } from '../../lib/design/model'
import { Icon } from '../../components/Icon'

export interface PropsPanelProps {
  elements: DesignElement[]
  selected: string[]
  viewMode: 'fit' | 'preview'
  onChange: (id: string, patch: Partial<DesignElement>) => void
  onAlign: (align: 'left' | 'centerH' | 'right' | 'top' | 'centerV' | 'bottom') => void
  onDelete: (ids: string[]) => void
}

export function PropsPanel({ elements, selected, viewMode, onChange, onAlign, onDelete }: PropsPanelProps) {
  if (viewMode === 'preview') {
    return <div className="props-empty">预览中：画布已锁定（隐藏网格 / 标尺 / 参考线），退出预览后可编辑。</div>
  }
  if (selected.length === 0) {
    return <div className="props-empty">在画布上选中元素后显示属性。</div>
  }
  const sel = elementsByIds(elements, selected)
  if (sel.length > 1) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div style={{ fontWeight: 600 }}>已选 {sel.length} 个元素</div>
        <div className="group">
          <div className="group-title">对齐（以包围框为基准）</div>
          <div className="align-grid">
            {(
              [
                ['左对齐', 'left'],
                ['水平居中', 'centerH'],
                ['右对齐', 'right'],
                ['上对齐', 'top'],
                ['垂直居中', 'centerV'],
                ['下对齐', 'bottom'],
              ] as const
            ).map(([label, key]) => (
              <button key={key} className="btn sm" onClick={() => onAlign(key)}>
                {label}
              </button>
            ))}
          </div>
        </div>
        <button className="btn danger" onClick={() => onDelete(selected)}>
          <Icon name="trash" size={13} />
          删除选中
        </button>
      </div>
    )
  }

  const e = sel[0]
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ fontWeight: 600, fontSize: 13 }}>
        {typeLabel(e)}
        {'key' in e && e.key ? <span className="mono" style={{ color: 'var(--accent)', marginLeft: 6 }}>{e.key}</span> : null}
      </div>

      <div className="group">
        <div className="group-title">位置 / 尺寸（mm，相对标签内容区）</div>
        <NumField label="X" value={e.x} onSet={(v) => onChange(e.id, { x: v })} />
        <NumField label="Y" value={e.y} onSet={(v) => onChange(e.id, { y: v })} />
        {e.type !== 'Line' && (
          <>
            <NumField
              label="宽"
              value={e.w}
              onSet={(v) => onChange(e.id, e.type === 'QrCode' ? { w: Math.max(1, v), h: Math.max(1, v) } : { w: Math.max(1, v) })}
            />
            <NumField
              label="高"
              value={e.h}
              onSet={(v) => onChange(e.id, e.type === 'QrCode' ? { h: Math.max(1, v), w: Math.max(1, v) } : { h: Math.max(1, v) })}
            />
          </>
        )}
      </div>

      {(e.type === 'Text' || e.type === 'Barcode' || e.type === 'QrCode') && (
        <>
          <ContentGroup e={e} onChange={onChange} />
          <div className="group">
            <div className="group-title">边框 / 内边距（通用）</div>
            <NumField label="左右内边距" value={e.paddingH ?? 0} onSet={(v) => onChange(e.id, { paddingH: Math.max(0, v) })} />
            <NumField label="上下内边距" value={e.paddingV ?? 0} onSet={(v) => onChange(e.id, { paddingV: Math.max(0, v) })} />
            <NumField label="边框" value={e.border ?? 0} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
          </div>
        </>
      )}

      {e.type === 'Text' && (
        <div className="group">
          <div className="group-title">文本 / 字体</div>
          <SelectField
            label="字体"
            value={e.fontFamily || 'Microsoft YaHei'}
            options={[
              ['微软雅黑', 'Microsoft YaHei'],
              ['宋体', 'SimSun'],
              ['黑体', 'SimHei'],
              ['楷体', 'KaiTi'],
              ['Arial', 'Arial'],
              ['Consolas', 'Consolas'],
            ]}
            onSet={(v) => onChange(e.id, { fontFamily: v })}
          />
          <NumField
            label="字高"
            value={e.fontH}
            onSet={(v) => {
              const fontH = Math.max(1, v)
              onChange(e.id, fontH > e.h ? { fontH, h: fontH } : { fontH })
            }}
          />
          <CheckField label="自动换行" value={e.wrap === true} onSet={(v) => onChange(e.id, { wrap: v })} />
          <CheckField label="加粗（打印更清晰）" value={e.bold === true} onSet={(v) => onChange(e.id, { bold: v })} />
          <NumField label="行间距" value={e.lineHeight ?? 1.2} onSet={(v) => onChange(e.id, { lineHeight: Math.max(1, v) })} />
          <SelectField
            label="水平对齐"
            value={e.align}
            options={[
              ['左对齐', 'Left'],
              ['居中', 'Center'],
              ['右对齐', 'Right'],
            ]}
            onSet={(v) => onChange(e.id, { align: v as 'Left' | 'Center' | 'Right' })}
          />
          <SelectField
            label="垂直对齐"
            value={e.valign ?? 'middle'}
            options={[
              ['顶端', 'top'],
              ['居中', 'middle'],
              ['底部', 'bottom'],
            ]}
            onSet={(v) => onChange(e.id, { valign: v as 'top' | 'middle' | 'bottom' })}
          />
          <SelectField
            label="单行溢出"
            value={e.fitMode ?? 'shrink'}
            options={[
              ['缩小适应', 'shrink'],
              ['隐藏', 'overflow'],
            ]}
            onSet={(v) => onChange(e.id, { fitMode: v as 'shrink' | 'overflow' })}
          />
          <div className="hint">单行：超宽整体缩小（或隐藏）。自动换行：超出右侧边界换行，换行后超过下边界隐藏（不缩小字体）。</div>
        </div>
      )}

      {e.type === 'Barcode' && (
        <div className="group">
          <div className="group-title">条码参数</div>
          <SelectField
            label="码制"
            value={e.barcodeFormat || 'CODE128'}
            options={[
              ['Code128', 'CODE128'],
              ['EAN13', 'EAN13'],
              ['CODE39', 'CODE39'],
              ['UPC', 'UPC'],
            ]}
            onSet={(v) => onChange(e.id, { barcodeFormat: v })}
          />
          <CheckField label="底部显示文字" value={e.displayValue !== false} onSet={(v) => onChange(e.id, { displayValue: v })} />
          <NumField label="模块宽" value={e.moduleWidth ?? 1} onSet={(v) => onChange(e.id, { moduleWidth: Math.max(0.5, v) })} />
        </div>
      )}

      {e.type === 'QrCode' && (
        <div className="group">
          <div className="group-title">二维码参数</div>
          <SelectField
            label="纠错级别"
            value={e.qrEcc ?? 'M'}
            options={[
              ['L（约 7%）', 'L'],
              ['M（约 15%）', 'M'],
              ['Q（约 25%）', 'Q'],
              ['H（约 30%）', 'H'],
            ]}
            onSet={(v) => onChange(e.id, { qrEcc: v as 'L' | 'M' | 'Q' | 'H' })}
          />
          <NumField label="边距" value={e.qrMargin ?? 2} onSet={(v) => onChange(e.id, { qrMargin: Math.max(0, v) })} />
        </div>
      )}

      {e.type === 'Rect' && (
        <div className="group">
          <div className="group-title">矩形（镂空，仅边框）</div>
          <NumField label="边框" value={e.border ?? 0} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
        </div>
      )}

      {e.type === 'Line' && (
        <div className="group">
          <div className="group-title">线（兼容显示）</div>
          <NumField label="长度 X" value={e.w} onSet={(v) => onChange(e.id, { w: v })} />
          <NumField label="长度 Y" value={e.h} onSet={(v) => onChange(e.id, { h: v })} />
          <NumField label="线宽" value={e.thickness ?? 0.5} onSet={(v) => onChange(e.id, { thickness: Math.max(0.1, v) })} />
        </div>
      )}

      {(e.type === 'Image' || e.type === 'Region') && (
        <div className="group">
          <div className="group-title">{e.type === 'Region' ? '容器' : '图片'}（兼容显示）</div>
          {e.type === 'Region' && <div className="hint">Id：{e.containerId}（只读）</div>}
          {e.type === 'Image' && <div className="hint">图片资源经模板包导入导出，本页不提供编辑入口。</div>}
          <NumField label="边框" value={e.border ?? 0} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
        </div>
      )}

      <button className="btn danger" onClick={() => onDelete([e.id])}>
        <Icon name="trash" size={13} />
        删除元素
      </button>
    </div>
  )
}

/** 填充：固定值 / 字段填充（键名称 + 预览值）。 */
function ContentGroup({ e, onChange }: { e: DesignElement; onChange: (id: string, patch: Partial<DesignElement>) => void }) {
  if (e.type !== 'Text' && e.type !== 'Barcode' && e.type !== 'QrCode') return null
  const set = (patch: Partial<typeof e>) => onChange(e.id, patch)
  return (
    <div className="group">
      <div className="group-title">填充（固定值或字段填充）</div>
      <SelectField
        label="来源"
        value={e.mode}
        options={[
          ['固定值', 'literal'],
          ['字段填充', 'field'],
        ]}
        onSet={(v) => set(v === 'literal' ? { mode: 'literal', key: '' } : { mode: 'field' })}
      />
      {e.mode === 'literal' ? (
        <label className="field">
          固定值（立即渲染）
          <input className="input" value={e.text} onChange={(ev) => set({ text: ev.target.value })} placeholder="例如：库位 A-01-02" />
        </label>
      ) : (
        <>
          <label className="field">
            键名称（契约字段，自动建立）
            <input className="input mono" value={e.key} onChange={(ev) => set({ key: ev.target.value })} placeholder="例如：location" />
          </label>
          <label className="field">
            预览值（仅画布显示）
            <input className="input" value={e.text} onChange={(ev) => set({ text: ev.target.value })} placeholder="打印以外界数据为准" />
          </label>
        </>
      )}
      <div className="hint">打印时从外界数据取「键名称」对应字段填充，预览值会被忽略。</div>
    </div>
  )
}

function NumField({ label, value, onSet }: { label: string; value: number; onSet: (v: number) => void }) {
  // 受控 + 同步：切换选中元素（value 变化）时输入框跟随刷新（此前 defaultValue 只在挂载时生效，切换后残留旧元素的值）
  const [text, setText] = useState(Number(value || 0).toFixed(1))
  useEffect(() => setText(Number(value || 0).toFixed(1)), [value])
  return (
    <label className="num-row">
      <span>{label}</span>
      <input
        className="input"
        type="number"
        step="0.5"
        value={text}
        onChange={(ev) => setText(ev.target.value)}
        onBlur={(ev) => {
          const v = parseFloat(ev.target.value)
          if (!isNaN(v) && v !== value) onSet(v)
          else setText(Number(value || 0).toFixed(1))
        }}
        onKeyDown={(ev) => {
          if (ev.key === 'Enter') (ev.target as HTMLInputElement).blur()
        }}
      />
    </label>
  )
}

function SelectField({ label, value, options, onSet }: { label: string; value: string; options: [string, string][]; onSet: (v: string) => void }) {
  return (
    <label className="num-row">
      <span>{label}</span>
      <select className="input" value={value} onChange={(ev) => onSet(ev.target.value)}>
        {options.map(([text, val]) => (
          <option key={val} value={val}>
            {text}
          </option>
        ))}
      </select>
    </label>
  )
}

function CheckField({ label, value, onSet }: { label: string; value: boolean; onSet: (v: boolean) => void }) {
  return (
    <label className="num-row">
      <span>{label}</span>
      <input type="checkbox" checked={value} onChange={(ev) => onSet(ev.target.checked)} style={{ width: 'auto' }} />
    </label>
  )
}
