/* zcode-workflow
description: LabelFrame 工作流管线·设计段：读 Issue 与仓库现状，产出实施方案（含独立方案评审），落 Issue 评论「📐 方案
  v1」。高档位运行（GLM-5.3）。
whenToUse: 工作流实验管线第 1 段：Issue 立项并带「工作流接管」标签后起跑，产出方案供用户确认。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 设计段（lf-design）
// 职责：读 Issue 与仓库现状，产出「新用户引导」实施方案（含独立方案评审），落 Issue 评论「📐 方案 v1」。
// 前置：Issue 带「工作流接管」标签；幂等：方案评论已存在则直接退出。

interface GuideStep {
  /** 目标页：页面组件名（如 Workbench / Designer / DataPrint），必须真实存在于 web/src/pages。 */
  page: string;
  /** 高亮锚点：目标 UI 元素的语义描述与定位建议（优先 data-* 属性方案）。 */
  anchor: string;
  /** 这一步要教会用户什么，一句话。 */
  intent: string;
  /** 文案需要覆盖的要点，不超过 3 条。 */
  points: string[];
}

interface GuidePlan {
  /** 引导场景与触发时机（首次进入哪个页面、何时判定首次、client/server 双模式如何处理）。 */
  scenario: string;
  /** 引导步骤，3~6 步，覆盖核心使用链。 */
  steps: GuideStep[];
  /** 技术选型建议：自研组件 vs 引导库，含对比与倾向。 */
  techChoice: string;
  /** 「首次」判定与状态存储建议（须对照 DESIGN 决策 #51/#57 的存储纪律）。 */
  storage: string;
  /** 「重新查看」入口的位置与形态建议。 */
  reEntry: string;
  /** 计划改动的文件（仓库相对路径）。 */
  filesToTouch: string[];
  /** CHANGELOG 条目草稿（中文，对齐既有条目粒度）。 */
  changelogDraft: string;
  /** 风险与拿不准的点；宁可列出也不要自行拍板。 */
  risks: string[];
  /** 待用户拍板项（方案确认时逐项问）。 */
  openQuestions: string[];
}

interface PlanReview {
  /** 必须先修改方案的阻断项，每条注明依据（Issue 条款 / 仓库事实 / DESIGN 决策号）。 */
  mustFix: string[];
  /** 建议项，不阻断。 */
  suggestions: string[];
}

interface Comment { body: string }
interface IssueJson { number: number; state: string; title: string; body: string; labels: { name: string }[]; comments?: Comment[] }

function latestCommentBody(prefix: string, comments: Comment[]): string | null {
  for (let i = comments.length - 1; i >= 0; i--) {
    const b = comments[i]?.body ?? "";
    if (b.startsWith(prefix)) return b;
  }
  return null;
}

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const PLAN_PREFIX = "**📐 方案 v1**";

phase("建场守卫：核对 Issue 状态与既有方案");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签（防轮值撞车的前置条件）`);
const comments = issue.comments ?? [];
if (latestCommentBody(PLAN_PREFIX, comments) !== null) {
  log("方案评论已存在，幂等退出");
  return { conclusion: `Issue #${issueNum} 已有「📐 方案 v1」评论，设计段幂等退出；如需修订方案请由主会话编辑后重新起跑。`, findings: [], verified: ["建场守卫：方案评论在场"], notCovered: [] };
}
log(`Issue #${issueNum} 守卫通过，开始勘察`);

phase("勘察现状并起草引导方案");
const designer = agent("设计师", {
  system:
    "你是 LabelFrame 的交互设计工程师，为「新用户首次使用引导」产出实施方案。只勘察不写代码，不改任何文件。" +
    "涉及产品取舍（形态、选型、默认值）一律写进 openQuestions 待用户拍板，不自行决定。" +
    "遇到做不到或指令矛盾时如实说明，绝不编造。",
});
const ctx =
  `Issue #${issueNum}《${issue.title}》正文：\n${issue.body}\n\n` +
  `勘察指引（用你自己的文件工具读，仓库根 = 当前工作目录，只读）：AGENTS.md、docs/WORKFLOW.md、` +
  `docs/DESIGN.md 中与前端 / UI 存储 / 双模式相关的决策（至少 #40 #51 #53 #57 #63 #164）、docs/CODE-REVIEW-BASELINE.md；` +
  `再看 web/src 现状：App.tsx 的页面路由与 client/server 模式差异、pages/ 下相关页面、i18n/ 资源组织方式、` +
  `state/ 与 components/ 可复用件（注意 Popover 的 portal 实践与页面脚部收纳史）。`;
