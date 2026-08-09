// 几何换算与常量（与原型一致）

/** 1mm = 4px（设计逻辑像素）。 */
export const PX = 4
/** 画布四周留白（mm）。 */
export const PAD_MM = 10
/** 标尺区宽度（逻辑 px，画进 Konva 与内容同坐标系）。 */
export const RULER = 20

/** px → mm。 */
export const mm = (px: number): number => px / PX
/** mm → px。 */
export const pxv = (v: number): number => v * PX
/** 保留两位小数。 */
export const r2 = (v: number): number => Math.round((Number(v) || 0) * 100) / 100
/** 数值夹取。 */
export const clamp = (v: number, min: number, max: number): number => Math.min(max, Math.max(min, v))

/** 打印 DPI 对应的每毫米点数（203dpi → 8，300dpi → 12）。 */
export function dotsPerMm(dpi: number): number {
  return Math.round(dpi / 25.4)
}

/** DPI 打印预览相对设计点（4px/mm）的放大倍数。 */
export function previewScale(dpi: number): number {
  return dotsPerMm(dpi) / PX
}

/** 设计态画布总尺寸（含标尺区 + 留白）。 */
export function designCanvasSize(paperW: number, paperH: number): { w: number; h: number } {
  return { w: RULER + (paperW + PAD_MM * 2) * PX, h: RULER + (paperH + PAD_MM * 2) * PX }
}

/** 预览态画布总尺寸（仅标签宽高范围）。 */
export function previewCanvasSize(paperW: number, paperH: number): { w: number; h: number } {
  return { w: paperW * PX, h: paperH * PX }
}

/** 内容区偏移：设计态 = 标尺 + 留白；预览态 = 0。 */
export function contentOffset(preview: boolean): { x: number; y: number } {
  return preview ? { x: 0, y: 0 } : { x: RULER + PAD_MM * PX, y: RULER + PAD_MM * PX }
}

/** 逻辑坐标（相对 stage 内容盒）→ 标签内容坐标（mm）。 */
export function logicToContentMm(x: number, y: number, preview: boolean): { x: number; y: number } {
  const off = contentOffset(preview)
  return { x: mm(x - off.x), y: mm(y - off.y) }
}

/** 适应窗口缩放：画布略小于视口。 */
export function fitScale(viewportW: number, viewportH: number, canvasW: number, canvasH: number): number {
  return Math.max(0.05, Math.min((viewportW - 32) / canvasW, (viewportH - 32) / canvasH))
}
