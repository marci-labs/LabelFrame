/* zcode-workflow
description: LabelFrame 工作流管线·文案评审段：提炼产品文案风格基准，主编校对草稿（重复/啰嗦/术语/中英对齐），再经独立「新用户视角」评审，产出定稿落 Issue 评论「✅ 文案定稿 v1」。高档位运行（GLM-5.3）。
whenToUse: 工作流实验管线第 3 段：「✍️ 文案草稿 v1」评论在场后起跑，产出文案定稿供用户过目与实施段使用。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 文案评审段（lf-copy-review）
// 职责：提炼风格基准 → 主编逐条校对草稿 → 独立「新用户视角」评审 → 终稿定量断言 → 落「✅ 文案定稿 v1」。
// 前置：草稿评论在场；幂等：定稿评论已存在则直接退出。

interface CopyRow {
  key: string;
  page: string;
  titleZh: string;
  bodyZh: string;
  btnZh: string;
  titleEn: string;
  bodyEn: string;
  btnEn: string;
}

interface StyleBaseline {
  /** 从现有 i18n 资源提炼的风格规则，每条一句（语气/称谓/标点/按钮动词/句长）。 */
  rules: string[];
}

interface CopyEdit {
  /** 行下标（对应草稿 rows）。 */
  index: number;
  /** 问题一句话（重复/啰嗦/术语不一致/中英不对齐/与基准冲突）。 */
  problem: string;
}

interface CopyRevision {
  /** 修订后的完整行数组（未改的行原样保留，key 不变）。 */
  revisedRows: CopyRow[];
  /** 每处修订的记录（index + problem 一句话）。 */
  edits: CopyEdit[];
}

interface RowsResult {
  /** 完整行数组。 */
  rows: CopyRow[];
}

