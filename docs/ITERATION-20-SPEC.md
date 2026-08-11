# 迭代 20 规格：服务端管理界面（插件式 UI）+ 设备 IP 定位

> 状态：2026-08-11 制定；待 hermes 评审前端分工后前后端一起开工。
> 目标：① 客户端连接服务端后，状态栏显示本机 IP（方便调试）；② 服务端可按需“安装”一个可选的管理界面——插件形式、放进去即生效；该界面不含任何打印机相关内容（服务端无驱动），保留工作台 / 设计器，新增“在线设备”菜单，数据与打印可浏览全部在线设备并选择其一发送打印测试。

## 1. 背景

- 0.15.x 已交付：服务端无头化（无 Web UI，仅 /healthz + API）、客户端（WinHost）托管完整 Web UI、Windows 服务 / Ubuntu systemd / Docker、长轮询推送通知、作业完成回报独立循环（0.15.5）。
- 新需求：
  1. 调试便利：客户端与服务端连接后，在客户端界面状态栏显示当前设备（本机）的 IP 地址；
  2. 服务端管理界面：默认服务端仍无头（不推翻决策 #53），但提供可选“插件”——放进去即生效；界面去掉所有与打印机有关的内容（服务端没有驱动），保留工作台 / 设计器，新增“在线设备”菜单；数据与打印页复用：浏览全部在线设备、选择其一发送打印测试。
- 与迭代 19 遗留问题衔接：迭代 19 讨论的“其他业务应用按 IP 查找设备并触发打印”在本迭代落地设备侧基础（服务端记录设备 IP + 按 IP 查找 + targetIp 提交便捷入参），完整业务对接流程见 DESIGN 未决问题更新。

## 2. 范围

### 2.1 设备 IP（后端）
- Server `devices` 表新增 `last_ip TEXT NULL`（SQLite：CREATE TABLE IF NOT EXISTS 含新列 + 旧库 ALTER TABLE 兼容迁移）。
- 设备注册（POST /api/devices）与心跳（GET /api/devices/{id}/jobs/notify、GET /api/devices/{id}/jobs/pending）时，从 `HttpContext.Connection.RemoteIpAddress` 记录 / 刷新 `last_ip`。
- `DeviceView` 增加 `lastIp: string?`（向后兼容，旧前端忽略）。
- 新增 `GET /api/devices/by-ip/{ip}`：精确匹配（忽略大小写）返回设备；未找到返回 404（`DeviceNotFound`）。
- `POST /api/jobs` 请求体支持可选 `targetIp`：服务端解析为 `deviceId` 后按现有定向投递逻辑执行（与 `targetDeviceId` 二选一；同时提供时 `targetDeviceId` 优先；`targetIp` 找不到设备返回 404）。
- WinHost：`GET /api/host/config` 响应增加 `ips: string[]`（本机 IPv4 列表，枚举 `NetworkInterface`，过滤回环）；客户端状态栏据此显示本机 IP。
- 说明：`lastIp` 是“服务端看到的来源 IP”，`ips` 是“客户端本机枚举 IP”，多网卡 / NAT / VPN 场景可能不同，文档注明。

### 2.2 服务端管理界面（插件式 UI，前端为主）
- 同一前端工程新增构建模式 `VITE_UI_MODE=server`（默认 `client`）：
  - `web/dist`（现有，client 产物，打包进 Client MSI）保持不变；
  - 新增 `web/dist-server`（server 产物，作为服务端 UI 插件包）。
- Server UI 菜单：**工作台 / 设计器 / 数据与打印 / 在线设备（新增）/ 作业历史 / 设备日志**；**移除**：设置页（连接方式 / 打印机 / 传输配置 / 服务端地址 / 退出程序）与一切打印机相关入口。
- 数据与打印（Server 版）：
  - “目标设备”改为**在线设备选择器**（必选，数据来自 `GET /api/devices`，仅在线设备可选；离线设备不可选）；
  - 保留：模板选择、测试数据、Excel 导入、调试出图（render-image / render-images，服务端渲染与打印机无关）、作业进度；
  - 移除：本机打印、打印机连接徽标、逐张失败重试（服务端作业无逐张明细，作业模型不变）。
