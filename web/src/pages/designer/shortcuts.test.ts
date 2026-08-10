import { describe, expect, it } from 'vitest'
import { CORE_SHORTCUTS, SHORTCUT_GROUPS } from './shortcuts'

describe('设计器快捷键清单', () => {
  it('分组建表且每项有键位与说明', () => {
    expect(SHORTCUT_GROUPS.length).toBeGreaterThanOrEqual(3)
    for (const g of SHORTCUT_GROUPS) {
      expect(g.title.length).toBeGreaterThan(0)
      expect(g.items.length).toBeGreaterThan(0)
      for (const item of g.items) {
        expect(item.keys.length).toBeGreaterThan(0)
        expect(item.desc.length).toBeGreaterThan(0)
      }
    }
  })

  it('键位条目不重复', () => {
    const all = SHORTCUT_GROUPS.flatMap((g) => g.items.flatMap((i) => i.keys))
    expect(new Set(all).size).toBe(all.length)
  })

  it('核心键位（Ctrl+Z / Ctrl+C / Delete）都在完整清单中', () => {
    const all = SHORTCUT_GROUPS.flatMap((g) => g.items.flatMap((i) => i.keys))
    expect(all).toContain('Ctrl+Z')
    expect(all).toContain('Ctrl+C')
    expect(all).toContain('Delete')
  })

  it('常驻提示条非空且含核心键位', () => {
    expect(CORE_SHORTCUTS.length).toBeGreaterThan(20)
    expect(CORE_SHORTCUTS).toContain('Ctrl+Z')
    expect(CORE_SHORTCUTS).toContain('中键平移')
  })
})
