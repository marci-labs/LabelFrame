// 设置持久化（sessionStorage 之外的浏览器存储）：后端地址。
// 显式 window.localStorage + typeof 守卫：Node 26 实验性全局 localStorage 会遮蔽 jsdom 注入版。

import { DEFAULT_BASE_URL } from './api/types'

const KEY = 'labelframe.baseUrl'

function getLocalStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null
  } catch {
    return null
  }
}

export function getBaseUrl(): string {
  const storage = getLocalStorage()
  const v = storage ? storage.getItem(KEY) : null
  const origin = typeof window !== 'undefined' ? window.location.origin : null
  if (v && v.trim()) {
    const cleaned = v.trim().replace(/\/+$/, '')
    // 方案 B（迭代 15 附九第 2 条）：旧版保存的默认地址（127.0.0.1）在非本机页面来源下视为残留，忽略并返回页面来源
    if (origin && cleaned === DEFAULT_BASE_URL && origin !== DEFAULT_BASE_URL) {
      return origin
    }
    return cleaned
  }
  // 无存储值：默认返回页面自身来源（PDA 远程访问等场景）；Node 环境（无 window）回退默认地址
  return origin ?? DEFAULT_BASE_URL
}

export function setBaseUrl(url: string): void {
  const storage = getLocalStorage()
  if (storage) storage.setItem(KEY, url.trim().replace(/\/+$/, ''))
}
