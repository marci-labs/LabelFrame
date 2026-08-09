import { describe, expect, it } from 'vitest'
import { findDuplicateKeys, isMappingComplete, normalizeName, rowToData, suggestMapping } from './mapping'

describe('mapping Excel 列映射建议', () => {
  it('归一化：忽略大小写 / 空白 / 下划线', () => {
    expect(normalizeName(' 库位 编码 ')).toBe('库位编码')
    expect(normalizeName('SKU Code')).toBe('skucode')
    expect(normalizeName('物料_编码')).toBe('物料编码')
  })

  it('按列名自动匹配字段键', () => {
    const keys = ['location', 'sku', '数量']
    expect(suggestMapping(['Location', 'SKU', '数量', '备注'], keys)).toEqual(['location', 'sku', '数量', ''])
  })

  it('无匹配列返回空串（需手工映射）', () => {
    expect(suggestMapping(['备注'], ['location'])).toEqual([''])
  })

  it('isMappingComplete：至少一列映射即可提交（未映射列跳过）', () => {
    expect(isMappingComplete(['location', 'sku'])).toBe(true)
    expect(isMappingComplete(['location', ''])).toBe(true)
    expect(isMappingComplete([''])).toBe(false)
    expect(isMappingComplete([])).toBe(false)
  })

  it('findDuplicateKeys 检出重复映射', () => {
    expect(findDuplicateKeys(['location', 'location', ''])).toEqual(['location'])
    expect(findDuplicateKeys(['location', 'sku'])).toEqual([])
  })

  it('rowToData 按映射拼数据，未映射列忽略', () => {
    const data = rowToData(['Location', 'SKU', 'Qty'], ['A-01', 'ABC', '10'], ['location', '', 'qty'])
    expect(data).toEqual({ location: 'A-01', qty: '10' })
  })

  it('行短于列数时补空串', () => {
    const data = rowToData(['a', 'b'], ['x'], ['a', 'b'])
    expect(data).toEqual({ a: 'x', b: '' })
  })
})
