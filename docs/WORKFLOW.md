# LabelFrame 工作流（GitHub）

> 版本：1 · 落地：2026-09-09（迭代 42，决策 #97）。
> 参考外部通用「产品迭代工作流」资料 v1.1，已按本仓库事实实例化；仓库运行不依赖外部资料。
> 修改本文件、门禁规则或流程模板属于**流程治理迭代**，普通迭代不做。

## 1. 平台与仓库事实

| 项 | 事实 |
|---|---|
| 权威仓库 | `github.com/marci-labs/LabelFrame`（公开；远端 `origin` 为唯一权威） |
| 默认分支 | `master` |
| 任务载体 | GitHub Issue（[迭代任务模板](../.github/ISSUE_TEMPLATE/iteration.md)立项；缺陷 / 功能沿用各自模板） |
| 变更载体 | 短主题分支 + PR |
| 合并方式 | squash；PR 标题 = Conventional Commits（中文说明为主） |
| 分支清理 | 仓库开启合并后自动删除远端分支；本地分支合并后手动清理 |
| 门禁 | ruleset「master 门禁」：必须 PR + 双必需检查（strict，要求目标分支新鲜度）+ 禁强推；**管理员无豁免名单** |
| 必需检查 | 「构建与测试（dotnet + 前端）」与「MSI 结构断言（安装包 UI 契约）」（来自 [ci.yml](../.github/workflows/ci.yml)）。改名必须**先改门禁再改 ci.yml**，避免出现无保护窗口 |
| 日常 CI | push master / 全部 PR / 手动：dotnet 构建 + 测试（排除 Perf/Soak）+ 前端 lint / 双模式测试 / 双构建 + MSI 结构断言；Perf/Soak 由 [nightly-perf.yml](../.github/workflows/nightly-perf.yml) 周一 04:00 跑 |
| 发布 | 记账 PR 合并后推 `v*` tag → [release.yml](../.github/workflows/release.yml) 自动构建发布（ghcr 镜像 + MSI / 插件 zip / Linux 归档上传 Release）；除发版 tag 外不推其他 tag |

## 2. 日常迭代流程

1. **立项**：从「迭代任务」模板建 Issue，写清目标与依据 / 范围 / 不在范围 / 验收标准（AC-xx）/ 启动命令。
2. **恢复上下文**：新会话读 `AGENTS.md` → 本文件 → Issue 全文（含评论）→ 相关文档（DESIGN / REQUIREMENTS / ROADMAP 状态总览）。不重新初始化流程。
3. **实施**：从最新 `master` 切短主题分支（建议 `iter/<N>-<slug>`、`fix/<slug>`、`feat/<slug>`）；严格按 Issue 范围，新想法开新 Issue 或记入 DESIGN「未决问题」。
4. **本地检查**：`dotnet build LabelFrame.slnx` + `dotnet test`（排除 Perf/Soak）；前端改动加 `pnpm lint / test / build`。不用历史结果充当本轮结果。
5. **提 PR**：正文按 PR 模板；关联 Issue 用 `#N` 普通引用——**验收未完成时禁用 `Closes` / `Fixes` 等关闭关键词**（避免代码一合并就提前结项）。
6. **CI 与复审**：两项必需检查必须针对**最新提交**全绿；旧提交的绿灯不覆盖新提交。自查清单按 PR 模板如实勾选。
7. **合并**：squash；合并后回读 master 提交与 PR 状态确认落地；清理本地分支。
8. **结项或待验收**：
   - 验收完成 → 验收证据（AC-xx 逐条：通过 / 失败 / 跳过 + 证据链接）回写 Issue → 关闭 Issue；ROADMAP 状态总览更新一行（✅ + Issue 链接），CHANGELOG 随 PR 更新。
   - 验收滞后（真机 / 用户 / 长测）→ Issue 打 `待验收` 标签**保持开放**，欠账项与恢复条件写进 Issue；结项时关闭。

发版记账（版本号 / ROADMAP / CHANGELOG）同样走 PR；tag 只推 `v*`。

## 3. 事实源唯一

| 事实 | 权威位置 |
|---|---|
| 本轮需求 / 范围 / 决议 / 进度 / 验收 | Issue（正文 + 评论） |
| 变更内容与检查证据 | PR + Actions 运行记录 |
| 迭代索引与状态总览 | [ROADMAP.md](ROADMAP.md)（结项时更新一行；迭代 0-41 历史详情在 [archive/ROADMAP-ITERATIONS.md](archive/ROADMAP-ITERATIONS.md)） |
| 版本正文 | [CHANGELOG.md](../CHANGELOG.md) + GitHub Release |
| 架构与关键技术决策 | [DESIGN.md](DESIGN.md) 决策表 |
| 验收欠账 | 打 `待验收` 标签的 Issue（存量见 [ACCEPTANCE-BACKLOG.md](ACCEPTANCE-BACKLOG.md)，消化后退役） |
| 契约行为 | 代码 + DESIGN.md「Server API 契约」等章节 |

同一事实只在一处维护，其他位置只留链接；Issue / PR 已能接续时不另建本地交接文件。

## 4. 风险裁剪

- **轻量**（文案 / 文档 / 局部小修）：Issue 与 PR 描述可精简（问题 / 验证 / 剩余三段）；CI 门禁不变。
- **标准**（默认）：模板全字段；UI 变更附真实界面验收证据；回归测试随风险补充。
- **强化**（跨端公共契约：模板包格式 / 打印 API / 作业模型；权限 / 数据迁移）：先更新 DESIGN 文档再改代码（AGENTS 既有约束）；PR 附兼容影响与回退方式；合并后增加真机 / 集成复验。

## 5. 门禁演练记录（迭代 42 落地证据）

| 演练 | 结果 | 证据 |
|---|---|---|
| 失败阻断：故意失败 PR 应被禁止合并 | 待结项 PR 填写 | 待结项 PR 填写 |
| 正常合并：合法变更全绿后可 squash 合并 | 待结项 PR 填写 | 待结项 PR 填写 |

## 6. 选配未启用

Issue 自动巡检、合并队列、GitHub Projects 看板、多会话 worktree 并行、规格驱动（Gherkin / TDD / 变异测试）——启用任一项需开流程治理迭代评估收益与代价。