const plan = await designer.ask<GuidePlan>(
  ctx + "\n\n产出实施方案（纯设计）。硬要求：scenario 写清触发时机与 client/server 双模式差异的处理（以 Issue 范围为准）；" +
  "steps 3~6 步覆盖核心使用链，page 必须是真实存在的页面组件名，anchor 给可定位的锚点建议（优先 data-* 属性）；" +
  "techChoice 给自研 vs 引库对比与倾向（引库须评估离线与依赖政策）；storage 必须对照 #51「禁 localStorage」的适用范围与 #57 机器级配置给建议；" +
  "risks 宁多勿漏；所有产品取舍进 openQuestions（每项给选项与建议）。");

phase("请没看过方案的人独立评审");
const planReview = await agent("方案评审员", {
  system: "你是独立评审员，只审不改、不问用户。找这份方案会失败或返工的原因，而不是确认它好；结论写进结构化结果。",
}).ask<PlanReview>(
  ctx + `\n\n请打开方案计划触碰的文件核对（只读不编辑），评审下面这份实施方案：\n${JSON.stringify(plan)}\n` +
  "重点三个角度：① 范围与 Issue 的范围 / 不在范围 / AC 是否对齐（缺 AC 覆盖、超范围都要指）；② 锚点与页面结构是否真实（页面组件名、可挂载点、双模式菜单差异）；③ 存储与 i18n 纪律是否与 DESIGN 决策冲突。");

let finalPlan = plan;
if (planReview.mustFix.length > 0) {
  finalPlan = await designer.ask<GuidePlan>(`方案评审提出阻断项，请修订并重新给出完整方案：\n${JSON.stringify(planReview.mustFix)}`);
}
report({ stage: "方案定稿", issue: issueNum, steps: finalPlan.steps.length, mustFix: planReview.mustFix.length, suggestions: planReview.suggestions.length });

phase("落稿 Issue 并交付方案");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const planMd = [
  PLAN_PREFIX + "（工作流·设计段 · " + ts + "）",
  "",
  "## 引导场景与触发",
  finalPlan.scenario,
  "",
  "## 步骤清单（" + finalPlan.steps.length + " 步）",
  ...finalPlan.steps.map((s, i) => "- **第 " + (i + 1) + " 步 · " + s.page + "**（锚点：" + s.anchor + "）——" + s.intent + "；文案要点：" + s.points.join("；")),
  "",
  "## 技术选型建议",
  finalPlan.techChoice,
  "",
  "## 首次判定与存储",
  finalPlan.storage,
  "",
  "## 重新查看入口",
  finalPlan.reEntry,
  "",
  "## 计划改动文件",
  ...finalPlan.filesToTouch.map((f) => "- " + f),
  "",
  "## CHANGELOG 草稿",
  finalPlan.changelogDraft,
  "",
  "## 风险",
  ...finalPlan.risks.map((r) => "- " + r),
  "",
  "## ⚠️ 待用户拍板项",
  ...finalPlan.openQuestions.map((q, i) => "- " + (i + 1) + ". " + q),
  "",
  planReview.suggestions.length > 0 ? "## 评审建议项（不阻断）\n" + planReview.suggestions.map((s) => "- " + s).join("\n") : "",
  "（方案评审阻断项 " + planReview.mustFix.length + " 条已吸收修订。）",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", planMd]);

await artifact.markdown("design-report", planMd, { title: "引导方案 v1（Issue #" + issueNum + "）", description: "设计段产出的实施方案全文，含待用户拍板项。", primary: true });
log("方案已落 Issue #" + issueNum + "，等待主会话交用户确认");
return {
  conclusion: `Issue #${issueNum} 设计段完成：${finalPlan.steps.length} 步引导方案经独立评审（阻断 ${planReview.mustFix.length} 条已吸收）落稿 Issue，含 ${finalPlan.openQuestions.length} 项待用户拍板。下一步：用户确认方案后起文案段 lf-copy。`,
  findings: [],
  verified: ["方案经独立评审员对照页面结构与 DESIGN 决策核审", "建场守卫确认 Issue OPEN 且带「工作流接管」标签"],
  notCovered: ["方案未经用户确认（主会话交用户拍板 openQuestions 后进入文案段）"],
};