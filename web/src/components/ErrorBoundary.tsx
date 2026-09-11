// 全局错误兜底（迭代 51，决策 #106）：
// ErrorBoundary：渲染异常错误页（仅文案 + 重新加载按钮，不附错误摘要——避免透出内部细节）；
// 开发线索由 componentDidCatch 的 console.error 承担（错误对象 + 组件栈完整留痕）；
// window.onerror / unhandledrejection 监听见 lib/errorLogging.ts（main.tsx 挂载前安装）。

import { Component } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import { Icon } from './Icon'

interface ErrorBoundaryProps {
  children: ReactNode
}

interface ErrorBoundaryState {
  hasError: boolean
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true }
  }

  componentDidCatch(error: unknown, errorInfo: ErrorInfo): void {
    console.error('[LabelFrame][ErrorBoundary] 界面渲染异常', error, errorInfo.componentStack)
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="page">
          <div className="empty" style={{ flex: 1 }}>
            <Icon name="alert" />
            <div className="empty-title">界面出现异常</div>
            <div className="hint">
              页面渲染遇到了问题，重新加载通常可以恢复。
            </div>
            <button className="btn primary" onClick={() => window.location.reload()}>
              <Icon name="refresh" size={13} />
              重新加载
            </button>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
