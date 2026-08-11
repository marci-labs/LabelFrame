import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
  build: {
    outDir: 'dist',
  },
  test: {
    environment: 'node',
    setupFiles: ['./vitest.setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    // 迭代 20（Y4）：显式注入 VITE_UI_MODE——默认 client 分支；可 `VITE_UI_MODE=server pnpm test`
    // 整仓验证 server 构建分支（组件级 server 分支用例通过 vi.mock ../lib/uiMode 注入）。
    define: {
      'import.meta.env.VITE_UI_MODE': JSON.stringify(process.env.VITE_UI_MODE === 'server' ? 'server' : 'client'),
    },
  },
})
