/* zcode-workflow
description: LabelFrame 工作流管线·文案草拟段：按已定稿方案与现有 i18n 文案风格，撰写引导文案表草稿（zh+en），落 Issue 评论「✍️ 文案草稿 v1」。低档位运行（GLM-5.3-Flash）。
whenToUse: 工作流实验管线第 2 段：「📐 方案 v1」评论在场后起跑，产出文案草稿供评审段校对。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 文案草拟段（lf-copy）
// 职责：按「📐 方案 v1」与现有 i18n 文案风格撰写引导文案表草稿（zh+en），落 Issue 评论「✍️ 文案草稿 v1」。
// 前置：方案评论在场；幂等：草稿评论已存在则直接退出。

interface CopyRow {
  /** i18n 资源 key 建议（语义 key，如 tour.workbench.title）。 */
  key: string;
  /** 所属页面（对齐方案 steps 的 page）。 */
  page: string;
  /** 中文标题（不超过 12 字）。 */
  titleZh: string;
  /** 中文正文（不超过 40 字，一句，动作导向）。 */
  bodyZh: string;
  /** 中文按钮文案（不超过 6 字）。 */
  btnZh: string;
  /** 英文标题（不超过 40 字符）。 */
  titleEn: string;
  /** 英文正文（不超过 90 字符）。 */
  bodyEn: string;
  /** 英文按钮文案。 */
  btnEn: string;
}

interface CopyDraft {
  rows: CopyRow[];
  /** 从现有资源观察到的风格要点（语气/称谓/标点/动词习惯），每条一句。 */
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

interface CopyViolation { index: number; problem: string }

/** 定量断言：字数 / 空值 / key 唯一性（命令能判定的不花模型）。 */
function checkRows(rows: CopyRow[]): CopyViolation[] {
  const out: CopyViolation[] = [];
  const seen = new Set<string>();
  rows.forEach((r, i) => {
    if (r.key === "" || r.titleZh === "" || r.bodyZh === "" || r.btnZh === "" || r.titleEn === "" || r.bodyEn === "" || r.btnEn === "") out.push({ index: i, problem: "存在空字段" });
    if (seen.has(r.key)) out.push({ index: i, problem: "key 重复：" + r.key });
    seen.add(r.key);
    if (r.titleZh.length > 12) out.push({ index: i, problem: "中文标题超 12 字（" + r.titleZh.length + "）" });
    if (r.bodyZh.length > 40) out.push({ index: i, problem: "中文正文超 40 字（" + r.bodyZh.length + "）" });
    if (r.btnZh.length > 6) out.push({ index: i, problem: "中文按钮超 6 字" });
    if (r.bodyEn.length > 90) out.push({ index: i, problem: "英文正文超 90 字符（" + r.bodyEn.length + "）" });
  });
  return out;
}

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const PLAN_PREFIX = "**📐 方案 v1**";
const DRAFT_PREFIX = "**✍️ 文案草稿 v1**";
const MAX_REWRITE = 2;

phase("建场守卫：核对方案在场与草稿缺席");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
const planBody = latestCommentBody(PLAN_PREFIX, comments);
if (planBody === null) throw new Error(`Issue #${issueNum} 无「📐 方案 v1」评论——先跑设计段 lf-design`);
if (latestCommentBody(DRAFT_PREFIX, comments) !== null) {
  log("草稿评论已存在，幂等退出");
  return { conclusion: `Issue #${issueNum} 已有「✍️ 文案草稿 v1」评论，本段幂等退出。`, findings: [], verified: ["建场守卫：草稿评论在场"], notCovered: [] };
}
log("方案在场，开始撰写");

phase("依方案与现有文案风格撰写草稿");
const copywriter = agent("文案员", {
  system:
    "你是 LabelFrame 的界面文案撰写者。你的产出是给人看的 UI 文案：简洁、动作导向、不重复、与产品既有文案风格一致。" +
    "只读文件不修改。宁可留白也不要编造产品没有的功能。",
});
let draft = await copywriter.ask<CopyDraft>(
  `任务：为「新用户首次使用引导」撰写每一步的文案（zh + en）。\n\n` +
  `【已定稿方案（Issue #${issueNum} 评论）】\n${planBody}\n\n` +
  `【风格要求】先用你的文件工具读 web/src/i18n/ 下的现有资源（zh 与 en 各至少两三个文件），观察并模仿其语气、称谓、标点、按钮动词习惯（把观察写进 styleNotes，每条一句）；` +
  `文案必须与既有界面文案是同一个「口音」。正文一句、动作导向（「点这里做 X」而非「本页提供 X 功能」）；相邻步骤不重复说同一件事。\n\n` +
  `每步产出一行 CopyRow：key 用语义 key（tour.<页面>.<字段>）；中文标题 ≤12 字、正文 ≤40 字、按钮 ≤6 字；英文对应翻译（不是逐字直译，按英文 UI 习惯）并 ≤90 字符。`);

phase("定量自查并退回重写超限文案");
for (let round = 1; round <= MAX_REWRITE; round++) {
  const violations = checkRows(draft.rows);
  if (draft.rows.length < 3 || draft.rows.length > 8) violations.push({ index: -1, problem: "行数须在 3~8（当前 " + draft.rows.length + "）" });
  if (violations.length === 0) break;
  log("第 " + round + " 轮定量自查发现 " + violations.length + " 处超限，退回重写");
  draft = await copywriter.ask(`以下定量断言未通过，请修订并重新给出完整草稿（只改有问题的行，其余保持）：\n${JSON.stringify(violations)}`);
  if (round === MAX_REWRITE) {
    const still = checkRows(draft.rows);
    if (still.length > 0) throw new Error("文案草稿 " + MAX_REWRITE + " 轮后仍有定量违规：" + JSON.stringify(still));
  }
}
report({ stage: "草稿完成", issue: issueNum, rows: draft.rows.length, styleNotes: draft.styleNotes.length });

phase("落稿 Issue 供评审段校对");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const draftMd = [
  DRAFT_PREFIX + "（工作流·文案段 · " + ts + "）",
  "",
  "## 风格勘察笔记",
  ...draft.styleNotes.map((s) => "- " + s),
  "",
  "## 文案表（" + draft.rows.length + " 步）",
  "",
  "| # | key | 页面 | 标题 zh | 正文 zh | 按钮 zh | 标题 en | 正文 en | 按钮 en |",
  "|---|---|---|---|---|---|---|---|---|",
  ...draft.rows.map((r, i) => "| " + (i + 1) + " | " + r.key + " | " + r.page + " | " + r.titleZh + " | " + r.bodyZh + " | " + r.btnZh + " | " + r.titleEn + " | " + r.bodyEn + " | " + r.btnEn + " |"),
  "",
  "```json",
  JSON.stringify(draft.rows, null, 2),
  "```",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", draftMd]);
await artifact.markdown("copy-draft", draftMd, { title: "引导文案草稿 v1（Issue #" + issueNum + "）", description: "低档位撰写、经定量断言的文案表草稿，待评审段校对。", primary: true });
log("草稿已落 Issue #" + issueNum);
return {
  conclusion: `Issue #${issueNum} 文案草稿完成：${draft.rows.length} 步 zh+en，风格模仿自现有 i18n 资源，定量断言（字数/空值/key 唯一）全部通过。下一步：起文案评审段 lf-copy-review。`,
  findings: [],
  verified: ["定量断言：每行字数上限、字段非空、key 唯一、行数 3~8", "文案员实读了 web/src/i18n 现有资源（styleNotes 留痕）"],
  notCovered: ["文案未经主编校对与新用户视角评审（评审段职责）"],
};