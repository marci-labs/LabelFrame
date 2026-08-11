// 全局 UI 状态：连接状态（服务端 healthz + serverMode）、本机连接（transportConfig）、机器级配置、
// DataPrint 会话草稿、状态栏消息、日志
// 迭代 18（F2）：serverBase 优先级 = 机器级配置（GET /api/host/config）> localStorage 兜底 > 默认 127.0.0.1:53961；
// 启动加载机器级配置后立即生效；保存服务端地址 = setHostConfig + 内存更新 + 重新探测（无需重启）。

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { localApi, serverApi, setServerBaseUrl } from '../lib/api/client'
import type { TransportConfig } from '../lib/api/types'
import { getBaseUrl, setBaseUrl as persistBaseUrl } from '../lib/settings'
import { probeHealthz } from '../lib/api/client'
import { isServerUi } from '../lib/uiMode'
import type { PrintDraft, StorageLike } from './draft'
import { applyDraftValue, loadPrintDraft, savePrintDraft } from './draft'

export interface LogLine {
  time: string
  msg: string
}

/** 业务 API 模式：server = 服务端（模板 / 作业中心）；standalone = 单机降级（本机 WinHost 全套 API）。
 *  迭代 20（K2）：server 构建下恒为 'server'，无 standalone 分支。 */
export type ServerMode = 'unknown' | 'server' | 'standalone'

interface AppContextValue {
  connected: boolean
  /** 生效中的服务端地址（机器级配置 / localStorage 兜底 / 默认）。 */
  baseUrl: string
  /** 业务 API 模式（healthz 探测服务端地址得出）。 */
  serverMode: ServerMode
  /** 本机 Client 的 deviceId（机器级配置；旧客户端为 null，F5 回退第一台在线；server 构建恒 null）。 */
  hostDeviceId: string | null
  /** 迭代 20：本机 Client 枚举的 IPv4 列表（/api/host/config.ips，客户端状态栏显示；server 构建恒空）。 */
  hostIps: string[]
  /** 迭代 20（Y2）：数据与打印「默认目标设备」（在线设备页点选，localStorage 持久化，跨页联动；client 构建不消费）。 */
  defaultTargetDeviceId: string | null
  /** healthz 的传输模式（旧字段，兼容展示兜底）。 */
  transport: string | null
  /** GET /api/transport 结果（mode + params，本机连接），切换成功后立即更新。 */
  transportConfig: TransportConfig | null
  statusMsg: string
  logs: LogLine[]
  drawerOpen: boolean
  /** DataPrint 会话草稿（迭代 15：切页 / 刷新保留，sessionStorage 持久化）。 */
  printDraft: PrintDraft
  setDrawerOpen: (open: boolean) => void
  log: (msg: string) => void
  setStatus: (msg: string) => void
  /** 迭代 20：设置数据与打印默认目标设备（localStorage 持久化；仅 server 构建使用）。 */
  setDefaultTargetDeviceId: (id: string | null) => void
  /** 探测服务端连接（healthz，5s 超时）。 */
  checkConnection: () => Promise<boolean>
  /** 探测任意地址的 /healthz（设置页「测试连接」用输入值，不保存）。 */
  checkUrl: (url: string) => Promise<boolean>
  /** 保存服务端地址（机器级配置持久化 + 立即生效 + 重新探测）；旧客户端回退 localStorage。 */
  changeBaseUrl: (url: string) => Promise<boolean>
  /** 连接切换成功后立即用响应 config 更新全局状态（不依赖 healthz 轮询）。 */
  applyTransportConfig: (cfg: TransportConfig) => void
  clearLogs: () => void
  setDraftSelected: (name: string) => void
  setDraftValue: (template: string, key: string, value: string) => void
  setDraftDebug: (on: boolean) => void
  setDraftJobId: (jobId: string | null) => void
}

const AppContext = createContext<AppContextValue | null>(null)

const MAX_LOGS = 300

/** 迭代 20（Y2）：数据与打印「默认目标设备」localStorage 键（在线设备页点选持久化，跨页联动）。 */
const DEFAULT_TARGET_KEY = 'labelframe.defaultTargetDeviceId'

