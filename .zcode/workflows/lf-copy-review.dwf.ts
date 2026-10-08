/* zcode-workflow
description: LabelFrame 工作流管线·文案评审段：提炼产品文案风格基准，主编校对草稿（重复/啰嗦/术语/中英对齐，含入口措辞候选），再经独立「新用户视角」评审，产出定稿落 Issue 评论「✅ 文案定稿 v1」。高档位运行（GLM-5.3）。
whenToUse: 工作流实验管线第 3 段：「✍️ 文案草稿 v1」评论在场后起跑，产出文案定稿供用户过目与实施段使用。
args:
  issue:
    type: number
    description: 迭代 Issue 号
    required: true
*/
// LabelFrame 工作流实验 · 文案评审段（lf-copy-review）· v2（迭代 121 扩容：校对全部文案行＋入口措辞候选）
// 职责：提炼风格基准 → 主编逐条校对草稿（含候选） → 独立「新用户视角」评审 → 终稿定量断言 → 落「✅ 文案定稿 v1」。
// 前置：草稿评论在场；幂等：定稿评论已存在则直接退出。

interface CopyRow {
  group: "tour" | "help" | "demo" | "modal";
  key: string;
  page: string;
  titleZh: string;
  bodyZh: string;
  btnZh: string;
  titleEn: string;
  bodyEn: string;
  btnEn: string;
}

interface EntryCandidate {
  entryZh: string;
  entryEn: string;
  startZh: string;
  startEn: string;
  noteZh: string;
}

interface StyleBaseline {
  /** 从现有 i18n 资源提炼的风格规则，每条一句（语气/称谓/标点/按钮动词/句长）。 */
  rules: string[];
}

interface CopyEdit {
  /** 行下标（对应草稿 rows；候选组问题用 index = -1 并在 problem 注明）。 */
  index: number;
  /** 问题一句话（重复/啰嗦/术语不一致/中英不对齐/与基准冲突）。 */
  problem: string;
}

interface CopyRevision {
  /** 修订后的完整行数组（未改的行原样保留，key 不变）。 */
  revisedRows: CopyRow[];
  /** 修订后的候选组（第 1 组仍为推荐组）。 */
  revisedCandidates: EntryCandidate[];
  /** 每处修订的记录（index + problem 一句话）。 */
  edits: CopyEdit[];
}

interface RowsResult {
  /** 完整行数组。 */
  rows: CopyRow[];
  /** 候选组（可仅在候选被修订时变化）。 */
  candidates: EntryCandidate[];
}

interface ReaderFeedback {
  /** 第一次用的人看不懂的行：index + 原因。 */
  unclear: string[];
  /** 与其他行重复表达的行：index + 与谁重复。 */
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

/** 从草稿/定稿评论里抽机器可读的 ```json 行块（第 1 块 = rows，第 2 块 = 候选组）。 */
function extractJsonBlocks(commentBody: string): string[] {
  const out: string[] = [];
  const re = /```json\r?\n([\s\S]*?)```/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(commentBody)) !== null) if (m[1] !== undefined) out.push(m[1]);
  if (out.length === 0) throw new Error("评论中未找到 json 行块（fail-closed）");
  return out;
}

interface CopyViolation { index: number; problem: string }

function checkRows(rows: CopyRow[], candidates: EntryCandidate[]): CopyViolation[] {
  const out: CopyViolation[] = [];
  const seen = new Set<string>();
  rows.forEach((r, i) => {
    if (r.key === "" || r.titleZh === "" || r.bodyZh === "" || r.btnZh === "" || r.titleEn === "" || r.bodyEn === "" || r.btnEn === "") out.push({ index: i, problem: "存在空字段" });
    if (seen.has(r.key)) out.push({ index: i, problem: "key 重复：" + r.key });
    seen.add(r.key);
    if (r.titleZh.length > 12) out.push({ index: i, problem: "中文标题超 12 字" });
    if (r.bodyZh.length > 60) out.push({ index: i, problem: "中文正文超 60 字" });
    const btnZhCap = r.group === "modal" ? 20 : 6;
    if (r.btnZh.length > btnZhCap) out.push({ index: i, problem: "中文按钮超 " + btnZhCap + " 字" });
    if (r.titleEn.length > 45) out.push({ index: i, problem: "英文标题超 45 字符" });
    if (r.bodyEn.length > 130) out.push({ index: i, problem: "英文正文超 130 字符" });
    const btnEnCap = r.group === "modal" ? 40 : 20;
    if (r.btnEn.length > btnEnCap) out.push({ index: i, problem: "英文按钮超 " + btnEnCap + " 字符" });
  });
  if (candidates.length !== 3) out.push({ index: -1, problem: "入口措辞候选须恰好 3 组" });
  candidates.forEach((c, i) => {
    if (c.entryZh === "" || c.entryEn === "" || c.startZh === "" || c.startEn === "" || c.noteZh === "") out.push({ index: -1, problem: "候选第 " + (i + 1) + " 组存在空字段" });
  });
  return out;
}

