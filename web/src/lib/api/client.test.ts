// @vitest-environment jsdom
// 迭代 20（K1）：getServerBaseUrl 双构建分支——client 构建保持现状（默认 53961 / localStorage 兜底 / setServerBaseUrl 内存更新）；
// server 构建恒同源相对路径 ''（不读 localStorage / 机器级配置，避免局域网访问时回环错连 / 残留地址错连）。
// 两分支均用 vi.doMock('../lib/uiMode') 显式注入（不依赖进程 env），任何 VITE_UI_MODE 环境下结果稳定。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { getServerBaseUrl as GetServerBaseUrlFn, probeHealthz as ProbeHealthzFn, setServerBaseUrl as SetServerBaseUrlFn } from './client'
import { ApiError } from './types'
import type { TemplatePackage } from './types'
import { setBaseUrl } from '../settings'

type ClientModule = {
  getServerBaseUrl: typeof GetServerBaseUrlFn
  setServerBaseUrl: typeof SetServerBaseUrlFn
  probeHealthz: typeof ProbeHealthzFn
  pluginPackageDownloadUrl: typeof import('./client')['pluginPackageDownloadUrl']
  localApi: typeof import('./client')['localApi']
  /** 迭代 91（F-01）：超时分档可变配置——测试注入秒级短超时验证超时分支。 */
  requestTimeouts: typeof import('./client')['requestTimeouts']
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

describe('pluginPackageDownloadUrl（迭代 23 §2.1：与 clientPackageDownloadUrl 同模式）', () => {
  it('client 构建：绝对 URL（默认 127.0.0.1:53961 + 路径编码）', async () => {
    const mod = await loadClient('client')
    expect(mod.pluginPackageDownloadUrl('sample-1.0.0.lfplugin')).toBe('http://127.0.0.1:53961/api/plugin-packages/sample-1.0.0.lfplugin')
  })

  it('server 构建：同源相对路径', async () => {
    const mod = await loadClient('server')
    expect(mod.pluginPackageDownloadUrl('sample-1.0.0.lfplugin')).toBe('/api/plugin-packages/sample-1.0.0.lfplugin')
  })
})

describe('probeHealthz 空地址语义（迭代 86，#142 a 案）', () => {
  it('空地址（空串 / 空白 / 纯斜杠）不发起网络探测直接判失败——空串 base 落同源会打到本机 WinHost /healthz 200 谎报可连接', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn()
    vi.stubGlobal('fetch', fetchStub)
    try {
      await expect(mod.probeHealthz('')).resolves.toBe(false)
      await expect(mod.probeHealthz('   ')).resolves.toBe(false)
      await expect(mod.probeHealthz('/')).resolves.toBe(false)
      expect(fetchStub).not.toHaveBeenCalled()
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('非空地址照常探测 /healthz 且去尾斜杠（既有行为不变）', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'ok' }), { status: 200 }))
    vi.stubGlobal('fetch', fetchStub)
    try {
      await expect(mod.probeHealthz('http://192.168.1.9:53961/')).resolves.toBe(true)
      expect(fetchStub).toHaveBeenCalledWith('http://192.168.1.9:53961/healthz', expect.objectContaining({ mode: 'cors' }))
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

describe('previewTemplate 请求形态（迭代 46 真机验收回归：旧版 Server 兼容）', () => {
  it('POST 携带 Content-Type: application/json 与空 JSON 体；返回 blob 与文件名', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(new Blob(['png-bytes'], { type: 'image/png' }), {
        status: 200,
        headers: { 'Content-Disposition': 'attachment; filename="70x50.png"' },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const result = await mod.localApi.previewTemplate('70*50 容器码')
      expect(fetchStub).toHaveBeenCalledTimes(1)
      const [url, init] = fetchStub.mock.calls[0] as [string, RequestInit]
      expect(url).toBe(`${window.location.origin}/api/templates/${encodeURIComponent('70*50 容器码')}/preview`)
      expect(init.method).toBe('POST')
      expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json')
      expect(init.body).toBe('{}')
      expect(result.filename).toBe('70x50.png')
      expect(result.blob.size).toBeGreaterThan(0)
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

// ── 迭代 91（F-01 / F-09 · #149 a 案决议）：业务请求超时分档 + 导出错误通道同构 ──

/** 挂起端点：不 resolve，仅在收到中止信号时拒绝（模拟后端「接了不回」——超时分支的唯一触发路径）。 */
function hangingFetch() {
  return vi.fn((_url: string, init?: RequestInit) =>
    new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('The operation was aborted.', 'AbortError')))
    }),
  )
}

const TIMEOUT_PKG: TemplatePackage = {
  name: '70x50',
  group: '默认',
  contract: { name: 'contract-t', version: '1', fields: [] },
  layout: { name: 'layout-t', contractName: 'contract-t', contractVersion: '1', widthMm: 70, heightMm: 50, elements: [] },
}

describe('业务请求超时（迭代 91 F-01：普通 30s / 出图与上传类 120s 分档，注入短超时秒级验证）', () => {
  it('普通请求挂起：超时归一化为 ApiError("TIMEOUT", 中文文案)，请求携带中止信号（不真实等待 30s）', async () => {
    const mod = await loadClient('client')
    mod.requestTimeouts.normal = 30 // 注入秒级短超时验证超时分支
    const fetchStub = hangingFetch()
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.saveTemplate(TIMEOUT_PKG).then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('TIMEOUT')
      expect((err as ApiError).message).toContain('请求超时')
      expect(fetchStub).toHaveBeenCalledTimes(1)
      expect((fetchStub.mock.calls[0] as [string, RequestInit])[1].signal).toBeTruthy()
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('大负载端点（renderImage 出图）挂起：走 heavy 档——normal 档保持 30s 不变，仅 heavy 注入短超时（分档落点反向断言）', async () => {
    const mod = await loadClient('client')
    mod.requestTimeouts.heavy = 30 // 仅 heavy 缩短；若 renderImage 误挂 normal（30s），本用例将挂起至测试超时失败
    const fetchStub = hangingFetch()
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.renderImage({ requestId: 'r-1', labels: [] }).then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('TIMEOUT')
      expect((err as ApiError).message).toContain('请求超时')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('普通端点不吃 heavy 档：listTemplates 仅随 normal 注入超时（normal 档分档落点反向断言）', async () => {
    const mod = await loadClient('client')
    mod.requestTimeouts.normal = 30
    mod.requestTimeouts.heavy = 60_000 // heavy 放宽不影响普通请求
    const fetchStub = hangingFetch()
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.listTemplates().then(
        () => null,
        (e: unknown) => e,
      )
      expect((err as ApiError).code).toBe('TIMEOUT')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('网络不可达（fetch 立即拒绝，非超时）：保持既有 NETWORK_ERROR 语义与中文文案，不误判为 TIMEOUT', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockRejectedValue(new TypeError('fetch failed'))
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.listTemplates().then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('NETWORK_ERROR')
      expect((err as ApiError).message).toContain('无法连接')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('成功请求不受超时改造影响：正常返回 JSON（计时器即清理，不驻留）', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(new Response(JSON.stringify([{ name: '70x50', group: '默认', updatedAt: '2026-09-18' }]), { status: 200 }))
    vi.stubGlobal('fetch', fetchStub)
    try {
      const list = await mod.localApi.listTemplates()
      expect(list).toHaveLength(1)
      expect(list[0].name).toBe('70x50')
      expect((fetchStub.mock.calls[0] as [string, RequestInit])[1].signal).toBeTruthy()
    } finally {
      vi.unstubAllGlobals()
    }
  })
})

describe('exportTemplate 错误通道与 fetchBlob 同构（迭代 91 F-09）', () => {
  it('AC-02：HTTP 500 + 后端 ErrorView → 呈现后端真实 code 与 message，不再固定「导出失败（HTTP 500）」', async () => {
    const mod = await loadClient('client')
    const fetchStub = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ code: 'TEMPLATE_NOT_FOUND', message: '模板「70x50」不存在。' }), {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchStub)
    try {
      const err = await mod.localApi.exportTemplate('70x50').then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('TEMPLATE_NOT_FOUND')
      expect((err as ApiError).message).toBe('模板「70x50」不存在。')
      // GET 请求形态不变
      expect((fetchStub.mock.calls[0] as [string, RequestInit])[1].method).toBe('GET')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('HTTP 500 无 JSON 体（网关 / 中间层错误）：回退既有中文文案「导出失败（HTTP 500）。」', async () => {
    const mod = await loadClient('client')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('bad gateway', { status: 500 })))
    try {
      const err = await mod.localApi.exportTemplate('70x50').then(
        () => null,
        (e: unknown) => e,
      )
      expect(err).toBeInstanceOf(Error)
      expect((err as ApiError).name).toBe('ApiError')
      expect((err as ApiError).code).toBe('HTTP_500')
      expect((err as ApiError).message).toBe('导出失败（HTTP 500）。')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('成功：返回 blob + Content-Disposition 文件名；响应头缺省时回退 {模板名}.lfpkg（解析收敛到 fetchBlob 一处）', async () => {
    const mod = await loadClient('client')
    const withName = vi.fn().mockResolvedValue(
      new Response(new Blob(['pkg-bytes']), { status: 200, headers: { 'Content-Disposition': 'attachment; filename="custom-name.lfpkg"' } }),
    )
    vi.stubGlobal('fetch', withName)
    try {
      const result = await mod.localApi.exportTemplate('70x50')
      expect(result.filename).toBe('custom-name.lfpkg')
      expect(result.blob.size).toBeGreaterThan(0)
    } finally {
      vi.unstubAllGlobals()
    }
    const withoutName = vi.fn().mockResolvedValue(new Response(new Blob(['pkg-bytes']), { status: 200 }))
    vi.stubGlobal('fetch', withoutName)
    try {
      const result = await mod.localApi.exportTemplate('70x50')
      expect(result.filename).toBe('70x50.lfpkg')
    } finally {
      vi.unstubAllGlobals()
    }
  })
})
