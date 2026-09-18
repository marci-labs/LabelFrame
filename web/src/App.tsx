// 应用框架：左侧主导航（state 切换，无路由库）+ 底部状态栏 + 日志抽屉
// 迭代 20：双构建（VITE_UI_MODE）——server 构建菜单移除设置与打印机相关内容，新增「在线设备」，
// 状态栏 server 显示服务端地址（同源）与 UI 模式、client 显示本机 IP。
// 迭代 80（#128 决议 2「三名义」）：client 状态栏改呈现「本机打印服务：运行中 / 未运行」——
// 「服务端」一词不再兼指本机后台服务（评审 #114 B-9）；服务端连通在设置页、加入状态在数据与打印页。
// 迭代 75（#112）：「PDA 日志 / 设备日志」页下线（回传链路不存在前界面收敛，决策 #140）——双形态导航入口移除。
// 迭代 92（#150 F-02）：导航切 tab 统一走 switchTab——设计器在编辑且有未保存更改时经其注册的离开守卫
// 弹三选确认（保存并离开 / 放弃更改 / 继续编辑），确认后才切换，防误触丢失排版工作。

import { useCallback, useEffect, useRef, useState } from 'react'
import { AppProvider, useApp } from './state/AppContext'
import { Icon, LabelLogo } from './components/Icon'
import type { IconName } from './components/Icon'
import type { DesignerRequest, TabId } from './state/types'
import { isServerUi } from './lib/uiMode'
import { Workbench } from './pages/Workbench'
import { Designer } from './pages/Designer'
import { DataPrint } from './pages/DataPrint'
import { Devices } from './pages/Devices'
import { JobHistory } from './pages/JobHistory'
import { Settings } from './pages/Settings'
import { DownloadCenter } from './pages/DownloadCenter'
import { PluginPackages } from './pages/PluginPackages'

const TABS: { id: TabId; label: string; icon: IconName }[] = isServerUi
  ? [
      { id: 'workbench', label: '工作台', icon: 'workbench' },
      { id: 'designer', label: '设计器', icon: 'designer' },
      { id: 'data', label: '数据与打印', icon: 'data' },
      { id: 'devices', label: '在线设备', icon: 'grid' },
      { id: 'jobs', label: '作业历史', icon: 'history' },
      // 迭代 59（决策 #119）：Server UI「客户端下载」页升级为统一「下载中心」——客户端安装包 + PDA APK 同页分区、扫码下载
      { id: 'packages', label: '下载中心', icon: 'download' },
      // 迭代 23 §5.4：Server UI「插件管理」页（插件包列表 / 上传 / 下载 / 删除，与「客户端下载」并列）
      { id: 'plugin-packages', label: '插件管理', icon: 'puzzle' },
      // 迭代 75（#112）：「设备日志」页下线——/api/logs 端点与 logs.db 保留（未来回传地基）
    ]
  : [
      { id: 'workbench', label: '工作台', icon: 'workbench' },
      { id: 'designer', label: '设计器', icon: 'designer' },
      { id: 'data', label: '数据与打印', icon: 'data' },
      { id: 'jobs', label: '作业历史', icon: 'history' },
      { id: 'settings', label: '设置', icon: 'settings' },
    ]

/** 状态栏多 IP 过长省略显示（title 给全量）。 */
function truncateIps(ips: string[], max = 28): string {
  const s = ips.join(', ')
  return s.length > max ? s.slice(0, max) + '…' : s
}

