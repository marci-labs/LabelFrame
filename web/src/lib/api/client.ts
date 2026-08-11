// fetch 封装（迭代 18 F1：双 base）：
// - serverApi：服务端地址（机器级配置 > localStorage 兜底 > 默认 127.0.0.1:53961）——模板 / 作业 / 设备 / 日志 / Excel / 调试出图 / healthz；
// - localApi：页面来源（托管本页的 LabelFrame Client，127.0.0.1:53960）——transport / printer / host/config 本机接口；
//   业务 API 同时保留（单机降级：Server 不可达时模板 / 作业 / 日志走本机 WinHost 全套 API）。
// 错误消息区分「服务端」与「本机客户端」。

import type {
  ApiErrorBody,
  DeviceView,
  ExcelImportResult,
  Healthz,
  HostConfig,
  JobView,
  LogEntry,
  PrinterStatus,
  PrinterTestResult,
  SubmitJobRequest,
  TemplatePackage,
  TemplateSummary,
  TransportApplyRequest,
  TransportConfig,
  TransportResult,
} from './types'
import { ApiError, DEFAULT_LOCAL_BASE_URL } from './types'
import { getBaseUrl } from '../settings'
import { UI_MODE } from '../uiMode'

// ── base 解析 ──

// 迭代 20（K1）：server 构建下 serverApi base 固定同源相对路径（''，不读 localStorage / 机器级配置）——
// Server UI 由服务端托管，同源即可访问 API；局域网其他机器访问 http://<服务端IP>:53961 时避免回环地址
// 错连到访问者本机、或 localStorage 残留旧值错连到客户端 WinHost。client 构建保持现状。
let serverBaseUrl = UI_MODE === 'server' ? '' : getBaseUrl()

/** 更新服务端地址（AppContext 机器级配置加载 / 保存后调用，模块级即时生效；server 构建下不调用）。 */
export function setServerBaseUrl(url: string): void {
  serverBaseUrl = url.trim().replace(/\/+$/, '')
}

export function getServerBaseUrl(): string {
  if (UI_MODE === 'server') return ''
  return serverBaseUrl
}

/** 本机客户端地址 = 页面来源（页面由 LabelFrame Client 托管）；无 window（Node 测试）回退默认本机地址。 */
export function getLocalBaseUrl(): string {
  if (typeof window !== 'undefined' && window.location && window.location.origin) {
    return window.location.origin
  }
  return DEFAULT_LOCAL_BASE_URL
}

// ── 请求原语 ──

function makeRequest(base: () => string, label: '服务端' | '本机客户端') {
  return async function request<T>(path: string, init?: RequestInit): Promise<T> {
    let res: Response
    try {
      res = await fetch(base() + path, { ...init, mode: 'cors' })
    } catch {
      throw new ApiError('NETWORK_ERROR', `无法连接${label}（${base()}），请检查服务端地址与本机客户端是否已启动。`)
    }
    if (!res.ok) {
      let body: ApiErrorBody | null = null
      try {
        body = (await res.json()) as ApiErrorBody
      } catch {
        body = null
      }
      throw new ApiError(body?.code ?? 'HTTP_' + res.status, body?.message ?? `请求失败（HTTP ${res.status}）。`, body?.fieldKey)
    }
    if (res.status === 204) return undefined as T
    try {
      return (await res.json()) as T
    } catch {
      // 部分端点返回纯文本（如模板导入返回模板名）
      return (await res.text()) as T
    }
  }
}

/** 下载型端点（render-image / render-images / 模板导出）：返回 blob + Content-Disposition 文件名，错误解析 ErrorView。 */
function makeFetchBlob(base: () => string, label: '服务端' | '本机客户端') {
  return async function fetchBlob(path: string, init: RequestInit, fallbackName: string, failMessage: string): Promise<{ blob: Blob; filename: string }> {
    let res: Response
    try {
      res = await fetch(base() + path, { ...init, mode: 'cors' })
    } catch {
      throw new ApiError('NETWORK_ERROR', `无法连接${label}（${base()}），请检查服务端地址与本机客户端是否已启动。`)
    }
    if (!res.ok) {
      let body: ApiErrorBody | null = null
      try {
        body = (await res.json()) as ApiErrorBody
      } catch {
        body = null
      }
      throw new ApiError(body?.code ?? 'HTTP_' + res.status, body?.message ?? `${failMessage}（HTTP ${res.status}）。`, body?.fieldKey)
    }
    const blob = await res.blob()
    const disposition = res.headers.get('Content-Disposition') ?? ''
    const match = /filename="?([^";]+)"?/.exec(disposition)
    const filename = match?.[1] ?? fallbackName
    return { blob, filename }
  }
}

