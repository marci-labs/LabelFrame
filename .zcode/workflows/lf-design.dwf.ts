/* zcode-workflow
description: LabelFrame 工作流管线·设计段：按 Issue 目标/范围/AC 勘察仓库现状并产出通用实施方案（含独立方案评审），落 Issue 评论「📐 方案 vN」（编号递增；勘察不成立时产出处置报告不占号）。高档位运行（GLM-5.3）。
whenToUse: 工作流实验管线第 1 段：Issue 立项并带「工作流接管」标签后起跑，产出方案供用户确认；适用任意主题迭代（前端 / 后端 / 文档 / 治理）。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 设计段（lf-design）· v2（迭代 124 泛化：任务按 Issue 目标 / 范围 / AC 产出，不再写死主题）
// 职责：读 Issue 与仓库现状，产出实施方案（含独立方案评审），落 Issue 评论「📐 方案 v{在场最大 N+1}」（取号宽松扫描，错位存档标题计入防撞号）；
//       勘察发现范围矛盾 / 前置缺失时改落「⚠️ 设计处置报告」（不匹配方案正则、不占 vN 槽，不得产出占位方案）。
// 前置：Issue 带「工作流接管」标签；幂等：最新方案评论晚于最新「🔧 PR 已建 / 🔨 修复轮」评论才退出，否则产出 v{N+1}。

interface CopyListedItem {
  /** i18n 语义 key 建议（如 tour.workbench.new / help.card.designer），供文案段 lf-copy 消费。 */
  key: string;
  /** 所属页面或组件位置（如 Workbench / Designer / Shell）。 */
  page: string;
  /** 文案场景：出现在哪、何时出现、干什么用（措辞由文案段撰写，此处只定场景）。 */
  scenario: string;
}

interface DesignPlan {
  /** 目标回顾：一句话复述本迭代意图与边界（与 Issue 正文对齐）。 */
  goalRecap: string;
  /** 现状锚点：与本迭代相关的仓库现状，逐条「文件:行号（或文件路径）——一句话事实」，必须实读所得。 */
  anchors: string[];
  /** 设计：逐 AC 对应的方案（ac 填 AC 编号，approach 给做法与理由；覆盖 Issue 全部 AC）。 */
  design: { ac: string; approach: string }[];
  /** 计划改动文件表：path（仓库相对路径）+ change（该文件改什么）。 */
  files: { path: string; change: string }[];
  /** 测试计划：逐 AC 说明怎么验证（测试文件 / 直测脚本 / 编译验证等）。 */
  testPlan: string[];
  /** CHANGELOG 条目草稿（中文，对齐既有条目粒度）。 */
  changelogDraft: string;
  /** 风险与拿不准的点；宁可列出也不要自行拍板。 */
  risks: string[];
  /** 待用户拍板项（方案确认时逐项问）；纯技术迭代可为空数组。 */
  openQuestions: string[];
  /** 界面文案清单：仅含界面文案的迭代必填（供文案段 lf-copy 消费的契约节）；无界面文案迭代给空数组。 */
  copyList: CopyListedItem[];
}

interface DesignDisposal {
  /** 勘察发现的问题：范围矛盾 / 前置缺失等，逐条给证据（文件:行号 / Issue 条款）。 */
  problems: string[];
  /** 建议处置：怎么化解（修范围 / 补前置 / 拆分立项等）。 */
  suggestion: string;
}

/** 设计段一次产出：kind=方案（plan 必填）或勘察不成立的处置报告（disposal 必填）。 */
interface DesignOutcome {
  kind: "方案" | "处置报告";
  plan?: DesignPlan;
  disposal?: DesignDisposal;
}

interface PlanReview {
  /** 必须先修改方案的阻断项，每条注明依据（Issue 条款 / 仓库事实 / DESIGN 决策号）。 */
  mustFix: string[];
  /** 建议项，不阻断。 */
  suggestions: string[];
}

interface Comment { body: string }
interface IssueJson { number: number; state: string; title: string; body: string; labels: { name: string }[]; comments?: Comment[] }

function commentLastIndex(prefix: string, comments: Comment[]): number {
  for (let i = comments.length - 1; i >= 0; i--) {
    if ((comments[i]?.body ?? "").startsWith(prefix)) return i;
  }
  return -1;
}

/** 评论首行正则定位：最新一条首行匹配 re 的评论下标（无则 -1）。 */
function commentHeadLastIndex(re: RegExp, comments: Comment[]): number {
  for (let i = comments.length - 1; i >= 0; i--) {
    const head = ((comments[i]?.body ?? "").split("\n")[0]) ?? "";
    if (re.test(head)) return i;
  }
  return -1;
}

