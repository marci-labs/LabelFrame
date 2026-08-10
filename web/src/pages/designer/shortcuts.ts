// 设计器快捷操作清单（迭代 15 增强：画布常驻提示条 + 工具栏「快捷键」弹窗完整清单）

export interface ShortcutItem {
  keys: string[]
  desc: string
}

export interface ShortcutGroup {
  title: string
  items: ShortcutItem[]
}

export const SHORTCUT_GROUPS: ShortcutGroup[] = [
  {
    title: '编辑',
    items: [
      { keys: ['Ctrl+Z', 'Ctrl+Y'], desc: '撤销 / 重做' },
      { keys: ['Delete', 'Backspace'], desc: '删除选中元素' },
      { keys: ['Esc'], desc: '取消放置' },
    ],
  },
  {
    title: '剪贴板',
    items: [
      { keys: ['Ctrl+C', 'Ctrl+V'], desc: '复制 / 粘贴元素' },
      { keys: ['Ctrl+Shift+C', 'Ctrl+Shift+V'], desc: '导出设计 / 导入设计（JSON）' },
    ],
  },
  {
    title: '画布',
    items: [
      { keys: ['中键拖动'], desc: '平移画布' },
      { keys: ['Ctrl+滚轮'], desc: '缩放画布' },
      { keys: ['Shift / Ctrl+点击'], desc: '多选元素' },
      { keys: ['拖拽'], desc: '移动元素（智能参考线吸附，网格兜底）' },
      { keys: ['拖动手柄'], desc: '缩放元素' },
    ],
  },
]

/** 画布顶部常驻提示条（编辑模式，与预览模式提示同款视觉）。 */
export const CORE_SHORTCUTS = 'Ctrl+Z 撤销 · Ctrl+C/V 复制粘贴 · Delete 删除 · 中键平移 · Ctrl+滚轮缩放'
