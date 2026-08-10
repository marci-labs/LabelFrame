// 设计器内部元素模型（labelframe-web-design 格式，与原型一致）

export type ElementType = 'Text' | 'Barcode' | 'QrCode' | 'Rect' | 'Image' | 'Line' | 'Region'

export type ContentMode = 'literal' | 'field'

interface ElementBase {
  id: string
  type: ElementType
  /** 左上角 X（mm，相对标签内容区） */
  x: number
  /** 左上角 Y（mm，相对标签内容区） */
  y: number
  /** 宽（mm） */
  w: number
  /** 高（mm） */
  h: number
  /** 边框线宽（mm，0 = 无边框） */
  border: number
  /** 锚定的容器 id（拖入容器自动建立） */
  regionId?: string
  /** 区域内对齐（旧模板保留，前端不做编辑 UI） */
  regionHAlign?: string
  regionVAlign?: string
}

export interface TextElement extends ElementBase {
  type: 'Text'
  fontH: number
  fontW: number
  fontFamily: string
  /** 加粗（迭代 14：小字号打印不清晰，加粗提高可读性；默认 false 不写契约字段） */
  bold: boolean
  /** 自动换行 */
  wrap: boolean
  lineHeight: number
  /** 垂直对齐：顶端 / 居中 / 底部 */
  valign: 'top' | 'middle' | 'bottom'
  mode: ContentMode
  /** 字段填充的键名称 */
  key: string
  /** 固定值内容 / 字段填充的预览值（仅画布显示） */
  text: string
  /** 水平对齐 */
  align: 'Left' | 'Center' | 'Right'
  paddingH: number
  paddingV: number
  /** 单行溢出：缩小适应 / 隐藏 */
  fitMode: 'shrink' | 'overflow'
}

export interface BarcodeElement extends ElementBase {
  type: 'Barcode'
  mode: ContentMode
  key: string
  text: string
  paddingH: number
  paddingV: number
  barcodeFormat: string
  /** 底部显示文字 */
  displayValue: boolean
  moduleWidth: number
}

export interface QrCodeElement extends ElementBase {
  type: 'QrCode'
  mode: ContentMode
  key: string
  text: string
  paddingH: number
  paddingV: number
  qrEcc: 'L' | 'M' | 'Q' | 'H'
  qrMargin: number
}

export interface RectElement extends ElementBase {
  type: 'Rect'
}

export interface ImageElement extends ElementBase {
  type: 'Image'
  key: string
}

export interface LineElement extends ElementBase {
  type: 'Line'
  thickness: number
}

export interface RegionElement extends ElementBase {
  type: 'Region'
  /** 容器标识（保存为后端 region.id） */
  containerId: string
}

export type DesignElement =
  | TextElement
  | BarcodeElement
  | QrCodeElement
  | RectElement
  | ImageElement
  | LineElement
  | RegionElement

let idCounter = 0

/** 生成唯一元素 id（与原型一致：随机短串）。 */
export function uid(): string {
  idCounter += 1
  return 'e' + Date.now().toString(36).slice(-6) + idCounter.toString(36)
}

/** 新建元素的默认值（与原型 defaultElement 一致）。 */
export function defaultElement(type: 'Text', id?: string): TextElement
export function defaultElement(type: 'Barcode', id?: string): BarcodeElement
export function defaultElement(type: 'QrCode', id?: string): QrCodeElement
export function defaultElement(type: 'Rect', id?: string): RectElement
export function defaultElement(type: 'Image', id?: string): ImageElement
export function defaultElement(type: 'Line', id?: string): LineElement
export function defaultElement(type: 'Region', id?: string): RegionElement
/** 控件栏入口（文本 / 条码 / 二维码 / 矩形）。 */
export function defaultElement(type: 'Text' | 'Barcode' | 'QrCode' | 'Rect', id?: string): TextElement | BarcodeElement | QrCodeElement | RectElement
export function defaultElement(type: ElementType, id = uid()): DesignElement {
  const base = { id, x: 5, y: 5, w: 40, h: 10, border: 0 }
  switch (type) {
    case 'Text':
      return { ...base, type, fontH: 5, fontW: 5, fontFamily: 'Microsoft YaHei', bold: false, wrap: false, lineHeight: 1.2, valign: 'middle', mode: 'literal', key: '', text: '文本', align: 'Left', paddingH: 1, paddingV: 1, fitMode: 'shrink' }
    case 'Barcode':
      return { ...base, y: 20, w: 50, h: 20, type, mode: 'literal', key: '', text: 'ABC-123', paddingH: 1, paddingV: 1, barcodeFormat: 'CODE128', displayValue: true, moduleWidth: 1 }
    case 'QrCode':
      return { ...base, y: 20, w: 20, h: 20, type, mode: 'literal', key: '', text: 'ABC-123', paddingH: 1, paddingV: 1, qrEcc: 'M', qrMargin: 2 }
    case 'Rect':
      return { ...base, h: 20, type, border: 0.3 }
    case 'Image':
      return { ...base, y: 20, w: 20, h: 20, type, key: '' }
    case 'Line':
      return { ...base, y: 5, w: 60, h: 0, type, thickness: 0.5 }
    case 'Region':
      return { ...base, y: 5, w: 60, h: 30, type, border: 0.3, containerId: 'c' + Math.random().toString(36).slice(2, 8) }
  }
}

/** 元素中文名。 */
export function typeLabel(e: DesignElement): string {
  switch (e.type) {
    case 'Text': return '文本'
    case 'Barcode': return '条码'
    case 'QrCode': return '二维码'
    case 'Rect': return '矩形'
    case 'Image': return '图片'
    case 'Line': return '线'
    case 'Region': return '容器'
  }
}

/** 图层显示名称：固定值显示内容；字段填充显示「(键名) 预览值」；条码 / 二维码带类型前缀。 */
export function layerLabel(e: DesignElement): string {
  switch (e.type) {
    case 'Text':
      if (e.mode === 'literal') return e.text || '文本'
      return '(' + (e.key || '未绑定') + ') ' + (e.text || '')
    case 'Barcode':
    case 'QrCode': {
      const t = e.type === 'Barcode' ? '条码' : '二维码'
      if (e.mode === 'literal') return '(' + t + ') ' + (e.text || '固定值')
      return '(' + t + ') (' + (e.key || '未绑定') + ') ' + (e.text || '')
    }
    case 'Rect': return '矩形'
    case 'Image': return '图片' + (e.key ? ' (' + e.key + ')' : '')
    case 'Line': return '线'
    case 'Region': return '容器'
  }
}

/** 画布显示内容：固定值原样；字段填充取预览值（仅画布显示，打印以外界数据为准）。 */
export function elementContent(e: DesignElement): string {
  if (e.type === 'Image' || e.type === 'Line' || e.type === 'Rect' || e.type === 'Region') return ''
  if (e.mode === 'literal') return e.text || '（固定值）'
  if (e.text) return e.text
  if (!e.key) return '（未绑定字段）'
  return e.key
}

/** 是否可填充内容（文本 / 条码 / 二维码）。 */
export function supportsContent(e: DesignElement): boolean {
  return e.type === 'Text' || e.type === 'Barcode' || e.type === 'QrCode'
}

/** 复制元素（生成新 id）。 */
export function cloneElement<T extends DesignElement>(e: T, newId = uid()): T {
  const copy = JSON.parse(JSON.stringify(e)) as T
  copy.id = newId
  return copy
}
