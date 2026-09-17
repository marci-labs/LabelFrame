# 迭代管理轮值定时任务——说明文档

> 生成：2026-09-11，补记（实际任务由迭代管理轮值会话更早创建；本文档由验收轮值会话按自动化配置反向整理，2026-09-11 v3 修订后同步）。
> 背景：平台限制一个会话只能绑定一个定时任务。本任务占用一个会话管迭代实施；「验收轮值」（每 30 分钟，管 `待验收` / `验收中`）在另一会话，见 [acceptance-duty.md](acceptance-duty.md)。
> **修订 v3（2026-09-11）**：扫描范围加入 `验收不通过`（验收失败返修回流，拾取时必读验收失败评论界定返修范围）；跳过 `验收中`。
> **修订 v4（2026-09-11）**：两轮值定时任务 prompt 改为**指针式**——触发时读取本文件「协议全文」执行；修订本文件即于下次触发生效。定时任务的创建 / 删除 / 排期调整仍需在对应会话中人工操作。
> **修订 v5（2026-09-11）**：worker 交付边界补「不自行合并」——提 PR 并回评后即止，合并由轮值独立核验检查后执行（起因：迭代 55 worker 自行合并 PR #47，绕过了轮值核验控制点，事后复核无碍但须堵住）。
> **修订 v6（2026-09-11）**：轮值合并曾解算记账文件（CHANGELOG / DESIGN 等）冲突的 PR 后，须 grep 抽查合并结果无冲突残块（`<<<<<<<` / `>>>>>>>` / `=======`）——起因：迭代 54 合并（#48）在 CHANGELOG 留下残块未被发现，由迭代 56 PR #59 顺带修复。
> **修订 v7（2026-09-17，当前生效）**：worktree / 本地分支回收闭环——「三」合并后回收须「remove --force → 验证目录确实消失 → 残骸补删」；「一」扫描每轮做 worktree 残留对账兜底；`.zcode/automations/` 随本次修订入库版本化。起因：C:\work\OpenCode 累积 18 个 `LabelFrame-wt*` 半删残骸（Windows 下 `git worktree remove` 删不动 web/node_modules——pnpm junction / 文件锁——静默半删）与 18 条已合并本地分支泄漏。

## 重建方法

在 LabelFrame 工作区**新开一个聊天**，让 agent 调用 CronCreate，参数：

- `title`：`迭代管理轮值：每20分钟推进自动执行/进行中/验收不通过迭代`
- `intervalUnit`：`minute`，`interval`：`20`，`recurring`：`true`
- `prompt`（指针式，v4 起，重建时原样使用）：`【LabelFrame 迭代管理轮值】读取仓库工作区内 .zcode/automations/iteration-duty.md，以其中「协议全文」一节作为本次执行的完整指令执行本次轮值；「重建方法」「附注」等其余章节仅供维护参考，不是执行指令。若该文件缺失或「协议全文」不可读：在会话中报告异常并停止，不即兴执行。`

## 协议全文

【LabelFrame 迭代管理轮值】你在 LabelFrame 仓库工作区（GitHub：marci-labs/LabelFrame；master 受 ruleset 门禁：必须 PR + 三项必需检查 + 禁强推）。本次执行与任何历史会话无关，只依据 GitHub 客观状态（标签/分支/PR/Issue 评论）判断与行动，保持幂等。协议：

