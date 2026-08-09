import { describe, expect, it } from 'vitest'
import { clamp, designCanvasSize, dotsPerMm, fitScale, logicToContentMm, mm, previewScale, pxv, r2 } from './geometry'

describe('geometry 毫米↔像素换算', () => {
  it('mm/px 互换算', () => {
    expect(mm(40)).toBe(10)
    expect(pxv(10)).toBe(40)
    expect(mm(pxv(7.5))).toBe(7.5)
  })

  it('r2 保留两位小数', () => {
    expect(r2(3.14159)).toBe(3.14)
    expect(r2(0.001)).toBe(0)
  })

  it('clamp 夹取', () => {
    expect(clamp(5, 0, 10)).toBe(5)
    expect(clamp(-2, 0, 10)).toBe(0)
    expect(clamp(12, 0, 10)).toBe(10)
  })

  it('DPI → 每毫米点数（203 → 8，300 → 12）', () => {
    expect(dotsPerMm(203)).toBe(8)
    expect(dotsPerMm(300)).toBe(12)
    expect(previewScale(203)).toBe(2) // 8 点 / 4px每mm
    expect(previewScale(300)).toBe(3)
  })

  it('画布尺寸：设计态含标尺 + 留白，预览态仅标签', () => {
    const d = designCanvasSize(100, 60)
    expect(d.w).toBe(20 + (100 + 20) * 4)
    expect(d.h).toBe(20 + (60 + 20) * 4)
    const p = designCanvasSize(100, 60) // 占位防未用
    expect(p.w).toBe(500)
  })

  it('逻辑坐标 → 内容 mm（设计态扣除标尺与留白）', () => {
    const pos = logicToContentMm(20 + 10 * 4 + 20, 20 + 10 * 4 + 10, false)
    expect(pos.x).toBe(5)
    expect(pos.y).toBe(2.5)
  })

  it('预览态偏移为 0', () => {
    const pos = logicToContentMm(20, 10, true)
    expect(pos.x).toBe(5)
    expect(pos.y).toBe(2.5)
  })

  it('fitScale 适应窗口（略小于视口，取宽高较小约束）', () => {
    expect(fitScale(500, 400, 500, 400)).toBeCloseTo(0.92) // min((500-32)/500, (400-32)/400)
    expect(fitScale(100, 100, 200, 200)).toBeCloseTo(0.34)
  })
})
