// 智能参考线吸附（纯函数）：候选线 = 画布边缘/中心 + 内容区边缘/中心 + 其它元素边缘/中心；
// 无目标时网格吸附兜底（贴最近 1mm 网格）。与原型 snapNode 逻辑一致。

export interface SnapRect {
  x: number
  y: number
  w: number
  h: number
}

export interface SnapResult {
  /** 需要施加的位移（逻辑 px）。 */
  dx: number
  dy: number
  /** 吸附到的参考线位置（逻辑 px），null = 未吸附。 */
  guideX: number | null
  guideY: number | null
}

/** 吸附阈值（逻辑 px）。 */
export const SNAP_THRESHOLD = 8

/**
 * 计算吸附结果。
 * @param rect 被拖动元素的包围盒（逻辑 px，相对 layer）
 * @param canvasW / canvasH 画布总尺寸（逻辑 px）
 * @param contentX / contentY 内容区偏移（逻辑 px）
 * @param contentW / contentH 标签内容区尺寸（逻辑 px）
 * @param others 其它元素包围盒（逻辑 px）
 */
export function computeSnap(
  rect: SnapRect,
  canvasW: number,
  canvasH: number,
  contentX: number,
  contentY: number,
  contentW: number,
  contentH: number,
  others: readonly SnapRect[],
): SnapResult {
  const xs = [rect.x, rect.x + rect.w / 2, rect.x + rect.w]
  const ys = [rect.y, rect.y + rect.h / 2, rect.y + rect.h]
  const cx = [0, canvasW / 2, canvasW, contentX, contentX + contentW / 2, contentX + contentW]
  const cy = [0, canvasH / 2, canvasH, contentY, contentY + contentH / 2, contentY + contentH]
  for (const o of others) {
    cx.push(o.x, o.x + o.w / 2, o.x + o.w)
    cy.push(o.y, o.y + o.h / 2, o.y + o.h)
  }

  let bestDx: { d: number; c: number } | null = null
  let bestDy: { d: number; c: number } | null = null
  for (const x of xs) {
    for (const c of cx) {
      const d = c - x
      if (Math.abs(d) <= SNAP_THRESHOLD && (bestDx === null || Math.abs(d) < Math.abs(bestDx.d))) bestDx = { d, c }
    }
  }
  for (const y of ys) {
    for (const c of cy) {
      const d = c - y
      if (Math.abs(d) <= SNAP_THRESHOLD && (bestDy === null || Math.abs(d) < Math.abs(bestDy.d))) bestDy = { d, c }
    }
  }

  // 网格吸附兜底：左上角贴最近 1mm 网格（4px）
  if (bestDx === null) {
    const gridX = Math.round(rect.x / 4) * 4
    const d = gridX - rect.x
    if (Math.abs(d) <= SNAP_THRESHOLD) bestDx = { d, c: gridX }
  }
  if (bestDy === null) {
    const gridY = Math.round(rect.y / 4) * 4
    const d = gridY - rect.y
    if (Math.abs(d) <= SNAP_THRESHOLD) bestDy = { d, c: gridY }
  }

  return {
    dx: bestDx ? bestDx.d : 0,
    dy: bestDy ? bestDy.d : 0,
    guideX: bestDx ? bestDx.c : null,
    guideY: bestDy ? bestDy.c : null,
  }
}
