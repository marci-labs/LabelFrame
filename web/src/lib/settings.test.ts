// @vitest-environment jsdom
// settings 单测（迭代 15 附九定稿）：getBaseUrl 默认值改页面自身来源 + 方案 B 残留自动纠正。
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DEFAULT_BASE_URL } from './api/types'
import { getBaseUrl, setBaseUrl } from './settings'

const KEY = 'labelframe.baseUrl'

beforeEach(() => {
  localStorage.removeItem(KEY)
})

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.removeItem(KEY)
})

describe('getBaseUrl 默认值（页面自身来源）', () => {
  it('无存储值 → 返回 window.location.origin', () => {
    expect(getBaseUrl()).toBe(window.location.origin)
  })

  it('有存储值（非默认）→ 返回存储值', () => {
    setBaseUrl('http://192.168.1.3:53960')
    expect(getBaseUrl()).toBe('http://192.168.1.3:53960')
  })

  it('方案 B：存储值 == 默认且 origin ≠ 默认 → 忽略存储值、返回 origin', () => {
    setBaseUrl(DEFAULT_BASE_URL)
    expect(getBaseUrl()).toBe(window.location.origin)
  })

  it('无 window（Node）→ 回退 DEFAULT_BASE_URL', () => {
    vi.stubGlobal('window', undefined)
    expect(getBaseUrl()).toBe(DEFAULT_BASE_URL)
  })

  it('尾部 / 归一化', () => {
    setBaseUrl('http://192.168.1.3:53960/')
    expect(getBaseUrl()).toBe('http://192.168.1.3:53960')
  })
})
