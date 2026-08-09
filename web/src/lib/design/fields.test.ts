import { describe, expect, it } from 'vitest'
import { deriveFields } from './fields'
import { defaultElement } from './types'

describe('fields 契约字段自动推导', () => {
  it('空元素 → 空字段', () => {
    expect(deriveFields([])).toEqual([])
  })

  it('字段填充按元素顺序去重（图片 / 线 / 容器不算字段）', () => {
    const text1 = defaultElement('Text')
    text1.mode = 'field'
    text1.key = 'location'
    const text2 = defaultElement('Text')
    text2.mode = 'field'
    text2.key = 'sku'
    const dup = defaultElement('Text')
    dup.mode = 'field'
    dup.key = 'location'
    const lit = defaultElement('Text')
    lit.mode = 'literal'
    const image = defaultElement('Image')
    image.key = 'photo'
    const line = defaultElement('Line')
    const region = defaultElement('Region')

    expect(deriveFields([text1, text2, dup, lit, image, line, region])).toEqual(['location', 'sku'])
  })

  it('固定值元素不产生字段；未绑定 key 不计', () => {
    const e = defaultElement('Text')
    e.mode = 'field'
    e.key = ''
    const b = defaultElement('Barcode')
    b.mode = 'literal'
    expect(deriveFields([e, b])).toEqual([])
  })

  it('条码 / 二维码字段填充计入字段', () => {
    const bar = defaultElement('Barcode')
    bar.mode = 'field'
    bar.key = 'sku'
    const qr = defaultElement('QrCode')
    qr.mode = 'field'
    qr.key = 'url'
    expect(deriveFields([bar, qr])).toEqual(['sku', 'url'])
  })
})
