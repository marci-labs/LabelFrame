// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { capturePasteOnce, copyText, legacyCopy, readClipboardText } from './clipboard'

/** jsdom 未实现 execCommand / ClipboardEvent 构造器，统一补桩。 */
function mockExecCommand(result: boolean) {
  const fn = vi.fn(() => result)
  Object.defineProperty(document, 'execCommand', { value: fn, configurable: true, writable: true })
  return fn
}

function dispatchPaste(ta: HTMLTextAreaElement, text: string) {
  // jsdom 无 ClipboardEvent 构造器：用 Event + 手工注入 clipboardData
  const ev = new Event('paste', { bubbles: true, cancelable: true })
  Object.defineProperty(ev, 'clipboardData', { value: { getData: () => text } })
  ta.dispatchEvent(ev)
}

afterEach(() => {
  vi.restoreAllMocks()
  document.body.innerHTML = ''
})

describe('clipboard 剪贴板工具（迭代 22 降级修复）', () => {
  it('copyText：Clipboard API 可用时优先使用且不创建隐藏 textarea', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })
    const spyAppend = vi.spyOn(document.body, 'appendChild')
    await expect(copyText('hello')).resolves.toBe(true)
    expect(writeText).toHaveBeenCalledWith('hello')
    expect(spyAppend).not.toHaveBeenCalled()
  })

  it('copyText：Clipboard API 抛 NotAllowedError（无焦点 / 权限拒绝）时降级 execCommand', async () => {
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockRejectedValue(new DOMException('denied', 'NotAllowedError')) },
    })
    const exec = mockExecCommand(true)
    await expect(copyText('hello')).resolves.toBe(true)
    expect(exec).toHaveBeenCalledWith('copy')
    // 降级创建的隐藏 textarea 已清理
    expect(document.querySelectorAll('textarea').length).toBe(0)
  })

  it('copyText：无 Clipboard API（非 secure context）时直接走 execCommand 降级', async () => {
    Object.assign(navigator, { clipboard: undefined })
    const exec = mockExecCommand(true)
    await expect(copyText('hello')).resolves.toBe(true)
    expect(exec).toHaveBeenCalledWith('copy')
  })

  it('legacyCopy：execCommand 失败返回 false 且清理 textarea', () => {
    mockExecCommand(false)
    expect(legacyCopy('x')).toBe(false)
    expect(document.querySelectorAll('textarea').length).toBe(0)
  })

  it('readClipboardText：优先 Clipboard API', async () => {
    Object.assign(navigator, { clipboard: { readText: vi.fn().mockResolvedValue('design-json') } })
    await expect(readClipboardText()).resolves.toBe('design-json')
  })

  it('readClipboardText：权限拒绝返回空串', async () => {
    Object.assign(navigator, {
      clipboard: { readText: vi.fn().mockRejectedValue(new DOMException('denied', 'NotAllowedError')) },
    })
    await expect(readClipboardText()).resolves.toBe('')
  })

  it('capturePasteOnce：用户按 Ctrl+V（paste 事件）即返回剪贴板文本并清理', async () => {
    const p = capturePasteOnce()
    const ta = document.querySelector('textarea')
    expect(ta).not.toBeNull()
    dispatchPaste(ta!, 'paste-text')
    await expect(p).resolves.toBe('paste-text')
    expect(document.querySelectorAll('textarea').length).toBe(0)
  })

  it('capturePasteOnce：按 Esc 取消返回空串', async () => {
    const p = capturePasteOnce()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await expect(p).resolves.toBe('')
    expect(document.querySelectorAll('textarea').length).toBe(0)
  })
})
