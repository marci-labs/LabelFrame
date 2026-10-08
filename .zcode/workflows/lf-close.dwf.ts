/* zcode-workflow
description: LabelFrame 工作流管线·收口段：等 CI 三项必需检查 → squash 合并 → 冲突残块抽查 → AC 自评回写与标签流转 → worktree/分支回收，落「📦 已合并」。低档位运行（GLM-5.3-Flash）。
whenToUse: 工作流实验管线第 6 段：「🔍 评审通过」评论在场后起跑；合并后浏览器取证与结项 wrapup 由主会话接手。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
  slug:
    type: string
    description: worktree 短标识，须与实施段一致
    required: false
    default: guide
*/
// LabelFrame 工作流实验 · 收口段（lf-close）
// 职责：等 CI 三检查（含失败修复一次机会与 BEHIND 处理）→ squash 合并 → 残块 grep → AC 自评回写 →
//       标签流转（needsHuman → 待验收）→ worktree 与本地分支回收 → 「📦 已合并」评论。
// 前置：「🔍 评审通过」评论晚于最新 🔧/🔨；幂等：已合并则直接退出。

interface AcSelfAssessment {
  /** AC 逐条自评。 */
  items: { ac: string; verdict: "通过" | "失败" | "跳过"; evidence: string }[];
  /** 需真人验收的项（浏览器 UI 行为 / 用户观感 / 真机）；空 = 全部可自证。 */
  needsHuman: string[];
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

interface ChecksSnapshot {
  complete: boolean;
  allSuccess: boolean;
  anyFailure: boolean;
  summary: string;
}

/** gh pr view statusCheckRollup 为对象包裹数组（9-21 实证坑①），此处正确解包。 */
function parseChecks(stdout: string): ChecksSnapshot {
  const keys = ["构建与测试", "MSI", "Android"];
  const parsed = JSON.parse(stdout) as { statusCheckRollup?: { name?: string; status?: string; conclusion?: string | null }[] };
  const rollup = parsed.statusCheckRollup ?? [];
  const relevant = rollup.filter((c) => c.name !== undefined && keys.some((k) => (c.name ?? "").includes(k)));
  const complete = relevant.length >= 3 && relevant.every((c) => c.status === "COMPLETED");
  const allSuccess = complete && relevant.every((c) => c.conclusion === "SUCCESS");
  const anyFailure = relevant.some((c) => c.status === "COMPLETED" && c.conclusion !== null && c.conclusion !== "SUCCESS");
  return { complete, allSuccess, anyFailure, summary: relevant.map((c) => `${c.name}:${c.status}/${c.conclusion}`).join(" | ") };
}

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const rawSlug = typeof args.slug === "string" && args.slug !== "" ? args.slug : "guide";
const slug = rawSlug.replace(/[^a-zA-Z0-9-]/g, "").slice(0, 30);
const PR_PREFIX = "**🔧 PR 已建**";
const FIX_PREFIX = "**🔨 修复轮";
const PASS_PREFIX = "**🔍 评审通过**";
const MERGED_PREFIX = "**📦 已合并**";

phase("建场守卫：定位 PR 与合并状态");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
if (latestCommentBody(MERGED_PREFIX, comments) !== null) {
  log("已合并评论在场，幂等退出");
  return { conclusion: `Issue #${issueNum} 已有「📦 已合并」评论，本段幂等退出。`, findings: [], verified: ["建场守卫：合并评论在场"], notCovered: [] };
}
const prComment = latestCommentBody(PR_PREFIX, comments);
if (prComment === null) throw new Error(`Issue #${issueNum} 无「🔧 PR 已建」评论`);
const b = /分支 (\S+)/.exec(prComment);
const p = /PR #(\d+)/.exec(prComment);
if (b === null || b[1] === undefined) throw new Error("无法解析分支名");
if (p === null || p[1] === undefined) throw new Error("无法解析 PR 号");
const branch = b[1];
const prNumber = p[1];
const wt = "../LabelFrame-wt" + issueNum + "-" + slug;
const lastChange = Math.max(commentLastIndex(PR_PREFIX, comments), commentLastIndex(FIX_PREFIX, comments));
if (commentLastIndex(PASS_PREFIX, comments) < lastChange) throw new Error(`最新改动之后没有「🔍 评审通过」评论——先跑评审段 lf-review`);
const prView = await world.run("gh", ["pr", "view", prNumber, "--json", "state,title"]);
if (prView.exitCode !== 0) throw new Error("PR 读取失败：" + prView.stderr.slice(0, 300));
const prInfo = JSON.parse(prView.stdout) as { state: string; title: string };
log("PR #" + prNumber + "《" + prInfo.title + "》state=" + prInfo.state);

const steward = agent("值守岗", {
  system:
    "你是 LabelFrame 的收口值守：修 CI 失败、写 AC 自评。修 CI 时在 worktree 内工作、如实汇报；" +
    "AC 自评只写有证据的结论（测试名 / 检查项 / 代码位置），需要浏览器或真人才能判断的如实写进 needsHuman，绝不自证。",
});

let merged = prInfo.state === "MERGED";
let ciFixUsed = false;
let updateTried = false;
if (!merged) {
  phase("等待三项必需检查并 squash 合并");
  for (let attempt = 0; attempt < 27 && !merged; attempt++) {
    if (attempt > 0) await world.run("node", ["-e", "setTimeout(()=>process.exit(0),90000)"]);
    const snapRes = await world.run("gh", ["pr", "view", prNumber, "--json", "statusCheckRollup"]);
    if (snapRes.exitCode !== 0) continue;
    const snap = parseChecks(snapRes.stdout);
    if (snap.anyFailure) {
      if (ciFixUsed) throw new Error(`CI 必需检查失败且修复机会已用：${snap.summary}（worktree 保留待人工）`);
      await steward.ask(`CI 必需检查失败：${snap.summary}。请读失败上下文（gh run view / gh api 查 Actions 日志；worktree ${wt} 若存在在其中修复，否则先重建），定位修复并 commit（不要 push，脚本会推）。`);
      const push = await world.run("git", ["-C", wt, "push"]);
      if (push.exitCode !== 0) throw new Error("CI 修复推送失败：" + push.stderr.slice(0, 300));
      ciFixUsed = true;
      continue;
    }
    if (!snap.complete || !snap.allSuccess) continue;
    const mRes = await world.run("gh", ["pr", "view", prNumber, "--json", "mergeable,mergeStateStatus"]);
    const m = JSON.parse(mRes.stdout) as { mergeable: string | null; mergeStateStatus: string };
    if (m.mergeStateStatus === "BEHIND" && !updateTried) {
      await world.run("gh", ["pr", "update-branch", prNumber]);
      updateTried = true;
      continue;
    }
    if (m.mergeable !== "MERGEABLE") continue;
    const merge = await world.run("gh", ["pr", "merge", prNumber, "--squash", "--subject", prInfo.title]);
    merged = merge.exitCode === 0;
    if (!merged && /freshness|behind/i.test(merge.stderr) && !updateTried) {
      await world.run("gh", ["pr", "update-branch", prNumber]);
      updateTried = true;
      continue;
    }
    if (!merged && attempt >= 25) throw new Error("合并持续失败：" + merge.stderr.slice(0, 300));
  }
  if (!merged) throw new Error("等待 CI 检查与合并超时（约 40 分钟），流水线中止（worktree 保留待人工）");
}
report({ stage: "已合并", issue: issueNum, pr: Number(prNumber) });

phase("合并后校验：同步 master 与冲突残块抽查");
const pull = await world.run("git", ["pull", "--ff-only", "origin", "master"]);
if (pull.exitCode !== 0) throw new Error("master 同步失败：" + pull.stderr.slice(0, 200));
const residue = await world.run("git", ["grep", "-l", "-E", "^(<{7}|>{7})", "--", "CHANGELOG.md", "docs/"]);
if (residue.exitCode === 0) throw new Error("合并后发现冲突残块标记，需人工检查：" + residue.stdout.slice(0, 300));

phase("AC 自评并回写 Issue");
const selfAssess = await steward.ask<AcSelfAssessment>(
  `对照 Issue #${issueNum} 正文 AC 表逐条自评（gh issue view ${issueNum} --json body），PR #${prNumber} 已 squash 合并入 master。` +
  `可自证项给证据（本地门禁已过：dotnet build/test + pnpm lint/test/build；PR 检查全绿；测试全限定名；代码位置）。` +
  `需要浏览器自动化走查（UI 步骤行为 / 跳过 / 重启不再现）或用户观感判断的，一律写进 needsHuman（每条注明是「浏览器取证」还是「用户观感」），不要自证。`);
const acBody = `**AC 自评（收口段值守，工作流自动回写）**\n${selfAssess.items.map((i) => "- " + i.ac + "：" + i.verdict + " —— " + i.evidence).join("\n")}` +
  (selfAssess.needsHuman.length > 0 ? `\n\n**待真人/浏览器验收项**：${selfAssess.needsHuman.join("；")}` : "");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", acBody]);

if (selfAssess.needsHuman.length > 0) {
  await world.run("gh", ["issue", "edit", String(issueNum), "--remove-label", "进行中"]);
  await world.run("gh", ["issue", "edit", String(issueNum), "--add-label", "待验收"]);
}

phase("回收 worktree 与本地分支");
const wtExistsRes = await world.run("node", ["-e", "console.log(require('fs').existsSync(process.argv[1]))", wt]);
if (wtExistsRes.stdout.trim() === "true") {
  await world.run("git", ["worktree", "remove", "--force", wt]);
  const wtList = await world.run("git", ["worktree", "list", "--porcelain"]);
  if (wtList.stdout.includes(wt)) {
    await world.run("node", ["-e", "require('fs').rmSync(process.argv[1],{recursive:true,force:true})", wt]);
  }
}
await world.run("git", ["branch", "-D", branch]);

phase("落「已合并」评论与交付汇报");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const mergedMd = [
  MERGED_PREFIX + "（工作流·收口段 · " + ts + "）",
  "",
  `PR #${prNumber} 经 CI 三项必需检查全绿后 squash 合并入 master；残块抽查无冲突标记；AC 自评已回写（${selfAssess.items.length} 条，needsHuman ${selfAssess.needsHuman.length} 项）；worktree 与本地分支已回收。`,
  selfAssess.needsHuman.length > 0
    ? "后续：主会话做浏览器取证（沙箱起前端，走引导步骤 / 跳过 / 重启不再现，截图回 Issue）+ 用户观感陪验；全过后走结项 wrapup（ROADMAP 一行 + 关 Issue + 摘「工作流接管」标签）。"
    : "后续：主会话走结项 wrapup（ROADMAP 一行 + 关 Issue + 摘「工作流接管」标签）。",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", mergedMd]);
await artifact.markdown("close-report",
  `# 收口汇报（Issue #${issueNum}）\n\nPR #${prNumber} 已 squash 合并入 master。\n\n## AC 自评\n${selfAssess.items.map((i) => "- " + i.ac + "：" + i.verdict + " —— " + i.evidence).join("\n")}\n\n## 待浏览器/真人验收\n${selfAssess.needsHuman.length > 0 ? selfAssess.needsHuman.map((h) => "- " + h).join("\n") : "无"}`,
  { title: "收口汇报（Issue #" + issueNum + "）", description: "CI 全绿合并、AC 自评回写、worktree 已回收。", primary: true });
return {
  conclusion: `Issue #${issueNum} 收口完成：PR #${prNumber} 合并入 master，AC 自评 ${selfAssess.items.length} 条回写${selfAssess.needsHuman.length > 0 ? "，" + selfAssess.needsHuman.length + " 项转待浏览器/真人验收（Issue 已打「待验收」）" : "，全部可自证"}。worktree 已回收。`,
  findings: [],
  verified: ["CI 三项必需检查针对最新提交全绿", "squash 合并后 master 同步 + 冲突残块 grep 无标记", "worktree 回收并确认目录消失"],
  notCovered: selfAssess.needsHuman.length > 0 ? ["浏览器 UI 行为取证与用户观感（主会话职责）"] : [],
};