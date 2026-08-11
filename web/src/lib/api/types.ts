// API 契约 DTO（docs/FRONTEND-SPEC.md §4，与后端实现对齐）
// 迭代 18（F1）：双 base——serverApi（服务端地址）/ localApi（页面来源 = 本机 LabelFrame Client）。

import type { BackendContract, BackendElement, BackendLayout } from '../design/convert'

/** 服务端地址默认值（迭代 18：语义改为「服务端地址」，默认 127.0.0.1:53961）。 */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:53961'

/** 本机客户端（WinHost）默认地址：localApi 无 window（Node 环境）时的回退。 */
export const DEFAULT_LOCAL_BASE_URL = 'http://127.0.0.1:53960'

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
  /** 迭代 18（B10）：WinHost 扩展 JobView 与 Server 对齐——创建时间（作业历史「时间」列；防御性声明为可选）。 */
  createdAt?: string
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
  /** 迭代 20：服务端记录的服务端所见来源 IP（注册 / 心跳刷新，IPv4 文本；旧设备可能为空）。 */
  lastIp?: string | null
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

// ── 连接管理（迭代 15 §6.2 恢复，迭代 18：全部走 localApi，接口 0.14 已在未删）──

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

// ── 本机客户端（迭代 18 F1/F2：GET/POST /api/host/config，机器级持久化）──

/** 机器级配置（B6：settings.json 缺失 / 损坏时 GET 返回 200 + 默认 serverUrl）。
 *  GET 响应含 deviceId / deviceName；POST 只更新 serverUrl（deviceId / deviceName 由客户端自身提供，后端忽略请求体中的值）。 */
export interface HostConfig {
  serverUrl: string
  deviceId?: string
  deviceName?: string
  /** 迭代 20：本机 IPv4 列表（枚举网卡、过滤回环；多 IP 状态栏逗号分隔显示全部）。 */
  ips?: string[]
}

/** GET /api/printer/status（迭代 15 恢复，F4）。 */
export interface PrinterStatus {
  isOnline: boolean
  isPaperOut: boolean
  isPaused: boolean
  message: string
}

/** POST /api/printer/test 响应。 */
export interface PrinterTestResult {
  sent: boolean
  bytes: number
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
