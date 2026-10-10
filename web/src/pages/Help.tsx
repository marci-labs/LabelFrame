// 帮助页（迭代 126 · #308，决策 #176①）：功能索引卡＋五界面文章（页面导览段＋主题小节，
// 拍板 1：索引卡＋单栏排版）。文章小节与界面内「?」文档点共用同一词条（lib/help.ts 单源
// 双视图，拍板 5）；有文档点的小节提供「去界面查看」深链——经调用方注入的回调跳转目标
// 界面并自动弹出对应文档点（App Shell 统一轮询锚点与超时降级）。
// 纯 client 构建：help tab 仅在 CLIENT_TABS，server 构建无导航入口、本页不可达（AC-07）。

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from '../components/Icon'
import { HELP_PAGES } from '../lib/help'
import type { HelpPageId } from '../lib/help'
import type { TabId } from '../state/types'

interface HelpProps {
  /** 深链：切到目标界面并弹出对应文档点（tab 切换走 Shell 统一入口，继承离开守卫）。 */
  onOpenInUi: (tab: TabId, anchor: string) => void
}

export function Help({ onOpenInUi }: HelpProps) {
  const { t } = useTranslation('help')
  // 页内两级视图：null = 功能索引；否则显示该页文章
  const [article, setArticle] = useState<HelpPageId | null>(null)
  const page = HELP_PAGES.find((p) => p.page === article)

  if (!page) {
    return (
      <div className="page help-page">
        <div className="page-head">
          <div className="page-title">
            {t('page.title')}
            <small>{t('page.subtitle')}</small>
          </div>
        </div>
        <div className="help-index">
          {HELP_PAGES.map((p) => (
            <button key={p.page} type="button" className="panel help-card" onClick={() => setArticle(p.page)}>
              <span className="help-card-title">{t(`index.${p.page}.title`)}</span>
              <span className="help-card-desc">{t(`index.${p.page}.desc`)}</span>
            </button>
          ))}
        </div>
      </div>
    )
  }

  return (
    <div className="page help-page">
      <div className="page-head">
        <div className="page-title">{t(`index.${page.page}.title`)}</div>
        <span className="spacer" />
        <button type="button" className="btn sm" onClick={() => setArticle(null)}>
          <Icon name="back" size={13} />
          {t('ui.backToIndex')}
        </button>
      </div>
      <div className="help-article">
        <p className="help-article-intro">{t(`article.${page.page}.intro`)}</p>
        {page.topics.map((topic) => (
          <section key={topic.id} className="help-topic">
            <h3>{t(`topic.${page.page}.${topic.id}.title`)}</h3>
            <p>{t(`topic.${page.page}.${topic.id}.body`)}</p>
            {topic.anchor && topic.target && (
              <button type="button" className="btn sm" onClick={() => onOpenInUi(topic.target!, topic.anchor!)}>
                <Icon name="link" size={13} />
                {t('ui.openInUi')}
              </button>
            )}
          </section>
        ))}
      </div>
    </div>
  )
}
