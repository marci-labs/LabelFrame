// API 契约 DTO（docs/FRONTEND-SPEC.md §4，与后端实现对齐）

import type { BackendContract, BackendElement, BackendLayout } from '../design/convert'

/** 后端地址（设置页配置，默认 127.0.0.1:53960）。 */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:53960'

export interface Healthz {
  service: string
  status: string
  transport: string
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
  items: JobItem[]
}

export interface ExcelImportResult {
  headers: string[]
  rows: string[][]
}

export interface PrinterStatus {
  isOnline: boolean
  isPaperOut: boolean
  isPaused: boolean
  message: string
}

export interface PrinterTestResult {
  sent: boolean
  bytes: number
}

export interface LogEntry {
  deviceId: string
  time: string
  line: string
}

/** 提交作业请求。 */
export interface SubmitJobRequest {
  requestId: string
  template: {
    /** 模板名（迭代 12：Image 打印时后端取模板图片资源；Vector 模式忽略） */
    name?: string
    contract: BackendContract
    layout: BackendLayout
  }
  labels: { data: Record<string, string> }[]
}

// ── 连接管理（迭代 15：GET/POST /api/transport）──

export type TransportMode = 'Log' | 'Tcp' | 'WindowsDriver' | 'Zebra'
export type ZebraKind = 'Tcp' | 'Usb' | 'Driver'

/** 传输参数：只含当前模式所需字段，未使用字段后端返回默认 / 空，前端不展示。 */
export interface TransportParams {
  tcpHost?: string
  tcpPort?: number
  printerName?: string
  zebraKind?: ZebraKind
  zebraUsbName?: string
}

export interface TransportConfig {
  mode: TransportMode
  params: TransportParams
  availableModes?: TransportMode[]
}

/** POST /api/transport 请求体（参数平铺，testOnly 由测试连接填充）。 */
export interface TransportApplyRequest {
  mode: TransportMode
  tcpHost?: string
  tcpPort?: number
  printerName?: string
  zebraKind?: ZebraKind
  zebraUsbName?: string
  testOnly?: boolean
}

/** POST /api/transport 响应：成功与失败（200）统一返回 config = 当前生效连接。 */
export interface TransportResult {
  ok: boolean
  message: string
  config: TransportConfig
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
