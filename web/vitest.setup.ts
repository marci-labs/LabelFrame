// 测试环境垫片：Node 26 实验性全局 localStorage 与 jsdom 未提供 window.localStorage 时，
// 提供内存实现，保证 draft / 会话保留测试可移植（浏览器运行时使用真实 Storage，不受影响）。
function createMemoryStorage(): Storage {
  const map = new Map<string, string>()
  return {
    get length() {
      return map.size
    },
    clear: () => map.clear(),
    getItem: (key: string) => (map.has(key) ? map.get(key)! : null),
    key: (index: number) => [...map.keys()][index] ?? null,
    removeItem: (key: string) => {
      map.delete(key)
    },
    setItem: (key: string, value: string) => {
      map.set(key, String(value))
    },
  } as Storage
}

function installStorage(target: object, name: 'localStorage' | 'sessionStorage') {
  try {
    Object.defineProperty(target, name, {
      value: createMemoryStorage(),
      configurable: true,
      writable: true,
    })
  } catch {
    // 属性不可覆盖时忽略（如 Node 已提供可用实现）
  }
}

installStorage(globalThis, 'localStorage')
installStorage(globalThis, 'sessionStorage')
if (typeof window !== 'undefined') {
  installStorage(window, 'localStorage')
  installStorage(window, 'sessionStorage')
}