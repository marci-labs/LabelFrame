import { describe, expect, it } from 'vitest'
import { fromBackendElements, toBackendElement, toContract, toLayout } from './convert'
import type { BackendLayout } from './convert'
import { defaultElement } from './types'
import type { BarcodeElement, DesignElement, LineElement, QrCodeElement, RectElement, RegionElement, TextElement } from './types'

/** 取转换结果并按具体类型收窄（fromBackendElements 返回联合类型）。 */
function first<T extends DesignElement>(list: readonly DesignElement[]): T {
  return list[0] as T
}

describe('convert 设计器模型 ↔ 后端模板契约', () => {
  describe('toBackendElement 写方向（镜像 LabelElementJsonConverter）', () => {
    it('文本：sourceKey 总是写；literal 仅固定值非空写；fontName 固定 ZPL 标识 "0"；textAlign 非 Left 写', () => {
      const t = defaultElement('Text')
      t.x = 10
      t.y = 5.5
      t.w = 40
      t.h = 8
      t.fontH = 4
      t.fontW = 4
      t.mode = 'literal'
      t.text = '库位 A'
      t.paddingH = 1
      t.paddingV = 0.5
      t.border = 0.2
      t.align = 'Center'
      const j = toBackendElement(t)
      expect(j.type).toBe('text')
      expect(j.xMm).toBe(10)
      expect(j.yMm).toBe(5.5)
      expect(j.sourceKey).toBe('')
      expect(j.literal).toBe('库位 A')
      expect(j.fontName).toBe('0')
      expect(j.fontHeightMm).toBe(4)
      expect(j.widthMm).toBe(40)
      expect(j.textAlign).toBe('Center')
      expect(j.paddingMm).toBe(1) // max(paddingH, paddingV)
      expect(j.borderMm).toBe(0.2)
    })

    it('文本字段填充：literal 不写，sourceKey 写键名', () => {
      const t = defaultElement('Text')
      t.mode = 'field'
      t.key = 'location'
      t.text = 'A-01'
      const j = toBackendElement(t)
      expect(j.sourceKey).toBe('location')
      expect(j.literal).toBeUndefined()
    })

    it('条码：heightMm 与 int 化 moduleWidth', () => {
      const b = defaultElement('Barcode')
      b.h = 15
      b.moduleWidth = 1.6
      const j = toBackendElement(b)
      expect(j.type).toBe('barcode')
      expect(j.heightMm).toBe(15)
      expect(j.moduleWidth).toBe(2)
    })

    it('二维码：sizeMm = 边长', () => {
      const q = defaultElement('QrCode')
      q.w = 22
      q.h = 20
      const j = toBackendElement(q)
      expect(j.type).toBe('qrcode')
      expect(j.sizeMm).toBe(22)
    })

    it('矩形 → region（后端无矩形类型）', () => {
      const r = defaultElement('Rect')
      const j = toBackendElement(r)
      expect(j.type).toBe('region')
      expect(j.id).toBeDefined()
      expect(j.widthMm).toBe(40)
      expect(j.heightMm).toBe(20)
    })

    it('线：x2/y2 为绝对坐标（端点 = 起点 + 长度）', () => {
      const l = defaultElement('Line')
      l.x = 3
      l.y = 4
      l.w = 60
      l.h = 0
      l.thickness = 0.5
      const j = toBackendElement(l)
      expect(j.type).toBe('line')
      expect(j.x2Mm).toBe(63)
      expect(j.y2Mm).toBe(4)
      expect(j.thicknessMm).toBe(0.5)
    })

    it('容器：id 与宽高', () => {
      const rg = defaultElement('Region')
      const j = toBackendElement(rg)
      expect(j.type).toBe('region')
      expect(j.id).toBe(rg.containerId)
      expect(j.widthMm).toBe(60)
    })

    it('padding/border 为 0 时省略（与后端省略规则一致）', () => {
      const t = defaultElement('Text')
      t.paddingH = 0
      t.paddingV = 0
      t.border = 0
      const j = toBackendElement(t)
      expect(j.paddingMm).toBeUndefined()
      expect(j.borderMm).toBeUndefined()
    })
  })

  describe('fromBackendElements 读方向', () => {
    it('文本：literal 非空 → 固定值；sourceKey 非空 → 字段填充', () => {
      const lit = first<TextElement>(fromBackendElements([
        { type: 'text', xMm: 1, yMm: 2, sourceKey: '', literal: '标题', fontHeightMm: 5, widthMm: 30, borderMm: 0.2 },
      ]))
      expect(lit.type).toBe('Text')
      expect(lit.mode).toBe('literal')
      expect(lit.text).toBe('标题')
      expect(lit.border).toBe(0.2)
      expect(lit.w).toBe(30)

      const f = first<TextElement>(fromBackendElements([{ type: 'text', xMm: 0, yMm: 0, sourceKey: 'location', fontHeightMm: 4 }]))
      expect(f.mode).toBe('field')
      expect(f.key).toBe('location')
    })

    it('文本高度回退：字高 + 2×内边距，下限 10mm（后端无高度字段）', () => {
      const t = first<TextElement>(fromBackendElements([{ type: 'text', xMm: 0, yMm: 0, sourceKey: '', fontHeightMm: 6, paddingMm: 1 }]))
      expect(t.h).toBe(10)
    })

    it('region 判别：被锚定 → 容器；独立 → 矩形', () => {
      const [container, , rect] = fromBackendElements([
        { type: 'region', xMm: 0, yMm: 0, id: 'c1', widthMm: 50, heightMm: 30 },
        { type: 'text', xMm: 5, yMm: 5, sourceKey: 'k', regionId: 'c1' },
        { type: 'region', xMm: 0, yMm: 40, id: 'r2', widthMm: 20, heightMm: 10 },
      ])
      expect(container.type).toBe('Region')
      expect((container as RegionElement).containerId).toBe('c1')
      expect(rect.type).toBe('Rect')

    })

    it('线读回为长度', () => {
      const l = first<LineElement>(fromBackendElements([{ type: 'line', xMm: 3, yMm: 4, x2Mm: 63, y2Mm: 4, thicknessMm: 0.5 }]))
      expect(l.type).toBe('Line')
      expect(l.w).toBe(60)
      expect(l.h).toBe(0)
      expect(l.thickness).toBe(0.5)
    })

    it('未知 / 缺省字段容错', () => {
      const t = first<TextElement>(fromBackendElements([{ type: 'text', xMm: 0, yMm: 0, sourceKey: '' }]))
      expect(t.type).toBe('Text')
      expect(t.fontH).toBe(5)
      expect(t.w).toBe(40)
      expect(t.h).toBeGreaterThanOrEqual(10)
    })
  })

  describe('toContract / toLayout 模板顶层', () => {
    it('契约字段 = 推导结果（displayName 取 Key，非必填，类型 Text）', () => {
      const c = toContract('库位标签', '1', ['location', 'sku'])
      expect(c.name).toBe('库位标签')
      expect(c.version).toBe('1')
      expect(c.fields).toEqual([
        { key: 'location', displayName: 'location', isRequired: false, type: 'Text' },
        { key: 'sku', displayName: 'sku', isRequired: false, type: 'Text' },
      ])
    })

    it('版式包含纸张与元素数组', () => {
      const t = defaultElement('Text')
      const l: BackendLayout = toLayout('库位标签', '库位标签', '1', 100, 60, [t])
      expect(l.widthMm).toBe(100)
      expect(l.heightMm).toBe(60)
      expect(l.elements).toHaveLength(1)
      expect(l.elements[0].type).toBe('text')
    })
  })

  describe('往返一致性（保存 → 读取 → 关键字段不丢）', () => {
    it('文本往返：位置 / 填充 / 边框 / 对齐', () => {
      const t = defaultElement('Text')
      t.x = 12.5
      t.y = 7
      t.w = 45
      t.h = 10
      t.fontH = 5
      t.mode = 'field'
      t.key = 'location'
      t.text = 'A-01'
      t.align = 'Right'
      t.border = 0.3
      t.paddingH = 1.5
      const back = first<TextElement>(fromBackendElements([toBackendElement(t)]))
      expect(back.type).toBe('Text')
      expect(back.x).toBe(12.5)
      expect(back.y).toBe(7)
      expect(back.mode).toBe('field')
      expect(back.key).toBe('location')
      expect(back.align).toBe('Right')
      expect(back.border).toBe(0.3)
      expect(back.w).toBe(45)
    })

    it('条码 / 二维码 / 容器 / 线往返', () => {
      const b = defaultElement('Barcode')
      b.h = 18
      b.mode = 'literal'
      b.text = 'SKU-001'
      const b2 = first<BarcodeElement>(fromBackendElements([toBackendElement(b)]))
      expect(b2.type).toBe('Barcode')
      expect(b2.h).toBe(18)
      expect(b2.mode).toBe('literal')
      expect(b2.text).toBe('SKU-001')

      const q = defaultElement('QrCode')
      q.w = 16
      q.h = 16
      const q2 = first<QrCodeElement>(fromBackendElements([toBackendElement(q)]))
      expect(q2.type).toBe('QrCode')
      expect(q2.w).toBe(16)
      expect(q2.h).toBe(16)

      const rg = defaultElement('Region')
      rg.x = 10
      rg.w = 55
      const rg2 = first<RectElement>(fromBackendElements([toBackendElement(rg)]))
      expect(rg2.type).toBe('Rect') // 无锚定 → 读回为矩形
      expect(rg2.w).toBe(55)
    })
  })
})
