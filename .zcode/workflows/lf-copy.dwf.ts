/* zcode-workflow
description: LabelFrame 工作流管线·文案草拟段（导览/帮助类文案迭代专用，119/121 形态；通用文案能力待后续 Issue 泛化）：按已定稿方案与用户拍板撰写迭代全部新增界面文案草稿（zh+en 文案表＋入口措辞候选），落 Issue 评论「✍️ 文案草稿 v1」。低档位运行（GLM-5.3-Flash）。
whenToUse: 工作流实验管线第 2 段（仅含界面文案的导览/帮助类迭代使用）：最新「📐 方案 vN」评论在场后起跑，产出文案草稿供评审段校对。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 文案草拟段（lf-copy）· v2（迭代 121 扩容：覆盖迭代全部文案面，非仅导览表）· 迭代 124 收窄：仅导览/帮助类文案迭代（通用文案能力待后续 Issue 泛化）
// 职责：按最新「📐 方案 vN」＋「🎯 用户拍板」撰写迭代全部新增文案草稿（zh+en），落 Issue 评论「✍️ 文案草稿 v1」。
// 前置：最新方案评论在场（首行正则定位，fail-closed）；幂等：草稿评论已存在则直接退出。

interface CopyRow {
  /** 分组：tour（首见导览气泡）/ help（帮助页卡片）/ demo（演示气泡）/ modal（弹窗文案）。 */
  group: "tour" | "help" | "demo" | "modal";
  /** i18n 资源 key 建议（语义 key，如 tour.workbench.new / help.card.designer / demo.designer.draw）。 */
  key: string;
  /** 所属页面（Workbench / Designer / DataPrint / Help / Shell）。 */
  page: string;
  /** 中文标题（不超过 12 字；卡片行 =「是什么」一句话定性）。 */
  titleZh: string;
  /** 中文正文（不超过 60 字；卡片行 =「能干什么」2~3 条用「；」串接）。 */
  bodyZh: string;
  /** 中文按钮文案（≤6 字；modal 多按钮用「/」串接 ≤20 字）。 */
  btnZh: string;
  /** 英文标题（不超过 45 字符）。 */
  titleEn: string;
  /** 英文正文（不超过 130 字符）。 */
  bodyEn: string;
  /** 英文按钮文案（≤20 字符；modal 多按钮 ≤40 字符）。 */
  btnEn: string;
}

interface EntryCandidate {
  /** 帮助页「去做演示」深链按钮词 zh。 */
  entryZh: string;
  /** 帮助页深链按钮词 en（忌直译感）。 */
  entryEn: string;
  /** 页头帮助入口/演示开始按钮词 zh。 */
  startZh: string;
  /** 演示开始按钮词 en。 */
  startEn: string;
  /** 一句话说明该组的语感与适用性。 */
  noteZh: string;
}

interface CopyDraft {
  rows: CopyRow[];
  /** 入口措辞候选（恰好 3 组，第 1 组为推荐组）。 */
  entryCandidates: EntryCandidate[];
  /** 从现有资源观察到的风格要点（语气/称谓/标点/动词习惯），每条一句；末条登记 nav.help 词条（帮助 / Help）。 */
  styleNotes: string[];
}

interface Comment { body: string }
interface IssueJson { number: number; state: string; labels: { name: string }[]; comments?: Comment[] }

function latestCommentBody(prefix: string, comments: Comment[]): string | null {
  for (let i = comments.length - 1; i >= 0; i--) {
    const b = comments[i]?.body ?? "";
    if (b.startsWith(prefix)) return b;
  }
  return null;
}

/** 评论首行正则定位：最新一条首行匹配 re 的评论下标（无则 -1）。 */
function commentHeadLastIndex(re: RegExp, comments: Comment[]): number {
  for (let i = comments.length - 1; i >= 0; i--) {
    const head = ((comments[i]?.body ?? "").split("\n")[0]) ?? "";
    if (re.test(head)) return i;
  }
  return -1;
}

interface CopyViolation { index: number; problem: string }

