import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 迭代 20（Y1）：dev 联调按 UI 构建模式分支——server 模式（VITE_UI_MODE=server）proxy 指向服务端
// 127.0.0.1:53961（Server UI 同源相对路径 '' 打到 vite dev 再被转发到服务端，避免误转发到 WinHost）；
// client 模式维持 53960（本机 LabelFrame Client）。
const uiMode = process.env.VITE_UI_MODE === 'server' ? 'server' : 'client'
const proxyTarget = uiMode === 'server' ? 'http://127.0.0.1:53961' : 'http://127.0.0.1:53960'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // dev 联调配套（迭代 15 附九第 5 条）：/healthz 为根路径、非 /api 前缀，必须同时覆盖
      '/api': proxyTarget,
      '/healthz': proxyTarget,
    },
  },
  build: {
    outDir: 'dist',
  },
})
