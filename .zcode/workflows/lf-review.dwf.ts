/* zcode-workflow
description: LabelFrame 工作流管线·评审段：双员扇出评审 PR（范围合规 + 契约规范，基线锚定）+ 阻断项独立复核，落「🔍
  评审通过」或「🔍 评审待修」（含 json 阻断清单）。高档位运行（GLM-5.3）。
whenToUse: 工作流实验管线第 5 段：「🔧 PR 已建」或「🔨 修复轮」评论在场后起跑；通过则进收口段，待修则重入实施段修复。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 评审段（lf-review）
// 职责：双员扇出（范围合规 + 契约规范，评审基线锚定）+ 确定性断言合并 + 阻断项独立复核。
// 产出：「🔍 评审通过」（建议项附后）或「🔍 评审待修」（json 阻断清单，供实施段修复模式消费）。

interface Finding {
  /** 位置：文件路径（可带行号）。 */
  where: string;
  /** 问题一句话：是什么问题，不是怎么修。 */
  what: string;
  /** 依据：基线条目 / Issue 条款 / 规则名；无基线依据只能给「建议」并注明「无基线可依」。 */
  basis: string;
  /** 阻断=必须修；建议=只评论不拦截。 */
  severity: "阻断" | "建议";
}

interface ReviewResult { findings: Finding[] }

interface Confirmation {
  /** 逐条复核结论，index 对应传入数组的下标。 */
  verdicts: { index: number; reproduced: boolean; note: string }[];
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

function commentLastIndex(prefix: string, comments: Comment[]): number {
  for (let i = comments.length - 1; i >= 0; i--) {
    if ((comments[i]?.body ?? "").startsWith(prefix)) return i;
  }
  return -1;
}

function extractSafety(protocolText: string): string {
  const secStart = protocolText.indexOf("## 协议全文");
  const secEnd = protocolText.indexOf("## 附注", secStart);
  if (secStart < 0 || secEnd < 0) throw new Error("协议文件缺少「协议全文」章节（fail-closed，中止）");
  const section = protocolText.slice(secStart, secEnd);
  const marker = "\n安全边界";
  const i = section.indexOf(marker);
  if (i < 0) throw new Error("「协议全文」中未找到段首「安全边界」段（fail-closed，中止）");
  const rest = section.slice(i + 1);
  const end = rest.indexOf("\n\n");
  const seg = end < 0 ? rest : rest.slice(0, end);
  if (seg.length < 30) throw new Error("「安全边界」段抽取异常（过短，fail-closed）");
  return seg;
}

const CC_RE = /^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([^)]{1,50}\))?\s*[:：]\s*\S+/;
const CLOSE_RE = /(close|fix|resolve)(s|es|ed|d)?\s*:?\s*#\d+/i;

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const PR_PREFIX = "**🔧 PR 已建**";
const FIX_PREFIX = "**🔨 修复轮";
const PASS_PREFIX = "**🔍 评审通过**";
const TODO_PREFIX = "**🔍 评审待修**";

phase("建场守卫：定位 PR 并核对新鲜度");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
const prComment = latestCommentBody(PR_PREFIX, comments);
if (prComment === null) throw new Error(`Issue #${issueNum} 无「🔧 PR 已建」评论——先跑实施段 lf-implement`);
const b = /分支 (\S+)/.exec(prComment);
const p = /PR #(\d+)/.exec(prComment);
if (b === null || b[1] === undefined) throw new Error("无法从「🔧 PR 已建」评论解析分支名");
if (p === null || p[1] === undefined) throw new Error("无法从「🔧 PR 已建」评论解析 PR 号");
const branch = b[1];
const prNumber = p[1];
const lastChange = Math.max(commentLastIndex(PR_PREFIX, comments), commentLastIndex(FIX_PREFIX, comments));
if (commentLastIndex(PASS_PREFIX, comments) > lastChange) {
  log("最新改动之后已有「评审通过」评论，幂等退出");
  return { conclusion: `Issue #${issueNum} PR #${prNumber} 已评审通过且其后无新改动，本段幂等退出。`, findings: [], verified: ["建场守卫：评审通过评论为最新状态"], notCovered: [] };
}
const prStateRes = await world.run("gh", ["pr", "view", prNumber, "--json", "state,title"]);
if (prStateRes.exitCode !== 0) throw new Error("PR 读取失败：" + prStateRes.stderr.slice(0, 300));
const prState = JSON.parse(prStateRes.stdout) as { state: string; title: string };
if (prState.state !== "OPEN") throw new Error(`PR #${prNumber} 状态为 ${prState.state}（非 OPEN）——已合并则跑 lf-close 收口，已关闭需人工排查`);
log("PR #" + prNumber + "《" + prState.title + "》OPEN，开始双员评审");

phase("双员评审扇出：范围合规与契约规范");
const protocol = await files.read(".zcode/automations/iteration-duty.md");
const safety = extractSafety(protocol);
const baseline = await files.read("docs/CODE-REVIEW-BASELINE.md");
const reviewGuide =
  `评审 PR #${prNumber}（${prState.title}，关联 Issue #${issueNum}）。取 diff：gh pr diff ${prNumber}；读 Issue：gh issue view ${issueNum} --json body,comments（「📐 方案 v1」与「✅ 文案定稿 v1」json 块是对照基准——定稿文案必须逐字一致）。` +
  `评审基线（判断锚点——基线没有的条目不得给「阻断」，只能「建议」并注明「无基线可依」）：\n${baseline}\n安全边界：${safety}`;
