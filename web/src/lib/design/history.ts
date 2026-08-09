// 撤销 / 重做栈：快照式（与原型一致），容量上限 100。

export interface History<T> {
  readonly data: T
  readonly undoCount: number
  readonly redoCount: number
  /** 提交一步新快照（内部 push 当前 data 的快照，再切换到 next）。 */
  commit: (next: T) => History<T>
  /** 撤销：无可撤销时返回 null。 */
  undo: () => History<T> | null
  /** 重做：无可重做时返回 null。 */
  redo: () => History<T> | null
}

/** 创建历史容器。 */
export function createHistory<T>(initial: T, snapshot: (d: T) => string, parse: (s: string) => T, maxDepth = 100): History<T> {
  const undoStack: string[] = []
  const redoStack: string[] = []
  const mk = (data: T): History<T> => ({
    data,
    undoCount: undoStack.length,
    redoCount: redoStack.length,
    commit: (next) => {
      undoStack.push(snapshot(data))
      if (undoStack.length > maxDepth) undoStack.shift()
      redoStack.length = 0
      return mk(next)
    },
    undo: () => {
      if (!undoStack.length) return null
      redoStack.push(snapshot(data))
      return mk(parse(undoStack.pop()!))
    },
    redo: () => {
      if (!redoStack.length) return null
      undoStack.push(snapshot(data))
      return mk(parse(redoStack.pop()!))
    },
  })
  return mk(initial)
}
