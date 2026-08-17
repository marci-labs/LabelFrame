// @vitest-environment jsdom
// 迭代 18 F2-F4：设置页三分组——服务端地址（机器级配置，保存即生效）/ 连接方式（本机 Client）/ 打印机。
// mock 覆盖组件树用到的全部 client 方法（含 AppContext 启动链：getHostConfig / getTransport / healthz）。

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { ApiError } from '../lib/api/types'
import type { InstalledPluginInfo, PluginPackageInfo } from '../lib/api/types'
import { AppProvider } from '../state/AppContext'
import { Settings } from './Settings'

const mocks = vi.hoisted(() => ({
  server: {
    healthz: vi.fn(),
    listClientPackages: vi.fn(),
    listPluginPackages: vi.fn(),
    downloadPluginPackage: vi.fn(),
  },
  local: {
    getHostConfig: vi.fn(),
    getTransport: vi.fn(),
    setTransport: vi.fn(),
    testTransport: vi.fn(),
    getPrinterStatus: vi.fn(),
    testPrinter: vi.fn(),
    setHostConfig: vi.fn(),
    listInstalledPlugins: vi.fn(),
    installPlugin: vi.fn(),
    uninstallPlugin: vi.fn(),
  },
  probeHealthz: vi.fn(),
}))

vi.mock('../lib/api/client', () => ({
  serverApi: mocks.server,
  localApi: mocks.local,
  setServerBaseUrl: vi.fn(),
  probeHealthz: mocks.probeHealthz,
  clientPackageDownloadUrl: (fileName: string) => `http://127.0.0.1:53961/api/client-packages/${encodeURIComponent(fileName)}`,
  pluginPackageDownloadUrl: (fileName: string) => `http://127.0.0.1:53961/api/plugin-packages/${encodeURIComponent(fileName)}`,
}))
// 迭代 20：本文件为 client 构建语义用例，显式注入 client 分支（VITE_UI_MODE=server 整仓测试时保持稳定）
vi.mock('../lib/uiMode', () => ({ UI_MODE: 'client', isServerUi: false }))

function renderSettings() {
  return render(
    <AppProvider>
      <Settings />
    </AppProvider>,
  )
}

/** 按分组限定查询（「测试连接」按钮在服务端地址与连接方式两组各有一个；「服务端地址」文本在 panel-head 与 label 各一处）。 */
function withinSection(title: string): HTMLElement {
  const el = title === '服务端地址' ? screen.getByLabelText('服务端地址') : screen.getByText(title)
  return el.closest('section') as HTMLElement
}

const HOST_CONFIG = { serverUrl: 'http://127.0.0.1:53961', deviceId: 'PC-1', deviceName: 'PC-1' }

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  window.sessionStorage.clear()
  mocks.server.healthz.mockResolvedValue({ service: 'LabelFrame.Server', status: 'ok' })
  mocks.server.listClientPackages.mockResolvedValue([])
  mocks.server.listPluginPackages.mockResolvedValue([])
  mocks.server.downloadPluginPackage.mockResolvedValue({ blob: new Blob(['pkg']), filename: 'sample-1.0.0.lfplugin' })
  mocks.local.listInstalledPlugins.mockResolvedValue([])
  mocks.local.installPlugin.mockResolvedValue({
    ok: true,
    message: '插件「示例插件 1.0.0」已安装，重启客户端后生效。',
    plugin: { pluginId: 'sample', name: '示例插件', version: '1.0.0', loaded: false, source: 'package' },
  })
  mocks.local.uninstallPlugin.mockResolvedValue({ ok: true, message: '插件「sample」已卸载，重启客户端后生效。' })
  mocks.local.getHostConfig.mockResolvedValue(HOST_CONFIG)
  mocks.local.getTransport.mockResolvedValue({ mode: 'Log', params: {} })
  mocks.local.setTransport.mockResolvedValue({ ok: true, message: '已切换到 TCP。', config: { mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 } } })
  mocks.local.testTransport.mockResolvedValue({ ok: true, message: '测试通过。', config: { mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 } } })
  mocks.local.getPrinterStatus.mockResolvedValue({ isOnline: true, isPaperOut: false, isPaused: false, message: '就绪' })
  mocks.local.testPrinter.mockResolvedValue({ sent: true, bytes: 128 })
  mocks.local.setHostConfig.mockResolvedValue(undefined)
  mocks.probeHealthz.mockResolvedValue(true)
})