function readDefaultTargetDevice(): string | null {
  try {
    const v = typeof window !== 'undefined' ? window.localStorage.getItem(DEFAULT_TARGET_KEY) : null
    return v && v.trim() ? v.trim() : null
  } catch {
    return null
  }
}

function persistDefaultTargetDevice(id: string | null): void {
  try {
    const storage = typeof window !== 'undefined' ? window.localStorage : null
    if (!storage) return
    if (id) storage.setItem(DEFAULT_TARGET_KEY, id)
    else storage.removeItem(DEFAULT_TARGET_KEY)
  } catch {
    // 隐私模式等容错：忽略
  }
}

/** 会话存储（sessionStorage）：显式 window 访问 + 守卫（Node 26 实验性全局 / 隐私模式容错）。 */
function getSessionStorage(): StorageLike | undefined {
  try {
    return typeof window !== 'undefined' ? window.sessionStorage : undefined
  } catch {
    return undefined
  }
}

export function AppProvider({ children }: { children: ReactNode }) {
  const [baseUrl, setBaseUrlState] = useState(() => (isServerUi ? '' : getBaseUrl()))
  const [serverMode, setServerMode] = useState<ServerMode>('unknown')
  const [connected, setConnected] = useState(false)
  const [hostDeviceId, setHostDeviceId] = useState<string | null>(null)
  const [hostIps, setHostIps] = useState<string[]>([])
  const [defaultTargetDeviceId, setDefaultTargetDeviceIdState] = useState<string | null>(() => (isServerUi ? readDefaultTargetDevice() : null))
  const [transport, setTransport] = useState<string | null>(null)
  const [transportConfig, setTransportConfig] = useState<TransportConfig | null>(null)
  const [statusMsg, setStatusMsg] = useState('就绪')
  const [logs, setLogs] = useState<LogLine[]>([])
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [printDraft, setPrintDraft] = useState<PrintDraft>(() => loadPrintDraft(getSessionStorage()))
  const pendingRef = useRef<Promise<boolean> | null>(null)

  // 草稿变更即持久化（sessionStorage，刷新保留、标签页天然隔离；不用 localStorage）
  useEffect(() => {
    savePrintDraft(getSessionStorage(), printDraft)
  }, [printDraft])

  const log = useCallback((msg: string) => {
    const time = new Date().toLocaleTimeString('zh-CN', { hour12: false })
    setLogs((prev) => [...prev.slice(-(MAX_LOGS - 1)), { time, msg }])
  }, [])

  const setStatus = useCallback(
    (msg: string) => {
      setStatusMsg(msg)
      log(msg)
    },
    [log],
  )

  const checkConnection = useCallback(async (): Promise<boolean> => {
    if (pendingRef.current) return pendingRef.current
    pendingRef.current = (async () => {
      try {
        await serverApi.healthz()
        setConnected(true)
        setServerMode('server')
        return true
      } catch {
        setConnected(false)
        // 迭代 20（K2）：server 构建无单机降级——探测失败仍保持 server 模式（页面用 serverApi 拉数据），仅置未连接。
        setServerMode(isServerUi ? 'server' : 'standalone')
        return false
      } finally {
        pendingRef.current = null
      }
    })()
    return pendingRef.current
  }, [])

  const checkUrl = useCallback((url: string): Promise<boolean> => probeHealthz(url), [])

  // 启动：读机器级配置（serverUrl 优先）→ 本机连接配置 → 探测服务端
  // 迭代 20（K2）：server 构建由服务端托管、无本机 Client——跳过 localApi 探测（getHostConfig / getTransport），
  // 直接探测服务端 healthz；serverMode 恒 'server'、无 standalone 分支。client 构建保持现状。
  useEffect(() => {
    let on = true
    if (isServerUi) {
      void checkConnection()
      return () => {
        on = false
      }
    }
    void localApi
      .getHostConfig()
      .then((cfg) => {
        if (!on) return
        setServerBaseUrl(cfg.serverUrl)
        setBaseUrlState(cfg.serverUrl)
        setHostDeviceId(cfg.deviceId ?? null)
        setHostIps(cfg.ips ?? [])
        setStatus(`已读取本机配置：服务端 ${cfg.serverUrl}。`)
      })
      .catch(() => {
        if (!on) return
        // 旧客户端（0.14 无 /api/host/config）：回退 localStorage 兜底（getBaseUrl 已含默认值）
        setStatus('本机配置接口不可用，使用浏览器本地保存的服务端地址。')
      })
      .finally(() => {
        if (on) void checkConnection()
      })
    void localApi
      .getTransport()
      .then((cfg) => {
        if (!on) return
        setTransportConfig(cfg)
        setTransport(cfg.mode)
      })
      .catch(() => {
        // 旧客户端无 /api/transport：忽略，保持现状
      })
    return () => {
      on = false
    }
  }, [checkConnection, setStatus])

  const changeBaseUrl = useCallback(
    async (url: string): Promise<boolean> => {
      const cleaned = url.trim().replace(/\/+$/, '')
      try {
        await localApi.setHostConfig({ serverUrl: cleaned })
        // 立即生效：内存更新（机器级配置为唯一事实来源；localStorage 仅兜底）→ 重新探测 → 页面随 baseUrl 重拉
        setServerBaseUrl(cleaned)
        setBaseUrlState(cleaned)
        persistBaseUrl(cleaned)
        setStatus(`服务端地址已保存并生效：${cleaned}`)
        void checkConnection()
        return true
      } catch {
        // 旧客户端 / 无本机配置接口：回退浏览器本地保存
        setServerBaseUrl(cleaned)
        setBaseUrlState(cleaned)
        persistBaseUrl(cleaned)
        setStatus('本机配置接口不可用，已使用浏览器本地保存。')
        void checkConnection()
        return false
      }
    },
    [checkConnection, setStatus],
  )

  const applyTransportConfig = useCallback((cfg: TransportConfig) => {
    setTransportConfig(cfg)
    setTransport(cfg.mode)
  }, [])

  const clearLogs = useCallback(() => setLogs([]), [])

  /** 迭代 20（Y2）：设置默认目标设备（localStorage 持久化；在线设备页点选，数据与打印初始化消费）。 */
  const setDefaultTargetDeviceId = useCallback((id: string | null) => {
    persistDefaultTargetDevice(id)
    setDefaultTargetDeviceIdState(id)
  }, [])

  const setDraftSelected = useCallback((name: string) => {
    setPrintDraft((d) => ({ ...d, selectedName: name }))
  }, [])

  const setDraftValue = useCallback((template: string, key: string, value: string) => {
    setPrintDraft((d) => applyDraftValue(d, template, key, value))
  }, [])

  const setDraftDebug = useCallback((on: boolean) => {
    setPrintDraft((d) => ({ ...d, debugMode: on }))
  }, [])

  const setDraftJobId = useCallback((jobId: string | null) => {
    setPrintDraft((d) => ({ ...d, jobId }))
  }, [])

  const value = useMemo<AppContextValue>(
    () => ({
      connected,
      baseUrl,
      serverMode,
      hostDeviceId,
      hostIps,
      defaultTargetDeviceId,
      transport,
      transportConfig,
      statusMsg,
      logs,
      drawerOpen,
      printDraft,
      setDrawerOpen,
      log,
      setStatus,
      setDefaultTargetDeviceId,
      checkConnection,
      checkUrl,
      changeBaseUrl,
      applyTransportConfig,
      clearLogs,
      setDraftSelected,
      setDraftValue,
      setDraftDebug,
      setDraftJobId,
    }),
    [
      connected,
      baseUrl,
      serverMode,
      hostDeviceId,
      hostIps,
      defaultTargetDeviceId,
      transport,
      transportConfig,
      statusMsg,
      logs,
      drawerOpen,
      printDraft,
      log,
      setStatus,
      setDefaultTargetDeviceId,
      checkConnection,
      checkUrl,
      changeBaseUrl,
      applyTransportConfig,
      clearLogs,
      setDraftSelected,
      setDraftValue,
      setDraftDebug,
      setDraftJobId,
    ],
  )

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}

export function useApp(): AppContextValue {
  const ctx = useContext(AppContext)
  if (!ctx) throw new Error('useApp 必须在 AppProvider 内使用')
  return ctx
}
