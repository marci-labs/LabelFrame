# LabelFrame

面向仓库场景的标签打印框架：模板契约 + 打印服务 + 设备宿主（PC / PDA）。

## 愿景

**方便仓库完成标签打印，提高办公效率。**

- **方便** = 少步骤、就近打印、零学习成本、少故障、少求人；
- **效率** = 打印快、能批量、不重复劳动、不出错、融进业务动作。

## 它能做什么

- **可视化模板设计器**：画布拖拽编排（毫米网格 / 参考线吸附 / 撤销 / 图层），条码二维码实时渲染，打印与预览同源。
- **数据打印**：表单填数或 Excel 导入（列映射）批量打印，可下载按模板生成的 Excel 模板。
- **作业中心**：一次请求多张、异步 jobId、逐张状态、幂等不重打、断点续打、失败项单独重打、历史按设备可查。
- **设备路由**：业务系统提交作业 → 服务端定向投递到指定电脑 / PDA 就近出纸；设备离线暂存、上线即领。
- **传输插件**：TCP 9100 / Windows 驱动 / Zebra SDK / 日志模拟，厂商可自研插件接入（服务端集中分发、客户端一键安装）。
- **零打印机验证**：调试出图（PNG / zip 下载）与日志模拟打印，不耗纸排查问题。

## 快速开始（按你的角色）

### 文员：一台电脑一台打印机（单机模式）

1. 从 [GitHub Releases](https://github.com/marci-labs/LabelFrame/releases) 下载并安装 `LabelFrame-Server-x.x.x.msi` 与 `LabelFrame-Client-x.x.x.msi`（需 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)，缺失时安装包会给出官方下载链接）。
2. Client 装完会自动打开浏览器 `http://127.0.0.1:53960`（没弹出就手动访问）。
3. 「设计器」页新建模板：画布拖入文本 / 条码 / 二维码，毫米级排版，实时预览。
4. 「数据与打印」页填数据（或「下载 Excel 模板」→ 填好 → 导入）。
5. 点打印。第一次使用先在「设置 → 连接方式」选连接（无打印机时选 `log` 模拟验证，出图在 `%LOCALAPPDATA%\LabelFrame\print`）。

### 管理员：一台服务器 + 多台打印电脑

1. 服务器安装 Server（[部署指南](docs/DEPLOY.md) 四种形态任选：MSI / Docker / Ubuntu / 单机同装）。
2. 每台打印电脑安装 `LabelFrame-Client-x.x.x.msi`。
3. 每台 Client 的「设置」页把服务端地址改为 `http://<服务器IP>:53961` → 测试连接 → 保存并**重启 Client**。
4. 模板集中在服务端维护，各电脑打印与历史互不干扰；可选用管理界面插件（服务端网页管理）与「客户端下载」页集中分发安装包。

### 业务系统开发者：HTTP 提交作业

```bash
curl -X POST http://<服务器>:53961/api/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "requestId": "order-123-1",          # 幂等键：重放不重复打印
    "targetDeviceId": "pc-01",           # 目标设备（可按 IP 查设备后提交）
    "templateName": "库位标签",
    "labels": [ { "data": { "zone": "A", "code": "A-01-02-03" } } ]
  }'
# → 202 {"jobId": "...", "status": "Pending"}；GET /api/jobs/{jobId} 查进度与逐张状态
```

设备注册 / 心跳 / 领取 / 回报等完整契约见 [docs/DESIGN.md](docs/DESIGN.md) 的「Server API 契约」。

## 部署形态对照

| 形态 | 服务端 | 适用 |
|---|---|---|
| 单机 | Server + Client 两个 MSI 同机安装 | 一台电脑一台打印机 |
| Windows 服务器 | Server MSI（Windows 服务，端口 53961） | 局域网多机 |
| Linux / Docker | `docker compose up -d` 或 systemd | 局域网多机 |

安装细节、Docker / Ubuntu 步骤、签名与配置项见 **[docs/DEPLOY.md](docs/DEPLOY.md)**。

## 仓库结构

| 项目 | 说明 |
|---|---|
| `src/LabelFrame.Core` | 契约 / 版式模型、数据校验、ZPL 图片编码、作业队列、模板库、传输插件接口 |
| `src/LabelFrame.Rendering` | Skia 整版渲染（打印与预览同源） |
| `src/LabelFrame.Api` | Server / WinHost 共享的 HTTP 契约（DTO / 错误码）与端点实现 |
| `src/LabelFrame.Server` | 无头服务端：模板库 / 作业中心 / 设备投递 / 调试出图 / 日志 |
| `src/LabelFrame.WinHost` | Windows 打印客户端：本地界面托管 / 作业打印 / 连接与插件管理 |
| `src/LabelFrame.AndroidHost` | Android / PDA 打印宿主（实验性，不随发布构建，见其 README） |
| `web/` | Web 前端（Vite + React + TS + Konva）：客户端界面与服务端管理界面双构建 |

## 开发

前置：.NET 10 SDK；前端需 node + pnpm。

```bash
dotnet build LabelFrame.slnx        # 构建
dotnet test LabelFrame.slnx         # 全量测试
cd web && pnpm install && pnpm dev  # 前端开发（连本机 WinHost）
```

- 无打印机验证打印闭环：`powershell -ExecutionPolicy Bypass -File .\scripts\demo-winhost.ps1`。
- 提交即跑 CI（`.github/workflows/ci.yml`：dotnet 构建 / 测试 + 前端 lint / 双模式测试 / 构建）。
- 发版：更新 ROADMAP / CHANGELOG 后推送 `v*` tag，由 `release.yml` 自动构建发布（见 [docs/DEPLOY.md](docs/DEPLOY.md) §7）。

## 文档

- [docs/DESIGN.md](docs/DESIGN.md) —— 架构设计与决策记录
- [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) —— 需求：场景、底线、能力、边界、成功衡量
- [docs/ROADMAP.md](docs/ROADMAP.md) —— 迭代计划与状态
- [docs/DEPLOY.md](docs/DEPLOY.md) —— 部署指南（MSI / Docker / Ubuntu / 分发 / 签名）
- [AGENTS.md](AGENTS.md) —— 给 AI 协作的常驻约束