afterEach(() => {
  cleanup()
})

describe('设置页三分组（迭代 18）', () => {
  it('渲染服务端地址 / 连接方式 / 打印机三个分组', async () => {
    renderSettings()
    expect(screen.getByLabelText('服务端地址')).toBeTruthy()
    expect(screen.getByText('连接方式')).toBeTruthy()
    expect(screen.getByText('打印机')).toBeTruthy()
    // 连接方式模式单选（Log / TCP / Windows 驱动 / Zebra）
    expect(screen.getByRole('radio', { name: /Log/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /TCP/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /Windows 驱动/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /Zebra/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /保存并应用/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /测试打印/ })).toBeTruthy()
  })

  it('启动加载机器级配置：服务端地址显示 hostConfig.serverUrl', async () => {
    renderSettings()
    await screen.findByDisplayValue('http://127.0.0.1:53961')
    expect(mocks.local.getHostConfig).toHaveBeenCalled()
  })
})

describe('服务端地址（F2）', () => {
  it('测试连接：探测输入框地址（probeHealthz），不保存（setHostConfig 不调用）', async () => {
    renderSettings()
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    fireEvent.click(within(withinSection('服务端地址')).getByRole('button', { name: /^测试连接/ }))
    expect(await screen.findByText(/连接成功：该地址可访问服务端/)).toBeTruthy()
    expect(mocks.probeHealthz).toHaveBeenCalledWith('http://192.168.1.10:53961')
    expect(mocks.local.setHostConfig).not.toHaveBeenCalled()
  })

  it('测试连接失败：提示失败原因', async () => {
    mocks.probeHealthz.mockResolvedValue(false)
    renderSettings()
    fireEvent.click(within(withinSection('服务端地址')).getByRole('button', { name: /^测试连接/ }))
    expect(await screen.findByText(/连接失败：请确认服务端已启动/)).toBeTruthy()
  })

  it('保存并生效：setHostConfig({ serverUrl }) + localStorage 兜底 + 提示立即生效', async () => {
    renderSettings()
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    fireEvent.click(screen.getByRole('button', { name: /保存并生效/ }))
    expect(await screen.findByText('已保存到本机配置并立即生效。')).toBeTruthy()
    expect(mocks.local.setHostConfig).toHaveBeenCalledWith({ serverUrl: 'http://192.168.1.10:53961' })
    expect(window.localStorage.getItem('labelframe.baseUrl')).toBe('http://192.168.1.10:53961')
  })

  it('旧客户端（setHostConfig 失败）：回退浏览器本地保存并提示', async () => {
    mocks.local.setHostConfig.mockRejectedValue(new Error('no api'))
    renderSettings()
    fireEvent.change(screen.getByLabelText('服务端地址'), { target: { value: 'http://192.168.1.10:53961' } })
    fireEvent.click(screen.getByRole('button', { name: /保存并生效/ }))
    expect(await screen.findByText(/本机配置接口不可用，已使用浏览器本地保存/)).toBeTruthy()
    expect(window.localStorage.getItem('labelframe.baseUrl')).toBe('http://192.168.1.10:53961')
  })
})

