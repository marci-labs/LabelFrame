name: Bug 报告
description: 报告一个缺陷
labels: ['bug']
body:
  - type: dropdown
    id: component
    attributes:
      label: 组件
      options:
        - Client（WinHost / 打印客户端）
        - Server（服务端 / 路由）
        - Web 前端（设计器 / 数据打印）
        - 传输插件（TCP / Zebra / 外部插件）
        - 安装包（MSI / Docker）
        - 文档
    validations:
      required: true

  - type: input
    id: version
    attributes:
      label: 版本
      description: 见设置页 / 控制面板，或 Release 页面下载的版本号
      placeholder: '如 0.21.0'
    validations:
      required: true

  - type: textarea
    id: what-happened
    attributes:
      label: 问题描述
      description: 发生了什么？期望什么行为？
      placeholder: |
        1. 打开「数据与打印」页
        2. 选择模板 xx
        3. 点击打印后……
        期望：……
    validations:
      required: true

  - type: textarea
    id: logs
    attributes:
      label: 日志 / 截图
      description: |
        - Client 日志：%LOCALAPPDATA%\LabelFrame\host.log
        - Server 日志：%ProgramData%\LabelFrame\server\logs
        - Web 控制台（F12）
      render: shell

  - type: dropdown
    id: os
    attributes:
      label: 操作系统
      options:
        - Windows 10
        - Windows 11
        - Windows Server
        - Ubuntu / Debian
        - 其他
