name: 功能建议
description: 提议一个新功能或改进
labels: ['enhancement']
body:
  - type: textarea
    id: problem
    attributes:
      label: 要解决什么问题？
      description: 描述当前遇到的痛点或缺失能力（而非解决方案）
      placeholder: 如「批量打印 200 张时打印机过热停机，需要控制节奏」
    validations:
      required: true

  - type: textarea
    id: solution
    attributes:
      label: 期望的方案
      description: 你希望它怎么工作？（可留空——如果你不确定，描述清楚问题即可）

  - type: dropdown
    id: component
    attributes:
      label: 涉及组件
      options:
        - Client（WinHost / 打印客户端）
        - Server（服务端 / 路由）
        - Web 前端（设计器 / 数据打印）
        - 传输插件
        - 安装包 / 部署
        - 其他