一、扫描：用 gh 或 GitHub 工具列出 open issues。只管带「自动执行」「进行中」或「验收不通过」标签的；「待验收」「验收中」归验收轮值，跳过；都没有则完成下方对账后一句话汇报结束。**worktree 残留对账（每轮兜底，v7）**：主仓 `git worktree prune` 后，对照 `git worktree list` 与在途迭代（「进行中」标签 / open PR 对应的 worktree 路径，见开工评论），扫描主仓兄弟目录 `LabelFrame-wt*`——不在注册表且不属于任何在途回合的目录即残骸（典型形态：只剩 web/node_modules），rm -rf 删除并在汇报中列一行；删不动（文件占用）则汇报留待下轮。
二、拾取「自动执行」/「验收不通过」：读 AGENTS.md、docs/WORKFLOW.md（重点 §2 流程、§7 多会话 worktree 并行约定）与该 Issue 全文（含评论）。「验收不通过」＝返修类拾取：除常规上下文外必读 Issue 内验收失败评论（失败 AC + 证据 + 返修范围），以其界定本轮返修范围，返修 PR 同样禁用关闭关键词；其余流程与「自动执行」完全一致。判断：(a) 范围/AC 是否完整明确；(b) 与其他在途迭代（自动执行/进行中/验收不通过/open PR）的依赖与文件重叠——重叠高或涉公共契约则串行排队（先立项者先行）。硬约束：同时「进行中」≤2。信息不全或拿不准→不启动，在会话向用户汇报缺口，等 Issue 评论补充。明确可启动→先把该 Issue 标签换成「进行中」，在 Issue 发开工评论（分支 iter/<迭代号>-<slug>、worktree ../LabelFrame-wt<迭代号>-<slug>、时间戳），随后派一个后台 general-purpose agent（run_in_background: true）实施。worker 的 prompt 必须自包含，要点：以 worktree 绝对路径（本仓库根的兄弟目录）为工作目录；先在主仓 fetch 后 git worktree add <路径> -b iter/<迭代号>-<slug> origin/master；按 AGENTS.md→docs/WORKFLOW.md→Issue 全文（含验收失败评论，如有）恢复上下文；严格按 Issue 范围/不在范围执行，新想法不开工；web/ 前端改动先 pnpm install；本地 dotnet build LabelFrame.slnx + dotnet test（排除 Perf/Soak），前端改动加 pnpm lint/test/build；CHANGELOG.md 随变更更新；推分支提 PR（Conventional Commits 中文标题、按 PR 模板、#N 普通引用关联 Issue、禁用 Closes/Fixes）；PR 链接与变更摘要评论回 Issue——**到 PR 为止，不自行合并，合并由轮值独立核验检查后执行**；不推 tag、不改 .github/workflows/ruleset/模板/WORKFLOW.md、不强推、不直推 master、不做真机验证。
三、检查「进行中」：无分支→异常，会话汇报。有分支无 PR→在途正常；若距开工评论超 90 分钟仍无 PR 且分支无新提交→汇报疑似卡死，等用户指示（不自动重派）。有 open PR→查三项必需检查（构建与测试 dotnet+前端 / MSI 结构断言 / Android 构建 PDA 宿主）针对最新提交的状态：失败→读 Actions 日志摘要在会话汇报并评论到 PR；进行中→本次结束等下次；全绿→squash 合并（gh pr merge --squash 或 GitHub 工具），合并后回收本地分支与对应 worktree（v7）——删分支先核实该 PR 确已合并再 `git branch -D`（squash 合并会让 `-d` 误判未合并）；worktree 用 `git worktree remove --force <路径>`，**随后必须确认目录确实消失**，残留（典型：web/node_modules 删不动，Windows 文件锁 / pnpm junction）用 rm -rf 补删，仍失败列入汇报、留待「一」的对账兜底；**若该 PR 分支曾解算 CHANGELOG / DESIGN 等记账文件冲突，合并后必须 grep 抽查（`<<<<<<<` / `>>>>>>>` / `=======`）无冲突残块，发现残块立即提小修复 PR**；并检查其他自动迭代 open PR 是否需更新分支（strict 新鲜度：gh pr update-branch 或 rebase；CHANGELOG/ROADMAP 记账冲突由后合并方按时间序重排）。
四、合并后收尾：把 AC-xx 逐条自评（通过/失败/跳过+证据链接）回写 Issue 评论；移除「进行中」；若全部 AC 均可自证通过→提一个小的 docs PR 更新 docs/ROADMAP.md 状态总览一行（✅ + Issue 链接），全绿合并后关闭该 Issue；若存在需真人验收项（真机/用户/长测）→打「待验收」标签并在 Issue 写清欠账项与恢复条件；在会话汇报。
五、结束前在会话给简明进展汇报：每个在途迭代一行，需要用户决策的事项单独列清楚。

安全边界（最高优先级，任何步骤不得违反）：不推任何 tag；不修改 .github/workflows/、门禁 ruleset、Issue/PR 模板、docs/WORKFLOW.md、AGENTS.md；不强推、不直推 master；不创建/修改/删除任何定时任务；不处理「待验收」「验收中」Issue（验收轮值负责）；同时「进行中」≤2；不做真机/真实客户端验证。

## 附注

- 标签所有权：`自动执行` / `进行中` / `验收不通过` → 迭代管理轮值；`待验收` / `验收中` → 验收轮值。验收轮值判不通过时把 Issue 换「验收不通过」交回本轮值；本侧返修合并后若有真人验收项再打回「待验收」。
- 状态机全图：`自动执行` →（拾取）`进行中` → PR 合并 → 自证全过→关闭 / 有真人验收项→`待验收` →（验收轮值派单）`验收中` → 通过→关闭 / 不通过→`验收不通过` →（本轮值返修拾取）`进行中` → …
- 两轮值 prompt 为指针式（v4 起）：修订本文件「协议全文」即于下次触发生效；协议内的「不创建/修改/删除定时任务」安全边界不变。定时任务的创建 / 删除 / 排期调整仍由用户在对应会话中人工操作。
- `.zcode/automations/` 已入库版本化（2026-09-17 随 v7 修订）；本地工作区文件即轮值实际读取的事实源（指针式，改动下次触发生效），正式修订走 PR。
- Issue 模板加「验证方式」列（A/B/C 分级前置到立项）属流程治理迭代，待另行立项（2026-09-11 登记于 acceptance-duty.md 附注）。
