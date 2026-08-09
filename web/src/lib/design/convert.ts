// 设计器内部模型 ↔ 后端模板契约（layout.elements）双向转换。
// 后端元素 JSON 由 LabelElementJsonConverter 判别 type（text/barcode/qrcode/image/line/region），
// 字段 camelCase；写方向严格镜像 converter 的省略规则（padding/border 仅 >0 时写等）。

import type { DesignElement, ElementType } from './types'
import { uid } from './types'
import { r2 } from './geometry'

/** 后端版式元素 JSON（layout.elements 项）。 */
export interface BackendElement {
  type: 'text' | 'barcode' | 'qrcode' | 'image' | 'line' | 'region'
  xMm: number
  yMm: number
  paddingMm?: number
  borderMm?: number
  regionId?: string
  regionHAlign?: string
  regionVAlign?: string
  sourceKey?: string
  literal?: string
  fontName?: string
  fontHeightMm?: number
  fontWidthMm?: number
  widthMm?: number
  textAlign?: string
  heightMm?: number
  moduleWidth?: number
  sizeMm?: number
  x2Mm?: number
  y2Mm?: number
  thicknessMm?: number
  id?: string
}

/** 契约字段 JSON。 */
export interface BackendField {
  key: string
  displayName: string
  isRequired: boolean
  type: 'Text' | 'Number'
  pattern?: string
}

/** 契约 JSON。 */
export interface BackendContract {
  name: string
  version: string
  fields: BackendField[]
}

/** 版式 JSON。 */
export interface BackendLayout {
  name: string
  contractName: string
  contractVersion: string
  widthMm: number
  heightMm: number
  elements: BackendElement[]
}

const backType = (t: ElementType): BackendElement['type'] =>
  t === 'Text' ? 'text' : t === 'Barcode' ? 'barcode' : t === 'QrCode' ? 'qrcode' : t === 'Rect' ? 'region' : t === 'Image' ? 'image' : t === 'Line' ? 'line' : 'region'

/** 内部元素 → 后端元素 JSON（与 LabelElementJsonConverter.Write 输出一致）。 */
export function toBackendElement(e: DesignElement): BackendElement {
  const base: BackendElement = { type: backType(e.type), xMm: r2(e.x), yMm: r2(e.y) }
  const padH = 'paddingH' in e ? (e.paddingH ?? 0) : 0
  const padV = 'paddingV' in e ? (e.paddingV ?? 0) : 0
  const pad = Math.max(padH, padV)
  if (pad > 0) base.paddingMm = r2(pad)
  if ((e.border ?? 0) > 0) base.borderMm = r2(e.border ?? 0)
  if (e.regionId) base.regionId = e.regionId
  if (e.regionHAlign) base.regionHAlign = e.regionHAlign
  if (e.regionVAlign) base.regionVAlign = e.regionVAlign

  switch (e.type) {
    case 'Text':
      base.sourceKey = e.key
      if (e.mode === 'literal' && e.text) base.literal = e.text
      base.fontName = '0' // ZPL 字体标识（前端字体仅影响画布预览，打印字体由宿主配置）
      base.fontHeightMm = r2(e.fontH)
      base.fontWidthMm = r2(e.fontW)
      if (e.w > 0) base.widthMm = r2(e.w)
      if (e.align !== 'Left') base.textAlign = e.align
      break
    case 'Barcode':
      base.sourceKey = e.key
      if (e.mode === 'literal' && e.text) base.literal = e.text
      base.heightMm = r2(e.h)
      base.moduleWidth = Math.max(1, Math.round(e.moduleWidth))
      break
    case 'QrCode':
      base.sourceKey = e.key
      if (e.mode === 'literal' && e.text) base.literal = e.text
      base.sizeMm = r2(Math.max(e.w, e.h))
      break
    case 'Image':
      base.sourceKey = e.key
      base.widthMm = r2(e.w)
      base.heightMm = r2(e.h)
      break
    case 'Line':
      base.x2Mm = r2(e.x + e.w)
      base.y2Mm = r2(e.y + e.h)
      base.thicknessMm = r2(e.thickness)
      break
    case 'Rect':
    case 'Region':
      base.id = e.type === 'Region' ? e.containerId : 'r' + e.id.slice(1)
      base.widthMm = r2(e.w)
      base.heightMm = r2(e.h)
      break
  }
  return base
}

