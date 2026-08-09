import { describe, expect, it } from 'vitest'
import { computeSnap } from './snapping'

// 画布：500x340；内容区起点 (60,60)，尺寸 400x240（纸 100x60，1mm=4px）
const CANVAS = { w: 500, h: 340 }
const CONTENT = { x: 60, y: 60, w: 400, h: 240 }

describe('snapping 智能参考线吸附', () => {
  it('无目标时网格吸附兜底（贴最近 1mm=4px 网格）', () => {
    const r = computeSnap({ x: 61, y: 62, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [])
    expect(r.dx).toBe(-1) // 61 → 60
    expect(r.dy).toBe(-2) // 62 → 60
  })

  it('吸附到内容区边缘', () => {
    const r = computeSnap({ x: 56, y: 56, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [])
    expect(r.dx).toBe(4) // 左缘 56 → 60
    expect(r.guideX).toBe(60)
    expect(r.dy).toBe(4)
    expect(r.guideY).toBe(60)
  })

  it('吸附到其它元素边缘', () => {
    const other = { x: 100, y: 100, w: 200, h: 30 } // 左缘 100（中心 200 / 右缘 300 远离）
    const r = computeSnap({ x: 106, y: 60, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [other])
    // 元素左缘 106 与其它元素左缘 100 差 6（阈值内）→ dx = -6
    expect(r.dx).toBe(-6)
    expect(r.guideX).toBe(100)
  })

  it('多目标取最近（中心线比边缘更近时吸附中心）', () => {
    const other = { x: 100, y: 100, w: 50, h: 30 } // 中心 125
    const r = computeSnap({ x: 106, y: 60, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [other])
    // 元素中心 126 与其它元素中心 125 差 1，比左缘差 6 更近
    expect(r.dx).toBe(-1)
    expect(r.guideX).toBe(125)
  })

  it('无其它目标时网格兜底对齐（贴最近 1mm 网格并显示参考线）', () => {
    const r = computeSnap({ x: 81, y: 82, w: 40, h: 20 }, CANVAS.w, CANVAS.h, CONTENT.x, CONTENT.y, CONTENT.w, CONTENT.h, [])
    expect(r.dx).toBe(-1) // 81 → 80（round(20.25)=20）
    expect(r.dy).toBe(2) // 82 → 84（round(20.5)=21）
    expect(r.guideX).toBe(80)
    expect(r.guideY).toBe(84)
  })
})
