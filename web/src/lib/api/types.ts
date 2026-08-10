// API 契约 DTO（docs/FRONTEND-SPEC.md §4，与后端实现对齐）

import type { BackendContract, BackendElement, BackendLayout } from '../design/convert'

/** 后端地址（设置页配置，默认 127.0.0.1:53960）。 */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:53960'

export interface Healthz {
  service: string
  status: string
  transport: string
  /** 服务端默认打印方式（迭代 12：供前端下拉显示） */
  printMode?: 'Vector' | 'Image'
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
  /** 打印方式：缺省用服务端 PrintMode 配置（迭代 12） */
  printMode?: 'Vector' | 'Image'
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
