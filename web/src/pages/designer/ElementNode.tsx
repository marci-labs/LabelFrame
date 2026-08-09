// 设计器元素渲染（react-konva 声明式）：Group = 边框 + 内容

import { useEffect, useMemo, useRef, useState } from 'react'
import { Group, Image as KImage, Line as KLine, Rect as KRect, Text as KText } from 'react-konva'
import type Konva from 'konva'
import type { KonvaEventObject } from 'konva/lib/Node'
import { pxv } from '../../lib/design/geometry'
import type { BarcodeElement, DesignElement, QrCodeElement, TextElement } from '../../lib/design/types'
import { elementContent, typeLabel } from '../../lib/design/types'
import { fitImageRect, makeBarcodeCanvas, makeQrCanvas } from '../../lib/design/barcode'

interface ElementNodeProps {
  e: DesignElement
  editable: boolean
  ox: number
  oy: number
  onDragStart?: (ev: KonvaEventObject<DragEvent>) => void
  onDragMove?: (ev: KonvaEventObject<DragEvent>) => void
  onDragEnd?: (ev: KonvaEventObject<DragEvent>) => void
}

export function ElementNode({ e, editable, ox, oy, onDragStart, onDragMove, onDragEnd }: ElementNodeProps) {
  const x = ox + pxv(e.x)
  const y = oy + pxv(e.y)
  const w = Math.max(2, pxv(e.w))
  const h = Math.max(2, pxv(e.h))
  const borderW = Math.max(1, pxv(e.border || 0))

  const common = {
    id: e.id,
    name: 'element',
    x,
    y,
    draggable: editable,
    onDragStart,
    onDragMove,
    onDragEnd,
  }

  switch (e.type) {
    case 'Text':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} strokeScaleEnabled={false} listening={false} />
          <TextContent e={e} />
        </Group>
      )
    case 'Barcode':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} strokeScaleEnabled={false} listening={false} />
          <ImageContent e={e} wPx={w} hPx={h} />
        </Group>
      )
    case 'QrCode':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} strokeScaleEnabled={false} listening={false} />
          <ImageContent e={e} wPx={w} hPx={h} qr />
        </Group>
      )
    case 'Rect':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} stroke={e.border > 0 ? '#000' : undefined} strokeWidth={borderW} fill="transparent" strokeScaleEnabled={false} listening={false} />
        </Group>
      )
    case 'Image':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} fill="#f5f6f8" stroke={e.border > 0 ? '#000' : '#aab4c0'} strokeWidth={borderW} dash={[4, 3]} strokeScaleEnabled={false} listening={false} />
          <KText x={4} y={4} text={'图片: ' + (e.key || '')} fontSize={11} fontFamily="Microsoft YaHei" fill="#6b7684" listening={false} />
        </Group>
      )
    case 'Line':
      return (
        <KLine id={e.id} name="element" x={x} y={y} points={[0, 0, pxv(e.w), pxv(e.h)]} stroke="#000" strokeWidth={Math.max(1, pxv(e.thickness || 0.5))} strokeScaleEnabled={false} draggable={editable} onDragStart={onDragStart} onDragMove={onDragMove} onDragEnd={onDragEnd} />
      )
    case 'Region':
      return (
        <Group {...common}>
          <KRect x={0} y={0} width={w} height={h} fill="rgba(0,128,255,0.06)" stroke={e.border > 0 ? '#000' : '#8a94a0'} strokeWidth={borderW} dash={[6, 4]} strokeScaleEnabled={false} listening={false} />
          <KText x={4} y={2} text={'容器 ' + (e.containerId || '')} fontSize={10} fontFamily="Microsoft YaHei" fill="#7a8490" listening={false} />
        </Group>
      )
  }
}

/** 文本内容：文本框 = 遮罩；单行溢出可缩小适应（shrink）或隐藏（overflow）。 */
function TextContent({ e }: { e: TextElement }) {
  const content = elementContent(e)
  const padH = pxv(e.paddingH || 0)
  const padV = pxv(e.paddingV || 0)
  const wPx = Math.max(2, pxv(e.w) - padH * 2)
  const hPx = Math.max(2, pxv(e.h) - padV * 2)
  const [fs, setFs] = useState(Math.max(1, pxv(e.fontH)))
  const textRef = useRef<Konva.Text>(null)

  // 单行缩小适应：measureSize 循环降字号直到放得下（与原型 applyTextFit 一致）
  useEffect(() => {
    const t = textRef.current
    const base = Math.max(1, pxv(e.fontH))
    if (!t || e.wrap || e.fitMode !== 'shrink') {
      setFs(base)
      return
    }
    let f = base
    const minFs = Math.max(1, pxv(1.5))
    t.fontSize(f)
    let m = t.measureSize(content)
    while ((m.width > wPx || m.height > hPx) && f > minFs) {
      f = Math.max(minFs, f - 0.5)
      t.fontSize(f)
      m = t.measureSize(content)
    }
    setFs(f)
  }, [content, wPx, hPx, e.fontH, e.wrap, e.fitMode])

  const align = e.align === 'Center' ? 'center' : e.align === 'Right' ? 'right' : 'left'
  const fontSize = e.wrap || e.fitMode === 'overflow' ? Math.max(1, pxv(e.fontH)) : fs

  return (
    <Group x={padH} y={padV} clipX={0} clipY={0} clipWidth={wPx} clipHeight={hPx}>
      <KText
        ref={textRef}
        text={content}
        fontSize={fontSize}
        fontFamily={e.fontFamily || 'Microsoft YaHei'}
        fill={e.mode === 'field' && !e.key ? '#999' : '#000'}
        width={wPx}
        height={hPx}
        align={align}
        verticalAlign={e.valign || 'middle'}
        lineHeight={e.lineHeight || 1.2}
        wrap={e.wrap ? 'word' : 'none'}
        ellipsis={false}
        listening={false}
        strokeScaleEnabled={false}
      />
    </Group>
  )
}

/** 条码 / 二维码图片内容（fit 到内盒居中）。 */
function ImageContent({ e, wPx, hPx, qr }: { e: BarcodeElement | QrCodeElement; wPx: number; hPx: number; qr?: boolean }) {
  const padH = pxv(e.paddingH || 0)
  const padV = pxv(e.paddingV || 0)
  const innerW = Math.max(2, wPx - padH * 2)
  const innerH = Math.max(2, hPx - padV * 2)
  const canvas = useMemo(() => (qr ? makeQrCanvas(e as QrCodeElement, innerW, innerH) : makeBarcodeCanvas(e as BarcodeElement)), [e, qr, innerW, innerH])
  const fit = useMemo(() => fitImageRect(canvas, innerW, innerH), [canvas, innerW, innerH])
  return <KImage image={canvas} x={fit.x + padH} y={fit.y + padV} width={fit.w} height={fit.h} listening={false} />
}

/** 图层 / 属性面板用：元素类型中文名。 */
export function elementTypeName(e: DesignElement): string {
  return typeLabel(e)
}