function Shell() {
  const [tab, setTab] = useState<TabId>('workbench')
  const [designerReq, setDesignerReq] = useState<DesignerRequest | null>(null)
  // 迭代 92（#150 F-02）：设计器注册的离开守卫——设计器 tab 在编辑且有未保存更改时，
  // 导航切 tab 交由守卫挂起（弹三选 Modal：保存并离开 / 放弃更改 / 继续编辑），确认后再切换。
  const designerLeaveRef = useRef<((leave: () => void) => void) | null>(null)
  const registerDesignerLeave = useCallback((fn: ((leave: () => void) => void) | null) => {
    designerLeaveRef.current = fn
  }, [])
  const app = useApp()
  // 迭代 93（#151 F-06）：解构出三个成员再入依赖——exhaustive-deps 按 `app` 聚合对象报缺依赖，
  // 而三者均为 AppContext 的 useCallback 稳定引用（baseUrl 用于地址变更后重排周期探测），语义与原写法一致。
  const { checkConnection, checkLocalService, baseUrl } = app

  useEffect(() => {
    void checkConnection()
    // 迭代 80（#128 决议 2「三名义」）：client 构建周期探测本机打印服务（页面来源 /healthz）——
    // 状态栏「本机打印服务：运行中 / 未运行」数据源，与服务端地址连通性（checkConnection）各自独立
    if (!isServerUi) void checkLocalService()
    // 周期探测连接（10s），后端重启后状态自动恢复
    const timer = setInterval(() => {
      void checkConnection()
      if (!isServerUi) void checkLocalService()
    }, 10000)
    return () => clearInterval(timer)
  }, [checkConnection, checkLocalService, baseUrl])

  const openDesigner = (req: DesignerRequest) => {
    setDesignerReq(req)
    setTab('designer')
  }

  const closeDesigner = () => {
    setDesignerReq(null)
    setTab('workbench')
  }

  // 迭代 92（#150 F-02）：导航切 tab 统一经此入口——设计器在编辑（dirty）时由其注册的守卫拦截：
  // 无未保存更改守卫直接放行（AC-02），有则弹三选 Modal，用户选择后再执行本次切换（AC-01）。
  const switchTab = (id: TabId) => {
    if (id === tab) return
    if (tab === 'designer' && designerReq && designerLeaveRef.current) {
      designerLeaveRef.current(() => setTab(id))
      return
    }
    setTab(id)
  }

  return (
    <div className="app">
      <div className="app-body">
        <nav className="nav" aria-label="主导航">
          <div className="nav-logo" title="LabelFrame 标签打印">
            <LabelLogo size={24} />
          </div>
          <div className="nav-tabs">
            {TABS.map((t) => (
              <button
                key={t.id}
                className={'nav-tab' + (tab === t.id ? ' active' : '')}
                onClick={() => switchTab(t.id)}
                title={t.label}
              >
                <Icon name={t.icon} />
                <span>{t.label}</span>
              </button>
            ))}
          </div>
          {/* 迭代 80（#128 决议 2「三名义」）：client 构建指向「本机打印服务」（页面来源的本机后台服务），
              不再沿用「服务端」一词（评审 #114 B-9 一词三义）；server 构建维持服务端自身连通（含义②语境） */}
          <div
            className="nav-foot"
            title={
              isServerUi
                ? app.connected
                  ? '服务端已连接'
                  : '服务端未连接'
                : app.localServiceUp
                  ? '本机打印服务：运行中'
                  : '本机打印服务：未运行'
            }
          >
            <span className={'status-dot' + ((isServerUi ? app.connected : app.localServiceUp) ? ' on' : '')} />
          </div>
        </nav>

        <main className="main">
          {tab === 'workbench' && <Workbench onOpenDesigner={openDesigner} />}
          {tab === 'designer' && designerReq && (
            <Designer key={designerReq.name ?? 'new'} request={designerReq} onClose={closeDesigner} registerLeaveGuard={registerDesignerLeave} />
          )}
          {tab === 'designer' && !designerReq && <DesignerEmpty onNew={() => openDesigner({ kind: 'new' })} />}
          {tab === 'data' && <DataPrint />}
          {tab === 'devices' && <Devices />}
          {tab === 'jobs' && <JobHistory />}
          {tab === 'packages' && <DownloadCenter />}
          {tab === 'plugin-packages' && <PluginPackages />}
          {tab === 'settings' && <Settings />}
        </main>
      </div>

      <footer className="statusbar">
        {/* 迭代 80（#128 决议 2「三名义」①）：状态栏呈现本机打印服务运行状态——
            client 构建 = 页面来源的本机 WinHost；server 构建此段不渲染（下方 meta 显示服务端地址） */}
        {!isServerUi && (
          <span className={'conn' + (app.localServiceUp ? ' on' : ' off')}>
            <span className={'status-dot' + (app.localServiceUp ? ' on' : '')} />
            {app.localServiceUp ? '本机打印服务：运行中' : '本机打印服务：未运行'}
          </span>
        )}
        <span className="msg">{app.statusMsg}</span>
        <span className="meta">
          {isServerUi ? (
            // 迭代 20：Server UI 状态栏显示服务端地址（页面 origin）与 UI 模式；无打印机相关内容
            // 迭代 73（#108）：「同源」开发者术语改为直接展示地址与服务端管理界面标识
            <span className="mono" title={window.location.origin}>
              {window.location.origin} · 服务端管理界面
            </span>
          ) : (
            <>
              <span className="mono">{app.baseUrl}</span>
              {/* 迭代 20：客户端状态栏显示本机 IP（/api/host/config.ips，多 IP 逗号分隔全部）；
                  迭代 80：随「本机打印服务」运行状态显示（本机事实不依赖服务端地址连通性） */}
              {app.localServiceUp && app.hostIps.length > 0 && (
                <span className="mono" title={app.hostIps.join(', ')}>
                  本机 IP：{truncateIps(app.hostIps)}
                </span>
              )}
              {/* 迭代 22 §2.1：客户端状态栏显示本机设备名称（/api/host/config.deviceName，与本机 IP 并列） */}
              {app.localServiceUp && app.hostDeviceName && <span className="mono">本机：{app.hostDeviceName}</span>}
            </>
          )}
          <button className="btn sm ghost" onClick={() => app.setDrawerOpen(!app.drawerOpen)}>
            <Icon name="logs" size={13} />
            日志
          </button>
        </span>
      </footer>

      {app.drawerOpen && (
        <div className="log-drawer">
          <div className="log-head">
            <span>运行日志</span>
            <span className="spacer" />
            <button className="btn sm ghost" style={{ color: '#8b96a3' }} onClick={app.clearLogs}>
              清空
            </button>
            <button className="btn sm ghost" style={{ color: '#8b96a3' }} onClick={() => app.setDrawerOpen(false)}>
              收起
            </button>
          </div>
          <div className="log-body">
            {app.logs.map((l, i) => (
              <div key={i}>
                <span className="t">{l.time}</span>
                {l.msg}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

function DesignerEmpty({ onNew }: { onNew: () => void }) {
  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">设计器</div>
      </div>
      <div className="empty" style={{ flex: 1 }}>
        <Icon name="designer" />
        <div className="empty-title">尚未打开模板</div>
        <div className="hint">从工作台新建或编辑模板后进入设计器</div>
        <button className="btn primary" onClick={onNew}>
          <Icon name="plus" size={13} />
          新建模板
        </button>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <AppProvider>
      <Shell />
    </AppProvider>
  )
}
