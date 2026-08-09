// 全局 UI 状态：连接状态、状态栏消息、日志

import { createContext, useCallback, useContext, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { api } from '../lib/api/client'
import { getBaseUrl, setBaseUrl as persistBaseUrl } from '../lib/settings'

export interface LogLine {
  time: string
  msg: string
}

interface AppContextValue {
  connected: boolean
  transport: string | null
  baseUrl: string
  statusMsg: string
  logs: LogLine[]
  drawerOpen: boolean
  setDrawerOpen: (open: boolean) => void
  log: (msg: string) => void
  setStatus: (msg: string) => void
  /** 探测后端连接（healthz）。 */
  checkConnection: () => Promise<boolean>
  /** 更新后端地址并重新探测。 */
  changeBaseUrl: (url: string) => void
  clearLogs: () => void
}

const AppContext = createContext<AppContextValue | null>(null)

const MAX_LOGS = 300

export function AppProvider({ children }: { children: ReactNode }) {
  const [baseUrl, setBaseUrlState] = useState(getBaseUrl())
  const [connected, setConnected] = useState(false)
  const [transport, setTransport] = useState<string | null>(null)
  const [statusMsg, setStatusMsg] = useState('就绪')
  const [logs, setLogs] = useState<LogLine[]>([])
  const [drawerOpen, setDrawerOpen] = useState(false)
  const pendingRef = useRef<Promise<boolean> | null>(null)

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
        return true
      } catch (err) {
        setConnected(false)
        setTransport(null)
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

  const value = useMemo<AppContextValue>(
    () => ({
      connected,
      transport,
      baseUrl,
      statusMsg,
      logs,
      drawerOpen,
      setDrawerOpen,
      log,
      setStatus,
      checkConnection,
      changeBaseUrl,
      clearLogs,
    }),
    [connected, transport, baseUrl, statusMsg, logs, drawerOpen, log, setStatus, checkConnection, changeBaseUrl, clearLogs],
  )

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}

export function useApp(): AppContextValue {
  const ctx = useContext(AppContext)
  if (!ctx) throw new Error('useApp 必须在 AppProvider 内使用')
  return ctx
}
