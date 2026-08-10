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
  return v && v.trim() ? v.trim().replace(/\/+$/, '') : DEFAULT_BASE_URL
}

export function setBaseUrl(url: string): void {
  const storage = getLocalStorage()
  if (storage) storage.setItem(KEY, url.trim().replace(/\/+$/, ''))
}
