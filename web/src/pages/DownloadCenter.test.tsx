// @vitest-environment jsdom
// 迭代 59（决策 #119）：Server UI「下载中心」页——客户端（client-packages）与 PDA（pda-packages）分区统一展示：
// 列表（时间倒序最新在上）/ 上传（各分区独立 multipart）/ 下载链接 / 删除（确认）；
// 每条目旁二维码（title = 页面 origin + 下载相对路径，即局域网完整 URL）；
// PDA 区常驻 Android「未知来源 / 安装未知应用」授权步骤文案；空态提示目录直放与上传两种方式。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { DownloadCenter } from './DownloadCenter'

const mocks = vi.hoisted(() => ({
  server: {
    listClientPackages: vi.fn(),
    uploadClientPackage: vi.fn(),
    deleteClientPackage: vi.fn(),
    listPdaPackages: vi.fn(),
    uploadPdaPackage: vi.fn(),
    deletePdaPackage: vi.fn(),
  },
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  clientPackageDownloadUrl: (fileName: string) => `/api/client-packages/${encodeURIComponent(fileName)}`,
  pdaPackageDownloadUrl: (fileName: string) => `/api/pda-packages/${encodeURIComponent(fileName)}`,
}))

const CLIENT_PKGS = [
  { fileName: 'LabelFrame.Client-0.18.0.msi', sizeBytes: 42 * 1024 * 1024, modifiedAt: '2026-08-17T10:00:00Z', url: '/api/client-packages/LabelFrame.Client-0.18.0.msi' },
  { fileName: 'LabelFrame.Client-linux.zip', sizeBytes: 1024, modifiedAt: '2026-08-17T09:00:00Z' },
]

const PDA_PKGS = [
  { fileName: 'LabelFrame-AndroidHost-0.26.0.apk', sizeBytes: 22 * 1024 * 1024, modifiedAt: '2026-09-10T08:00:00Z', url: '/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk' },
  { fileName: 'LabelFrame-AndroidHost-0.25.0.apk', sizeBytes: 24 * 1024 * 1024, modifiedAt: '2026-09-01T08:00:00Z' },
]

beforeEach(() => {
  vi.clearAllMocks()
  mocks.server.listClientPackages.mockResolvedValue(CLIENT_PKGS)
  mocks.server.uploadClientPackage.mockResolvedValue([])
  mocks.server.deleteClientPackage.mockResolvedValue(undefined)
  mocks.server.listPdaPackages.mockResolvedValue(PDA_PKGS)
  mocks.server.uploadPdaPackage.mockResolvedValue([])
  mocks.server.deletePdaPackage.mockResolvedValue(undefined)
})

afterEach(() => {
  cleanup()
})

/** 列表顺序断言辅助：按文本出现顺序取行号（同一文本只应出现一次）。 */
function indexOfText(text: string): number {
  const rows = Array.from(document.querySelectorAll('tbody tr'))
  return rows.findIndex((tr) => tr.textContent?.includes(text))
}

