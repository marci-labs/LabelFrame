// 应用框架：左侧主导航（state 切换，无路由库）+ 底部状态栏 + 日志抽屉
// 迭代 20：双构建（VITE_UI_MODE）——server 构建菜单移除设置与打印机相关内容，新增「在线设备」，
// 日志页更名「设备日志」；状态栏 server 显示服务端地址（同源）与 UI 模式、client 显示本机 IP。

import { useEffect, useState } from 'react'
import { AppProvider, useApp } from './state/AppContext'
import { Icon, LabelLogo } from './components/Icon'
import type { IconName } from './components/Icon'
import type { DesignerRequest, TabId } from './state/types'
import { isServerUi } from './lib/uiMode'
import { Workbench } from './pages/Workbench'
import { Designer } from './pages/Designer'
import { DataPrint } from './pages/DataPrint'
import { Devices } from './pages/Devices'
import { PdaLogs } from './pages/PdaLogs'
import { JobHistory } from './pages/JobHistory'
import { Settings } from './pages/Settings'

const TABS: { id: TabId; label: string; icon: IconName }[] = isServerUi
  ? [
      { id: 'workbench', label: '工作台', icon: 'workbench' },
      { id: 'designer', label: '设计器', icon: 'designer' },
      { id: 'data', label: '数据与打印', icon: 'data' },
      { id: 'devices', label: '在线设备', icon: 'grid' },
      { id: 'jobs', label: '作业历史', icon: 'history' },
      // 迭代 20（Y5）：Server 版命名「设备日志」（集中查看全部设备日志）；client 版保持「PDA 日志」
      { id: 'logs', label: '设备日志', icon: 'logs' },
    ]
  : [
      { id: 'workbench', label: '工作台', icon: 'workbench' },
      { id: 'designer', label: '设计器', icon: 'designer' },
      { id: 'data', label: '数据与打印', icon: 'data' },
      { id: 'jobs', label: '作业历史', icon: 'history' },
      { id: 'logs', label: 'PDA 日志', icon: 'logs' },
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
  const app = useApp()

  useEffect(() => {
    void app.checkConnection()
    // 周期探测连接（10s），后端重启后状态自动恢复
    const timer = setInterval(() => void app.checkConnection(), 10000)
    return () => clearInterval(timer)
  }, [app.checkConnection, app.baseUrl])

  const openDesigner = (req: DesignerRequest) => {
    setDesignerReq(req)
    setTab('designer')
  }

  const closeDesigner = () => {
    setDesignerReq(null)
    setTab('workbench')
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
                onClick={() => setTab(t.id)}
                title={t.label}
              >
                <Icon name={t.icon} />
                <span>{t.label}</span>
              </button>
            ))}
          </div>
          <div className="nav-foot" title={app.connected ? '服务端已连接' : '服务端未连接（单机模式可用）'}>
            <span className={'status-dot' + (app.connected ? ' on' : '')} />
          </div>
        </nav>

        <main className="main">
          {tab === 'workbench' && <Workbench onOpenDesigner={openDesigner} />}
          {tab === 'designer' && designerReq && <Designer key={designerReq.name ?? 'new'} request={designerReq} onClose={closeDesigner} />}
          {tab === 'designer' && !designerReq && <DesignerEmpty onNew={() => openDesigner({ kind: 'new' })} />}
          {tab === 'data' && <DataPrint />}
          {tab === 'devices' && <Devices />}
          {tab === 'jobs' && <JobHistory />}
          {tab === 'logs' && <PdaLogs />}
          {tab === 'settings' && <Settings />}
        </main>
      </div>

      <footer className="statusbar">
        <span className={'conn' + (app.connected ? ' on' : ' off')}>
          <span className={'status-dot' + (app.connected ? ' on' : '')} />
          {app.connected ? '服务端已连接' : '服务端未连接（单机模式可用）'}
        </span>
        <span className="msg">{app.statusMsg}</span>
        <span className="meta">
          {isServerUi ? (
            // 迭代 20：Server UI 状态栏显示服务端地址（页面 origin /「同源」）与 UI 模式；无打印机相关内容
            <span className="mono" title={window.location.origin}>
              同源（{window.location.origin}）· Server 管理界面
            </span>
          ) : (
            <>
              <span className="mono">{app.baseUrl}</span>
              {/* 迭代 20：客户端状态栏在服务端已连接时显示本机 IP（/api/host/config.ips，多 IP 逗号分隔全部） */}
              {app.connected && app.hostIps.length > 0 && (
                <span className="mono" title={app.hostIps.join(', ')}>
                  本机 IP：{truncateIps(app.hostIps)}
                </span>
              )}
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
