// API 契约 DTO（docs/FRONTEND-SPEC.md §4，与后端实现对齐）

import type { BackendContract, BackendElement, BackendLayout } from '../design/convert'

/** 后端地址（设置页配置，默认 127.0.0.1:53960）。 */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:53960'

export interface Healthz {
  service: string
  status: string
  /** 传输模式（旧单机 WinHost 字段；Server 不返回，前端仅作兼容展示，可选）。 */
  transport?: string
}

export interface TemplateSummary {
  name: string
  group: string
  updatedAt: string
}

export interface TemplatePackage {
  name: string
  group: string
  contract: BackendContract
  layout: BackendLayout
  testData?: Record<string, string>
}

export type ElementJson = BackendElement

export interface JobItem {
  index: number
  status: string
  errorCode?: string
  errorMessage?: string
}

export interface JobView {
  jobId: string
  requestId: string
  status: string
  totalItems: number
  completedItems: number
  /** Server（迭代 16）：目标设备 ID 与在线状态（WinHost 不返回）。 */
  targetDeviceId?: string
  deviceStatus?: string
  /** Server：失败张数与错误消息（无逐张明细）。 */
  failedItems?: number
  errorMessage?: string
  /** WinHost：逐张明细（Server 不返回）。 */
  items?: JobItem[]
  /** Log 模拟打印：PNG 目录（仅 WinHost Log 连接时有值） */
  printImageDir?: string
  /** Log 模拟打印：PNG 张数 */
  printImageCount?: number
}

/** 设备视图（GET /api/devices；status = Online / Offline）。 */
export interface DeviceView {
  deviceId: string
  name: string
  registeredAt: string
  lastSeenAt: string
  status: string
}

export interface ExcelImportResult {
  headers: string[]
  rows: string[][]
}

export interface LogEntry {
  deviceId: string
  time: string
  line: string
}

/**
 * 提交作业请求（迭代 16：服务端模式优先 `templateName` 引用模板库 + `targetDeviceId` 定向投递；
 * 自包含 `template` 保留兼容——单机 WinHost / 调试出图使用）。
 */
export interface SubmitJobRequest {
  requestId: string
  /** 服务端模式：引用服务端模板库（优先于 template）。 */
  templateName?: string
  /** 服务端模式：目标设备（客户端）ID。 */
  targetDeviceId?: string
  /** 自包含模板（单机 / 兼容路径；templateName 与 template 同时存在时后端优先 templateName）。 */
  template?: {
    /** 模板名（WinHost 本机提交时用于加载图片资源） */
    name?: string
    contract: BackendContract
    layout: BackendLayout
  }
  labels: { data: Record<string, string> }[]
}

/** 后端错误响应（ErrorView）。 */
export interface ApiErrorBody {
  code: string
  message: string
  fieldKey?: string
}

/** 前端统一错误：code 用于判断，message 为中文人话可直接展示。 */
export class ApiError extends Error {
  readonly code: string
  readonly fieldKey?: string
  constructor(code: string, message: string, fieldKey?: string) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.fieldKey = fieldKey
  }
}
