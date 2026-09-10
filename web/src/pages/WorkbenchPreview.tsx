// 工作台模板预览放大灯箱（迭代 46，2026-09-10 二次验收修订：行旁小浮层 → 居中大图模态）：
// 半透明暗背景 + 居中卡片，图片按真实宽高比约束在视口 75% 且 ≤900px（203 DPI 原生约 560px，
// 再往上是放大插值、接受轻微发虚）；Esc / 点背景 / 点 × 关闭；点卡片本体不关闭。

import { Icon } from '../components/Icon'
import type { TemplatePreviewEntry } from './useTemplatePreview'

/** 居中灯箱：背景点击关闭；卡片内点击（图片 / 标题）不冒泡关闭。 */
export function TemplatePreviewModal({ name, entry, onClose }: { name: string; entry: TemplatePreviewEntry; onClose: () => void }) {
  return (
    <div className="preview-modal" onClick={onClose} aria-hidden="true">
      <div
        className="preview-modal-card"
        role="dialog"
        aria-label={`模板「${name}」预览`}
        onClick={(ev) => ev.stopPropagation()}
      >
        <div className="preview-modal-title">
          {name}
          <span className="spacer" style={{ flex: 1 }} />
          <button className="preview-modal-close" onClick={onClose} title="关闭预览（Esc）">
            <Icon name="x" size={13} />
          </button>
        </div>
        {entry.status === 'loading' ? (
          <div className="preview-modal-state">
            <Icon name="refresh" size={13} />
            正在生成预览…
          </div>
        ) : entry.status === 'error' ? (
          <div className="preview-modal-state err">
            <Icon name="alert" size={13} />
            预览不可用
            <small>{entry.message}</small>
          </div>
        ) : (
          <>
            <img className="preview-modal-img" src={entry.url} alt={`模板「${name}」预览`} />
            <div className="preview-modal-foot">按模板测试数据渲染</div>
          </>
        )}
      </div>
    </div>
  )
}
