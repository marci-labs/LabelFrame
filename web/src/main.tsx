import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'
import App from './App.tsx'
import { ErrorBoundary } from './components/ErrorBoundary.tsx'
import { installGlobalErrorLogging } from './lib/errorLogging.ts'

// 全局错误兜底（迭代 51）：先装监听再挂载——挂载前后 / 渲染中的异常都有 console.error 留痕；
// 渲染异常由最外层 ErrorBoundary 兜底为错误页（仅文案 + 重新加载按钮，决策 #106）
installGlobalErrorLogging()

createRoot(document.getElementById('root')!).render(
  <ErrorBoundary>
    <StrictMode>
      <App />
    </StrictMode>
  </ErrorBoundary>,
)
