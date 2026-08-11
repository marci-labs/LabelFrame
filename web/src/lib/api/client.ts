// fetch 封装：base 地址来自设置（可跨机器），错误统一归一为 ApiError。

import type {
  ApiErrorBody,
  DeviceView,
  ExcelImportResult,
  Healthz,
  JobView,
  LogEntry,
  SubmitJobRequest,
  TemplatePackage,
  TemplateSummary,
} from './types'
import { ApiError } from './types'
import { getBaseUrl } from '../settings'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(getBaseUrl() + path, { ...init, mode: 'cors' })
  } catch {
    throw new ApiError('NETWORK_ERROR', `无法连接后端（${getBaseUrl()}），请检查「设置」中的地址与后端是否已启动。`)
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

/** 下载型端点（render-image / render-images / 模板导出）：返回 blob + Content-Disposition 文件名，错误解析 ErrorView。 */
async function fetchBlob(path: string, init: RequestInit, fallbackName: string, failMessage: string): Promise<{ blob: Blob; filename: string }> {
  let res: Response
  try {
    res = await fetch(getBaseUrl() + path, { ...init, mode: 'cors' })
  } catch {
    throw new ApiError('NETWORK_ERROR', `无法连接后端（${getBaseUrl()}），请检查「设置」中的地址与后端是否已启动。`)
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

export const api = {
  healthz: () => request<Healthz>('/healthz'),

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
      res = await fetch(getBaseUrl() + `/api/templates/${encodeURIComponent(name)}/export`, { mode: 'cors' })
    } catch {
      throw new ApiError('NETWORK_ERROR', '无法连接后端，请检查「设置」中的地址。')
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

  /** 设备 / 客户端目录（迭代 16，Server）；404 / 失败 = 单机 WinHost，前端降级为单机模式。 */
  listDevices: () => request<DeviceView[]>('/api/devices'),

  renderImage: (req: SubmitJobRequest) => fetchBlob('/api/print/render-image', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  }, 'label-print.png', '出图失败'),

  /** 调试批量出图：后端渲染全部标签为 PNG 打包 zip 下载（迭代 15，不建作业）。 */
  renderImages: (req: SubmitJobRequest) => fetchBlob('/api/print/render-images', {
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
