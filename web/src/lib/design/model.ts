// 元素查询工具

import type { DesignElement } from './types'

export function elementById(els: readonly DesignElement[], id: string): DesignElement | undefined {
  return els.find((e) => e.id === id)
}

export function elementsByIds(els: readonly DesignElement[], ids: readonly string[]): DesignElement[] {
  return els.filter((e) => ids.includes(e.id))
}