const reviewPair = await Promise.all([
  agent("范围合规员", {
    system: "你只审不改、不问用户。专职视角：范围合规与 AC 覆盖——对照 Issue 的范围/不在范围/AC 表与定稿文案逐文件核对，找范围外改动、AC 无覆盖、文案与定稿不一致。findings 每条给 where/what/basis/severity。",
  }).ask<ReviewResult>(reviewGuide),
  agent("契约规范员", {
    system: "你只审不改、不问用户。专职视角：契约与规范——对照评审基线审 diff（放置路由、命名与品牌红线、注释 rubric、i18n 纪律、测试规范、流程红线）。findings 每条给 where/what/basis/severity。",
  }).ask<ReviewResult>(reviewGuide),
]);

const assertProblems: string[] = [];
const prTitleRes = await world.run("gh", ["pr", "view", prNumber, "--json", "title,body,files"]);
const prMeta = JSON.parse(prTitleRes.stdout) as { title: string; body: string; files: { path: string }[] };
if (!CC_RE.test(prMeta.title)) assertProblems.push("PR 标题不符合 Conventional Commits");
if (CLOSE_RE.test(prMeta.body)) assertProblems.push("PR 正文含 Closes/Fixes 关闭关键词");
if (!prMeta.files.some((f) => f.path === "CHANGELOG.md")) assertProblems.push("代码改动未更新 CHANGELOG.md");

const allFindings: Finding[] = [
  ...reviewPair[0].findings,
  ...reviewPair[1].findings,
  ...assertProblems.map((s) => ({ where: "PR 元数据", what: s, basis: "仓库流程确定性断言", severity: "阻断" as const })),
];
const blockers = allFindings.filter((f) => f.severity === "阻断");
const advisory = allFindings.filter((f) => f.severity === "建议");
report({ stage: "评审扇出完成", issue: issueNum, pr: Number(prNumber), findings: allFindings.length, blockers: blockers.length });

phase("阻断项独立复核");
let openBlockers = blockers;
if (openBlockers.length > 0) {
  const conf = await agent("阻断复核员", {
    system: "你独立复核评审发现，只读代码与 diff、不编辑、不问用户。逐条以证据判断阻断项是否真实成立；不迁就评审员，也不放过真问题。",
  }).ask<Confirmation>(
    `独立复核以下阻断项（gh pr diff ${prNumber} 取 diff；只读不编辑）。逐条给 reproduced 与一句依据：\n${JSON.stringify(openBlockers.map((f, i) => ({ index: i, ...f })))}`);
  openBlockers = openBlockers.filter((_, i) => conf.verdicts.some((v) => v.index === i && v.reproduced));
}

const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();

if (openBlockers.length > 0) {
  phase("落「评审待修」评论");
  const todoMd = [
    TODO_PREFIX + "（工作流·评审段 · " + ts + "）",
    "",
    `PR #${prNumber} 有 ${openBlockers.length} 条经独立复核确认的阻断项，需实施段修复模式重入（lf-implement）：`,
    ...openBlockers.map((f) => "- " + f.where + "：" + f.what + "（" + f.basis + "）"),
    "",
    "```json",
    JSON.stringify(openBlockers, null, 2),
    "```",
    advisory.length > 0 ? "\n建议项（不阻断，修复时可顺带参考）：\n" + advisory.map((f) => "- " + f.where + "：" + f.what).join("\n") : "",
  ].join("\n");
  await world.run("gh", ["issue", "comment", String(issueNum), "--body", todoMd]);
  await artifact.markdown("review-report", `# 评审待修（Issue #${issueNum} · PR #${prNumber}）\n\n${openBlockers.map((f) => "- **" + f.where + "**：" + f.what + "（" + f.basis + "）").join("\n")}`, { title: "评审待修（PR #" + prNumber + "）", description: "经独立复核确认的阻断项，待实施段修复。", primary: true });
  return {
    conclusion: `Issue #${issueNum} PR #${prNumber} 评审未通过：${openBlockers.length} 条阻断（独立复核后），已落「评审待修」。下一步：重入实施段 lf-implement（修复模式），修完再跑本段复审。`,
    findings: allFindings, verified: ["双员扇出（范围合规 + 契约规范）+ PR 元数据确定性断言", "阻断项全部经独立复核"], notCovered: ["CI 三检查（lf-close 段职责）"],
  };
}

phase("落「评审通过」评论");
const passMd = [
  PASS_PREFIX + "（工作流·评审段 · " + ts + "）",
  "",
  `PR #${prNumber} 评审通过：双员扇出 + 确定性断言共 ${allFindings.length} 条发现，阻断项 ${blockers.length} 条（经独立复核${blockers.length > 0 ? "后全部未成立或已解决" : "为零"}），建议项 ${advisory.length} 条（不阻断，已附）。`,
  advisory.length > 0 ? "建议项：\n" + advisory.map((f) => "- " + f.where + "：" + f.what + "（" + f.basis + "）").join("\n") : "无建议项。",
  "下一步：起收口段 lf-close（等 CI 三检查 → squash 合并 → AC 自评 → 回收）。",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", passMd]);
await artifact.markdown("review-report", `# 评审通过（Issue #${issueNum} · PR #${prNumber}）\n\n阻断 0 · 建议 ${advisory.length}\n${advisory.map((f) => "- " + f.where + "：" + f.what).join("\n")}`, { title: "评审通过（PR #" + prNumber + "）", primary: true });
return {
  conclusion: `Issue #${issueNum} PR #${prNumber} 评审通过（发现 ${allFindings.length} 条，建议 ${advisory.length} 条不阻断）。下一步：起收口段 lf-close。`,
  findings: allFindings, verified: ["双员扇出（范围合规 + 契约规范）", "PR 元数据确定性断言（Conventional Commits / 无关闭关键词 / CHANGELOG）", ...(blockers.length > 0 ? ["阻断项独立复核"] : [])], notCovered: ["CI 三检查（lf-close 段职责）"],
};