import type { DesignElement } from './types'

/**
 * 契约字段自动推导：字段集合 = 「字段填充」元素的 SourceKey 按元素顺序去重
 * （决策 #37；图片 / 线 / 容器不算字段）。
 */
export function deriveFields(elements: readonly DesignElement[]): string[] {
  const keys: string[] = []
  for (const e of elements) {
    // 图片 / 线 / 容器无填充概念（'mode' in e 收窄）
    if (!('mode' in e)) continue
    if (e.mode === 'field' && e.key && !keys.includes(e.key)) keys.push(e.key)
  }
  return keys
}
