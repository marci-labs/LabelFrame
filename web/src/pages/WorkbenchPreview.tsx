// 工作台模板悬停预览浮层（迭代 46）：加载中 / 失败态 / 预览图三态展示；
// fixed 定位避开滚动容器裁剪，pointer-events 关闭——浮层永不拦截鼠标，列表 hover / 操作不受影响。

import { Icon } from '../components/Icon'
import type { TemplatePreviewEntry } from './useTemplatePreview'

/** 悬停行位置锚点（触发时取一次 boundingClientRect）。 */
export interface PreviewAnchor {
  name: string
  top: number
  left: number
  right: number
}

const POP_WIDTH = 296

/** 预览浮层：优先停在悬停行右侧，视口不够则翻到左侧。 */
export function TemplatePreviewPop({ anchor, entry }: { anchor: PreviewAnchor; entry: TemplatePreviewEntry }) {
  const fitsRight = anchor.right + 12 + POP_WIDTH <= window.innerWidth
  const left = fitsRight ? anchor.right + 12 : Math.max(12, anchor.left - POP_WIDTH - 12)
  const top = Math.max(12, Math.min(anchor.top, window.innerHeight - 140))
  return (
    <div className="preview-pop" style={{ left, top }}>
      <div className="preview-pop-title">{anchor.name}</div>
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
