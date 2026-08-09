import { describe, expect, it } from 'vitest'
import { createHistory } from './history'

const snap = (n: number) => String(n)
const parse = (s: string) => parseInt(s, 10)

describe('history 撤销 / 重做栈', () => {
  it('初始状态：无撤销无重做', () => {
    const h = createHistory(0, snap, parse)
    expect(h.data).toBe(0)
    expect(h.undoCount).toBe(0)
    expect(h.redoCount).toBe(0)
  })

  it('commit 前进，undo/redo 往返', () => {
    let h = createHistory(0, snap, parse)
    h = h.commit(1)
    h = h.commit(2)
    expect(h.data).toBe(2)
    expect(h.undoCount).toBe(2)

    const u1 = h.undo()!
    expect(u1.data).toBe(1)
    expect(u1.redoCount).toBe(1)
    const u2 = u1.undo()!
    expect(u2.data).toBe(0)
    expect(u2.undoCount).toBe(0)

    const r1 = u2.redo()!
    expect(r1.data).toBe(1)
    const r2 = r1.redo()!
    expect(r2.data).toBe(2)
    expect(r2.redo()).toBeNull() // 重做耗尽返回 null
  })

  it('空栈 undo/redo 返回 null', () => {
    const h = createHistory(5, snap, parse)
    expect(h.undo()).toBeNull()
    const h2 = createHistory(5, snap, parse).commit(6)
    expect(h2.redo()).toBeNull()
  })

  it('commit 清空重做栈', () => {
    let h = createHistory(0, snap, parse)
    h = h.commit(1)
    h = h.commit(2)
    h = h.undo()!
    expect(h.redoCount).toBe(1)
    h = h.commit(9)
    expect(h.redoCount).toBe(0)
    expect(h.undoCount).toBe(2)
  })

  it('容量上限 100', () => {
    let h = createHistory(0, snap, parse, 3)
    for (let i = 1; i <= 5; i++) h = h.commit(i)
    expect(h.undoCount).toBe(3)
  })

  it('快照为独立拷贝（修改当前 data 不影响历史）', () => {
    const objSnap = (o: { v: number }) => JSON.stringify(o)
    const objParse = (s: string) => JSON.parse(s) as { v: number }
    let h = createHistory({ v: 0 }, objSnap, objParse)
    h = h.commit({ v: 1 })
    const u = h.undo()!
    expect(u.data.v).toBe(0)
  })
})