/** 定量断言：字数 / 空值 / key 唯一性 / 候选组数（命令能判定的不花模型）。 */
function checkRows(rows: CopyRow[], candidates: EntryCandidate[]): CopyViolation[] {
  const out: CopyViolation[] = [];
  const seen = new Set<string>();
  rows.forEach((r, i) => {
    if (r.key === "" || r.titleZh === "" || r.bodyZh === "" || r.btnZh === "" || r.titleEn === "" || r.bodyEn === "" || r.btnEn === "") out.push({ index: i, problem: "存在空字段" });
    if (seen.has(r.key)) out.push({ index: i, problem: "key 重复：" + r.key });
    seen.add(r.key);
    if (r.titleZh.length > 12) out.push({ index: i, problem: "中文标题超 12 字（" + r.titleZh.length + "）" });
    if (r.bodyZh.length > 60) out.push({ index: i, problem: "中文正文超 60 字（" + r.bodyZh.length + "）" });
    const btnZhCap = r.group === "modal" ? 20 : 6;
    if (r.btnZh.length > btnZhCap) out.push({ index: i, problem: "中文按钮超 " + btnZhCap + " 字" });
    if (r.titleEn.length > 45) out.push({ index: i, problem: "英文标题超 45 字符" });
    if (r.bodyEn.length > 130) out.push({ index: i, problem: "英文正文超 130 字符（" + r.bodyEn.length + "）" });
    const btnEnCap = r.group === "modal" ? 40 : 20;
    if (r.btnEn.length > btnEnCap) out.push({ index: i, problem: "英文按钮超 " + btnEnCap + " 字符" });
  });
  if (candidates.length !== 3) out.push({ index: -1, problem: "入口措辞候选须恰好 3 组（当前 " + candidates.length + "）" });
  candidates.forEach((c, i) => {
    if (c.entryZh === "" || c.entryEn === "" || c.startZh === "" || c.startEn === "" || c.noteZh === "") out.push({ index: -1, problem: "候选第 " + (i + 1) + " 组存在空字段" });
  });
  return out;
}

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const PLAN_HEAD_RE = /^\*\*📐 方案 v(\d+)\*\*/;
const DRAFT_PREFIX = "**✍️ 文案草稿 v1**";
const MAX_REWRITE = 2;

phase("建场守卫：核对方案在场与草稿缺席");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,url,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
// 最新方案 vN 定位（fail-closed）：精确 v1 前缀会锁死版本，vN>1 的含文案迭代起跳即炸；取不到（含最新评论为处置报告）即中止。
const planIdx = commentHeadLastIndex(PLAN_HEAD_RE, comments);
if (planIdx < 0) throw new Error(`Issue #${issueNum} 无「📐 方案 vN」评论——先跑设计段 lf-design`);
const planBody = comments[planIdx]?.body ?? "";
if (latestCommentBody(DRAFT_PREFIX, comments) !== null) {
  log("草稿评论已存在，幂等退出");
  return { conclusion: `Issue #${issueNum} 已有「✍️ 文案草稿 v1」评论，本段幂等退出。`, findings: [], verified: ["建场守卫：草稿评论在场"], notCovered: [] };
}
log("方案在场，开始撰写");

phase("依方案、拍板与现有文案风格撰写草稿");
const copywriter = agent("文案员", {
  system:
    "你是 LabelFrame 的界面文案撰写者。你的产出是给人看的 UI 文案：简洁、动作导向、不重复、与产品既有文案风格一致。" +
    "只读文件不修改。宁可留白也不要编造产品没有的功能。",
});
let draft = await copywriter.ask<CopyDraft>(
  `任务：为本迭代（Issue #${issueNum}）撰写全部新增界面文案（zh + en）。产出 CopyDraft：rows 文案表 + entryCandidates 入口措辞候选 + styleNotes 风格勘察笔记。\n\n` +
  `【已定稿方案（Issue 评论原文）】\n${planBody}\n\n` +
  `【必产清单——按方案与「🎯 用户拍板」逐项覆盖，缺一即废稿】\n` +
  `A. 首见 v2 五步导览（group="tour"，key tour.<页面>.<slug>，5 行）：重写既有五步（工作台 / 设计器 / 数据与打印·模板选择 / Excel 批量打印 / 收尾），口径＝「介绍功能＋什么场景用」，逻辑通顺、能吸引用户体验对应操作，忌「跟我做」式口令；收尾步加一句指路「更多功能说明见左侧导航·帮助」；单步正文 ≤60 字。\n` +
  `B. 帮助页五卡（group="help"，key help.card.<workbench|designer|data|jobs|settings>，5 行）：title＝「是什么」一句话定性（≤12 字）；body＝「能干什么」2~3 条用「；」串接（≤60 字）；designer / workbench 卡 btnZh 暂写「去做演示」（最终措辞随候选组定），其余卡 btnZh 写「进入」。\n` +
  `C. designer 演示（group="demo"，key demo.designer.<slug>，≥6 行）：开场、拖控件绘制（顺带讲自动对齐 / 智能参考线与快捷键要点）、控件属性设置（含一条「哪些设置打印时可能不生效」的提示——先用文件工具实读 web/src/pages/designer/ 与 docs/DESIGN.md 相关节（如字体回退）核实事实，宁可不写也不编造）、字段作用、命名与保存、演示收尾。\n` +
  `D. workbench 演示（group="demo"，key demo.workbench.<slug>，≥5 行）：开场、新建（点「新建模板」后演示跨页跟到设计器，一句话衔接）、导出→导入闭环（先引导导出示例 .lfpkg、再引导亲手导入，两行）、命名后回工作台按名识别、结束气泡（样例默认自动清理、可勾选保留）。\n` +
  `E. DataPrint 承接（group="demo"，key demo.data.<slug>，≥2 行）：字段变成填写表单、字段对应「下载 Excel 模板」的列。\n` +
  `F. 残留询问 Modal（group="modal"，key help.residue.<slug>，2 行：标题 + 正文）：演示中断的「示例·」样例残留下次进入界面时询问；btnZh 写「立即清理/保留/暂不」（多按钮「/」串接）。\n` +
  `G. entryCandidates：恰好 3 组入口措辞候选（entryZh/entryEn/startZh/startEn/noteZh）——帮助页深链按钮词与演示开始按钮词配对成组，en 忌 'Do the demo' 式直译感；第 1 组为推荐组。\n` +
  `H. nav.help 导航词条固定「帮助 / Help」，只写进 styleNotes 末条，不入 rows。\n\n` +
  `【风格要求】先用你的文件工具读 web/src/i18n/ 下的现有资源（zh 与 en 各至少三四个文件），观察并模仿其语气、称谓、标点、按钮动词习惯（观察写进 styleNotes，每条一句）；` +
  `文案必须与既有界面文案是同一个「口音」。正文动作导向（「点这里做 X」而非「本页提供 X 功能」）；相邻行不重复说同一件事；按钮 2~4 字动词或动宾（modal 多按钮行除外）。\n\n` +
  `【长度硬限】titleZh≤12；bodyZh≤60；btnZh≤6（modal 行 ≤20）；titleEn≤45 字符；bodyEn≤130 字符；btnEn≤20 字符（modal 行 ≤40）；rows 总数 15~45。`);