interface ReaderFeedback {
  /** 第一次用的人看不懂的行：index + 原因。 */
  unclear: string[];
  /** 与其他步骤重复表达的行：index + 与谁重复。 */
  redundant: string[];
  /** 可整句删掉的废话：index + 句子。 */
  waste: string[];
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

/** 从草稿/定稿评论里抽机器可读的 ```json 行块。 */
function extractJsonRows(commentBody: string): CopyRow[] {
  const m = /```json\r?\n([\s\S]*?)```/.exec(commentBody);
  if (m === null || m[1] === undefined) throw new Error("评论中未找到 json 行块（fail-closed）");
  const rows = JSON.parse(m[1]) as CopyRow[];
  if (!Array.isArray(rows) || rows.length === 0) throw new Error("json 行块解析结果为空");
  return rows;
}

interface CopyViolation { index: number; problem: string }

function checkRows(rows: CopyRow[]): CopyViolation[] {
  const out: CopyViolation[] = [];
  const seen = new Set<string>();
  rows.forEach((r, i) => {
    if (r.key === "" || r.titleZh === "" || r.bodyZh === "" || r.btnZh === "" || r.titleEn === "" || r.bodyEn === "" || r.btnEn === "") out.push({ index: i, problem: "存在空字段" });
    if (seen.has(r.key)) out.push({ index: i, problem: "key 重复：" + r.key });
    seen.add(r.key);
    if (r.titleZh.length > 12) out.push({ index: i, problem: "中文标题超 12 字" });
    if (r.bodyZh.length > 40) out.push({ index: i, problem: "中文正文超 40 字" });
    if (r.btnZh.length > 6) out.push({ index: i, problem: "中文按钮超 6 字" });
    if (r.bodyEn.length > 90) out.push({ index: i, problem: "英文正文超 90 字符" });
  });
  return out;
}

function rowsTable(rows: CopyRow[]): string[] {
  return [
    "| # | key | 页面 | 标题 zh | 正文 zh | 按钮 zh | 标题 en | 正文 en | 按钮 en |",
    "|---|---|---|---|---|---|---|---|---|",
    ...rows.map((r, i) => "| " + (i + 1) + " | " + r.key + " | " + r.page + " | " + r.titleZh + " | " + r.bodyZh + " | " + r.btnZh + " | " + r.titleEn + " | " + r.bodyEn + " | " + r.btnEn + " |"),
  ];
}

const issueNum = Number(args.issue);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const DRAFT_PREFIX = "**✍️ 文案草稿 v1**";
const FINAL_PREFIX = "**✅ 文案定稿 v1**";

phase("建场守卫：核对草稿在场与定稿缺席");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,body,labels,comments"]);
if (viewRes.exitCode !== 0) throw new Error("Issue 读取失败：" + viewRes.stderr.slice(0, 300));
const issue = JSON.parse(viewRes.stdout) as IssueJson;
if (issue.state !== "OPEN") throw new Error(`Issue #${issueNum} 不是 OPEN 状态`);
if (!issue.labels.map((l) => l.name).includes("工作流接管")) throw new Error(`Issue #${issueNum} 未带「工作流接管」标签`);
const comments = issue.comments ?? [];
const draftBody = latestCommentBody(DRAFT_PREFIX, comments);
if (draftBody === null) throw new Error(`Issue #${issueNum} 无「✍️ 文案草稿 v1」评论——先跑文案段 lf-copy`);
if (latestCommentBody(FINAL_PREFIX, comments) !== null) {
  log("定稿评论已存在，幂等退出");
  return { conclusion: `Issue #${issueNum} 已有「✅ 文案定稿 v1」评论，本段幂等退出。`, findings: [], verified: ["建场守卫：定稿评论在场"], notCovered: [] };
}
const draftRows = extractJsonRows(draftBody);
log("草稿 " + draftRows.length + " 行已解析，开始提炼风格基准");

phase("提炼本产品文案风格基准");
const editor = agent("主编", {
  system:
    "你是 LabelFrame 的文案主编，负责让界面文案像同一个人写的。你只读文件、只产出结构化结果，不改代码。" +
    "判断必须有依据（基准规则或既有资源例句）；说不清依据的不要写。",
});
const baseline = await editor.ask<StyleBaseline>(
  "提炼「本产品文案风格基准」：用你的文件工具读 web/src/i18n/ 下 zh 与 en 资源各至少三个文件，" +
  "提炼 5~10 条可操作的规则（语气、称谓、标点、按钮动词习惯、句长节奏），每条一句，供校对引导文案时对照。");

phase("主编逐条校对草稿");
const copyRevision = await editor.ask<CopyRevision>(
  `【风格基准】\n${baseline.rules.map((r, i) => (i + 1) + ". " + r).join("\n")}\n\n` +
  `【草稿（Issue #${issueNum}）】\n${JSON.stringify(draftRows, null, 2)}\n\n` +
  `逐条校对并直接产出修订表：① 相邻或全局步骤重复表达同一意思的，合并或删；② 啰嗦句砍到一句动作导向；` +
  `③ 术语与既有资源不一致的（读资源核对）改为一致；④ 中英文语义不对齐的改 en；⑤ 与基准冲突的改。` +
  `edits 记录每处修订（index + problem 一句话），revisedRows 给修订后的完整行数组（未改的行原样保留，key 不变）。`);
const readerRows = copyRevision.revisedRows;

phase("请第一次使用的人读一遍");
const readerFeedback = await agent("新用户视角", {
  system: "你扮演一个第一次使用标签打印软件的仓库员工，中文母语。你只根据拿到的文字判断，不查任何文件、不看代码。",
}).ask<ReaderFeedback>(
  `下面是一款标签打印软件新用户引导的文案表（JSON，字段：key/page/titleZh 标题/bodyZh 正文/btnZh 按钮）。` +
  `以第一次使用的人的身份读一遍，只回答三件事：哪些行看不懂或术语陌生（unclear）；哪些行和别的行说的是同一件事（redundant，注明与第几行重复）；` +
  `哪些正文是删掉也不影响理解的废话（waste，原样引用该句）。没有就给空数组，不要硬凑。\n${JSON.stringify(readerRows)}`);
const readerIssueCount = readerFeedback.unclear.length + readerFeedback.redundant.length + readerFeedback.waste.length;

phase("吸收反馈出终稿");
let finalRows = readerRows;
if (readerIssueCount > 0) {
  const absorbed = await editor.ask<RowsResult>(
    `新用户视角反馈如下，吸收可取项修订（不合理的可忽略但在结果里附一句理由），重新给出完整行数组（key 不变）：\n${JSON.stringify(readerFeedback)}`);
  finalRows = absorbed.rows;
}
const violations = checkRows(finalRows);
if (violations.length > 0) {
  const fixed = await editor.ask<RowsResult>(
    `终稿定量断言未通过，修订后重新给完整行数组：\n${JSON.stringify(violations)}`);
  finalRows = fixed.rows;
  const still = checkRows(finalRows);
  if (still.length > 0) throw new Error("终稿仍有定量违规：" + JSON.stringify(still));
}
report({ stage: "文案定稿", issue: issueNum, rows: finalRows.length, edits: copyRevision.edits.length, readerIssues: readerIssueCount });

phase("落定稿评论与交付");
const tsRes = await world.run("node", ["-e", "console.log(new Date().toISOString())"]);
const ts = tsRes.stdout.trim();
const finalMd = [
  FINAL_PREFIX + "（工作流·文案评审段 · " + ts + "）",
  "",
  "## 风格基准（主编提炼）",
  ...baseline.rules.map((r, i) => "- " + (i + 1) + ". " + r),
  "",
  "## 主编修订记录（" + copyRevision.edits.length + " 处）",
  ...copyRevision.edits.map((e) => "- 第 " + (e.index + 1) + " 行：" + e.problem),
  "",
  "## 新用户视角反馈（" + readerIssueCount + " 条，已吸收）",
  ...readerFeedback.unclear.map((u) => "- 看不懂：" + u),
  ...readerFeedback.redundant.map((r) => "- 重复：" + r),
  ...readerFeedback.waste.map((w) => "- 废话：" + w),
  "",
  "## 定稿文案表（实施段以此为准）",
  "",
  ...rowsTable(finalRows),
  "",
  "```json",
  JSON.stringify(finalRows, null, 2),
  "```",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", finalMd]);
await artifact.markdown("copy-final", finalMd, { title: "引导文案定稿 v1（Issue #" + issueNum + "）", description: "主编校对 + 新用户视角评审后的定稿，含修订记录。", primary: true });
log("定稿已落 Issue #" + issueNum);
return {
  conclusion: `Issue #${issueNum} 文案定稿完成：${finalRows.length} 行，主编修订 ${copyRevision.edits.length} 处，新用户视角反馈 ${readerIssueCount} 条已吸收，定量断言通过。下一步：主会话交用户过目（可改可跳过）后起实施段 lf-implement。`,
  findings: [],
  verified: ["风格基准从现有 i18n 资源实读提炼", "主编与撰写者不同上下文（独立复审）", "新用户视角只拿文案不看代码", "定量断言：字数/空值/key 唯一"],
  notCovered: ["文案最终观感属用户主观判断（B 类），待用户过目或陪验"],
};