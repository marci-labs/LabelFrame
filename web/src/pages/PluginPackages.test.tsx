// @vitest-environment jsdom
// 迭代 23 §2.1/§7.3：Server UI「插件管理」页——列表（名称 / 版本 / pluginId / 大小 / 修改时间 / valid 状态，invalid 红标 + 原因）、
// 上传（multipart + 64MB 预检）、下载（pluginPackageDownloadUrl）、删除（确认后调用 + 刷新）；空态 / 加载失败。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { PluginPackages } from './PluginPackages'

const mocks = vi.hoisted(() => ({
  server: {
    listPluginPackages: vi.fn(),
    uploadPluginPackage: vi.fn(),
    deletePluginPackage: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  pluginPackageDownloadUrl: (fileName: string) => `/api/plugin-packages/${encodeURIComponent(fileName)}`,
  pluginPackageSizeError: (sizeBytes: number) => (sizeBytes > 64 * 1024 * 1024 ? '插件包超过 64MB 大小上限，无法上传 / 安装。' : null),
}))

const PKGS = [
  { fileName: 'sample.lfplugin', pluginId: 'sample', name: '示例插件', version: '1.0.0', description: '', sizeBytes: 4096, modifiedAt: '2026-08-17T10:00:00Z', valid: true },
  { fileName: 'broken.lfplugin', pluginId: undefined, name: undefined, version: undefined, sizeBytes: 128, modifiedAt: '2026-08-17T09:00:00Z', valid: false, invalidReason: '缺少根 manifest.json' },
]

beforeEach(() => {
  vi.clearAllMocks()
  mocks.server.listPluginPackages.mockResolvedValue(PKGS)
  mocks.server.uploadPluginPackage.mockResolvedValue({ fileName: 'sample.lfplugin', valid: true })
  mocks.server.deletePluginPackage.mockResolvedValue(undefined)
})

afterEach(() => {
  cleanup()
})

describe('插件管理页（迭代 23 §2.1）', () => {
  it('列表渲染：名称 / 版本 / pluginId / 大小 / 状态徽标 / 下载链接（同源相对路径）', async () => {
    render(<PluginPackages />)
    expect(await screen.findByText('示例插件')).toBeTruthy()
    expect(screen.getByText('sample')).toBeTruthy()
    expect(screen.getByText('1.0.0')).toBeTruthy()
    expect(screen.getByText('4.0 KB')).toBeTruthy()
    expect(screen.getByText('正常')).toBeTruthy()
    const links = screen.getAllByRole('link', { name: /下载/ })
    expect(links[0].getAttribute('href')).toBe('/api/plugin-packages/sample.lfplugin')
    expect(links[1].getAttribute('href')).toBe('/api/plugin-packages/broken.lfplugin')
  })

  it('invalid 条目：红标「解析失败」+ 原因，仍可删除', async () => {
    render(<PluginPackages />)
    expect(await screen.findByText('解析失败')).toBeTruthy()
    expect(screen.getByText('缺少根 manifest.json')).toBeTruthy()
    // invalid 行同样有删除按钮（共 2 行）
    expect(screen.getAllByRole('button', { name: /删除/ }).length).toBe(2)
  })

  it('空列表：空态提示上传与目录直放两种方式', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([])
    render(<PluginPackages />)
    expect(await screen.findByText('暂无插件包')).toBeTruthy()
    expect(screen.getByText(/plugin-packages/)).toBeTruthy()
    expect(screen.getByRole('button', { name: '上传插件包' })).toBeTruthy()
  })

  it('加载失败：显示错误信息', async () => {
    mocks.server.listPluginPackages.mockRejectedValue(new Error('network down'))
    render(<PluginPackages />)
    expect(await screen.findByText(/获取插件包列表失败/)).toBeTruthy()
  })

  it('上传：选择文件 → uploadPluginPackage(file) + 列表刷新', async () => {
    render(<PluginPackages />)
    await screen.findByText('示例插件')
    const file = new File(['x'], 'vendor.lfplugin')
    fireEvent.change(document.getElementById('pluginPkgFile')!, { target: { files: [file] } })
    await waitFor(() => expect(mocks.server.uploadPluginPackage).toHaveBeenCalledWith(file))
    await waitFor(() => expect(mocks.server.listPluginPackages).toHaveBeenCalledTimes(2))
    expect(await screen.findByText(/已上传/)).toBeTruthy()
  })

  it('上传超 64MB：前端预检提示，不调用 uploadPluginPackage', async () => {
    render(<PluginPackages />)
    await screen.findByText('示例插件')
    const file = new File(['x'], 'huge.lfplugin')
    Object.defineProperty(file, 'size', { value: 64 * 1024 * 1024 + 1 })
    fireEvent.change(document.getElementById('pluginPkgFile')!, { target: { files: [file] } })
    expect(await screen.findByText(/超过 64MB 大小上限/)).toBeTruthy()
    expect(mocks.server.uploadPluginPackage).not.toHaveBeenCalled()
  })

  it('删除：确认后调用 deletePluginPackage + 刷新；取消不调用', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockImplementation(() => true)
    render(<PluginPackages />)
    await screen.findByText('示例插件')

    fireEvent.click(screen.getAllByRole('button', { name: /删除/ })[0])
    await waitFor(() => expect(mocks.server.deletePluginPackage).toHaveBeenCalledWith('sample.lfplugin'))
    await waitFor(() => expect(mocks.server.listPluginPackages).toHaveBeenCalledTimes(2))

    confirmSpy.mockImplementation(() => false)
    fireEvent.click(screen.getAllByRole('button', { name: /删除/ })[1])
    expect(mocks.server.deletePluginPackage).toHaveBeenCalledTimes(1)
    confirmSpy.mockRestore()
  })
})