/** 取号扫描：各评论首行以宽松正则（数字后不要求紧跟 **）取在场最大 N（无则 0）。宽松是刻意的——#291 式错位存档标题「方案 v1（错位产出·仅存档）」也计入，防重跑撞号；只认首行，正文前瞻提及不虚增。方案定位/守卫仍用严格 PLAN_HEAD_RE（存档评论不算有效方案）。 */
function maxPlanVersion(comments: Comment[], re: RegExp): number {
  let max = 0;
  for (const c of comments) {
    const head = ((c?.body ?? "").split("\n")[0]) ?? "";
    const m = re.exec(head);
    if (m !== null && m[1] !== undefined) max = Math.max(max, Number(m[1]));
  }
  return max;
}

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
// 两个方案正则刻意不同：PLAN_HEAD_RE（严格，数字后紧跟 **）用于方案定位与幂等守卫——错位存档标题不算有效方案；
// PLAN_VERSION_SCAN_RE（宽松）仅用于取号——#291 式存档标题「**📐 方案 v1（错位产出·仅存档）**（…）」数字后是「（」，
// 严格版不匹配，若取号也用严格版会得 max=0、重跑再产 v1 撞号（PR #295 评审阻断项）。
const PLAN_HEAD_RE = /^\*\*📐 方案 v(\d+)\*\*/;
const PLAN_VERSION_SCAN_RE = /^\*\*📐 方案 v(\d+)/;
const DISPOSAL_PREFIX = "**⚠️ 设计处置报告**";
const PR_PREFIX = "**🔧 PR 已建**";
const FIX_PREFIX = "**🔨 修复轮";

phase("建场守卫：核对 Issue 状态与方案新鲜度");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签（防轮值撞车的前置条件）`);
const comments = issue.comments ?? [];
const lastPlan = commentHeadLastIndex(PLAN_HEAD_RE, comments);
const lastChange = Math.max(commentLastIndex(PR_PREFIX, comments), commentLastIndex(FIX_PREFIX, comments));
if (lastPlan !== -1 && lastPlan > lastChange) {
  log("最新方案评论晚于最新改动评论，幂等退出");
  return { conclusion: `Issue #${issueNum} 最新「📐 方案 vN」评论晚于最新 🔧/🔨 改动评论，设计段幂等退出；如需修订方案请由主会话编辑后重新起跑。`, findings: [], verified: ["建场守卫：最新方案评论为最新状态"], notCovered: [] };
}
log(`Issue #${issueNum} 守卫通过，开始勘察`);

phase("勘察现状并起草方案");
const designer = agent("设计师", {
  system:
    "你是 LabelFrame 的设计工程师，按 Issue 的目标 / 范围 / 验收标准产出实施方案。只勘察不写代码，不改任何文件。" +
    "涉及产品取舍（形态、选型、默认值）一律写进 openQuestions 待用户拍板，不自行决定。" +
    "遇到做不到或指令矛盾时如实说明，绝不编造。",
});
const ctx =
  `Issue #${issueNum}《${issue.title}》正文：\n${issue.body}\n\n` +
  `勘察指引（用你自己的文件工具读，仓库根 = 当前工作目录，只读）：AGENTS.md、docs/WORKFLOW.md、docs/CODE-REVIEW-BASELINE.md；` +
  `docs/DESIGN.md 决策表与正文中和本迭代范围相关的决策；再按 Issue 范围实读相关现状——` +
  `前端迭代看 web/src（页面 / 组件 / i18n / state），后端迭代看 src/ 与 test/，治理迭代看 .zcode/ 与 docs/。`;
const outcome = await designer.ask<DesignOutcome>(
  ctx + "\n\n产出实施方案（纯设计）。硬要求：anchors 必须是实读所得的现状（文件:行号，禁止臆造）；" +
  "design 逐 AC 对应且覆盖 Issue 全部 AC；files 只列范围内文件；testPlan 逐 AC 说明验证方式；risks 宁多勿漏；" +
  "所有产品取舍进 openQuestions（每项给选项与建议，纯技术迭代可为空数组）。" +
  "本迭代含界面文案时 copyList 必填（逐条 key / page / scenario——供文案段 lf-copy 消费的契约节）；无界面文案时给空数组。" +
  "勘察发现范围矛盾或前置缺失（方案无法成立）时改产 kind=处置报告：problems 逐条给证据，suggestion 给化解建议，不得产出占位方案。");

if (outcome.kind === "处置报告" || outcome.plan === undefined) {
  if (outcome.disposal === undefined) throw new Error("设计师返回形态异常：非方案但缺处置报告内容（fail-closed）");
  phase("落处置报告评论（不占方案 vN 槽）");
  const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
  const ts = tsRes.stdout.trim();
  const reRunVersion = maxPlanVersion(comments, PLAN_VERSION_SCAN_RE) + 1;
  const disposalMd = [
    DISPOSAL_PREFIX + "（工作流·设计段 · " + ts + "）",
    "",
    "勘察未成立，本轮不产出实施方案（本评论不匹配方案正则、不占「📐 方案 vN」编号槽）：",
    ...outcome.disposal.problems.map((p) => "- " + p),
    "",
    "建议处置：" + outcome.disposal.suggestion,
    "",
    "主会话请核对后修 Issue 范围或补前置，再重新起跑设计段（届时产出 v" + reRunVersion + "，与既有方案评论不撞号）。",
  ].join("\n");
  await world.run("gh", ["issue", "comment", String(issueNum), "--body", disposalMd]);
  await artifact.markdown("design-disposal", disposalMd, { title: "设计处置报告（Issue #" + issueNum + "）", description: "勘察不成立的处置报告，待主会话核对范围或前置。" });
  return {
    conclusion: `Issue #${issueNum} 设计段产出处置报告：勘察发现 ${outcome.disposal.problems.length} 项范围矛盾 / 前置缺失，未产出方案。主会话核对后修范围或补前置再重跑。`,
    findings: [], verified: ["建场守卫确认 Issue OPEN 且带「工作流接管」标签"], notCovered: ["方案未产出（勘察不成立）"],
  };
}

