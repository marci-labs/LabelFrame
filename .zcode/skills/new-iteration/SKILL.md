---
name: new-iteration
description: LabelFrame 迭代立项。当用户想开启 / 立项一个新迭代（如「开个迭代做 X」「立项 X」「下一轮迭代做 X」）时使用：按仓库流程起草并创建 GitHub 迭代 Issue，交付启动命令。只立项不实施；实施按 AGENTS.md 与 docs/WORKFLOW.md。
---

# LabelFrame 迭代立项

把用户的一句话想法变成符合仓库流程的 GitHub 迭代 Issue，并交付可粘贴新会话的启动命令。**本 skill 只立项，不实施。**

## 前置调查

1. 读 `AGENTS.md`、`docs/WORKFLOW.md`、`docs/ROADMAP.md`（状态总览）。
2. 确定迭代号：状态总览中最大迭代号 + 1（「发布补丁」「检查点」「待需求」行不计入）。
3. 主题溯源：用户的想法若对应 `docs/ROADMAP.md`「待需求」或 `docs/DESIGN.md`「风险与未决问题」的既有条目，沿用其记载（含已列的修复方向与先例决策编号），并在「来源」中注明；与开放 Issue 重复时先指出（`gh issue list`）。
4. 基线 = 当前 `origin/master` 最新提交（`git rev-parse origin/master`）。

## 起草 Issue 正文

按仓库「迭代任务」模板字段起草（结构参照 `.github/ISSUE_TEMPLATE/iteration.md`），要点：

- **目标与依据**：使用者与场景、现状与期望（可观察差异）、来源（用户反馈 / 未决问题 / 决议）、基线。
- **范围**：本轮交付（可独立验收）；不在范围（明确排除项及去向）。
- **待决议**：凡涉及产品取舍（默认值、行为形态、公共契约语义），逐项列「选项 + 建议」，并注明执行会话须先征求用户——立项时不得替用户拍板。
- **验收标准 AC-xx 表**：每条可观察的期望结果 + 阻断点（合并 / 发布 / 结项）。
- **启动命令**：新会话可独立接续——读 AGENTS.md、docs/WORKFLOW.md、本 Issue（含评论），按范围执行、验收以 AC-xx 为准、待决议先问用户、公共契约先文档后代码、短分支 + PR + squash、Conventional Commits、不出现公司 / 业务线品牌字样。

## 确认与创建

1. **先把完整草稿展示给用户确认**（重点是范围边界、AC、待决议），按反馈修改后再创建。
2. `gh issue create --title "迭代 N：<主题>" --body-file <草稿文件>`。
3. 交付两样东西：Issue 链接；可直接粘贴到新会话的启动命令（含 Issue 链接）。
4. 询问用户是否本会话直接执行；若执行，按 AGENTS.md → docs/WORKFLOW.md → Issue 的顺序恢复上下文后开工（短分支 + PR + squash 合并）。

## 禁止事项

- 不修改 ROADMAP / CHANGELOG / 任何仓库文件（这些由执行迭代在结项 PR 中更新）；
- 不推 tag、不合并 PR、不使用 Closes / Fixes 等关闭关键词；
- 起草内容不得出现公司 / 业务线品牌名称（仓库命名规范）。