- 在线设备页（新增）：设备列表（deviceId / 名称 / lastIp / 在线状态 / 最近心跳），每 5s 自动刷新；点击某设备可设为“数据与打印”的默认目标设备。
- 状态栏：显示服务端地址与 UI 模式（Server 管理界面）；无打印机相关内容。
- 客户端（client 构建）：状态栏在“服务端已连接”时显示本机 IP（来自 /api/host/config.ips）；其余 UI 不变。

### 2.3 插件宿主（后端）
- `ServerOptions` 新增 `WebUiPath`：
  - 默认 Windows `%ProgramData%\LabelFrame\server\plugins\web-ui`；Linux `/var/lib/labelframe/server/plugins/web-ui`；
  - `LABELFRAME_SERVER_WEB_UI` 环境变量覆盖；为空 = 不启用；
  - appsettings-server.json 默认不写（保持无头默认）。
- 托管中间件：**每次请求运行时检测 `Directory.Exists(WebUiPath)`**（放进去即时生效、无需重启；移除即失效）：
  - 存在且请求为 `/` 或静态资源 → 托管插件静态文件 + SPA fallback（/ → index.html，未知路径回退 index.html）；
  - 不存在 → 保持现状（/healthz 与 API 正常，根路径 404）。
- 新增 `GET /api/server/info`：`{ listenUrl, uiEnabled, version }`（调试 / 前端可选探测用）。
- 插件产物：`web/dist-server` 打包为 `artifacts/labelframe-server-webui-<version>.zip`，README/文档说明“解压到插件目录即生效”。
- 服务端 MSI 不打包 UI（默认无头）；Docker compose 增加可选卷挂载示例：`./plugins/web-ui:/var/lib/labelframe/server/plugins/web-ui`。

## 3. 不在范围

- 服务端不提供任何打印机连接 / 驱动 / 传输相关内容（明确不做）。
- 服务端 UI 鉴权 / 多用户（与现有 API 一致，局域网内无鉴权；风险记录，后续按需）。
- 真正的 .NET 程序集插件（AssemblyLoadContext）——本迭代以“静态前端包目录”作为插件形态；未来如需业务插件再演进。
- 服务端逐张作业明细 / 失败重试 / 作业模型变更。
- 业务系统完整对接（仅提供设备 IP 记录、by-ip 查找、targetIp 提交入参；对接文档另出）。
- “服务端直接连打印机 / 后端打印”语义（打印永远由目标设备本机客户端执行）。

## 4. 决策

1. **插件形态 = 静态前端包目录**：`plugins/web-ui` 目录存在即托管、移除即无头；运行时检测，放进去即时生效。不做程序集插件（避免过度设计）。
2. **前端单一代码库双构建**：`VITE_UI_MODE=client|server` 产两个产物，菜单 / 功能按模式裁剪，避免双份维护。
3. **设备 IP 语义**：服务端记录“服务端看到的来源 IP”（last_ip，每次心跳刷新）；客户端状态栏显示“本机枚举 IP”（ips）。IP 是便捷查找不是身份，deviceId 仍是唯一稳定键。
4. **Server UI 的“打印测试”** = 提交作业到所选在线设备（由该设备客户端执行），服务端不连打印机；离线设备不可选，避免歧义。
5. **不推翻决策 #53**：服务端默认无头；管理界面是可选插件。

## 5. 契约变更（前后端对齐）

