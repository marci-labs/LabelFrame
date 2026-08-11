// @vitest-environment jsdom
// 迭代 20（K1）：getServerBaseUrl 双构建分支——client 构建保持现状（默认 53961 / localStorage 兜底 / setServerBaseUrl 内存更新）；
// server 构建恒同源相对路径 ''（不读 localStorage / 机器级配置，避免局域网访问时回环错连 / 残留地址错连）。
// 两分支均用 vi.doMock('../lib/uiMode') 显式注入（不依赖进程 env），任何 VITE_UI_MODE 环境下结果稳定。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { getServerBaseUrl as GetServerBaseUrlFn, setServerBaseUrl as SetServerBaseUrlFn } from './client'
import { setBaseUrl } from '../settings'

type ClientModule = {
  getServerBaseUrl: typeof GetServerBaseUrlFn
  setServerBaseUrl: typeof SetServerBaseUrlFn
}

const KEY = 'labelframe.baseUrl'

beforeEach(() => {
  localStorage.removeItem(KEY)
})

afterEach(() => {
  vi.doUnmock('../uiMode')
  vi.resetModules()
  localStorage.removeItem(KEY)
})

async function loadClient(mode: 'client' | 'server'): Promise<ClientModule> {
  vi.resetModules()
  vi.doMock('../uiMode', () => ({
    UI_MODE: mode,
    isServerUi: mode === 'server',
  }))
  return import('./client')
}

describe('client 构建（VITE_UI_MODE=client）：getServerBaseUrl 保持现状', () => {
  it('无存储值 → 默认 127.0.0.1:53961', async () => {
    const mod = await loadClient('client')
    expect(mod.getServerBaseUrl()).toBe('http://127.0.0.1:53961')
  })

  it('localStorage 兜底生效（模块加载时读取一次；机器级配置不可用时的回退）', async () => {
    setBaseUrl('http://192.168.1.9:53961')
    const mod = await loadClient('client')
    expect(mod.getServerBaseUrl()).toBe('http://192.168.1.9:53961')
  })

  it('setServerBaseUrl 内存更新即时生效（机器级配置保存路径）', async () => {
    const mod = await loadClient('client')
    mod.setServerBaseUrl('http://192.168.1.9:53961')
    expect(mod.getServerBaseUrl()).toBe('http://192.168.1.9:53961')
  })
})

describe('server 构建（K1）：getServerBaseUrl 恒同源相对路径', () => {
  it('返回 ""（同源相对路径）', async () => {
    const mod = await loadClient('server')
    expect(mod.getServerBaseUrl()).toBe('')
  })

  it('不读 localStorage：残留旧值（如客户端 53960 / 服务端地址）也不生效', async () => {
    setBaseUrl('http://127.0.0.1:53960')
    const mod = await loadClient('server')
    expect(mod.getServerBaseUrl()).toBe('')
  })

  it('setServerBaseUrl 不生效（server 构建下地址固定同源，AppContext 也不会调用）', async () => {
    const mod = await loadClient('server')
    mod.setServerBaseUrl('http://192.168.1.9:53961')
    expect(mod.getServerBaseUrl()).toBe('')
  })
})
