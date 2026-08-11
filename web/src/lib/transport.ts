// 连接方式（迭代 15 §6.2 恢复，迭代 18 F3）：模式标签 / 徽标格式化 / 参数默认值

import type { TransportConfig, TransportMode, TransportParams, ZebraKind } from './api/types'

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

/** 徽标 / 状态栏文本：`LOG` / `TCP 192.168.1.50:9100` / `WindowsDriver 打印机名` / `Zebra USB`。 */
export function formatTransport(cfg: TransportConfig | null | undefined): string {
  if (!cfg) return ''
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
