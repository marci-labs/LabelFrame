import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'node',
    setupFiles: ['./vitest.setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    // 迭代 20（Y4）：显式注入 VITE_UI_MODE——默认 client 分支；可 `VITE_UI_MODE=server pnpm test`
    // 整仓验证 server 构建分支（组件级 server 分支用例通过 vi.mock ../lib/uiMode 注入）。
    define: {
      'import.meta.env.VITE_UI_MODE': JSON.stringify(process.env.VITE_UI_MODE === 'server' ? 'server' : 'client'),
    },
    // 迭代 96（#183）：单测超时 5s → 20s——CI 高负载 runner 上偶发墙钟停顿会击穿同步断言用例的
    // 默认超时（实证：Settings「三分组渲染」全同步断言 5000ms 超时）；同时容纳挂载链显式等待的
    // 8s 上限（超时口径见各测试文件 MOUNT_WAIT 注释）。挂起用例仍会失败，只是判死更晚、报错更准。
    testTimeout: 20_000,
  },
})
