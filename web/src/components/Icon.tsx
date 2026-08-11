// 内联 SVG 图标（线条风格，stroke 1.6，与工具主题一致）

import type { SVGProps } from 'react'

export type IconName =
  | 'workbench' | 'designer' | 'data' | 'logs' | 'settings' | 'history'
  | 'plus' | 'edit' | 'trash' | 'download' | 'upload' | 'refresh'
  | 'retry' | 'test' | 'link' | 'printer' | 'clear' | 'layers'
  | 'check' | 'x' | 'search' | 'file' | 'grid' | 'zoom' | 'save'
  | 'back' | 'preview' | 'copy' | 'clipboard' | 'alert' | 'keyboard'

const PATHS: Record<IconName, React.ReactNode> = {
  workbench: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 9h18M8 4v5M16 4v5" />
    </>
  ),
  designer: (
    <>
      <path d="M13 3 5 11v3h3l8-8z" />
      <path d="m15 5 2 2" />
      <path d="M4 20h16" />
    </>
  ),
  data: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 9h18M8 9v11M16 9v11" />
    </>
  ),
  logs: (
    <>
      <path d="M4 6h16M4 12h16M4 18h10" />
      <circle cx="17.5" cy="18" r="2.2" />
    </>
  ),
  settings: (
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M19.1 4.9 17 7M7 17l-2.1 2.1" />
    </>
  ),
  history: (
    <>
      <circle cx="12" cy="12" r="8" />
      <path d="M12 7v5l3.5 2" />
    </>
  ),
  plus: <path d="M12 5v14M5 12h14" />,
  edit: <path d="M13 3 5 11v3h3l8-8zM15 5l2 2" />,
  trash: <path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v5M14 11v5" />,
  download: <path d="M12 3v12m0 0 4-4m-4 4-4-4M4 19h16" />,
  upload: <path d="M12 15V3m0 0 4 4m-4-4-4 4M4 19h16" />,
  refresh: <path d="M20 12a8 8 0 1 1-2.3-5.6M20 4v4h-4" />,
  retry: <path d="M3 12a9 9 0 1 0 2.6-6.3M3 4v5h5" />,
  test: <path d="M12 3v6l5 8a2 2 0 0 1-1.7 3H8.7A2 2 0 0 1 7 17l5-8V3M12 3h-2M12 3h2" />,
  link: <path d="M10 14a5 5 0 0 0 7.5.5l2-2a5 5 0 0 0-7-7l-1.2 1.2M14 10a5 5 0 0 0-7.5-.5l-2 2a5 5 0 0 0 7 7l1.2-1.2" />,
  printer: (
    <>
      <path d="M6 9V3h12v6" />
      <rect x="3" y="9" width="18" height="8" rx="2" />
      <path d="M6 14h12v7H6z" />
    </>
  ),
  clear: <path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13" />,
  layers: <path d="m12 3 9 5-9 5-9-5 9-5zM3 13l9 5 9-5" />,
  check: <path d="m4 12.5 5 5L20 6.5" />,
  x: <path d="M6 6l12 12M18 6 6 18" />,
  search: (
    <>
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 5 5" />
    </>
  ),
  file: <path d="M6 2h8l4 4v16H6zM14 2v4h4" />,
  grid: (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </>
  ),
  zoom: (
    <>
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 5 5" />
      <path d="M8 11h6M11 8v6" />
    </>
  ),
  save: <path d="M5 3h11l4 4v14H5zM8 3v5h7V3M8 21v-7h8v7" />,
  back: <path d="M15 5l-7 7 7 7" />,
  preview: (
    <>
      <path d="M2 12s3.5-6.5 10-6.5S22 12 22 12s-3.5 6.5-10 6.5S2 12 2 12z" />
      <circle cx="12" cy="12" r="2.6" />
    </>
  ),
  copy: (
    <>
      <rect x="8" y="8" width="12" height="12" rx="2" />
      <path d="M16 8V4H4v12h4" />
    </>
  ),
  clipboard: (
    <>
      <rect x="5" y="4" width="14" height="18" rx="2" />
      <path d="M9 4a3 3 0 0 1 6 0M9 11h6M9 15h6" />
    </>
  ),
  alert: <path d="M12 3 2.5 20h19zM12 9v5M12 17.5v.5" />,
  keyboard: (
    <>
      <rect x="2.5" y="6" width="19" height="12.5" rx="2" />
      <path d="M6.5 10h.01M9.5 10h.01M12.5 10h.01M15.5 10h.01M18.5 10h.01M6.5 14h11" />
    </>
  ),
}

interface IconProps extends SVGProps<SVGSVGElement> {
  name: IconName
  size?: number
}

export function Icon({ name, size = 16, ...rest }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      {PATHS[name]}
    </svg>
  )
}

/** 品牌图形：标签纸（虚线轮廓 + 条码线），呼应「标签打印」主题。 */
export function LabelLogo({ size = 26 }: { size?: number }) {
  return (
    <svg viewBox="0 0 34 26" width={size} height={(size * 26) / 34} aria-hidden="true">
      <rect x="1.5" y="1.5" width="31" height="23" rx="3" fill="none" stroke="currentColor" strokeWidth="1.6" strokeDasharray="3 2.4" />
      <path d="M6 17.5h14M6 13.5h10M23.5 13.5h4.5M23.5 17.5h4.5" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" />
      <path d="M7 7.5h3.5M13 7.5h6M21.5 7.5h2.5M26.5 7.5h1" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  )
}
