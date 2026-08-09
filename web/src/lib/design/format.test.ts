import { describe, expect, it } from 'vitest'
import { exportDesign, parseDesign } from './format'
import { defaultElement } from './types'

describe('format labelframe-web-design 导入导出', () => {
  it('导出包含 format/version/paperW/paperH/elements', () => {
    const text = defaultElement('Text')
    const json = exportDesign(100, 60, [text])
    const data = JSON.parse(json)
    expect(data.format).toBe('labelframe-web-design')
    expect(data.version).toBe(1)
    expect(data.paperW).toBe(100)
    expect(data.paperH).toBe(60)
    expect(data.elements).toHaveLength(1)
  })

  it('导入往返：结构与导出一致，元素重新生成 id', () => {
    const a = defaultElement('Text')
    const b = defaultElement('Barcode')
    const json = exportDesign(80, 50, [a, b])
    const d = parseDesign(json)
    expect(d.paperW).toBe(80)
    expect(d.paperH).toBe(50)
    expect(d.elements).toHaveLength(2)
    expect(d.elements[0].id).not.toBe(a.id)
    expect(d.elements[0].type).toBe('Text')
    expect(d.elements[1].type).toBe('Barcode')
  })

  it('非法输入报错（非 JSON / 格式不符 / 元素结构不完整）', () => {
    expect(() => parseDesign('not json')).toThrow()
    expect(() => parseDesign(JSON.stringify({ format: 'other', elements: [] }))).toThrow()
    expect(() => parseDesign(JSON.stringify({ format: 'labelframe-web-design', elements: [{ type: 'Text' }] }))).toThrow()
  })

  it('缺纸张时回退默认 100×60', () => {
    const d = parseDesign(JSON.stringify({ format: 'labelframe-web-design', version: 1, elements: [] }))
    expect(d.paperW).toBe(100)
    expect(d.paperH).toBe(60)
  })
})