/**
 * 后端元素 JSON 数组 → 内部元素。
 * region 判别：有其它元素锚定（regionId 指向）→ 容器；否则 → 矩形（用户画的镂空矩形）。
 */
export function fromBackendElements(list: readonly BackendElement[]): DesignElement[] {
  const regionRefs = new Set<string>()
  for (const j of list) if (j.regionId) regionRefs.add(j.regionId)
  return list.map((j) => {
    const base = {
      id: uid(),
      x: j.xMm ?? 0,
      y: j.yMm ?? 0,
      border: j.borderMm ?? 0,
      paddingH: j.paddingMm ?? 0,
      paddingV: j.paddingMm ?? 0,
      regionId: j.regionId,
      regionHAlign: j.regionHAlign,
      regionVAlign: j.regionVAlign,
    }
    switch (j.type) {
      case 'text': {
        const literal = j.literal ?? ''
        const key = j.sourceKey ?? ''
        const mode = literal ? 'literal' : key ? 'field' : 'literal'
        const fontH = j.fontHeightMm ?? 5
        const pad = j.paddingMm ?? 0
        return {
          ...base,
          type: 'Text',
          w: j.widthMm && j.widthMm > 0 ? j.widthMm : 40,
          h: Math.max(fontH + pad * 2, 10),
          fontH,
          fontW: j.fontWidthMm ?? fontH,
          fontFamily: 'Microsoft YaHei',
          wrap: false,
          lineHeight: 1.2,
          valign: 'middle',
          mode,
          key: mode === 'field' ? key : '',
          text: literal || '',
          align: (j.textAlign as 'Left' | 'Center' | 'Right') || 'Left',
          fitMode: 'shrink',
        }
      }
      case 'barcode': {
        const literal = j.literal ?? ''
        const key = j.sourceKey ?? ''
        const mode = literal ? 'literal' : key ? 'field' : 'literal'
        return {
          ...base,
          type: 'Barcode',
          w: 50,
          h: j.heightMm ?? 20,
          mode,
          key: mode === 'field' ? key : '',
          text: literal || '',
          barcodeFormat: 'CODE128',
          displayValue: true,
          moduleWidth: j.moduleWidth ?? 2,
        }
      }
      case 'qrcode': {
        const literal = j.literal ?? ''
        const key = j.sourceKey ?? ''
        const mode = literal ? 'literal' : key ? 'field' : 'literal'
        const size = j.sizeMm ?? 20
        return {
          ...base,
          type: 'QrCode',
          w: size,
          h: size,
          mode,
          key: mode === 'field' ? key : '',
          text: literal || '',
          qrEcc: 'M',
          qrMargin: 2,
        }
      }
      case 'image':
        return { ...base, type: 'Image', w: j.widthMm ?? 20, h: j.heightMm ?? 20, key: j.sourceKey ?? '' }
      case 'line':
        return {
          ...base,
          type: 'Line',
          w: Math.abs((j.x2Mm ?? j.xMm ?? 0) - (j.xMm ?? 0)),
          h: Math.abs((j.y2Mm ?? j.yMm ?? 0) - (j.yMm ?? 0)),
          thickness: j.thicknessMm ?? 0.5,
        }
      case 'region': {
        const isContainer = !!j.id && regionRefs.has(j.id)
        return isContainer
          ? { ...base, type: 'Region', w: j.widthMm ?? 60, h: j.heightMm ?? 30, containerId: j.id || 'c' + Math.random().toString(36).slice(2, 8) }
          : { ...base, type: 'Rect', w: j.widthMm ?? 40, h: j.heightMm ?? 20 }
      }
    }
  })
}

/** 字段推导结果 → 契约 fields（决策 #37：displayName 取 Key，非必填，类型 Text）。 */
export function toContractFields(keys: readonly string[]): BackendField[] {
  return keys.map((k) => ({ key: k, displayName: k, isRequired: false, type: 'Text' as const }))
}

/** 内部状态 → 契约（POST /api/templates 用）。 */
export function toContract(name: string, version: string, keys: readonly string[]): BackendContract {
  return { name, version, fields: toContractFields(keys) }
}

/** 内部状态 → 版式（POST /api/templates 用）。 */
export function toLayout(name: string, contractName: string, contractVersion: string, paperW: number, paperH: number, elements: readonly DesignElement[]): BackendLayout {
  return { name, contractName, contractVersion, widthMm: r2(paperW), heightMm: r2(paperH), elements: elements.map(toBackendElement) }
}