describe('连接方式（F3，恢复迭代 15）', () => {
  it('模式单选只显示当前模式参数（切到 TCP 显示 IP / 端口）', async () => {
    renderSettings()
    fireEvent.click(screen.getByRole('radio', { name: /^TCP/ }))
    expect(await screen.findByLabelText('打印机 IP / 主机名')).toBeTruthy()
    expect(screen.getByLabelText('端口')).toBeTruthy()
  })

  it('测试连接：发送候选参数（testOnly 注入在 client 层，组件只传表单参数），成功后显示后端 message 且不调 setTransport', async () => {
    renderSettings()
    fireEvent.click(screen.getByRole('radio', { name: /^TCP/ }))
    await screen.findByLabelText('打印机 IP / 主机名')
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '192.168.1.50' } })
    fireEvent.click(within(withinSection('连接方式')).getByRole('button', { name: /^测试连接/ }))
    expect(await screen.findByText('测试通过。')).toBeTruthy()
    expect(mocks.local.testTransport).toHaveBeenCalledWith(expect.objectContaining({ mode: 'Tcp', tcpHost: '192.168.1.50', tcpPort: 9100 }))
    expect(mocks.local.setTransport).not.toHaveBeenCalled()
  })

  it('保存并应用：setTransport 不带 testOnly，成功后当前生效连接徽标更新', async () => {
    renderSettings()
    fireEvent.click(screen.getByRole('radio', { name: /^TCP/ }))
    await screen.findByLabelText('打印机 IP / 主机名')
    fireEvent.change(screen.getByLabelText('打印机 IP / 主机名'), { target: { value: '192.168.1.50' } })
    fireEvent.click(screen.getByRole('button', { name: /保存并应用/ }))
    expect(await screen.findByText('已切换到 TCP。')).toBeTruthy()
    expect(mocks.local.setTransport).toHaveBeenCalledWith(expect.objectContaining({ mode: 'Tcp', tcpHost: '192.168.1.50', tcpPort: 9100 }))
    expect(mocks.local.setTransport.mock.calls[0][0].testOnly).toBeUndefined()
    // 全局状态立即更新（连接方式分组内徽标显示 TCP 地址，不依赖轮询）
    expect(await within(withinSection('连接方式')).findByText(/TCP 192\.168\.1\.50:9100/)).toBeTruthy()
  })

  it('保存失败（后端返回 ok:false）：展示 message，当前生效连接保持', async () => {
    mocks.local.setTransport.mockResolvedValue({ ok: false, message: '连接测试失败：无法连接打印机。', config: { mode: 'Log', params: {} } })
    renderSettings()
    fireEvent.click(screen.getByRole('button', { name: /保存并应用/ }))
    expect(await screen.findByText('连接测试失败：无法连接打印机。')).toBeTruthy()
    // 生效连接仍是 Log（未切换）
    expect(screen.getByText('LOG')).toBeTruthy()
  })
})

describe('打印机（F4）', () => {
  it('状态展示：在线 + 附加信息', async () => {
    renderSettings()
    expect(await screen.findByText('在线')).toBeTruthy()
    expect(screen.getByText('就绪')).toBeTruthy()
  })

  it('缺纸 / 暂停徽标', async () => {
    mocks.local.getPrinterStatus.mockResolvedValue({ isOnline: true, isPaperOut: true, isPaused: true, message: '' })
    renderSettings()
    expect(await screen.findByText('缺纸')).toBeTruthy()
    expect(screen.getByText('已暂停')).toBeTruthy()
  })

  it('测试打印：调用 testPrinter 并显示字节数', async () => {
    renderSettings()
    fireEvent.click(screen.getByRole('button', { name: /测试打印/ }))
    expect(await screen.findByText(/测试页已发送（128 字节）/)).toBeTruthy()
    expect(mocks.local.testPrinter).toHaveBeenCalled()
  })
})

describe('更新与安装包（迭代 22 §2.3）', () => {
  const PKGS = [
    { fileName: 'LabelFrame.Client-0.18.0.msi', sizeBytes: 42 * 1024 * 1024, modifiedAt: '2026-08-17T10:00:00Z', url: '/api/client-packages/LabelFrame.Client-0.18.0.msi' },
    { fileName: 'LabelFrame.Client-linux.zip', sizeBytes: 1024, modifiedAt: '2026-08-17T09:00:00Z' },
  ]

  it('服务端已连接：拉取安装包列表，显示文件名 / 大小 / 修改时间与下载链接', async () => {
    mocks.server.listClientPackages.mockResolvedValue(PKGS)
    renderSettings()
    expect(await screen.findByText('LabelFrame.Client-0.18.0.msi')).toBeTruthy()
    expect(screen.getByText('LabelFrame.Client-linux.zip')).toBeTruthy()
    expect(screen.getByText('42.0 MB')).toBeTruthy()
    // 下载链接指向 {serverBaseUrl}/api/client-packages/{fileName}（client 构建 baseUrl = hostConfig.serverUrl）
    const links = screen.getAllByRole('link', { name: /下载/ })
    expect(links[0].getAttribute('href')).toBe('http://127.0.0.1:53961/api/client-packages/LabelFrame.Client-0.18.0.msi')
    expect(links[1].getAttribute('href')).toBe('http://127.0.0.1:53961/api/client-packages/LabelFrame.Client-linux.zip')
  })

  it('服务端已连接但无安装包：提示去服务端管理界面上传', async () => {
    renderSettings()
    expect(await screen.findByText(/服务端暂无客户端安装包/)).toBeTruthy()
    expect(mocks.server.listClientPackages).toHaveBeenCalled()
  })

  it('单机模式（服务端不可达）：提示需先连接服务端，不调 listClientPackages', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    renderSettings()
    expect(await screen.findByText(/当前未连接服务端（单机模式）/)).toBeTruthy()
    expect(screen.getByText(/请先在上方「服务端地址」中连接服务端/)).toBeTruthy()
    expect(mocks.server.listClientPackages).not.toHaveBeenCalled()
  })
})

