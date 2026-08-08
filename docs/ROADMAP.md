# LabelFrame 路线图

> 状态总览与迭代计划。每个迭代一条「启动命令」，复制给 AI 执行；完成即更新状态与 CHANGELOG。
> 设计细节见 [DESIGN.md](DESIGN.md)，需求见 [REQUIREMENTS.md](REQUIREMENTS.md)。

## 状态总览

| 迭代 | 主题 | 状态 |
|---|---|---|
| 0 | 奠基：文档体系 + 解决方案骨架 | ✅ 已完成 |
| 1 | 契约与 ZPL | 📋 计划中 |
| 2 | WinHost 打印闭环 | 📋 计划中 |
| 3 | Server 路由 | 📋 计划中 |
| 4 | 模板管理 + 预览 | 📋 计划中 |
| 5 | PDA 宿主 | 📋 计划中 |
| 6 | P1 收尾 | 📋 计划中 |
| 检查点 | 试点验收（成功衡量） | 待定 |
| 待需求 | 兼容与扩展（net48 / WMS 模板下发 / TSPL / 统计） | 待定 |

---

## 迭代 0：奠基（已完成）

**目标**：建立文档体系和解决方案骨架，让后续迭代可以独立会话执行。

**范围**：
- 文档：README（愿景）、AGENTS、DESIGN、REQUIREMENTS、ROADMAP、CHANGELOG。
- 解决方案骨架：`LabelFrame.slnx` + Core / Server / WinHost 项目（占位）、AndroidHost 目录占位。
- git 提交与推送。

**不在范围**：任何业务编码（契约模型、ZPL、API 等均在后继迭代）。

**验收**：
- `dotnet build LabelFrame.slnx` 通过。
- 文档覆盖：愿景、角色、场景、底线、能力、边界、成功衡量、决策记录、迭代计划。
- 仓库无公司 / 业务线品牌字样。

**启动命令**：
> 继续 LabelFrame 迭代 0（奠基）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md，按范围执行；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 1：契约与 ZPL（计划中）

**目标**：跑通「契约 → 校验 → ZPL」，用日志模拟打印机验证输出。

**范围**：
- `LabelFrame.Core`：LabelContract（字段清单）、LabelLayout（版式元素：文本 / 条码 / 二维码 / 图片 / 线，毫米坐标）、数据校验（必填缺失拒绝）。
- ZPL 编码器：文本、Code128、图片占位，毫米 → 点换算。
- 日志传输（模拟打印机）。
- 单元测试：golden test、校验用例。

**不在范围**：作业队列、HTTP API、真实打印机、中文位图（迭代 2）、Android。

**验收**：
- `dotnet test` 全绿。
- 库位码契约 → 校验 → ZPL 输出正确（含 `^BC`）。
- 缺必填字段时校验返回问题码。

**启动命令**：
> 继续 LabelFrame 迭代 1（契约与 ZPL）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 1 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 2：WinHost 打印闭环（计划中）

**目标**：Windows 上端到端打印闭环：作业队列 + 本地 HTTP API + 真实打印机。

**范围**：
- 作业队列：SQLite 持久化、幂等（requestId）、逐张状态、挂起 / 恢复 / 取消、批内顺序。
- 本地 HTTP API：提交（异步返回 jobId）、进度查询、错误码。
- 传输：TCP 9100、Windows 驱动（USB）。
- 中文渲染：内嵌字体栅格化为位图（^GF）。

**不在范围**：Server 路由（迭代 3）、模板管理（迭代 4）、Android（迭代 5）。

**验收**：
- 真实 Zebra（USB / IP）打出库位码，条码可扫。
- 批量 50 张连续打印；缺纸挂起、恢复续打；服务重启不丢作业。
- 中文标签真实打印可读。

**启动命令**：
> 继续 LabelFrame 迭代 2（WinHost 打印闭环）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 2 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 3：Server 路由（计划中）

**目标**：设备注册 + 定向投递，多人 / 多设备并发打印互不干扰；无业务系统也能测试。

**范围**：
- Server：设备注册、设备目录、作业定向投递（请求带发起设备 ID）。
- WinHost 注册到 Server、接收作业。
- Server 测试入口（无业务系统也能提交打印、连打印机验证）。
- 作业状态集中可查。

**不在范围**：模板下发（P2）、Android（迭代 5）。

**验收**：
- 两台设备并发打印互不干扰。
- 作业状态可查；设备离线语义明确。

**启动命令**：
> 继续 LabelFrame 迭代 3（Server 路由）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 3 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 4：模板管理 + 预览（计划中）

**目标**：单机模板管理（增删改 + 导入 / 导出模板包）与设计期预览。

**范围**：
- 模板存储：本机文件 / SQLite；契约 + 版式 + 静态图片资源的「模板包」导入导出（zip）。
- 预览渲染：LabelDocument → PNG（设计期，PC）。
- 模板按项目 / 客户分组。

**不在范围**：WMS 模板下发（P2）。

**验收**：
- 模板包可在两台电脑间导入导出。
- 预览与真实打印效果一致（抽查）。
- 模板按项目分组可用。

**启动命令**：
> 继续 LabelFrame 迭代 4（模板管理 + 预览）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 4 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 5：PDA 宿主（计划中）

**目标**：Android / PDA 上跑通「网页 → Server → PDA 宿主 → IP 打印机」与本地直连。

**范围**：
- AndroidHost：前台服务、开机自启、本地服务（本地 HTTP / JS 桥预留）。
- 传输：IP 9100；蓝牙在迭代 6。
- 注册 Server、接收定向投递。
- PDA 单张同步快捷路径。

**不在范围**：蓝牙（迭代 6）。

**验收**：
- PDA 网页 → Server → PDA 宿主 → IP 打印机打出物料码。
- 开机自启、前台服务常驻；失败回执明确。

**启动命令**：
> 继续 LabelFrame 迭代 5（PDA 宿主）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 5 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 迭代 6：P1 收尾（计划中）

**目标**：补齐 P1 能力并完成试点验收准备。

**范围**：
- PDA 蓝牙传输。
- 失败项单独重打。
- 打印机测试页 / 在线状态。
- 模板按项目分组（如迭代 4 未完成则在此补齐）。

**不在范围**：P2 项。

**验收**：各项有真实设备验收；试点指标（扫码通过率、重打 / 漏打率、批量成功率、耗时对比）可测量。

**启动命令**：
> 继续 LabelFrame 迭代 6（P1 收尾）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md（迭代 6 小节），对照上一迭代成果，严格按范围执行；提交用 Conventional Commits；不推 tag；不改未规划内容；仓库内容不得出现公司 / 业务线品牌字样。

---

## 检查点：试点验收（待定）

按 [REQUIREMENTS.md](REQUIREMENTS.md) §8 成功衡量执行：
- 先测基线，再测新系统，同指标对比；
- 真实扫码枪抽 50 张；连续 100 张压力验证（含重启 / 断网）；
- 产出试点对比报告。

## 待需求（有真实需求再排）

- net48 版 WinHost（Win7 / 8 老电脑，尽量兼容）。
- WMS 模板下发（复用模板包格式）。
- 其他打印机指令集（TSPL / CPCL）。
- 打印历史统计。
- 多打印机并行。