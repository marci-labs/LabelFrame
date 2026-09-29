// 属性面板：选中元素才显示；多选显示对齐操作；文本 / 条码 / 二维码 / 矩形等分组属性
// 迭代 112（#246）：文案 key 化（designer 域 props.*）；字体下拉显示名随界面语言
// （字体内部名 / 模板存储值不变——白名单 6 项维持现状不动）。

import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
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
  const { t } = useTranslation('designer')
  if (viewMode === 'preview') {
    return <div className="props-empty">{t('props.previewLocked')}</div>
  }
  if (selected.length === 0) {
    return <div className="props-empty">{t('props.empty')}</div>
  }
  const sel = elementsByIds(elements, selected)
  if (sel.length > 1) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div style={{ fontWeight: 600 }}>{t('props.selectedCount', { count: sel.length })}</div>
        <div className="group">
          <div className="group-title">{t('props.alignTitle')}</div>
          <div className="align-grid">
            {(
              [
                [t('props.alignLeft'), 'left'],
                [t('props.alignCenterH'), 'centerH'],
                [t('props.alignRight'), 'right'],
                [t('props.alignTop'), 'top'],
                [t('props.alignCenterV'), 'centerV'],
                [t('props.alignBottom'), 'bottom'],
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
          {t('props.deleteSelected')}
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
        <div className="group-title">{t('props.positionTitle')}</div>
        <NumField label="X" value={e.x} onSet={(v) => onChange(e.id, { x: v })} />
        <NumField label="Y" value={e.y} onSet={(v) => onChange(e.id, { y: v })} />
        {e.type !== 'Line' && (
          <>
            <NumField
              label={t('props.width')}
              value={e.w}
              onSet={(v) => onChange(e.id, e.type === 'QrCode' ? { w: Math.max(1, v), h: Math.max(1, v) } : { w: Math.max(1, v) })}
            />
            <NumField
              label={t('props.height')}
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
            <div className="group-title">{t('props.boxTitle')}</div>
            <NumField label={t('props.paddingH')} value={e.paddingH ?? 0} onSet={(v) => onChange(e.id, { paddingH: Math.max(0, v) })} />
            <NumField label={t('props.paddingV')} value={e.paddingV ?? 0} onSet={(v) => onChange(e.id, { paddingV: Math.max(0, v) })} />
            <NumField label={t('props.border')} value={e.border ?? 0} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
          </div>
        </>
      )}

      {e.type === 'Text' && (
        <div className="group">
          <div className="group-title">{t('props.textTitle')}</div>
          <SelectField
            label={t('props.font')}
            value={e.fontFamily || 'Microsoft YaHei'}
            options={[
              [t('props.fontYaHei'), 'Microsoft YaHei'],
              [t('props.fontSimSun'), 'SimSun'],
              [t('props.fontSimHei'), 'SimHei'],
              [t('props.fontKaiTi'), 'KaiTi'],
              ['Arial', 'Arial'],
              ['Consolas', 'Consolas'],
            ]}
            onSet={(v) => onChange(e.id, { fontFamily: v })}
          />
          <NumField
            label={t('props.fontHeight')}
            value={e.fontH}
            onSet={(v) => {
              const fontH = Math.max(1, v)
              onChange(e.id, fontH > e.h ? { fontH, h: fontH } : { fontH })
            }}
          />
          <CheckField label={t('props.wrap')} value={e.wrap === true} onSet={(v) => onChange(e.id, { wrap: v })} />
          <CheckField label={t('props.bold')} value={e.bold === true} onSet={(v) => onChange(e.id, { bold: v })} />
          <NumField label={t('props.lineHeight')} value={e.lineHeight ?? 1.2} onSet={(v) => onChange(e.id, { lineHeight: Math.max(1, v) })} />
          <SelectField
            label={t('props.alignH')}
            value={e.align}
            options={[
              [t('props.alignOptionLeft'), 'Left'],
              [t('props.alignOptionCenter'), 'Center'],
              [t('props.alignOptionRight'), 'Right'],
            ]}
            onSet={(v) => onChange(e.id, { align: v as 'Left' | 'Center' | 'Right' })}
          />
          <SelectField
            label={t('props.alignV')}
            value={e.valign ?? 'middle'}
            options={[
              [t('props.valignOptionTop'), 'top'],
              [t('props.valignOptionMiddle'), 'middle'],
              [t('props.valignOptionBottom'), 'bottom'],
            ]}
            onSet={(v) => onChange(e.id, { valign: v as 'top' | 'middle' | 'bottom' })}
          />
          <SelectField
            label={t('props.fitMode')}
            value={e.fitMode ?? 'shrink'}
            options={[
              [t('props.fitOptionShrink'), 'shrink'],
              [t('props.fitOptionOverflow'), 'overflow'],
            ]}
            onSet={(v) => onChange(e.id, { fitMode: v as 'shrink' | 'overflow' })}
          />
          <div className="hint">{t('props.fitHint')}</div>
        </div>
      )}

      {e.type === 'Barcode' && (
        <div className="group">
          <div className="group-title">{t('props.barcodeTitle')}</div>
          <SelectField
            label={t('props.barcodeFormat')}
            value={e.barcodeFormat || 'CODE128'}
            options={[
              ['Code128', 'CODE128'],
              ['EAN13', 'EAN13'],
              ['CODE39', 'CODE39'],
              ['UPC', 'UPC'],
            ]}
            onSet={(v) => onChange(e.id, { barcodeFormat: v })}
          />
          <CheckField label={t('props.displayValue')} value={e.displayValue !== false} onSet={(v) => onChange(e.id, { displayValue: v })} />
          <NumField label={t('props.moduleWidth')} value={e.moduleWidth ?? 1} onSet={(v) => onChange(e.id, { moduleWidth: Math.max(0.5, v) })} />
        </div>
      )}

      {e.type === 'QrCode' && (
        <div className="group">
          <div className="group-title">{t('props.qrTitle')}</div>
          <SelectField
            label={t('props.qrEcc')}
            value={e.qrEcc ?? 'M'}
            options={[
              [t('props.qrEccL'), 'L'],
              [t('props.qrEccM'), 'M'],
              [t('props.qrEccQ'), 'Q'],
              [t('props.qrEccH'), 'H'],
            ]}
            onSet={(v) => onChange(e.id, { qrEcc: v as 'L' | 'M' | 'Q' | 'H' })}
          />
          <NumField label={t('props.qrMargin')} value={e.qrMargin ?? 2} onSet={(v) => onChange(e.id, { qrMargin: Math.max(0, v) })} />
        </div>
      )}

      {e.type === 'Rect' && (
        <div className="group">
          <div className="group-title">{t('props.rectTitle')}</div>
          <NumField label={t('props.border')} value={e.border ?? 0} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
        </div>
      )}

      {e.type === 'Line' && (
        <div className="group">
          <div className="group-title">{t('props.lineTitle')}</div>
          <NumField label={t('props.lengthX')} value={e.w} onSet={(v) => onChange(e.id, { w: v })} />
          <NumField label={t('props.lengthY')} value={e.h} onSet={(v) => onChange(e.id, { h: v })} />
          <NumField label={t('props.thickness')} value={e.thickness ?? 0.5} onSet={(v) => onChange(e.id, { thickness: Math.max(0.1, v) })} />
        </div>
      )}

      {(e.type === 'Image' || e.type === 'Region') && (
        <div className="group">
          <div className="group-title">{t('props.compatTitle', { type: e.type === 'Region' ? t('type.region') : t('type.image') })}</div>
          {e.type === 'Region' && <div className="hint">{t('props.regionId', { id: e.containerId })}</div>}
          {e.type === 'Image' && <div className="hint">{t('props.imageHint')}</div>}
          <NumField label={t('props.border')} value={e.border ?? 0} onSet={(v) => onChange(e.id, { border: Math.max(0, v) })} />
        </div>
      )}

      <button className="btn danger" onClick={() => onDelete([e.id])}>
        <Icon name="trash" size={13} />
        {t('props.deleteElement')}
      </button>
    </div>
  )
}

/** 填充：固定值 / 字段填充（字段名 + 显示名 + 预览值）。 */
function ContentGroup({ e, onChange }: { e: DesignElement; onChange: (id: string, patch: Partial<DesignElement>) => void }) {
  const { t } = useTranslation('designer')
  if (e.type !== 'Text' && e.type !== 'Barcode' && e.type !== 'QrCode') return null
  const set = (patch: Partial<typeof e>) => onChange(e.id, patch)
  return (
    <div className="group">
      <div className="group-title">{t('props.fillTitle')}</div>
      <SelectField
        label={t('props.source')}
        value={e.mode}
        options={[
          [t('props.sourceLiteral'), 'literal'],
          [t('props.sourceField'), 'field'],
        ]}
        onSet={(v) => set(v === 'literal' ? { mode: 'literal', key: '', displayName: undefined } : { mode: 'field' })}
      />
      {e.mode === 'literal' ? (
        <label className="field">
          {t('props.literalLabel')}
          <input className="input" value={e.text} onChange={(ev) => set({ text: ev.target.value })} placeholder={t('props.literalPlaceholder')} />
        </label>
      ) : (
        <>
          <label className="field">
            {t('props.fieldKeyLabel')}
            <input className="input mono" value={e.key} onChange={(ev) => set({ key: ev.target.value })} placeholder={t('props.fieldKeyPlaceholder')} />
          </label>
          <label className="field">
            {t('props.displayNameLabel')}
            <input className="input" value={e.displayName ?? ''} onChange={(ev) => set({ displayName: ev.target.value })} placeholder={t('props.displayNamePlaceholder')} />
          </label>
          <label className="field">
            {t('props.previewValueLabel')}
            <input className="input" value={e.text} onChange={(ev) => set({ text: ev.target.value })} placeholder={t('props.previewValuePlaceholder')} />
          </label>
          <div className="hint">{t('props.fillHint')}</div>
        </>
      )}
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
