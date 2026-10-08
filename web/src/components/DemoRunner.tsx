// 分界面交互演示引擎（迭代 121 · #280，决议 1/2：真实操作 + 一次性样例；DemoRunner 挂 Shell 级）。
// 结构复用迭代 119 Guide 资产：spotlight（box-shadow 巨影法）、锚点轮询降级（80ms/4s）、气泡视口约束、
// createPortal fixed 锚定；差异（#280 方案技术选型）：无阻断模式——demo 层 pointer-events:none、
// 仅气泡可点（真实操作必须可点，遮罩不拦截）；步骤定义携带 tab，跨页步骤经注入的 switchTab（#149
// 离开守卫统一入口）——演示本体在目标界面上下文运行（决议 2）。
// 推进口径（拍板 6）：完成真实操作自动推进（步骤 waitFor 纯数据观测 data-demo 钩子 / Shell tab）+
// 「下一步」手动兑底 + 跳过 / Esc 退出。
// 样例生命周期（决议 1 + 拍板 3/9）：演示启动即创建「示例·基础标签」并登记残留名单（localStorage 精确
// 名单，后端模板库为残留事实源）；「完成」默认清理（deleteTemplate + 逐名核对）、勾选「保留样例」只清
// 登记；跳过 / Esc 中途退出不即时清理——残留下次进入对应界面询问（DemoResiduePrompt，Workbench 挂载）。
// 已知边界（#280 方案 risks，如实收口）：清理失败静默降级保留登记（不阻断收尾、无新增文案）；Esc 为
// document 级监听与 Modal / Popover 同键并存沿用 Guide 既有边界（#170 ⑧）。

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { CSSProperties, ReactElement } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { localApi, serverApi } from '../lib/api/client'
import { useApp } from '../state/AppContext'
import type { TabId } from '../state/types'
import { Icon } from './Icon'
import { Modal } from './Modal'
import {
  DEMO_STEPS,
  SAMPLE_TEMPLATE_NAME,
  clearDemoResidue,
  getDemoResidue,
  notifyTemplatesChanged,
  registerDemoResidue,
  sampleTemplatePackage,
} from '../lib/helpDemo'
import type { DemoId } from '../lib/helpDemo'

/** 锚点轮询间隔与上限（口径对齐 Guide.tsx：超时降级全屏暗幕 + 居中气泡，不阻塞演示）。 */
const ANCHOR_POLL_MS = 80
const ANCHOR_POLL_MAX_MS = 4000
/** waitFor 观测轮询间隔（真实操作达成检测；比锚点轮询宽——业务操作以秒计）。 */
const WAIT_POLL_MS = 300
/** 气泡与视口边缘最小留白 / 与锚点间距 / 高亮外扩（口径对齐 Guide.tsx）。 */
const VIEWPORT_MARGIN = 8
const GAP = 10
const SPOT_PAD = 6

interface SpotRect {
  left: number
  top: number
  width: number
  height: number
}

interface DemoRunnerProps {
  /** 当前运行的演示（App Shell activeDemo 状态；无运行时组件不挂载）。 */
  demoId: DemoId
  /** 当前激活 tab（App Shell 状态；waitFor 的 tab 观测与切页判等数据源）。 */
  tab: TabId
  /** 切页回调——调用方必须传 App 的 switchTab（#149 设计器离开守卫统一入口）。 */
  onSwitchTab: (id: TabId) => void
  /** 结束回调（完成清理后 / 跳过 / Esc 共用；App 置 activeDemo = null 卸载本组件）。 */
  onFinish: () => void
}