/** 业务 API（双端共有：Server 与 WinHost 均实现；调用方按模式选择）。 */
function makeBusinessApi(base: () => string, label: '服务端' | '本机客户端') {
  const request = makeRequest(base, label)
  const fetchBlob = makeFetchBlob(base, label)
  return {
    /** 健康探测（5s 超时：远程服务端不可达时快速判为离线，不挂起连接状态）。 */
    healthz: async () => {
      let res: Response
      try {
        res = await fetch(base() + '/healthz', { mode: 'cors', signal: AbortSignal.timeout(5000) })
      } catch {
        throw new ApiError('NETWORK_ERROR', `无法连接${label}（${base()}），请检查服务端地址与本机客户端是否已启动。`)
      }
      if (!res.ok) throw new ApiError('HTTP_' + res.status, `请求失败（HTTP ${res.status}）。`)
      return (await res.json()) as Healthz
    },

    listTemplates: (group?: string) => request<TemplateSummary[]>(group ? `/api/templates?group=${encodeURIComponent(group)}` : '/api/templates'),
    getTemplate: (name: string) => request<TemplatePackage>(`/api/templates/${encodeURIComponent(name)}`),
    saveTemplate: (pkg: TemplatePackage) =>
      request<{ name: string; group: string }>('/api/templates', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(pkg),
      }),
    deleteTemplate: (name: string) => request<void>(`/api/templates/${encodeURIComponent(name)}`, { method: 'DELETE' }),
    exportTemplate: async (name: string): Promise<{ blob: Blob; filename: string }> => {
      let res: Response
      try {
        res = await fetch(base() + `/api/templates/${encodeURIComponent(name)}/export`, { mode: 'cors' })
      } catch {
        throw new ApiError('NETWORK_ERROR', `无法连接${label}，请检查服务端地址。`)
      }
      if (!res.ok) throw new ApiError('EXPORT_FAILED', `导出失败（HTTP ${res.status}）。`)
      const blob = await res.blob()
      const disposition = res.headers.get('Content-Disposition') ?? ''
      const match = /filename="?([^";]+)"?/.exec(disposition)
      const filename = match?.[1] ?? `${name}.lfpkg`
      return { blob, filename }
    },
    importTemplate: async (file: File): Promise<string> => {
      const form = new FormData()
      form.append('file', file)
      return request<string>('/api/templates/import', { method: 'POST', body: form })
    },

    importExcel: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return request<ExcelImportResult>('/api/import/excel', { method: 'POST', body: form })
    },

    submitJob: (req: SubmitJobRequest) =>
      request<JobView>('/api/jobs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(req),
      }),
    getJob: (jobId: string) => request<JobView>(`/api/jobs/${encodeURIComponent(jobId)}`),
    retryJobItem: (jobId: string, index: number) =>
      request<JobView>(`/api/jobs/${encodeURIComponent(jobId)}/items/${index}/retry`, { method: 'POST' }),

    /** 作业历史（迭代 18 F6 / B10）：默认 100、上限 500，倒序。 */
    getJobs: (limit = 100) => request<JobView[]>(`/api/jobs?limit=${limit}`),

    /** 设备 / 客户端目录（迭代 16，Server）；404 / 失败 = 单机 WinHost，前端降级为单机模式。 */
    listDevices: () => request<DeviceView[]>('/api/devices'),

    renderImage: (req: SubmitJobRequest) =>
      fetchBlob('/api/print/render-image', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(req),
      }, 'label-print.png', '出图失败'),

    /** 调试批量出图：后端渲染全部标签为 PNG 打包 zip 下载（迭代 15，不建作业）。 */
    renderImages: (req: SubmitJobRequest) =>
      fetchBlob('/api/print/render-images', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(req),
      }, 'labels-debug.zip', '下载调试图片失败'),

    getLogs: (deviceId?: string, since?: string) => {
      const params = new URLSearchParams()
      if (deviceId) params.set('deviceId', deviceId)
      if (since) params.set('since', since)
      const qs = params.toString()
      return request<LogEntry[]>(qs ? `/api/logs?${qs}` : '/api/logs')
    },
  }
}

/** 服务端 API（模板 / 作业 / 设备 / 日志 / Excel / 调试出图 / healthz → 服务端地址）。 */
export const serverApi = makeBusinessApi(getServerBaseUrl, '服务端')

const localRequest = makeRequest(getLocalBaseUrl, '本机客户端')

/** 本机客户端 API（transport / printer / host/config 本机接口 + 业务 API 供单机降级）。 */
export const localApi = {
  ...makeBusinessApi(getLocalBaseUrl, '本机客户端'),

  // ── 连接管理（迭代 15 恢复，F3）──
  getTransport: () => localRequest<TransportConfig>('/api/transport'),
  setTransport: (req: TransportApplyRequest) =>
    localRequest<TransportResult>('/api/transport', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    }),
  testTransport: (req: TransportApplyRequest) =>
    localRequest<TransportResult>('/api/transport', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...req, testOnly: true }),
    }),

  // ── 打印机（迭代 15 恢复，F4）──
  getPrinterStatus: () => localRequest<PrinterStatus>('/api/printer/status'),
  testPrinter: () => localRequest<PrinterTestResult>('/api/printer/test', { method: 'POST' }),

  // ── 机器级配置（迭代 18 F2 / B6）──
  getHostConfig: () => localRequest<HostConfig>('/api/host/config'),
  setHostConfig: (cfg: HostConfig) =>
    localRequest<void>('/api/host/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(cfg),
    }),
}

/** 探测任意地址的 /healthz（设置页「测试连接」用输入值探测，不保存；5s 超时防挂起）。 */
export async function probeHealthz(url: string): Promise<boolean> {
  try {
    const cleaned = url.trim().replace(/\/+$/, '')
    const res = await fetch(cleaned + '/healthz', { mode: 'cors', signal: AbortSignal.timeout(5000) })
    return res.ok
  } catch {
    return false
  }
}
