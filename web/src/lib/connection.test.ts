// 连接地址候选选择纯函数单测（迭代 118 · #272：下载中心「连接信息」卡地址默认选择规则）。

import { describe, expect, it } from 'vitest'
import { joinAddress, pickDefaultAddress } from './connection'

describe('pickDefaultAddress（默认地址选择规则，迭代 118 · #272）', () => {
  it('origin 主机在候选内：原样使用当前 origin（AC-05 匹配优先）', () => {
    expect(pickDefaultAddress('http://192.168.1.9:53961', ['10.0.0.5', '192.168.1.9'])).toBe('http://192.168.1.9:53961')
  })

  it('localhost 打开：回退首个候选（服务端私网优先排序），不产生 localhost 废码（AC-05）', () => {
    expect(pickDefaultAddress('http://localhost:53961', ['10.0.0.5', '192.168.1.9'])).toBe('http://10.0.0.5:53961')
    expect(pickDefaultAddress('http://127.0.0.1:53961', ['192.168.1.9'])).toBe('http://192.168.1.9:53961')
  })

  it('无匹配（DNS 名等）：同样回退首个候选', () => {
    expect(pickDefaultAddress('http://print-server.local:53961', ['10.0.0.5'])).toBe('http://10.0.0.5:53961')
  })

  it('候选为空 / 未加载 / 请求失败：origin 兜底（无更优信息时不猜测地址）', () => {
    expect(pickDefaultAddress('http://localhost:53961', [])).toBe('http://localhost:53961')
    expect(pickDefaultAddress('http://10.0.0.5:53961', null)).toBe('http://10.0.0.5:53961')
    expect(pickDefaultAddress('http://10.0.0.5:53961', undefined)).toBe('http://10.0.0.5:53961')
  })

  it('端口沿用 origin：默认端口（空）时省略端口段', () => {
    expect(pickDefaultAddress('https://example.com', ['10.0.0.5'])).toBe('https://10.0.0.5')
  })

  it('origin 为空串：原样返回不构造', () => {
    expect(pickDefaultAddress('', ['10.0.0.5'])).toBe('')
  })
})

describe('joinAddress（候选 IP × origin 协议端口拼装）', () => {
  it('带端口：protocol//ip:port', () => {
    expect(joinAddress('http:', '10.0.0.5', '53961')).toBe('http://10.0.0.5:53961')
  })

  it('默认端口（空串）：省略端口段', () => {
    expect(joinAddress('https:', '10.0.0.5', '')).toBe('https://10.0.0.5')
  })
})
