// 应用框架：左侧主导航（state 切换，无路由库）+ 底部状态栏 + 日志抽屉

import { useEffect, useState } from 'react'
import { AppProvider, useApp } from './state/AppContext'
import { Icon, LabelLogo } from './components/Icon'
import type { IconName } from './components/Icon'
import type { DesignerRequest, TabId } from './state/types'
import { Workbench } from './pages/Workbench'
import { Designer } from './pages/Designer'
import { DataPrint } from './pages/DataPrint'
import { PdaLogs } from './pages/PdaLogs'
import { JobHistory } from './pages/JobHistory'
import { Settings } from './pages/Settings'

const TABS: { id: TabId; label: string; icon: IconName }[] = [
  { id: 'workbench', label: '工作台', icon: 'workbench' },
  { id: 'designer', label: '设计器', icon: 'designer' },
  { id: 'data', label: '数据与打印', icon: 'data' },
  { id: 'jobs', label: '作业历史', icon: 'history' },
  { id: 'logs', label: 'PDA 日志', icon: 'logs' },
  { id: 'settings', label: '设置', icon: 'settings' },
]

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
          <span className="mono">{app.baseUrl}</span>
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
