import { describe, expect, it } from 'vitest'
import { deriveFieldInfos, deriveFields } from './fields'
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

describe('deriveFieldInfos 显示名推导（迭代 83 · #131 决议 1）', () => {
  it('字段带显示名 → FieldInfo 携带显示名；未填 / 空白显示名 → 仅键（显示处回退键名）', () => {
    const withName = defaultElement('Text')
    withName.mode = 'field'
    withName.key = 'location'
    withName.displayName = '库位'
    const withoutName = defaultElement('Text')
    withoutName.mode = 'field'
    withoutName.key = 'sku'
    const blank = defaultElement('Text')
    blank.mode = 'field'
    blank.key = 'batch'
    blank.displayName = '   '
    expect(deriveFieldInfos([withName, withoutName, blank])).toEqual([
      { key: 'location', displayName: '库位' },
      { key: 'sku' },
      { key: 'batch' },
    ])
  })

  it('同键多元素去重：显示名取首个非空（后出现不覆盖）', () => {
    const first = defaultElement('Text')
    first.mode = 'field'
    first.key = 'location'
    first.displayName = '库位'
    const later = defaultElement('Barcode')
    later.mode = 'field'
    later.key = 'location'
    later.displayName = '货架位置'
    expect(deriveFieldInfos([first, later])).toEqual([{ key: 'location', displayName: '库位' }])

    // 首个未填、后出现有值 → 取后出现的非空值
    const noName = defaultElement('Text')
    noName.mode = 'field'
    noName.key = 'sku'
    const hasName = defaultElement('QrCode')
    hasName.mode = 'field'
    hasName.key = 'sku'
    hasName.displayName = '商品码'
    expect(deriveFieldInfos([noName, hasName])).toEqual([{ key: 'sku', displayName: '商品码' }])
  })
})