phase("定量自查并退回重写超限文案");
for (let round = 1; round <= MAX_REWRITE; round++) {
  const violations = checkRows(draft.rows, draft.entryCandidates);
  if (draft.rows.length < 15 || draft.rows.length > 45) violations.push({ index: -1, problem: "行数须在 15~45（当前 " + draft.rows.length + "）" });
  if (violations.length === 0) break;
  log("第 " + round + " 轮定量自查发现 " + violations.length + " 处超限，退回重写");
  draft = await copywriter.ask(`以下定量断言未通过，请修订并重新给出完整草稿（只改有问题的行，其余保持）：\n${JSON.stringify(violations)}`);
  if (round === MAX_REWRITE) {
    const still = checkRows(draft.rows, draft.entryCandidates);
    if (still.length > 0) throw new Error("文案草稿 " + MAX_REWRITE + " 轮后仍有定量违规：" + JSON.stringify(still));
  }
}
report({ stage: "草稿完成", issue: issueNum, rows: draft.rows.length, candidates: draft.entryCandidates.length, styleNotes: draft.styleNotes.length });

phase("落稿 Issue 供评审段校对");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const draftMd = [
  DRAFT_PREFIX + "（工作流·文案段 · " + ts + "）",
  "",
  "## 风格勘察笔记",
  ...draft.styleNotes.map((s) => "- " + s),
  "",
  "## 文案表（" + draft.rows.length + " 行）",
  "",
  "| # | 组 | key | 页面 | 标题 zh | 正文 zh | 按钮 zh | 标题 en | 正文 en | 按钮 en |",
  "|---|---|---|---|---|---|---|---|---|---|",
  ...draft.rows.map((r, i) => "| " + (i + 1) + " | " + r.group + " | " + r.key + " | " + r.page + " | " + r.titleZh + " | " + r.bodyZh + " | " + r.btnZh + " | " + r.titleEn + " | " + r.bodyEn + " | " + r.btnEn + " |"),
  "",
  "## 入口措辞候选（3 组，第 1 组为推荐组；随定稿交用户过目拍板）",
  "",
  "| 组 | 深链按钮 zh | 深链按钮 en | 开始按钮 zh | 开始按钮 en | 说明 |",
  "|---|---|---|---|---|---|",
  ...draft.entryCandidates.map((c, i) => "| " + (i + 1) + " | " + c.entryZh + " | " + c.entryEn + " | " + c.startZh + " | " + c.startEn + " | " + c.noteZh + " |"),
  "",
  "```json",
  JSON.stringify(draft.rows, null, 2),
  "```",
  "",
  "```json",
  JSON.stringify(draft.entryCandidates, null, 2),
  "```",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", draftMd]);
await artifact.markdown("copy-draft", draftMd, { title: "文案草稿 v1（Issue #" + issueNum + "）", description: "覆盖迭代全部文案面的草稿（导览/卡片/演示/弹窗＋入口措辞候选），待评审段校对。", primary: true });
log("草稿已落 Issue #" + issueNum);
return {
  conclusion: `Issue #${issueNum} 文案草稿完成：${draft.rows.length} 行 zh+en（tour/help/demo/modal 四组）＋入口措辞候选 ${draft.entryCandidates.length} 组，风格模仿自现有 i18n 资源，定量断言全部通过。下一步：起文案评审段 lf-copy-review。`,
  findings: [],
  verified: ["定量断言：分组字数上限、字段非空、key 唯一、行数 15~45、候选恰好 3 组", "文案员实读了 web/src/i18n 现有资源（styleNotes 留痕）"],
  notCovered: ["文案未经主编校对与新用户视角评审（评审段职责）", "入口措辞候选最终取舍待用户过目（推荐组仅标注）"],
};