function rowsTable(rows: CopyRow[]): string[] {
  return [
    "| # | 组 | key | 页面 | 标题 zh | 正文 zh | 按钮 zh | 标题 en | 正文 en | 按钮 en |",
    "|---|---|---|---|---|---|---|---|---|---|",
    ...rows.map((r, i) => "| " + (i + 1) + " | " + r.group + " | " + r.key + " | " + r.page + " | " + r.titleZh + " | " + r.bodyZh + " | " + r.btnZh + " | " + r.titleEn + " | " + r.bodyEn + " | " + r.btnEn + " |"),
  ];
}

function candidatesTable(candidates: EntryCandidate[]): string[] {
  return [
    "| 组 | 深链按钮 zh | 深链按钮 en | 开始按钮 zh | 开始按钮 en | 说明 |",
    "|---|---|---|---|---|---|",
    ...candidates.map((c, i) => "| " + (i + 1) + " | " + c.entryZh + " | " + c.entryEn + " | " + c.startZh + " | " + c.startEn + " | " + c.noteZh + " |"),
  ];
}

const issueNum = Number(args.issue ?? 280);
if (!Number.isFinite(issueNum) || issueNum <= 0) throw new Error("参数 issue 缺失或非法");
const DRAFT_PREFIX = "**✍️ 文案草稿 v1**";
const FINAL_PREFIX = "**✅ 文案定稿 v1**";

phase("建场守卫：核对草稿在场与定稿缺席");
const viewRes = await world.run("gh", ["issue", "view", String(issueNum), "--json", "number,title,state,url,body,labels,comments"]);
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
const draftBlocks = extractJsonBlocks(draftBody);
const draftRows = JSON.parse(draftBlocks[0] ?? "[]") as CopyRow[];
if (!Array.isArray(draftRows) || draftRows.length === 0) throw new Error("草稿 rows json 块解析结果为空");
const draftCandidates = draftBlocks[1] !== undefined ? JSON.parse(draftBlocks[1]) as EntryCandidate[] : [];
log("草稿 " + draftRows.length + " 行＋候选 " + draftCandidates.length + " 组已解析，开始提炼风格基准");

phase("提炼本产品文案风格基准");
const editor = agent("主编", {
  system:
    "你是 LabelFrame 的文案主编，负责让界面文案像同一个人写的。你只读文件、只产出结构化结果，不改代码。" +
    "判断必须有依据（基准规则或既有资源例句）；说不清依据的不要写。",
});
const baseline = await editor.ask<StyleBaseline>(
  "提炼「本产品文案风格基准」：用你的文件工具读 web/src/i18n/ 下 zh 与 en 资源各至少三个文件，" +
  "提炼 5~10 条可操作的规则（语气、称谓、标点、按钮动词习惯、句长节奏），每条一句，供校对引导文案时对照。");