export function DemoRunner({ demoId, tab, onSwitchTab, onFinish }: DemoRunnerProps) {
  const { t } = useTranslation('help')
  const { t: tGuide } = useTranslation('guide')
  const app = useApp()
  // 业务 API 跟随模式（与 Workbench/Designer 同口径：unknown 时不发请求，样例创建推迟重试）
  const biz = app.serverMode === 'server' ? serverApi : localApi
  const steps = DEMO_STEPS[demoId]
  const [stepIndex, setStepIndex] = useState(0)
  const [keepSample, setKeepSample] = useState(false)
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [placement, setPlacement] = useState<{ spot: SpotRect | null; bubble: { left: number; top: number } | null }>({ spot: null, bubble: null })
  const bubbleRef = useRef<HTMLDivElement>(null)
  const step = steps[stepIndex]
  const isLast = stepIndex === steps.length - 1

  const tabRef = useRef(tab)
  tabRef.current = tab
  const switchTabRef = useRef(onSwitchTab)
  switchTabRef.current = onSwitchTab
  const finishRef = useRef(onFinish)
  finishRef.current = onFinish
  const keepRef = useRef(keepSample)
  keepRef.current = keepSample

  // 样例创建（每场演示一次）：saveTemplate 覆盖写固定名样例（拍板 9）+ 登记残留精确名单；
  // 失败静默降级（方案 risks：建样例失配不阻断演示，后续步骤按真实库态呈现）。创建成功广播模板库
  // 变更，Workbench 列表刷新出样例卡（导出 / 按名识别步骤的锚点依赖）。
  const startedRef = useRef(false)
  useEffect(() => {
    if (startedRef.current) return
    startedRef.current = true
    void (async () => {
      try {
        await biz.saveTemplate(sampleTemplatePackage())
        registerDemoResidue(demoId, [SAMPLE_TEMPLATE_NAME])
        notifyTemplatesChanged()
      } catch {
        // 静默降级：样例创建失败不登记、不清理（无残留即无收尾删除），演示继续
      }
    })()
    // biz 随 serverMode 探测稳定；演示启动仅一次，依赖不入（eslint-disable 与 Guide 首见判定同口径）
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 步骤驱动：目标 tab 与当前不一致时经 switchTab 切页（拍板 4 跨页跟随）。tab 经 ref 取最新值——
  // 演示打开期间用户自行导航不被拽回步骤页（Guide 既有口径）。
  useEffect(() => {
    const target = steps[stepIndex].tab
    if (target !== tabRef.current) switchTabRef.current(target)
  }, [stepIndex, steps])

  // 锚点就位：轮询等锚点出现；超时降级为全屏暗幕 + 居中气泡（Guide 同法）
  useEffect(() => {
    setAnchorEl(null)
    const selector = steps[stepIndex].anchor
    if (!selector) return
    const startedAt = Date.now()
    let timer: ReturnType<typeof setTimeout> | undefined
    const poll = () => {
      const el = document.querySelector<HTMLElement>(selector)
      if (el) {
        setAnchorEl(el)
        return
      }
      if (Date.now() - startedAt < ANCHOR_POLL_MAX_MS) timer = setTimeout(poll, ANCHOR_POLL_MS)
    }
    poll()
    return () => clearTimeout(timer)
  }, [stepIndex, steps])

  // 真实操作达成观测（拍板 6 自动推进）：按步骤 waitFor 纯数据轮询 data-demo 钩子 / Shell tab；
  // 「下一步」手动兑底永在——等待永不成时用户可自行推进（方案 risks 卡步兑底）。
  useEffect(() => {
    const wait = steps[stepIndex].waitFor
    if (!wait) return
    let timer: ReturnType<typeof setTimeout> | undefined
    const poll = () => {
      let done = false
      if (wait.kind === 'tab') {
        done = tabRef.current === wait.tab
      } else {
        const el = document.querySelector<HTMLElement>(wait.selector)
        const raw = el?.getAttribute(wait.attr)
        done = wait.attr === 'data-demo-count' ? Number(raw ?? '0') >= (wait.min ?? 1) : raw !== null && raw === wait.value
      }
      if (done) {
        // 达成即推进（只推进一步；timer 链自然终止）
        setStepIndex((cur) => (cur === stepIndex ? cur + 1 : cur))
        return
      }
      timer = setTimeout(poll, WAIT_POLL_MS)
    }
    poll()
    return () => clearTimeout(timer)
  }, [stepIndex, steps])

  // 定位：spotlight 套锚点矩形（外扩 SPOT_PAD），气泡下方优先 / 放不下翻转 / 贴边（Guide 同法）；
  // 无可用矩形（超时 / 零矩形）时全屏暗幕 + 气泡居中。定位完成前 visibility:hidden 防闪烁。
  useLayoutEffect(() => {
    const compute = () => {
      const bubble = bubbleRef.current
      const bw = bubble?.offsetWidth ?? 0
      const bh = bubble?.offsetHeight ?? 0
      const rect = anchorEl?.getBoundingClientRect()
      if (!rect || rect.width <= 0 || rect.height <= 0) {
        setPlacement({
          spot: null,
          bubble: {
            left: Math.max(VIEWPORT_MARGIN, (window.innerWidth - bw) / 2),
            top: Math.max(VIEWPORT_MARGIN, (window.innerHeight - bh) / 2),
          },
        })
        return
      }
      const spot: SpotRect = {
        left: rect.left - SPOT_PAD,
        top: rect.top - SPOT_PAD,
        width: rect.width + SPOT_PAD * 2,
        height: rect.height + SPOT_PAD * 2,
      }
      let left = spot.left + spot.width / 2 - bw / 2
      left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - bw - VIEWPORT_MARGIN))
      let top = spot.top + spot.height + GAP
      if (top + bh > window.innerHeight - VIEWPORT_MARGIN) {
        top = Math.max(VIEWPORT_MARGIN, spot.top - GAP - bh)
      }
      setPlacement({ spot, bubble: { left, top } })
    }
    compute()
    window.addEventListener('resize', compute)
    return () => window.removeEventListener('resize', compute)
  }, [anchorEl, stepIndex])

  // 收尾（「完成」）：默认清理样例（删除 + 逐名核对，决议 1）；勾选「保留样例」只清登记（样例留下练习）。
  // 删除失败（后端不可达等）核对后仍有残留 → 保留登记不阻断收尾（下次进入询问，方案 risks 静默降级）。
  const finish = useCallback(() => {
    void (async () => {
      if (keepRef.current) {
        clearDemoResidue()
      } else {
        try {
          await biz.deleteTemplate(SAMPLE_TEMPLATE_NAME)
        } catch {
          // 缺失 / 删除失败交给下方模板库核对裁决
        }
        try {
          const list = await biz.listTemplates()
          if (!list.some((x) => x.name === SAMPLE_TEMPLATE_NAME)) clearDemoResidue()
          else notifyTemplatesChanged()
        } catch {
          // 模板库不可达：保留登记（下次进入再核对询问）
        }
      }
      notifyTemplatesChanged()
      finishRef.current()
    })()
  }, [biz])

  // 中途退出（跳过 / Esc）：不即时清理，残留登记原样保留（决议 1「残留下次进入时询问」）。
  const skip = useCallback(() => {
    finishRef.current()
  }, [])

  // Esc = 中途退出（对齐 Guide 的 document 级监听先例；同键并存已知边界见文件头）
  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') skip()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [skip])

  const spotStyle: CSSProperties | undefined = placement.spot
    ? {
        left: placement.spot.left,
        top: placement.spot.top,
        width: placement.spot.width,
        height: placement.spot.height,
      }
    : undefined

  const bubble: ReactElement = (
    <div
      ref={bubbleRef}
      className="guide-bubble demo-bubble"
      role="dialog"
      aria-label={t(`${step.key}.title`)}
      style={{
        left: placement.bubble?.left ?? 0,
        top: placement.bubble?.top ?? 0,
        visibility: placement.bubble ? 'visible' : 'hidden',
      }}
    >
      <div className="guide-bubble-title">{t(`${step.key}.title`)}</div>
      <p className="guide-bubble-body">{t(`${step.key}.body`)}</p>
      {isLast && (
        // 收尾勾选（决议 1「默认自动清理、可勾选保留」）；词条「保留样例」逐字取定稿 outro 正文引用
        <label className="demo-keep">
          <input type="checkbox" checked={keepSample} onChange={(ev) => setKeepSample(ev.target.checked)} />
          {t('demo.keepSample')}
        </label>
      )}
      <div className="guide-bubble-foot">
        {/* 气泡按钮为共享词条 btn.next / btn.skip / btn.done（guide 域，草稿勘察：tour 与 demo 不另造词） */}
        <button type="button" className="btn sm" onClick={skip}>
          {tGuide('btn.skip')}
        </button>
        <span className="guide-flex" />
        <button type="button" className="btn sm primary" onClick={() => (isLast ? finish() : setStepIndex(stepIndex + 1))}>
          {isLast ? tGuide('btn.done') : tGuide('btn.next')}
        </button>
      </div>
    </div>
  )

  // 无阻断：demo-layer pointer-events:none（styles.css），仅气泡可点——真实操作不被遮罩拦截（#280 技术选型）
  return createPortal(
    <div className="guide-layer demo-layer">
      {placement.spot ? <div className="guide-spot" style={spotStyle} /> : <div className="guide-veil" />}
      {bubble}
    </div>,
    document.body,
  )
}