| 位置 | 变更 |
|---|---|
| `DeviceView` | + `lastIp: string?` |
| `GET /api/devices/by-ip/{ip}` | 新增：按 IP 查找设备（404 未找到） |
| `POST /api/jobs` | body 可选 + `targetIp`（与 `targetDeviceId` 二选一） |
| `GET /api/host/config` | 响应 + `ips: string[]` |
| `GET /api/server/info` | 新增：`{ listenUrl, uiEnabled, version }` |
| 前端构建 | `VITE_UI_MODE=client|server`，新增 `web/dist-server` |

## 6. 前后端分工

### 后端（本 Agent）
- Server：`last_ip` 列与迁移、注册/心跳记录 IP、`DeviceView.lastIp`、`GET /api/devices/by-ip/{ip}`、`POST /api/jobs` 支持 `targetIp`、`WebUiPath` 配置 + 静态托管中间件（运行时检测 + SPA fallback）、`GET /api/server/info`、插件 zip 产物、compose 卷挂载示例。
- WinHost：`/api/host/config` 增加 `ips`（枚举本机 IPv4）。
- 测试：迁移、by-ip 解析、targetIp 提交、静态托管开/关、ips 枚举。

### 前端（hermes）
- 客户端状态栏：连接服务端后显示本机 IP（`/api/host/config.ips`）。
- `VITE_UI_MODE=server` 构建模式：菜单裁剪（移除设置页 / 打印机相关内容），保留工作台 / 设计器 / 作业历史 / 设备日志。
- 在线设备页（新增）+ 数据与打印“在线设备选择器”（仅在线可选）+ 点击在线设备设为默认目标。
- 产出 `web/dist-server`；`npm test` 全绿。

## 7. 验收标准

- `dotnet test` 全绿；`npm test` 全绿。
- 设备注册 / 心跳后 `GET /api/devices` 返回 `lastIp`；`GET /api/devices/by-ip/{ip}` 返回对应设备；`POST /api/jobs` 用 `targetIp` 可正常投递打印。
- 插件目录放入 `web/dist-server` 后，浏览器访问服务端根路径可打开管理界面（无需重启）；移除后恢复无头；`/api/server/info.uiEnabled` 正确。
- Server UI：无打印机相关内容；在线设备页可见设备（含 lastIp / 在线状态）；数据与打印选在线设备 → 调试出图 / 打印测试 → 作业进度正常（客户端执行打印）。
- 客户端状态栏连接后显示本机 IP。
- 文档：README / ARCHITECTURE-SPLIT 注明插件 UI；ROADMAP / CHANGELOG / DESIGN 更新。

## 8. 验收步骤（用户）

1. 安装 Server 0.16.0 → 浏览器访问 `http://127.0.0.1:53961/` → 应为 404 / 无 UI（默认无头）。
2. 解压 `labelframe-server-webui-0.16.0.zip` 到 `%ProgramData%\LabelFrame\server\plugins\web-ui` → 刷新浏览器 → 打开管理界面（工作台 / 设计器 / 在线设备 / 数据与打印）。
3. 客户端安装 0.16.0 并连接服务端 → 状态栏显示本机 IP；在线设备页显示客户端 lastIp 与在线状态。
4. Server UI 数据与打印选择该在线设备 → 调试出图 / 打印测试 → 客户端执行打印 → 作业进度 100%（完成回报 ≤2s）。

## 9. 风险

- 静态托管与 API 路由冲突：中间件仅接管 `/` 与静态资源路径，`/api/*`、`/healthz` 不拦截（验收覆盖）。
- 旧库无 `last_ip` 列：初始化时 ALTER TABLE 迁移，失败静默忽略（已存在列）。
- 多网卡 / NAT：lastIp 与客户端本机 ips 可能不同，文档注明以服务端所见为准。
- 无鉴权：插件 UI 与 API 同样无鉴权，仅建议局域网部署；如需外网访问先做鉴权（后续迭代）。

## 10. 启动命令

> 继续 LabelFrame 迭代 20（服务端管理界面插件 + 设备 IP）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ARCHITECTURE-SPLIT.md、docs/ITERATION-20-SPEC.md；按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。