describe('插件管理（迭代 23 §5.6）', () => {
  const PKG: PluginPackageInfo = {
    fileName: 'sample-1.0.0.lfplugin',
    pluginId: 'sample',
    name: '示例插件',
    version: '1.0.0',
    sizeBytes: 2048,
    modifiedAt: '2026-08-17T10:00:00Z',
    valid: true,
  }
  const INVALID: PluginPackageInfo = {
    fileName: 'broken.lfplugin',
    sizeBytes: 1024,
    modifiedAt: '2026-08-17T09:00:00Z',
    valid: false,
    invalidReason: 'manifest 缺少 pluginId',
  }
  const INSTALLED: InstalledPluginInfo = {
    pluginId: 'sample',
    name: '示例插件',
    version: '1.0.0',
    loaded: true,
    source: 'package',
  }

  it('可用列表渲染：valid 显示名称 / 版本 / pluginId / 有效徽标与安装按钮；invalid 红标 + 原因 + 安装禁用', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG, INVALID])
    renderSettings()
    expect(await screen.findByText('示例插件')).toBeTruthy()
    expect(screen.getByText('1.0.0')).toBeTruthy()
    expect(screen.getByText('sample')).toBeTruthy()
    expect(screen.getByText('有效')).toBeTruthy()
    expect(screen.getByText('无效')).toBeTruthy()
    expect(screen.getByText(/manifest 缺少 pluginId/)).toBeTruthy()
    const buttons = screen.getAllByRole('button', { name: /^安装$/ })
    expect(buttons).toHaveLength(2)
    expect((buttons[0] as HTMLButtonElement).disabled).toBe(false)
    expect((buttons[1] as HTMLButtonElement).disabled).toBe(true)
  })

  it('安装流程：下载插件包 → 本机安装（保留原始文件名）→ 提示后端 message + 刷新已安装列表', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    await waitFor(() => expect(mocks.server.downloadPluginPackage).toHaveBeenCalledWith('sample-1.0.0.lfplugin'))
    await waitFor(() => expect(mocks.local.installPlugin).toHaveBeenCalledTimes(1))
    const file = mocks.local.installPlugin.mock.calls[0][0] as File
    expect(file.name).toBe('sample-1.0.0.lfplugin')
    expect(await screen.findByText(/已安装，重启客户端后生效/)).toBeTruthy()
    // 挂载 1 次 + 安装成功后刷新
    await waitFor(() => expect(mocks.local.listInstalledPlugins).toHaveBeenCalledTimes(2))
  })

  it('覆盖安装：已安装同 pluginId 时 confirm「将覆盖 x → y」；确认后安装', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    mocks.local.listInstalledPlugins.mockResolvedValue([INSTALLED])
    const confirmSpy = vi.spyOn(window, 'confirm').mockImplementation(() => true)
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    await waitFor(() => expect(confirmSpy).toHaveBeenCalled())
    expect(String(confirmSpy.mock.calls[0][0])).toContain('将覆盖')
    expect(mocks.local.installPlugin).toHaveBeenCalledTimes(1)
    confirmSpy.mockRestore()
  })

  it('覆盖安装取消：confirm 返回 false → 不下载不安装', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    mocks.local.listInstalledPlugins.mockResolvedValue([INSTALLED])
    const confirmSpy = vi.spyOn(window, 'confirm').mockImplementation(() => false)
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    await waitFor(() => expect(confirmSpy).toHaveBeenCalled())
    expect(mocks.server.downloadPluginPackage).not.toHaveBeenCalled()
    expect(mocks.local.installPlugin).not.toHaveBeenCalled()
    confirmSpy.mockRestore()
  })

  it('单机模式：可用插件区提示需先连接服务端、不调 listPluginPackages；已安装区仍渲染', async () => {
    mocks.server.healthz.mockRejectedValue(new Error('down'))
    renderSettings()
    expect(await screen.findByText(/当前未连接服务端，处于单机模式/)).toBeTruthy()
    expect(mocks.server.listPluginPackages).not.toHaveBeenCalled()
    expect(mocks.local.listInstalledPlugins).toHaveBeenCalled()
  })

  it('旧 Server（可用插件区 404）：区分展示「服务端不支持插件管理（旧版本）」', async () => {
    mocks.server.listPluginPackages.mockRejectedValue(new ApiError('HTTP_404', '请求失败（HTTP 404）。'))
    renderSettings()
    expect(await screen.findByText(/服务端不支持插件管理（旧版本）/)).toBeTruthy()
  })

  it('已安装徽标四态：已加载 / 待重启生效 / 加载失败 + 原因 / 手动放置只读无卸载', async () => {
    mocks.local.listInstalledPlugins.mockResolvedValue([
      { pluginId: 'a', name: '已加载插件', version: '1.0.0', loaded: true, source: 'package' },
      { pluginId: 'b', name: '待重启插件', version: '1.0.0', loaded: false, source: 'package' },
      { pluginId: 'c', name: '坏插件', version: '1.0.0', loaded: false, loadError: '加载失败：找不到依赖', source: 'package' },
      { pluginId: 'd', name: '手动插件', version: '?', loaded: true, source: 'manual' },
    ])
    renderSettings()
    expect(await screen.findByText('已加载插件')).toBeTruthy()
    expect(screen.getByText('已加载')).toBeTruthy()
    expect(screen.getByText('待重启生效')).toBeTruthy()
    expect(screen.getByText('加载失败')).toBeTruthy()
    expect(screen.getByText(/找不到依赖/)).toBeTruthy()
    expect(screen.getByText('手动放置')).toBeTruthy()
    expect(screen.getAllByRole('button', { name: /^卸载$/ })).toHaveLength(3)
    expect(screen.queryByText('卸载中…')).toBeNull()
  })

  it('卸载：confirm → uninstallPlugin(pluginId) → 提示 message + 刷新', async () => {
    mocks.local.listInstalledPlugins.mockResolvedValue([INSTALLED])
    const confirmSpy = vi.spyOn(window, 'confirm').mockImplementation(() => true)
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^卸载$/ }))
    await waitFor(() => expect(mocks.local.uninstallPlugin).toHaveBeenCalledWith('sample'))
    expect(await screen.findByText(/已卸载，重启客户端后生效/)).toBeTruthy()
    confirmSpy.mockRestore()
  })

  it('安装失败（后端 400 ErrorView）：展示 message（文件锁提示）', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([PKG])
    mocks.local.installPlugin.mockRejectedValue(
      new ApiError('LF_PLUGIN_BUSY', '插件「sample」正在使用中（DLL 被客户端占用），请重启客户端后重试。'),
    )
    renderSettings()
    fireEvent.click(await screen.findByRole('button', { name: /^安装$/ }))
    expect(await screen.findByText(/正在使用中（DLL 被客户端占用），请重启客户端后重试/)).toBeTruthy()
  })

  it('已安装列表加载失败：错误提示（非 404 不显示版本提示）', async () => {
    mocks.local.listInstalledPlugins.mockRejectedValue(new Error('network down'))
    renderSettings()
    expect(await screen.findByText(/获取已安装插件列表失败/)).toBeTruthy()
    expect(screen.queryByText(/当前客户端版本不支持插件管理/)).toBeNull()
  })

  it('旧 WinHost（已安装区 404）：显示「当前客户端版本不支持插件管理」', async () => {
    mocks.local.listInstalledPlugins.mockRejectedValue(new ApiError('HTTP_404', '请求失败（HTTP 404）。'))
    renderSettings()
    expect(await screen.findByText('当前客户端版本不支持插件管理。')).toBeTruthy()
  })

  it('超过 64MB 的插件包：安装按钮禁用（预检提示）', async () => {
    mocks.server.listPluginPackages.mockResolvedValue([{ ...PKG, sizeBytes: 64 * 1024 * 1024 + 1 }])
    renderSettings()
    const btn = (await screen.findByRole('button', { name: /^安装$/ })) as HTMLButtonElement
    expect(btn.disabled).toBe(true)
  })
})
