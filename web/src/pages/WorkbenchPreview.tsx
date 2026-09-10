// 工作台模板预览放大浮层（迭代 46，修订：点击缩略图触发）：加载中 / 失败态 / 预览图三态展示；
// fixed 定位避开滚动容器裁剪；由半透明遮罩承接点击关闭（Esc 同效），点击浮层自身也可关闭。

import { Icon } from '../components/Icon'
import type { TemplatePreviewEntry } from './useTemplatePreview'

/** 缩略图单元格位置锚点（点击时取一次 boundingClientRect）。 */
export interface PreviewAnchor {
  name: string
  top: number
  left: number
  right: number
}

const POP_WIDTH = 296

/** 半透明遮罩：点击任意处（含浮层自身）关闭放大视图；Esc 同效（父组件监听）。 */
export function PreviewOverlay({ onClose }: { onClose: () => void }) {
  return <div className="preview-overlay" onClick={onClose} aria-hidden="true" />
}

/** 预览放大浮层：优先停在缩略图右侧，视口不够则翻到左侧。 */
export function TemplatePreviewPop({ anchor, entry, onClose }: { anchor: PreviewAnchor; entry: TemplatePreviewEntry; onClose: () => void }) {
  const fitsRight = anchor.right + 12 + POP_WIDTH <= window.innerWidth
  const left = fitsRight ? anchor.right + 12 : Math.max(12, anchor.left - POP_WIDTH - 12)
  const top = Math.max(12, Math.min(anchor.top, window.innerHeight - 140))
  return (
    <div className="preview-pop" style={{ left, top }} onClick={onClose} title="点击关闭预览">
      <div className="preview-pop-title">
        {anchor.name}
        <span className="spacer" style={{ flex: 1 }} />
        <Icon name="x" size={12} />
      </div>
      {entry.status === 'loading' ? (
        <div className="preview-pop-state">
          <Icon name="refresh" size={13} />
          正在生成预览…
        </div>
      ) : entry.status === 'error' ? (
        <div className="preview-pop-state err">
          <Icon name="alert" size={13} />
          预览不可用
          <small>{entry.message}</small>
        </div>
      ) : (
        <>
          <img className="preview-pop-img" src={entry.url} alt={`模板「${anchor.name}」预览`} />
          <div className="preview-pop-foot">按模板测试数据渲染</div>
        </>
      )}
    </div>
  )
}