describe('下载中心页（迭代 59 决策 #119）', () => {
  it('双分区列表渲染：客户端（MSI / zip）与 PDA（APK）条目、大小、下载链接（同源相对路径）', async () => {
    render(<DownloadCenter />)
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame.Client-linux.zip')).toBeTruthy()
    expect(screen.getByText('LabelFrame-AndroidHost-0.26.0.apk')).toBeTruthy()
    expect(screen.getByText('LabelFrame-AndroidHost-0.25.0.apk')).toBeTruthy()
    expect(screen.getByText('42.0 MB')).toBeTruthy()
    expect(screen.getByText('22.0 MB')).toBeTruthy()

    const clientLink = screen.getByTitle('下载 LabelFrame.Client-0.18.0.msi')
    expect(clientLink.tagName).toBe('A')
    expect(clientLink.getAttribute('href')).toBe('/api/client-packages/LabelFrame.Client-0.18.0.msi')
    const pdaLink = screen.getByTitle('下载 LabelFrame-AndroidHost-0.26.0.apk')
    expect(pdaLink.tagName).toBe('A')
    expect(pdaLink.getAttribute('href')).toBe('/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk')
  })

  it('时间排序：各分区内最新在上（乱序输入按修改时间倒序）', async () => {
    mocks.server.listClientPackages.mockResolvedValue([...CLIENT_PKGS].reverse())
    mocks.server.listPdaPackages.mockResolvedValue([...PDA_PKGS].reverse())
    render(<DownloadCenter />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    expect(indexOfText('LabelFrame.Client-0.18.0.msi')).toBeLessThan(indexOfText('LabelFrame.Client-linux.zip'))
    expect(indexOfText('LabelFrame-AndroidHost-0.26.0.apk')).toBeLessThan(indexOfText('LabelFrame-AndroidHost-0.25.0.apk'))
  })

  it('二维码：每条目渲染二维码（title = 页面 origin + 下载路径的局域网完整 URL）', async () => {
    render(<DownloadCenter />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    const origin = window.location.origin
    const clientQr = screen.getByTitle(`${origin}/api/client-packages/LabelFrame.Client-0.18.0.msi`)
    expect(clientQr.tagName).toBe('IMG')
    expect(clientQr.getAttribute('src')).toMatch(/^data:image\/gif;base64,/)
    const pdaQr = screen.getByTitle(`${origin}/api/pda-packages/LabelFrame-AndroidHost-0.26.0.apk`)
    expect(pdaQr.tagName).toBe('IMG')
    expect(pdaQr.getAttribute('src')).toMatch(/^data:image\/gif;base64,/)
    // 两个分区共 4 条条目 = 4 张二维码
    expect(screen.getAllByAltText('扫码下载二维码')).toHaveLength(4)
  })

  it('Android 授权步骤文案：PDA 区展示「未知来源 / 安装未知应用」提示', async () => {
    render(<DownloadCenter />)
    await screen.findByText('LabelFrame-AndroidHost-0.26.0.apk')
    expect(screen.getByText(/未知来源/)).toBeTruthy()
    expect(screen.getByText(/安装未知应用/)).toBeTruthy()
    expect(screen.getByText(/同一局域网/)).toBeTruthy()
  })

  it('空列表：两分区各自空态提示（client-packages / pda-packages 目录直放说明）', async () => {
    mocks.server.listClientPackages.mockResolvedValue([])
    mocks.server.listPdaPackages.mockResolvedValue([])
    render(<DownloadCenter />)
    expect(await screen.findByText('暂无客户端安装包')).toBeTruthy()
    expect(screen.getByText('暂无 PDA 安装包')).toBeTruthy()
    expect(screen.getByRole('button', { name: /上传客户端安装包/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /上传 APK/ })).toBeTruthy()
  })

  it('加载失败：显示错误信息', async () => {
    mocks.server.listClientPackages.mockRejectedValue(new Error('network down'))
    render(<DownloadCenter />)
    expect(await screen.findByText(/获取安装包列表失败/)).toBeTruthy()
  })

  it('上传：客户端选 MSI → uploadClientPackage；PDA 选 APK → uploadPdaPackage；均刷新列表', async () => {
    render(<DownloadCenter />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    const msi = new File(['x'], 'LabelFrame.Client-0.19.0.msi')
    fireEvent.change(document.getElementById('clientPkgFile')!, { target: { files: [msi] } })
    await waitFor(() => expect(mocks.server.uploadClientPackage).toHaveBeenCalledWith(msi))

    const apk = new File(['y'], 'LabelFrame-AndroidHost-0.26.0.apk')
    fireEvent.change(document.getElementById('pdaPkgFile')!, { target: { files: [apk] } })
    await waitFor(() => expect(mocks.server.uploadPdaPackage).toHaveBeenCalledWith(apk))

    // 挂载 1 次 + 两次上传各刷新 1 次 = 3 次
    await waitFor(() => expect(mocks.server.listClientPackages).toHaveBeenCalledTimes(3))
    await waitFor(() => expect(mocks.server.listPdaPackages).toHaveBeenCalledTimes(3))
    expect(await screen.findByText(/APK「LabelFrame-AndroidHost-0\.26\.0\.apk」已上传/)).toBeTruthy()
  })

  it('删除：PDA 条目确认后调 deletePdaPackage + 刷新；取消不调用', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockImplementation(() => true)
    render(<DownloadCenter />)
    await screen.findByText('LabelFrame-AndroidHost-0.26.0.apk')

    const delButtons = screen.getAllByRole('button', { name: /删除/ })
    const pdaDelete = delButtons.find((b) => b.closest('tr')?.textContent?.includes('AndroidHost-0.26.0'))
    fireEvent.click(pdaDelete!)
    await waitFor(() => expect(mocks.server.deletePdaPackage).toHaveBeenCalledWith('LabelFrame-AndroidHost-0.26.0.apk'))
    await waitFor(() => expect(mocks.server.listPdaPackages).toHaveBeenCalledTimes(2))

    confirmSpy.mockImplementation(() => false)
    fireEvent.click(pdaDelete!)
    expect(mocks.server.deletePdaPackage).toHaveBeenCalledTimes(1)
    confirmSpy.mockRestore()
  })

  it('删除：客户端条目确认后调 deleteClientPackage', async () => {
    vi.spyOn(window, 'confirm').mockImplementation(() => true)
    render(<DownloadCenter />)
    await screen.findByText('LabelFrame.Client-0.18.0.msi')

    const delButtons = screen.getAllByRole('button', { name: /删除/ })
    const clientDelete = delButtons.find((b) => b.closest('tr')?.textContent?.includes('Client-linux'))
    fireEvent.click(clientDelete!)
    await waitFor(() => expect(mocks.server.deleteClientPackage).toHaveBeenCalledWith('LabelFrame.Client-linux.zip'))
  })
})
