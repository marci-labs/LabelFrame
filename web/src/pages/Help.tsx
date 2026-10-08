// 帮助总界面：功能索引页（迭代 121 · #280，决议 2「只做功能索引」）——五菜单卡片各给
// 「是什么（title）/ 能干什么（body）」，点击卡片跳转对应界面（经 Shell switchTab 统一入口，
// 继承 #149 设计器离开守卫）；designer / workbench 卡带「去做演示」深链（跳转后自动开始对应演示，
// 演示本体在目标界面上下文运行——DemoRunner 挂 Shell 级，本页不承载演示步骤）；jobs / settings/data
// 本轮仅索引图文、不渲染演示按钮（拍板 8，V3 演示到位再露出）。
// 卡片数据纯数据化（拍板 11「可复用结构」）：HELP_CARDS 为模块级纯数据，server 版后续换一组卡数据
// 即可低成本接入；server 构建本页不挂载（App 层 CLIENT_TABS 单点裁剪 + main 渲染 !isServerUi 双保险）。
// 文案：help 域语义 key，逐字来源 = #280「✅ 文案定稿 v1」。

import { useTranslation } from 'react-i18next'
import { Icon } from '../components/Icon'
import { HELP_CARDS } from '../lib/helpDemo'
import type { DemoId } from '../lib/helpDemo'
import type { TabId } from '../state/types'

interface HelpProps {
  /** 跳转对应界面——调用方必须传 App 的 switchTab（#150 设计器离开守卫统一入口）。 */
  onOpenTab: (id: TabId) => void
  /** 发起「去做演示」深链（App 的 requestDemo：登记 pendingDemo → 跳目标页 → 自动开始演示）。 */
  onStartDemo: (id: DemoId) => void
}

export function Help({ onOpenTab, onStartDemo }: HelpProps) {
  const { t } = useTranslation('help')
  // 页面标题复用导航词条 nav.help（shell 域「帮助 / Help」）——定稿无独立页题词条，不新造文案
  const { t: tShell } = useTranslation('shell')
  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">{tShell('nav.help')}</div>
      </div>
      <div className="help-cards" data-help="cards">
        {HELP_CARDS.map((card) => (
          <div
            key={card.id}
            className="help-card"
            data-help={`card-${card.id}`}
            role="button"
            tabIndex={0}
            aria-label={t(`card.${card.id}.title`)}
            onClick={() => onOpenTab(card.tab)}
            onKeyDown={(ev) => {
              if (ev.key === 'Enter') onOpenTab(card.tab)
            }}
          >
            <div className="help-card-head">
              <Icon name={card.icon} />
              <span className="help-card-title">{t(`card.${card.id}.title`)}</span>
            </div>
            <p className="help-card-body">{t(`card.${card.id}.body`)}</p>
            <div className="help-card-foot">
              {card.demo ? (
                // 深链按钮（定稿行内按钮词「上手试一遍 / Try It Yourself」，拍板组 1）：跳目标页并自动开始演示
                <button
                  type="button"
                  className="btn sm primary"
                  data-help={`demo-${card.demo}`}
                  onClick={(ev) => {
                    ev.stopPropagation()
                    onStartDemo(card.demo!)
                  }}
                >
                  {t(`card.${card.id}.action`)}
                </button>
              ) : (
                // 跳转按钮（定稿行内按钮词「进入 / Open」）
                <button type="button" className="btn sm" onClick={(ev) => {
                  ev.stopPropagation()
                  onOpenTab(card.tab)
                }}>
                  {t(`card.${card.id}.action`)}
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
