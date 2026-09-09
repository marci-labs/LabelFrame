name: 迭代任务
description: 立项一个产品迭代（目标 / 范围 / 验收 / 启动命令）
body:
  - type: textarea
    id: goal
    attributes:
      label: 目标与依据
      description: 使用者、触发场景、现状与期望、来源（用户反馈 / 决议 / 数据）；假设单独列
      placeholder: |
        使用者与场景：……
        现状与期望：……
        来源：……
    validations:
      required: true

  - type: textarea
    id: scope
    attributes:
      label: 范围
      description: 本轮交付（可独立验收的结果）；不在范围（明确排除项及去向）
      placeholder: |
        本轮交付：……
        不在范围：……
    validations:
      required: true

  - type: textarea
    id: acceptance
    attributes:
      label: 验收标准（AC-xx）
      description: 每条可观察的期望结果 + 必要环境 / 证据；注明阻断点（合并 / 发布 / 结项）
      placeholder: |
        | 编号 | 场景与输入 | 期望结果 | 阻断点 |
        |---|---|---|---|
        | AC-01 | …… | …… | 合并 |
    validations:
      required: true

  - type: textarea
    id: launch
    attributes:
      label: 启动命令
      description: 发给执行会话的第一条指令（含 Issue 链接与补充上下文）
      placeholder: |
        继续 LabelFrame 迭代 NN（主题）。读 AGENTS.md、docs/WORKFLOW.md 与本 Issue（含评论），
        按范围执行；验收以 AC-xx 为准；短分支 + PR + squash；提交用 Conventional Commits；
        仓库内容不得出现公司 / 业务线品牌字样。
    validations:
      required: true

  - type: textarea
    id: constraints
    attributes:
      label: 约束与风险（可选）
      description: 涉及公共契约 / 数据迁移走强化路径（先改 DESIGN 再改码）；依赖真机 / 设备时注明可得性
