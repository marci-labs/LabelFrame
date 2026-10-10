// @vitest-environment jsdom
// 迭代 126（#308 AC-02）：帮助页测试——功能索引 / 五界面文章 / 词条单源 / 深链回调 / 返回索引。

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Help } from './Help'
import { HELP_PAGES } from '../lib/help'
// 组件级单测不经 App 引用链，显式 import 初始化 i18next 单例（语言包资源就位，词条断言可用）
import '../i18n'

afterEach(cleanup)

describe('帮助页功能索引（AC-02）', () => {
  it('五界面索引卡齐全，标题与页面副标题渲染', () => {
    render(<Help onOpenInUi={() => {}} />)
    expect(screen.getByText('帮助')).toBeTruthy()
    expect(screen.getByText('五个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
    for (const title of ['工作台', '设计器', '数据与打印', '作业历史', '设置']) {
      expect(screen.getAllByText(title).length).toBeGreaterThan(0)
    }
  })

  it('点击索引卡进入对应文章（工作台篇：导览段＋三个纯文章小节、无「去界面查看」）', () => {
    render(<Help onOpenInUi={() => {}} />)
    fireEvent.click(screen.getByText('新建、导入模板并按分组管理'))
    expect(screen.getByText(/工作台是模板的起点/)).toBeTruthy()
    expect(screen.getByText('模板卡片与缩略图')).toBeTruthy()
    expect(screen.getByText('卡片操作')).toBeTruthy()
    expect(screen.getByText('搜索与分组过滤')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /去界面查看/ })).toBeNull()
  })

  it('设计器文章：文档点小节带「去界面查看」深链，回调带 tab＋anchor；纯文章小节无按钮', () => {
    const onOpenInUi = vi.fn()
    render(<Help onOpenInUi={onOpenInUi} />)
    fireEvent.click(screen.getByText('在画布上设计标签版式'))
    // 词条与文档点气泡同源（单源双视图）：正文与 help.topic.designer.fill 一致
    expect(screen.getByText(/「来源」决定打印内容：固定值立即按所填内容渲染/)).toBeTruthy()
    // 纯文章 4 节无深链；文档点 10 节有深链
    const openButtons = screen.getAllByRole('button', { name: /去界面查看/ })
    expect(openButtons.length).toBe(10)
    fireEvent.click(openButtons[0])
    expect(onOpenInUi).toHaveBeenCalledWith('designer', 'designer.position')
  })

  it('设置文章：7 个卡片主题全部可深链；返回索引回到功能索引视图', () => {
    const onOpenInUi = vi.fn()
    render(<Help onOpenInUi={onOpenInUi} />)
    fireEvent.click(screen.getByText('语言、服务端地址、打印机与插件等本机配置'))
    const openButtons = screen.getAllByRole('button', { name: /去界面查看/ })
    expect(openButtons.length).toBe(7)
    fireEvent.click(openButtons[6])
    expect(onOpenInUi).toHaveBeenCalledWith('settings', 'settings.plugins')
    fireEvent.click(screen.getByRole('button', { name: /返回索引/ }))
    expect(screen.getByText('五个页面的使用文章；界面内「?」就近说明')).toBeTruthy()
  })

  it('主题清单纯数据自洽：有 anchor 必有 target，且 anchor = <page>.<id>（深链与文档点同键）', () => {
    for (const page of HELP_PAGES) {
      for (const topic of page.topics) {
        if (topic.anchor) {
          expect(topic.target).toBe(page.tab)
          expect(topic.anchor).toBe(`${page.page}.${topic.id}`)
        } else {
          expect(topic.target).toBeUndefined()
        }
      }
    }
  })
})