phase("主编逐条校对草稿（含入口措辞候选）");
const copyRevision = await editor.ask<CopyRevision>(
  `【风格基准】\n${baseline.rules.map((r, i) => (i + 1) + ". " + r).join("\n")}\n\n` +
  `【草稿 rows（Issue #${issueNum}）】\n${JSON.stringify(draftRows, null, 2)}\n\n` +
  `【入口措辞候选】\n${JSON.stringify(draftCandidates, null, 2)}\n\n` +
  `逐条校对并直接产出修订结果：① 相邻或全局重复表达同一意思的，合并或删；② 啰嗦句砍到一句动作导向；` +
  `③ 术语与既有资源不一致的（读资源核对）改为一致；④ 中英文语义不对齐的改 en（候选组 en 措辞重点核：忌直译感、按英文 UI 习惯）；⑤ 与基准冲突的改。` +
  `edits 记录每处修订（index + problem 一句话；候选组问题 index 用 -1 并注明「候选」），revisedRows / revisedCandidates 给修订后的完整结果（未改的原样保留，key 不变，候选保持 3 组且第 1 组为推荐组）。`);
const readerRows = copyRevision.revisedRows;
const readerCandidates = copyRevision.revisedCandidates;

phase("请第一次使用的人读一遍");
const readerFeedback = await agent("新用户视角", {
  system: "你扮演一个第一次使用标签打印软件的仓库员工，中文母语。你只根据拿到的文字判断，不查任何文件、不看代码。",
}).ask<ReaderFeedback>(
  `下面是一款标签打印软件的界面文案表（JSON，字段：group 分组/key/page 页面/titleZh 标题/bodyZh 正文/btnZh 按钮）。` +
  `以第一次使用的人的身份读一遍，只回答三件事：哪些行看不懂或术语陌生（unclear）；哪些行和别的行说的是同一件事（redundant，注明与第几行重复）；` +
  `哪些正文是删掉也不影响理解的废话（waste，原样引用该句）。没有就给空数组，不要硬凑。\n${JSON.stringify(readerRows)}`);
const readerIssueCount = readerFeedback.unclear.length + readerFeedback.redundant.length + readerFeedback.waste.length;

phase("吸收反馈出终稿");
let finalRows = readerRows;
let finalCandidates = readerCandidates;
if (readerIssueCount > 0) {
  const absorbed = await editor.ask<RowsResult>(
    `新用户视角反馈如下，吸收可取项修订（不合理的可忽略但在结果里附一句理由），重新给出完整行数组与候选组（key 不变，候选保持 3 组）：\n${JSON.stringify(readerFeedback)}`);
  finalRows = absorbed.rows;
  finalCandidates = absorbed.candidates;
}
const violations = checkRows(finalRows, finalCandidates);
if (violations.length > 0) {
  const fixed = await editor.ask<RowsResult>(
    `终稿定量断言未通过，修订后重新给完整行数组与候选组：\n${JSON.stringify(violations)}`);
  finalRows = fixed.rows;
  finalCandidates = fixed.candidates;
  const still = checkRows(finalRows, finalCandidates);
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
  "## 入口措辞候选（3 组，第 1 组为推荐组——待用户过目拍板后实施段采用）",
  "",
  ...candidatesTable(finalCandidates),
  "",
  "```json",
  JSON.stringify(finalRows, null, 2),
  "```",
  "",
  "```json",
  JSON.stringify(finalCandidates, null, 2),
  "```",
].join("\n");
await world.run("gh", ["issue", "comment", String(issueNum), "--body", finalMd]);
await artifact.markdown("copy-final", finalMd, { title: "文案定稿 v1（Issue #" + issueNum + "）", description: "主编校对 + 新用户视角评审后的定稿（全文案行＋入口措辞候选），含修订记录。", primary: true });
log("定稿已落 Issue #" + issueNum);
return {
  conclusion: `Issue #${issueNum} 文案定稿完成：${finalRows.length} 行＋入口措辞候选 3 组，主编修订 ${copyRevision.edits.length} 处，新用户视角反馈 ${readerIssueCount} 条已吸收，定量断言通过。下一步：主会话交用户过目（含候选组选择，可改可跳过）后起实施段 lf-implement。`,
  findings: [],
  verified: ["风格基准从现有 i18n 资源实读提炼", "主编与撰写者不同上下文（独立复审）", "新用户视角只拿文案不看代码", "定量断言：分组字数/空值/key 唯一/候选 3 组"],
  notCovered: ["文案最终观感属用户主观判断（B 类），待用户过目或陪验", "入口措辞候选最终取舍待用户拍板"],
};
