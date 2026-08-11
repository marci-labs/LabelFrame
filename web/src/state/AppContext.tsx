// 全局 UI 状态：连接状态（healthz）、DataPrint 会话草稿、状态栏消息、日志
// 迭代 17：移除连接管理（transportConfig / transport，迁至客户端本机）；healthz 仅用于连接探测。

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { api } from '../lib/api/client'
import { getBaseUrl, setBaseUrl as persistBaseUrl } from '../lib/settings'
import type { PrintDraft, StorageLike } from './draft'
import { applyDraftValue, loadPrintDraft, savePrintDraft } from './draft'

export interface LogLine {
  time: string
  msg: string
}

interface AppContextValue {
  connected: boolean
  baseUrl: string
  statusMsg: string
  logs: LogLine[]
  drawerOpen: boolean
  /** DataPrint 会话草稿（迭代 15：切页 / 刷新保留，sessionStorage 持久化）。 */
  printDraft: PrintDraft
  setDrawerOpen: (open: boolean) => void
  log: (msg: string) => void
  setStatus: (msg: string) => void
  /** 探测后端连接（healthz）。 */
  checkConnection: () => Promise<boolean>
  /** 更新后端地址并重新探测。 */
  changeBaseUrl: (url: string) => void
  clearLogs: () => void
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
        await api.healthz()
        setConnected(true)
        return true
      } catch {
        setConnected(false)
        return false
      } finally {
        pendingRef.current = null
      }
    })()
    return pendingRef.current
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
      setDraftSelected,
      setDraftValue,
      setDraftDebug,
      setDraftJobId,
    }),
    [
      connected,
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
