import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // dev 联调配套（迭代 15 附九第 5 条）：/healthz 为根路径、非 /api 前缀，必须同时覆盖
      '/api': 'http://127.0.0.1:53960',
      '/healthz': 'http://127.0.0.1:53960',
    },
  },
  build: {
    outDir: 'dist',
  },
})
