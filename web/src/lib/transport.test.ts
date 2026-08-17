// 迭代 22：传输插件工具单元测试——displayText 优先徽标、mode→pluginId 映射、
// spec 默认值 / Select 枚举兼容解析（string[] 与 { value, label? }[]）、配置参数提取。

import { describe, expect, it } from 'vitest'
import type { TransportConfig, TransportPluginInfo } from './api/types'
import {
  MODE_TO_PLUGIN_ID,
  defaultPluginParams,
  effectivePluginId,
  formatTransport,
  isPluginMode,
  pluginParamsFromConfig,
  specDefaultValue,
  specOptions,
} from './transport'

const TCP9100_PLUGIN: TransportPluginInfo = {
  id: 'tcp9100',
  displayName: 'TCP 9100',
  description: '通过网络口直连打印机（ZPL 指令）。',
  parameters: [
    { key: 'host', label: '打印机 IP / 主机名', type: 'String', required: true, hint: '192.168.1.50' },
    { key: 'port', label: '端口', type: 'Int', defaultValue: '9100', hint: '默认 9100' },
    { key: 'timeoutSeconds', label: '连接超时（秒）', type: 'Int', defaultValue: 10 },
  ],
}

const ZEBRA_PLUGIN: TransportPluginInfo = {
  id: 'zebra',
  displayName: 'Zebra',
  parameters: [
    { key: 'kind', label: '连接方式', type: 'Select', defaultValue: 'Tcp', options: ['Tcp', 'Usb', 'Driver'] },
    { key: 'printerName', label: '打印机名称', type: 'String' },
  ],
}

const VENDOR_PLUGIN: TransportPluginInfo = {
  id: 'vendor-ble',
  displayName: '厂商蓝牙',
  parameters: [
    {
      key: 'mode',
      label: '模式',
      type: 'Select',
      defaultValue: 'fast',
      options: [
        { value: 'fast', label: '快速' },
        { value: 'stable', label: '稳定' },
      ],
    },
    { key: 'pairedOnly', label: '仅已配对', type: 'Bool', defaultValue: 'true' },
  ],
}

describe('formatTransport：displayText 优先（迭代 22）', () => {
  it('新后端 displayText 存在时直接使用', () => {
    const cfg: TransportConfig = {
      pluginId: 'tcp9100',
      displayName: 'TCP 9100',
      displayText: 'TCP 192.168.1.50:9100',
      params: { host: '192.168.1.50', port: 9100 },
      mode: 'Tcp',
    }
    expect(formatTransport(cfg)).toBe('TCP 192.168.1.50:9100')
  })

  it('旧后端无 displayText：按 mode + params 本地格式化', () => {
    expect(formatTransport({ mode: 'Log', params: {} })).toBe('LOG')
    expect(formatTransport({ mode: 'Tcp', params: { tcpHost: '192.168.1.50', tcpPort: 9100 } })).toBe('TCP 192.168.1.50:9100')
    expect(formatTransport({ mode: 'Zebra', params: { zebraKind: 'Usb', zebraUsbName: 'ZDesigner' } })).toBe('Zebra USB（ZDesigner）')
    expect(formatTransport(null)).toBe('')
  })
})

describe('mode ↔ 插件 id 映射', () => {
  it('MODE_TO_PLUGIN_ID 与后端旧配置映射表一致', () => {
    expect(MODE_TO_PLUGIN_ID).toEqual({ Log: 'log', Tcp: 'tcp9100', WindowsDriver: 'winspool', Zebra: 'zebra' })
  })

  it('effectivePluginId：pluginId 优先，旧后端按 mode 映射，无配置回退 log', () => {
    expect(effectivePluginId({ pluginId: 'vendor-ble', params: {}, mode: 'Log' })).toBe('vendor-ble')
    expect(effectivePluginId({ mode: 'Tcp', params: {} })).toBe('tcp9100')
    expect(effectivePluginId({ mode: 'WindowsDriver', params: {} })).toBe('winspool')
    expect(effectivePluginId(null)).toBe('log')
  })

  it('isPluginMode：availablePlugins 非空才为插件模式（旧后端回退内置 4 模式）', () => {
    expect(isPluginMode({ mode: 'Log', params: {}, availablePlugins: [TCP9100_PLUGIN] })).toBe(true)
    expect(isPluginMode({ mode: 'Log', params: {} })).toBe(false)
    expect(isPluginMode({ mode: 'Log', params: {}, availablePlugins: [] })).toBe(false)
    expect(isPluginMode(null)).toBe(false)
  })
})

describe('spec 解析（兼容后端两种序列化）', () => {
  it('specOptions：string[] 与 { value, label? }[] 均可解析', () => {
    expect(specOptions(ZEBRA_PLUGIN.parameters[0])).toEqual([
      { value: 'Tcp', label: 'Tcp' },
      { value: 'Usb', label: 'Usb' },
      { value: 'Driver', label: 'Driver' },
    ])
    expect(specOptions(VENDOR_PLUGIN.parameters[0])).toEqual([
      { value: 'fast', label: '快速' },
      { value: 'stable', label: '稳定' },
    ])
    expect(specOptions({ key: 'k', label: 'l', type: 'Select' })).toEqual([])
  })

  it('specDefaultValue：Bool / Int 字符串序列化防御解析，Select 回退第一项', () => {
    expect(specDefaultValue(VENDOR_PLUGIN.parameters[1])).toBe(true) // 'true' 字符串 → true
    expect(specDefaultValue(TCP9100_PLUGIN.parameters[1])).toBe(9100) // '9100' → 9100
    expect(specDefaultValue(TCP9100_PLUGIN.parameters[2])).toBe(10)
    expect(specDefaultValue(ZEBRA_PLUGIN.parameters[0])).toBe('Tcp')
    expect(specDefaultValue(VENDOR_PLUGIN.parameters[0])).toBe('fast')
    expect(specDefaultValue({ key: 'k', label: 'l', type: 'Int' })).toBe(0)
    expect(specDefaultValue({ key: 'k', label: 'l', type: 'Bool' })).toBe(false)
    expect(specDefaultValue({ key: 'k', label: 'l', type: 'String' })).toBe('')
  })

  it('defaultPluginParams：全部参数按 spec 默认值生成', () => {
    expect(defaultPluginParams(TCP9100_PLUGIN)).toEqual({ host: '', port: 9100, timeoutSeconds: 10 })
    expect(defaultPluginParams(ZEBRA_PLUGIN)).toEqual({ kind: 'Tcp', printerName: '' })
  })

  it('pluginParamsFromConfig：从当前配置提取有效键（按 spec 类型转换），未命中用默认值', () => {
    const cfg: TransportConfig = {
      pluginId: 'tcp9100',
      params: { host: '192.168.1.50', port: '9100' }, // port 字符串 → Int
      mode: 'Tcp',
    }
    expect(pluginParamsFromConfig(TCP9100_PLUGIN, cfg)).toEqual({ host: '192.168.1.50', port: 9100, timeoutSeconds: 10 })
    // 旧后端平铺 params 也可提取（如 printerName）
    const oldCfg: TransportConfig = { mode: 'WindowsDriver', params: { printerName: 'ZDesigner' } }
    expect(pluginParamsFromConfig({ id: 'winspool', displayName: 'Windows 驱动', parameters: [{ key: 'printerName', label: '打印机名称', type: 'String', required: true }] }, oldCfg)).toEqual({
      printerName: 'ZDesigner',
    })
  })
})
