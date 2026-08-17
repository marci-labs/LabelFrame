// @vitest-environment jsdom
// 迭代 22 §2.3：Server UI「客户端下载」页——列表（文件名 / 大小 / 修改时间 / 下载链接）、
// 上传（multipart）、删除（确认后调用 + 刷新）；空态提示目录直放与上传两种方式。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ClientPackages } from './ClientPackages'

const mocks = vi.hoisted(() => ({
  server: {
    listClientPackages: vi.fn(),
    uploadClientPackage: vi.fn(),
    deleteClientPackage: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  clientPackageDownloadUrl: (fileName: string) => `/api/client-packages/${encodeURIComponent(fileName)}`,
}))

const PKGS = [
  { fileName: 'LabelFrame.Client-0.18.0.msi', sizeBytes: 42 * 1024 * 1024, modifiedAt: '2026-08-17T10:00:00Z', url: '/api/client-packages/LabelFrame.Client-0.18.0.msi' },
  { fileName: 'LabelFrame.Client-linux.zip', sizeBytes: 1024, modifiedAt: '2026-08-17T09:00:00Z' },
]

beforeEach(() => {
  vi.clearAllMocks()
  mocks.server.listClientPackages.mockResolvedValue(PKGS)
  mocks.server.uploadClientPackage.mockResolvedValue([])
  mocks.server.deleteClientPackage.mockResolvedValue(undefined)
})

afterEach(() => {
  cleanup()
})

describe('客户端下载页（迭代 22 §2.3）', () => {
  it('列表渲染：文件名 / 大小 / 修改时间 / 下载链接（同源相对路径）', async () => {
    render(<ClientPackages />)
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame.Client-linux.zip')).toBeTruthy()
    expect(screen.getByText('42.0 MB')).toBeTruthy()
    expect(screen.getByText('1.0 KB')).toBeTruthy()
    const links = screen.getAllByRole('link', { name: /下载/ })
    expect(links[0].getAttribute('href')).toBe('/api/client-packages/LabelFrame.Client-0.18.0.msi')
    expect(links[1].getAttribute('href')).toBe('/api/client-packages/LabelFrame.Client-linux.zip')
  })

  it('空列表：空态提示上传与目录直放两种方式', async () => {
    mocks.server.listClientPackages.mockResolvedValue([])
    render(<ClientPackages />)
    expect(await screen.findByText('暂无安装包')).toBeTruthy()
    expect(screen.getByText(/client-packages/)).toBeTruthy()
    expect(screen.getByRole('button', { name: '上传安装包' })).toBeTruthy()
  })

  it('加载失败：显示错误信息', async () => {
    mocks.server.listClientPackages.mockRejectedValue(new Error('network down'))
    render(<ClientPackages />)
    expect(await screen.findByText(/获取安装包列表失败/)).toBeTruthy()
  })

  it('上传：选择文件 → uploadClientPackage(file) + 列表刷新', async () => {
    render(<ClientPackages />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')
    const file = new File(['x'], 'LabelFrame.Client-0.19.0.msi')
    fireEvent.change(document.getElementById('pkgFile')!, { target: { files: [file] } })
    await waitFor(() => expect(mocks.server.uploadClientPackage).toHaveBeenCalledWith(file))
    // 上传后重新拉取列表
    await waitFor(() => expect(mocks.server.listClientPackages).toHaveBeenCalledTimes(2))
    expect(await screen.findByText(/已上传/)).toBeTruthy()
  })

  it('删除：确认后调用 deleteClientPackage + 刷新；取消不调用', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockImplementation(() => true)
    render(<ClientPackages />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    fireEvent.click(screen.getAllByRole('button', { name: /删除/ })[0])
    await waitFor(() => expect(mocks.server.deleteClientPackage).toHaveBeenCalledWith('LabelFrame.Client-0.18.0.msi'))
    await waitFor(() => expect(mocks.server.listClientPackages).toHaveBeenCalledTimes(2))

    confirmSpy.mockImplementation(() => false)
    fireEvent.click(screen.getAllByRole('button', { name: /删除/ })[1])
    expect(mocks.server.deleteClientPackage).toHaveBeenCalledTimes(1)
    confirmSpy.mockRestore()
  })
})