const plan = outcome.plan;

phase("请没看过方案的人独立评审");
const planReview = await agent("方案评审员", {
  system: "你是独立评审员，只审不改、不问用户。找这份方案会失败或返工的原因，而不是确认它好；结论写进结构化结果。",
}).ask<PlanReview>(
  ctx + `\n\n请打开方案计划触碰的文件核对（只读不编辑），评审下面这份实施方案：\n${JSON.stringify(plan)}\n` +
  "重点三个角度：① 范围与 Issue 的范围 / 不在范围 / AC 是否对齐（缺 AC 覆盖、超范围都要指）；② 现状锚点是否真实（文件与行号逐一实读核对，行号漂移或文件不实存都要指）；③ 与 DESIGN 决策 / 评审基线（i18n、测试、命名红线）是否冲突。");

let finalPlan = plan;
if (planReview.mustFix.length > 0) {
  finalPlan = await designer.ask<DesignPlan>(`方案评审提出阻断项，请修订并重新给出完整方案：\n${JSON.stringify(planReview.mustFix)}`);
}
report({ stage: "方案定稿", issue: issueNum, acCovered: finalPlan.design.length, mustFix: planReview.mustFix.length, suggestions: planReview.suggestions.length });

phase("落稿 Issue 并交付方案");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const nextVersion = maxPlanVersion(comments, PLAN_VERSION_SCAN_RE) + 1;
const hasCopy = finalPlan.copyList.length > 0;
const planMd = [
  "**📐 方案 v" + nextVersion + "**（工作流·设计段 · " + ts + "）",
  "",
  "## 一、目标回顾",
  finalPlan.goalRecap,
  "",
  "## 二、现状锚点",
  ...finalPlan.anchors.map((a) => "- " + a),
  "",
  "## 三、设计（逐 AC）",
  ...finalPlan.design.map((d) => "- **" + d.ac + "**：" + d.approach),
  "",
  "## 四、计划改动文件",
  ...finalPlan.files.map((f) => "- " + f.path + "——" + f.change),
  "",
  "## 五、测试计划",
  ...finalPlan.testPlan.map((t) => "- " + t),
  "",
  "## 六、CHANGELOG 草稿",
  finalPlan.changelogDraft,
  "",
  "## 七、风险",
  ...finalPlan.risks.map((r) => "- " + r),
  ...(hasCopy ? ["", "## 界面文案清单（供文案段 lf-copy 消费）", ...finalPlan.copyList.map((c) => "- `" + c.key + "`（" + c.page + "）：" + c.scenario)] : []),
  "",
  "## ⚠️ 待用户拍板项",
  ...(finalPlan.openQuestions.length > 0 ? finalPlan.openQuestions.map((q, i) => "- " + (i + 1) + ". " + q) : ["- 无（纯技术迭代，技术选择按本稿执行）"]),
  "",
  planReview.suggestions.length > 0 ? "## 评审建议项（不阻断）\n" + planReview.suggestions.map((s) => "- " + s).join("\n") : "",
  "（方案评审阻断项 " + planReview.mustFix.length + " 条已吸收修订。）",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", planMd]);

await artifact.markdown("design-report", planMd, { title: "方案 v" + nextVersion + "（Issue #" + issueNum + "）", description: "设计段产出的实施方案全文，含待用户拍板项。", primary: true });
log("方案 v" + nextVersion + " 已落 Issue #" + issueNum + "，等待主会话交用户确认");
return {
  conclusion: `Issue #${issueNum} 设计段完成：方案 v${nextVersion} 经独立评审（阻断 ${planReview.mustFix.length} 条已吸收）落稿 Issue，含 ${finalPlan.openQuestions.length} 项待用户拍板。下一步：` +
    (hasCopy
      ? "用户确认方案后起文案段 lf-copy。"
      : "用户确认方案后由主控在 Issue 贴「✅ 文案定稿 v1」无文案占位声明，再直接起实施段 lf-implement。"),
  findings: [],
  verified: ["方案经独立评审员对照仓库现状与 DESIGN 决策核审", "建场守卫确认 Issue OPEN 且带「工作流接管」标签"],
  notCovered: ["方案未经用户确认（主会话交用户拍板 openQuestions 后进入下一段）"],
};
