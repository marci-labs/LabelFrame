// 全局 UI 状态：连接状态（healthz + transportConfig）、DataPrint 会话草稿、状态栏消息、日志

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { api } from '../lib/api/client'
import type { TransportConfig } from '../lib/api/types'
import { getBaseUrl, setBaseUrl as persistBaseUrl } from '../lib/settings'
import type { PrintDraft, StorageLike } from './draft'
import { applyDraftValue, loadPrintDraft, savePrintDraft } from './draft'

export interface LogLine {
  time: string
  msg: string
}

interface AppContextValue {
  connected: boolean
  /** healthz 的传输模式（旧字段，兼容展示兜底）。 */
  transport: string | null
  /** GET /api/transport 结果（mode + params），切换成功后立即更新。 */
  transportConfig: TransportConfig | null
  baseUrl: string
  statusMsg: string
  logs: LogLine[]
  drawerOpen: boolean
  /** DataPrint 会话草稿（迭代 15：切页 / 刷新保留，sessionStorage 持久化）。 */
  printDraft: PrintDraft
  setDrawerOpen: (open: boolean) => void
  log: (msg: string) => void
  setStatus: (msg: string) => void
  /** 探测后端连接（healthz）；成功后顺带刷新 transportConfig（轮询仅作后端重启兜底）。 */
  checkConnection: () => Promise<boolean>
  /** 更新后端地址并重新探测。 */
  changeBaseUrl: (url: string) => void
  clearLogs: () => void
  /** 连接切换成功后立即用响应 config 更新全局状态（不依赖 healthz 轮询）。 */
  applyTransportConfig: (cfg: TransportConfig) => void
  setDraftSelected: (name: string) => void
  setDraftValue: (template: string, key: string, value: string) => void
  setDraftDebug: (on: boolean) => void
  setDraftJobId: (jobId: string | null) => void
}

const AppContext = createContext<AppContextValue | null>(null)

const MAX_LOGS = 300

/** 会话存储（sessionStorage）：显式 window 访问 + 守卫（Node 26 实验性全局 / 隐私模式容错）。 */
function getSessionStorage(): StorageLike | undefined {
  try {
    return typeof window !== 'undefined' ? window.sessionStorage : undefined
  } catch {
    return undefined
  }
}

export function AppProvider({ children }: { children: ReactNode }) {
  const [baseUrl, setBaseUrlState] = useState(getBaseUrl())
  const [connected, setConnected] = useState(false)
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
        const h = await api.healthz()
        setConnected(true)
        setTransport(h.transport)
        // transportConfig 兜底刷新（后端重启后恢复全局态）；旧后端无 /api/transport 时忽略
        try {
          setTransportConfig(await api.getTransport())
        } catch {
          // 忽略：保持现有 transportConfig（如切换后尚未轮询到的场景）
        }
        return true
      } catch {
        setConnected(false)
        setTransport(null)
        setTransportConfig(null)
        return false
      } finally {
        pendingRef.current = null
      }
    })()
    return pendingRef.current
  }, [])

  const applyTransportConfig = useCallback((cfg: TransportConfig) => {
    setTransportConfig(cfg)
    setTransport(cfg.mode)
  }, [])

  const changeBaseUrl = useCallback(
    (url: string) => {
      persistBaseUrl(url)
      setBaseUrlState(getBaseUrl())
      void checkConnection()
    },
    [checkConnection],
  )

  const clearLogs = useCallback(() => setLogs([]), [])

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
      transport,
      transportConfig,
      baseUrl,
      statusMsg,
      logs,
      drawerOpen,
      printDraft,
      setDrawerOpen,
      log,
      setStatus,
      checkConnection,
      changeBaseUrl,
      clearLogs,
      applyTransportConfig,
      setDraftSelected,
      setDraftValue,
      setDraftDebug,
      setDraftJobId,
    }),
    [
      connected,
      transport,
      transportConfig,
      baseUrl,
      statusMsg,
      logs,
      drawerOpen,
      printDraft,
      log,
      setStatus,
      checkConnection,
      changeBaseUrl,
      clearLogs,
      applyTransportConfig,
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
