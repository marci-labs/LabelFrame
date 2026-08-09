// 设置持久化（localStorage）：后端地址。

import { DEFAULT_BASE_URL } from './api/types'

const KEY = 'labelframe.baseUrl'

export function getBaseUrl(): string {
  const v = localStorage.getItem(KEY)
  return v && v.trim() ? v.trim().replace(/\/+$/, '') : DEFAULT_BASE_URL
}

export function setBaseUrl(url: string): void {
  localStorage.setItem(KEY, url.trim().replace(/\/+$/, ''))
}
