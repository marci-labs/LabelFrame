// 连接方式（迭代 15 §6.2 恢复，迭代 18 F3）：模式标签 / 徽标格式化 / 参数默认值
// 迭代 22：传输插件化——availablePlugins（spec 驱动表单）优先；旧后端（无 availablePlugins）回退内置 4 模式。
// 徽标：新后端 displayText 优先，旧后端按 mode + params 本地格式化。

import type { PluginParams, PluginParamValue, TransportConfig, TransportMode, TransportParams, TransportParameterSpec, TransportPluginInfo, ZebraKind } from './api/types'

export const ALL_TRANSPORT_MODES: TransportMode[] = ['Log', 'Tcp', 'WindowsDriver', 'Zebra']

export const MODE_LABELS: Record<TransportMode, string> = {
  Log: 'Log（模拟）',
  Tcp: 'TCP',
  WindowsDriver: 'Windows 驱动',
  Zebra: 'Zebra',
}

export const ZEBRA_KIND_LABELS: Record<ZebraKind, string> = {
  Tcp: 'TCP',
  Usb: 'USB（自动发现）',
  Driver: 'Windows 驱动',
}

// ── 迭代 22：旧模式 ↔ 插件 id 映射（回退显示 / 初始选中；与后端 connection.json 旧配置映射表一致）──

export const MODE_TO_PLUGIN_ID: Record<TransportMode, string> = {
  Log: 'log',
  Tcp: 'tcp9100',
  WindowsDriver: 'winspool',
  Zebra: 'zebra',
}

/** 当前生效插件 id：新后端取 pluginId，旧后端按 mode 映射（无配置默认 log）。 */
export function effectivePluginId(cfg: TransportConfig | null | undefined): string {
  if (cfg?.pluginId) return cfg.pluginId
  if (cfg?.mode) return MODE_TO_PLUGIN_ID[cfg.mode]
  return 'log'
}

/** 是否插件模式（新后端）：availablePlugins 非空时按 spec 渲染表单。 */
export function isPluginMode(cfg: TransportConfig | null | undefined): boolean {
  return Array.isArray(cfg?.availablePlugins) && cfg.availablePlugins.length > 0
}

/** 徽标 / 状态栏文本：新后端 displayText 优先；旧后端按 mode 格式化（`LOG` / `TCP 192.168.1.50:9100` / `WindowsDriver 打印机名` / `Zebra USB`）。 */
export function formatTransport(cfg: TransportConfig | null | undefined): string {
  if (!cfg) return ''
  if (cfg.displayText) return cfg.displayText
  const p = cfg.params
  switch (cfg.mode) {
    case 'Log':
      return 'LOG'
    case 'Tcp':
      return `TCP ${p.tcpHost || '?'}${p.tcpPort ? `:${p.tcpPort}` : ''}`
    case 'WindowsDriver':
      return `WindowsDriver ${p.printerName || '?'}`
    case 'Zebra':
      if (p.zebraKind === 'Usb') return `Zebra USB${p.zebraUsbName ? `（${p.zebraUsbName}）` : ''}`
      if (p.zebraKind === 'Driver') return `Zebra ${p.printerName || '驱动'}`
      return `Zebra TCP ${p.tcpHost || '?'}${p.tcpPort ? `:${p.tcpPort}` : ''}`
  }
}

/** 切换模式时的参数默认值（当前模式有生效配置则沿用）。 */
export function defaultParams(mode: TransportMode, current?: TransportConfig | null): TransportParams {
  if (current?.mode === mode) return { ...current.params }
  switch (mode) {
    case 'Tcp':
      return { tcpPort: 9100 }
    case 'Zebra':
      return { zebraKind: 'Tcp', tcpPort: 9100 }
    default:
      return {}
  }
}

// ── 迭代 22：插件参数 spec 工具（兼容后端可能的两种序列化）──

/** Select 枚举项解析：兼容 `string[]` 与 `{ value, label? }[]` 两种后端序列化。 */
export function specOptions(spec: TransportParameterSpec): { value: string; label: string }[] {
  const opts = spec.options ?? []
  return opts.map((o) => {
    if (typeof o === 'string') return { value: o, label: o }
    return { value: String(o.value), label: o.label ?? String(o.value) }
  })
}

/** spec 默认值解析（Bool / Int 可能以字符串序列化，按 type 防御转换）。 */
export function specDefaultValue(spec: TransportParameterSpec): PluginParamValue {
  const dv = spec.defaultValue
  switch (spec.type) {
    case 'Bool':
      return dv === true || dv === 'true'
    case 'Int': {
      if (typeof dv === 'number') return dv
      const n = Number(dv)
      return Number.isFinite(n) ? n : 0
    }
    case 'Select': {
      if (typeof dv === 'string' && dv !== '') return dv
      return specOptions(spec)[0]?.value ?? ''
    }
    default: {
      if (dv === null || dv === undefined) return ''
      if (typeof dv === 'string') return dv
      return String(dv)
    }
  }
}

/** 插件全部参数默认值（spec.defaultValue 优先，缺省按类型给空值）。 */
export function defaultPluginParams(plugin: TransportPluginInfo): PluginParams {
  const out: PluginParams = {}
  for (const spec of plugin.parameters) out[spec.key] = specDefaultValue(spec)
  return out
}

/** 从当前生效配置提取插件参数（键按 spec 校验，未命中用 spec 默认值；兼容旧后端平铺 params 字典）。 */
export function pluginParamsFromConfig(plugin: TransportPluginInfo, cfg: TransportConfig | null | undefined): PluginParams {
  const raw = cfg?.params as PluginParams | undefined
  const out = defaultPluginParams(plugin)
  if (!raw || typeof raw !== 'object') return out
  for (const spec of plugin.parameters) {
    const v = raw[spec.key]
    if (v === undefined || v === null) continue
    if (spec.type === 'Bool') out[spec.key] = v === true || v === 'true'
    else if (spec.type === 'Int') out[spec.key] = Number(v)
    else out[spec.key] = String(v)
  }
  return out
}