/**
 * 中断残留询问（决议 1 + 拍板 3：进入对应界面即问；Modal 三选，#280 方案步骤 5）。
 * Workbench 挂载（#280 方案「计划改动文件」：残留询问挂载在 Workbench；workbench 为默认落地 tab，
 * designer 演示中断的残留同样在此询问）。三源核对：登记名单 × 模板库事实（biz.listTemplates）——
 * 登记在而模板已被用户手删 → 静默清登记不弹窗；「立即清理」逐名删除后复核，仍删不掉保留登记下次再问。
 * 「立即清理」属删除类操作：danger 实心按钮 + 本 Modal 即确认（决策 #161）。
 */
export function DemoResiduePrompt({
  ready,
  knownNames,
  onReload,
}: {
  /** 模板库探测就绪（AppContext serverMode ≠ unknown）——就绪前不核对。 */
  ready: boolean
  /** 当前模板库名单（Workbench 已加载列表；join 键做依赖，避免数组身份抖动重核对）。 */
  knownNames: readonly string[]
  /** 清理成功后刷新列表（Workbench load）。 */
  onReload: () => void
}) {
  const { t } = useTranslation('help')
  const app = useApp()
  const biz = app.serverMode === 'server' ? serverApi : localApi
  const [names, setNames] = useState<string[] | null>(null)
  const [busy, setBusy] = useState(false)
  const namesKey = knownNames.join('\n')
  const reloadRef = useRef(onReload)
  reloadRef.current = onReload

  useEffect(() => {
    if (!ready) return
    void (async () => {
      const record = getDemoResidue()
      if (!record) return
      try {
        const list = await biz.listTemplates()
        const known = namesKey ? namesKey.split('\n') : []
        const present = record.names.filter((n) => list.some((x) => x.name === n) || known.includes(n))
        if (present.length === 0) {
          // 登记在而模板库已无：核对确认已不存在即移除登记，不弹窗（方案 §首次判定与存储）
          clearDemoResidue()
          return
        }
        setNames((cur) => cur ?? present)
      } catch {
        // 模板库暂不可达：不询问（下次进入再核对）
      }
    })()
    // biz 稳定引用；namesKey 为 join 键（数组身份不入驻，防重核对循环）
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready, namesKey])

  const close = useCallback(() => setNames(null), [])

  const clean = useCallback(() => {
    void (async () => {
      setBusy(true)
      try {
        for (const n of names ?? []) {
          try {
            await biz.deleteTemplate(n)
          } catch {
            // 逐名容错：缺失视为已清理；删除失败交给下方复核
          }
        }
        const list = await biz.listTemplates()
        const left = (names ?? []).filter((n) => list.some((x) => x.name === n))
        if (left.length === 0) {
          clearDemoResidue()
          close()
          reloadRef.current()
        }
        // 仍有残留：保留登记与弹窗（静默降级，下次进入再问）
      } catch {
        // 模板库不可达：保留登记，下次进入再问
      } finally {
        setBusy(false)
      }
    })()
  }, [biz, close, names])

  const keep = useCallback(() => {
    // 保留 = 用户拍板留下练习（拍板 9：上轮保留样例被新一轮覆盖）——清登记，不再询问
    clearDemoResidue()
    close()
  }, [close])

  if (!names) return null
  return (
    <Modal title={t('residue.title')} onClose={close} footer={
      <>
        {/* 「暂不」= 关闭不删不记改动（下次进入再问）；「保留」= 留下并免除追问；「立即清理」= 删除类 danger（决策 #161） */}
        <button className="btn" onClick={close}>
          {t('residue.later')}
        </button>
        <button className="btn" onClick={keep}>
          {t('residue.keep')}
        </button>
        <button className="btn danger" onClick={clean} disabled={busy}>
          <Icon name="trash" size={13} />
          {t('residue.clean')}
        </button>
      </>
    }>
      <p>{t('residue.body')}</p>
      <p>{t('residue.bodyExtra')}</p>
    </Modal>
  )
}
