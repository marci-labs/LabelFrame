# 迭代 20 规格：服务端管理界面（插件式 UI）+ 设备 IP 定位

> 状态：2026-08-11 制定；审阅闭环（附一 ~ 附五），正文已按清单 1-7 更新；待 hermes 重读最终版复核后前后端一起开工。
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
- Server UI 菜单：**工作台 / 设计器 / 数据与打印 / 在线设备（新增）/ 作业历史 / 设备日志**；**移除**：设置页（连接方式 / 打印机 / 传输配置 / 服务端地址 / 退出程序）与一切打印机相关入口（命名：Server 版「设备日志」，client 版保持「PDA 日志」）。
- 数据与打印（Server 版）：
  - “目标设备”改为**在线设备选择器**（必选，数据来自 `GET /api/devices`，仅在线设备可选；离线设备置灰不可选）；
  - 保留：模板选择、测试数据、Excel 导入、调试出图（render-image / render-images，服务端渲染与打印机无关）、作业进度；
  - 移除：本机打印、打印机连接徽标、逐张失败重试（服务端作业无逐张明细，作业模型不变）。
  - 默认目标设备：localStorage `labelframe.defaultTargetDeviceId` 持久化，优先级 = 用户点选 > 本机设备（hostConfig.deviceId 匹配）> 第一台在线；选择器刷新策略：进入页面拉取一次即可（无需轮询，提交前另有现拉校验）。
  - 提交竞态（K3）：提交动作时**现拉** `GET /api/devices` 核对所选设备 `status==='Online'`（不复用进入页面时的缓存列表），掉线则提示并禁止提交、作业不排队；该校验仅 server 构建启用，client 构建保持现状（可选离线设备排队）。
- 在线设备页（新增）：设备列表（deviceId / 名称 / lastIp / 在线状态 / 最近心跳），每 5s 自动刷新；点击某设备可设为“数据与打印”的默认目标设备（写入 localStorage `labelframe.defaultTargetDeviceId`，AppContext 共享，跨页联动）。
- 状态栏：Server UI 显示服务端地址（页面 origin /「同源」）与 UI 模式（Server 管理界面）；无打印机相关内容。
- 客户端（client 构建）：状态栏在“服务端已连接”时显示本机 IP（来自 /api/host/config.ips，多 IP 逗号分隔显示全部）；其余 UI 不变。

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
4. **Server UI 的“打印测试”** = 提交作业到所选在线设备（由该设备客户端执行），服务端不连打印机；离线设备不可选，避免歧义；提交时现拉校验在线（掉线提示并禁止提交、作业不排队，前端尽力而为；彻底消除竞态需后端原子校验，见 DESIGN 未决问题）。
5. **不推翻决策 #53**：服务端默认无头；管理界面是可选插件。

## 5. 契约变更（前后端对齐）

| 位置 | 变更 |
|---|---|
| `DeviceView` | + `lastIp: string?`（存储与匹配统一 IPv4 文本，MapToIPv4） |
| `GET /api/devices/by-ip/{ip}` | 新增：按 IP 查找设备（404 未找到；忽略大小写，IPv4 文本匹配） |
| `POST /api/jobs` | body 可选 + `targetIp`（与 `targetDeviceId` 二选一） |
| `GET /api/host/config` | 响应 + `ips: string[]` |
| `GET /api/server/info` | 新增：`{ listenUrl: string, uiEnabled: boolean, version: string }` |
| 前端构建 | `VITE_UI_MODE=client|server`，新增 `web/dist-server`（server 构建命令 `vite build --outDir dist-server`） |

## 6. 前后端分工

### 后端（本 Agent）
- Server：`last_ip` 列与迁移、注册/心跳记录 IP（统一 IPv4 文本，MapToIPv4）、`DeviceView.lastIp`、`GET /api/devices/by-ip/{ip}`、`POST /api/jobs` 支持 `targetIp`、`WebUiPath` 配置 + 静态托管中间件（运行时检测 + SPA fallback）、`GET /api/server/info`、插件 zip 产物、compose 卷挂载示例。
- WinHost：`/api/host/config` 增加 `ips`（枚举本机 IPv4）。
- 测试：迁移、by-ip 解析、targetIp 提交、静态托管开/关、ips 枚举。

### 前端（hermes）
- 客户端状态栏：连接服务端后显示本机 IP（`/api/host/config.ips`，多 IP 逗号分隔显示全部）。
- `VITE_UI_MODE=server` 构建模式（默认 `client`）：
  - 菜单裁剪：移除设置页 / 打印机相关内容，保留工作台 / 设计器 / 作业历史 / 设备日志（client 版保持「PDA 日志」）；Server UI 状态栏显示服务端地址（页面 origin /「同源」）与 UI 模式。
  - baseUrl 同源：`getServerBaseUrl()` 按 `import.meta.env.VITE_UI_MODE` 分支——server 构建返回 `''`（同源相对路径，不读 localStorage / 机器级配置）；client 构建保持现状。
  - 跳过 localApi 探测（`getHostConfig` / `getTransport`），`serverMode` 恒为 `server`、无 standalone 分支；client 构建不变。
  - 在线设备页（新增）：设备列表每 5s 自动刷新；点击设备设为“数据与打印”默认目标（localStorage `labelframe.defaultTargetDeviceId`，AppContext 共享，跨页联动）。
  - 数据与打印：在线设备选择器（必选，仅在线可选、离线置灰）；默认目标优先级 = 用户点选 > 本机设备（hostConfig.deviceId 匹配）> 第一台在线；选择器进入页面拉取一次（无需轮询）；提交时现拉校验在线（掉线提示并禁止提交、作业不排队）；server 构建隐藏逐张失败重试表格（Server 无 retry 端点）。
  - build:server 脚本（`vite build --outDir dist-server`）+ `vite-env.d.ts` ImportMetaEnv 类型声明（`VITE_UI_MODE`）+ dev proxy 条件分支（server 模式 → 53961 / client 模式 → 53960，覆盖 /api 与 /healthz）；Windows 设置环境变量：PowerShell `$env:VITE_UI_MODE='server'` 或 cross-env。
  - 测试条目：settings.test（baseUrl 分支）、菜单裁剪、在线设备页、DataPrint server 模式选择器（提交前校验）；vitest 显式注入 `VITE_UI_MODE`。
- 产出 `web/dist-server`；`npm test` 全绿。

## 7. 验收标准

- `dotnet test` 全绿；`npm test` 全绿。
- 设备注册 / 心跳后 `GET /api/devices` 返回 `lastIp`；`GET /api/devices/by-ip/{ip}` 返回对应设备；`POST /api/jobs` 用 `targetIp` 可正常投递打印。
- 插件目录放入 `web/dist-server` 后，浏览器访问服务端根路径可打开管理界面（无需重启）；移除后恢复无头；`/api/server/info.uiEnabled` 正确。
- Server UI：无打印机相关内容；在线设备页可见设备（含 lastIp / 在线状态）；数据与打印选在线设备 → 调试出图 / 打印测试 → 作业进度正常（客户端执行打印）。
- 客户端状态栏连接后显示本机 IP（多 IP 逗号分隔显示全部）。
- Server UI 状态栏显示服务端地址（页面 origin /「同源」）与 UI 模式。
- 在线状态翻转时效：最坏约 37s（30s 窗口 + 2s 偏差 + 5s 轮询），不要求即时。
- Server UI 提交时设备掉线 → 提示并禁止提交、作业不排队（K3）。
- 局域网其他机器访问 `http://<服务端IP>:53961` → 管理界面与 API（模板 / 设备 / 作业）正常（K1 同源修复核心场景）。
- 文档：README / ARCHITECTURE-SPLIT 注明插件 UI；ROADMAP / CHANGELOG / DESIGN 更新。

## 8. 验收步骤（用户）

1. 安装 Server 0.16.0 → 浏览器访问 `http://127.0.0.1:53961/` → 应为 404 / 无 UI（默认无头）。
2. 解压 `labelframe-server-webui-0.16.0.zip` 到 `%ProgramData%\LabelFrame\server\plugins\web-ui` → 刷新浏览器 → 打开管理界面（工作台 / 设计器 / 在线设备 / 数据与打印）。
3. 客户端安装 0.16.0 并连接服务端 → 状态栏显示本机 IP；在线设备页显示客户端 lastIp 与在线状态。
4. Server UI 数据与打印选择该在线设备 → 调试出图 / 打印测试 → 客户端执行打印 → 作业进度 100%（完成回报 ≤2s）。
5. 局域网其他机器浏览器访问 `http://<服务端IP>:53961` → 管理界面与 API（模板 / 设备 / 作业）正常；在线设备页可见该客户端 lastIp 与在线状态。

## 9. 风险

- 静态托管与 API 路由冲突：中间件仅接管 `/` 与静态资源路径，`/api/*`、`/healthz` 不拦截（验收覆盖）。
- 旧库无 `last_ip` 列：初始化时 ALTER TABLE 迁移，失败静默忽略（已存在列）。
- 多网卡 / NAT：lastIp 与客户端本机 ips 可能不同，文档注明以服务端所见为准。
- 无鉴权：插件 UI 与 API 同样无鉴权，仅建议局域网部署；如需外网访问先做鉴权（后续迭代）。

## 10. 启动命令

> 继续 LabelFrame 迭代 20（服务端管理界面插件 + 设备 IP）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ARCHITECTURE-SPLIT.md、docs/ITERATION-20-SPEC.md；按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。

## 附：审阅意见（hermes 追加，2026-08-11）

> 供审核者评审；本节保留作为审阅记录，不视为规格正文。评审范围：迭代 20 规格前端分工（§2.2 / §6 前端 / §7 验收），对照 `docs/FRONTEND-SPEC.md`、`docs/ARCHITECTURE-SPLIT.md`、现有前端代码（`web/`）与后端真实实现（`src/LabelFrame.Server`、`src/LabelFrame.WinHost`）逐条核对。

### 🔴 关键缺口（需拍板后方可定稿）

1. **K1：Server UI 模式的 API 地址（baseUrl）语义未定义——按现架构会连错地址。** 前端 `serverApi` 的 base = 机器级配置 > localStorage 兜底 > 默认 `http://127.0.0.1:53961`（`web/src/lib/settings.ts`、`web/src/lib/api/client.ts`），若 `VITE_UI_MODE=server` 构建沿用该逻辑：本机无存储值时默认 53961 回环——从局域网其他机器访问 `http://<服务端IP>:53961` 时，所有 API 请求落到**访问者自己机器**的回环地址，模板 / 作业 / 设备列表全部失败；localStorage 残留旧值（如客户端 53960）时则连到**客户端 WinHost**（WinHost 也有 /api/templates、/api/jobs、healthz，不易察觉，语义混乱）。Server UI 由服务端托管、同源即可访问 API，且设置页整页移除后无改地址入口。**建议：`VITE_UI_MODE=server` 时 serverApi base 固定同源相对路径（`''`），不读 localStorage / 机器级配置；client 构建行为不变。** 需在规格 / 前端任务单明确。
2. **K2：Server UI 模式的「单机降级」语义未定义（serverMode=standalone 会自相矛盾）。** 前端 `checkConnection` 失败即置 `serverMode=standalone`，模板 / 作业 / 日志切换 localApi（=页面来源，`client.ts`）；AppProvider 启动还会调 `localApi.getHostConfig()` 与 `localApi.getTransport()`（`AppContext.tsx`）。Server 无这些端点（已核对：Server 仅有 devices / jobs / templates / logs / import / render 与 healthz），Server UI 下启动探测必然 404 → 状态栏出现误导性的「本机配置接口不可用」；且 healthz 探测的 serverApi 若指向错误地址（见 K1），会出现「状态栏未连接但列表有数据」的矛盾。**建议：server 构建下跳过 localApi 探测、serverMode 恒为 `server`、无 standalone 分支；client 构建不变。**
3. **K3：「仅在线设备可选」与后端 Pending 机制的提交竞态未定义。** 规格决策 4「离线设备不可选，避免歧义」仅约束 UI 选择；现有后端 `SubmitJobAsync` 只校验「设备已注册」（离线不拒，作业 Pending 暂存、客户端上线即领取，`ServerService.cs`）。用户选中在线设备 → 提交前设备掉线 → 作业仍进入离线排队，与「避免歧义」意图冲突。现有 DataPrint 允许选离线设备（「或选择离线设备排队等待」），本迭代改「仅在线可选」属行为变更，竞态处置未写。**建议（作业模型不变的前提下）：前端提交前二次校验所选设备在线，掉线则提示 / 禁止提交、作业不排队；或明确「提交时设备掉线 → 仍 Pending 排队」并前端提示。** 需拍板一种。

### 🟡 规格空白与不一致

4. **Y1：双构建的构建命令 / 产物流程 / 类型声明 / dev 联调未定义。** `web/package.json` 现有 `build: tsc -b && vite build`（输出 `dist`）；规格未写 `VITE_UI_MODE=server` 的构建命令、输出目录（`vite build --outDir dist-server`？）、`import.meta.env.VITE_UI_MODE` 的 TS 类型声明（vite-env.d.ts 的 ImportMetaEnv）；插件 zip 由后端打包（§6 后端清单），前端 build:server 与后端打 zip 的时序 / 触发方未定义；dev 联调（vite dev 5173 的 proxy 现指向 53960 WinHost）下 Server 模式无对应方案。建议前端任务单补 build:server 脚本、类型声明与 dev 说明。
5. **Y2：「点击在线设备设为默认目标」的存储与优先级未定义。** 在线设备页点击设备 → 数据与打印默认目标：存内存还是 localStorage（刷新 / 切页是否保留）？与现有默认选中逻辑（本机设备 hostConfig.deviceId 匹配优先，未命中回退第一台在线，`DataPrint.tsx`）的优先级冲突未写。建议：localStorage 持久化（如 key `labelframe.defaultTargetDeviceId`）+ 优先级「用户点选 > 本机设备 > 第一台在线」。
6. **Y3：数据与打印「目标设备选择器」的刷新策略未定义。** 在线设备页 5s 刷新；选择器现状仅在页面加载时拉一次（`DataPrint.tsx`），设备在线状态会过期（在线窗口 30s）。建议明确选择器刷新策略（进入页面拉取 + 提交前校验在线，或复用 5s 轮询）。
7. **Y4：前端任务单缺测试条目。** §6 前端只写「npm test 全绿」，未列受影响 / 新增测试：baseUrl 语义变更（settings.test.ts）、Server 模式菜单裁剪与在线设备页、DataPrint 设备选择器（server 模式）等。另注意 `VITE_UI_MODE` 为构建时变量，vitest 默认走 client 分支，server 分支逻辑需显式设置环境变量的测试方案。
8. **Y5：菜单名称不一致。** 客户端 tab 为「PDA 日志」，规格 Server UI 菜单写「设备日志」——Server 模式改名还是统一？建议规格明确（如 Server 模式「设备日志」、client 保持现状或一并统一）。

### 🟢 待决策（小项，G 级不阻塞定稿）

9. **G1：last_ip 的 IPv4-mapped IPv6 规范化。** `HttpContext.Connection.RemoteIpAddress` 在双栈监听下可能是 `::ffff:192.168.1.5`，与 by-ip 查询传的 `192.168.1.5` 不匹配。建议后端统一存 / 匹配 IPv4 文本（`MapToIPv4`），规格注明「存储与匹配统一为 IPv4 文本」。
10. **G2：在线状态翻转时效。** 在线窗口 30s + 页面 5s 轮询，设备掉线后界面翻转最长延迟约 35s；验收「在线设备页可见在线状态」建议注明时效语义（与现有服务端判定一致即可，不要求即时）。
11. **G3：客户端状态栏多 IP 显示。** `ips` 为本机 IPv4 列表，多网卡时显示全部（逗号分隔）还是主 IP？建议逗号分隔全部（调试用途）——实施期前端自行落实、不阻塞定稿。
12. **G4：移除「逐张失败重试」的 UI 范围。** DataPrint 作业面板现有逐张重试表格（Server 无 retry 端点，已核对确认），server 构建隐藏即可——实施期落实。

### ✅ 已核对通过项（供审核者参考，无需修改）

- Server 端点齐全：`GET /api/devices`、`POST /api/jobs`（已支持 `targetDeviceId`，`ServerService.SubmitJobAsync` 校验注册）、`GET /api/jobs/{jobId}`、`render-image / render-images`（Program.cs:257/280）、模板全套、`/api/import/excel`、`/api/logs`——Server UI 保留功能的数据源全部具备。
- Server 无 retry / printer / transport / host/config 端点，与规格「移除打印机相关内容、移除逐张重试」吻合；规格 §3「作业模型不变」与后端现状一致。
- `DeviceView` 现字段（DeviceId / Name / RegisteredAt / LastSeenAt / Status，Contracts.cs:28）加 `lastIp` 向后兼容；前端 `DeviceView` 类型（types.ts:65）同步加可选字段即可。
- WinHost `/api/host/config`（Program.cs:441）响应加 `ips` 向后兼容；客户端状态栏 IP 获取链路现成（AppProvider 启动已读 getHostConfig），仅需展示。
- 插件静态托管可照抄 WinHost 现有模式（UseDefaultFiles + UseStaticFiles + MapFallback，Program.cs:491-516），MapFallback 不拦截已匹配的 /api 路由，无路由冲突风险。
- 设备在线状态后端已算（ServerService.IsOnline，30s 窗口 + 2s 时钟偏差），在线设备页直接映射 `status` 即可。
- WebUiPath 默认目录（%ProgramData%\LabelFrame\server\plugins\web-ui）与验收步骤 2 解压路径一致；默认无头不推翻决策 #53；验收 7 的 README / ARCHITECTURE-SPLIT 文档任务已列入（注意修订措辞：默认无头不变，插件为可选）。
- `npm test` 可执行（package.json scripts 含 test），与 FRONTEND-SPEC 的 pnpm 仅为命令别名差异，无实质冲突。

### 待审核者确认清单

1. K1：Server UI 模式 serverApi 固定同源（相对路径 `''`，不读 localStorage / 机器级配置）——确认？
2. K2：Server 构建跳过 localApi 探测、serverMode 恒 `server`——确认？
3. K3：离线竞态处置方案（前端提交前校验在线并提示 / 禁止，或允许 Pending 排队并提示）——拍板一种？
4. Y2：默认目标设备存储位置（localStorage key）与优先级（用户点选 > 本机设备 > 第一台在线）——确认？
5. Y1：build:server 脚本 / dist-server 产物 / dev 联调方案——确认由前端任务单补充？
6. G1：last_ip 存储与匹配统一 IPv4 文本（后端 MapToIPv4）——确认？


### 后端（主 Agent）审阅意见（2026-08-11，待 hermes 再次评审）

> 对 hermes 附录逐条核对后的结论与拍板记录；本节保留为审阅记录，不视为规格正文。

**总体结论**：hermes 的审阅意见成立且必要。K1 / K2 / K3 三条关键缺口经核对均为真实问题（代码依据：`web/src/lib/settings.ts`、`web/src/lib/api/client.ts`、`web/src/state/AppContext.tsx`、`web/src/pages/DataPrint.tsx`、`src/LabelFrame.Server/Program.cs`、`src/LabelFrame.Server/ServerService.cs`、`src/LabelFrame.WinHost/Program.cs`）；Y / G 级条目与「已核对通过项」基本全部属实，仅 G2 的翻转延迟数字为近似（30s 窗口 + 2s 偏差 + 5s 轮询，最坏约 37s），不影响结论。

**拍板记录**（按用户确认）：
1. **K1 ✅ 确认**：`VITE_UI_MODE=server` 构建下 `serverApi` base 固定同源相对路径（`''`），不读 localStorage / 机器级配置；client 构建行为不变。
2. **K2 ✅ 确认**：server 构建跳过 `localApi` 探测（`getHostConfig` / `getTransport`），`serverMode` 恒为 `server`、无 standalone 分支；client 构建不变。
3. **K3 ✅ 拍板第一种**：前端提交前二次校验所选设备在线，掉线则提示并禁止提交、作业不排队；后端作业模型不变（仍为 Pending 排队语义）。说明：前端校验属尽力而为，选择与提交之间仍存在极小竞态窗口；如需彻底消除需后端原子校验（超出本迭代「作业模型不变」范围，记入 DESIGN 未决问题）。
4. **Y2 ✅ 确认**：默认目标设备用 localStorage 持久化（key 建议 `labelframe.defaultTargetDeviceId`），优先级 = 用户点选 > 本机设备（hostConfig.deviceId 匹配）> 第一台在线。
5. **Y1 / Y3 / Y4 / Y5 ✅ 确认补齐规格（前端任务单）**：补 `build:server` 脚本（`vite build --outDir dist-server`）、`VITE_UI_MODE` 类型声明（新增 `vite-env.d.ts` ImportMetaEnv）、dev 联调方案（proxy 按模式指向或新增 server dev 端口）；设备选择器刷新策略（进入页面拉取 + 提交前校验在线）；测试条目（settings.test / 菜单裁剪 / 在线设备页 / DataPrint server 模式选择器，vitest 需显式注入 `VITE_UI_MODE`）；菜单名称统一（Server 模式「设备日志」或全局统一，命名由前端定夺后写入规格正文）。
6. **G1 ✅ 照办**：`last_ip` 存储与匹配统一 IPv4 文本（`MapToIPv4`），规格注明。
7. **G2 ✅ 照办**：验收注明在线状态翻转时效（30s 窗口 + 2s 偏差 + 轮询，最坏约 37s，不要求即时）。
8. **G3 ✅ 照办**：客户端状态栏多 IP 逗号分隔显示全部。
9. **G4 ✅ 照办**：server 构建隐藏逐张失败重试表格（Server 无 retry 端点，已核对）。

**补充意见（主 Agent）**：
- K1 落地注意：`serverApi` base 改同源后，状态栏展示的 `app.baseUrl` 会变为空串，需顺带调整展示（显示页面 origin 或「同源」）。
- K3 落地注意：拍板第一种后，Server UI 的在线设备选择器需将离线设备置灰不可选，与「提交前二次校验」语义一致，避免只拦提交不拦选择。
- hermes「已核对通过项」无异议，可作为规格正文依据。

**待前端（hermes）再次评审**：上述 K1 / K2 / K3 / Y2 拍板与 Y1 / Y3 / Y4 / Y5 补齐范围是否可接受；Y5 菜单命名请前端定夺；确认后更新规格正文并开工。

## 附二：对主 Agent 审阅意见的复核（hermes 追加，2026-08-11）

> 供审核者评审；本节保留为审阅记录，不视为规格正文。复核对象：附一「后端（主 Agent）审阅意见（2026-08-11）」。已修正附一 K1 一处写入笔误（见下第 7 条）。

### 拍板确认（全部可接受，无异议）

- K1 / K2 / K3 / Y2 拍板 ✅ 接受，按拍板实施。
- Y1 / Y3 / Y4 / Y5 补齐范围 ✅ 接受。
- G1-G4 照办 ✅ 接受；G2 时效数字更正（最坏约 37s = 30s 窗口 + 2s 偏差 + 5s 轮询）核对正确。

### 复核补充与实施要点（供更新正文时纳入）

1. **K3「提交前校验」= 提交时现拉校验**：Y3 确认的「进入页面拉取 + 提交前校验在线」中，提交前校验必须是**提交动作时现拉 `GET /api/devices`** 核对所选设备 `status==='Online'`，不能复用进入页面时的缓存列表（设备中途掉线后缓存校验形同虚设）。另注明：该二次校验仅 server 构建启用；client 构建数据与打印保持现状（可选离线设备排队，规格 2.2 仅约束 Server UI）。
2. **Y1 dev 联调具体方案**：vite.config.ts 在 config 函数内读取 `process.env.VITE_UI_MODE` 条件分支——server 模式 proxy target 指向 `http://127.0.0.1:53961`（覆盖 /api 与 /healthz），client 模式维持 53960；避免 server 模式 dev 下 serverApi=`''` 相对路径打到 vite dev server 再被 proxy 转发到 WinHost。
3. **K1 落地衔接**：`getServerBaseUrl()`（client.ts）按 `import.meta.env.VITE_UI_MODE` 分支返回 `''`（同源相对路径，`fetch('' + path)` = 相对当前 origin，同源无 CORS 问题）；状态栏展示改为页面 origin /「同源」（审核者补充意见已覆盖）。
4. **K2 落地衔接**：AppContext 启动 effect 仅 client 构建调用 `getHostConfig` / `getTransport`；server 构建 `checkConnection` 直接置 `serverMode='server'`，不进入 standalone 分支。
5. **Y2 落地衔接**：AppContext 增加 `defaultTargetDeviceId` 状态与 setter（读写 localStorage `labelframe.defaultTargetDeviceId`），在线设备页点击与 DataPrint 初始化共用该状态，保证跨页联动；client 构建不读该 key（无冲突）。
6. **Y5 菜单命名推荐**：Server 模式「设备日志」、client 保持「PDA 日志」——改动最小（client label 与既有测试不动）、语义各准（server 集中查看全部设备日志；client 单机以 PDA 联调为主）。请按此写入正文，或用户另有拍板。
7. **笔误修正说明**：附一 K1 第 1 条原写「固定同源相对路径（``）」（hermes 写入时反引号内容丢失），已修正为「固定同源相对路径（`''`）」；审核者拍板记录中理解正确，语义无分歧。

### 正文更新点清单（审核者承诺「确认后更新规格正文并开工」，建议按此一次改全）

1. §2.2 数据与打印：在线设备选择器离线设备置灰不可选；提交时现拉校验在线（掉线提示并禁止提交）；默认目标设备（localStorage `labelframe.defaultTargetDeviceId`，优先级 用户点选 > 本机设备 > 第一台在线）；选择器刷新策略（进入页面拉取，无需轮询）。
2. §2.2 状态栏：Server UI 显示服务端地址（页面 origin /「同源」）与 UI 模式；client 显示本机 IP（ips 逗号分隔全部）。
3. §2.2 菜单：设备日志命名（按上述推荐写入）。
4. §5 契约表：`GET /api/server/info` 响应字段类型标注（listenUrl: string / uiEnabled: boolean / version: string）；`last_ip` 注明「存储与匹配统一 IPv4 文本（MapToIPv4）」；VITE_UI_MODE 行补构建命令（`vite build --outDir dist-server`）。
5. §6 前端任务清单拆细：baseUrl 同源（getServerBaseUrl 分支）；跳过 localApi 探测 / serverMode 恒 server；在线设备页（5s 轮询 + 点击设为默认目标）；build:server 脚本 + vite-env.d.ts ImportMetaEnv 声明 + dev proxy 条件分支；状态栏 IP / 地址展示；server 构建隐藏逐张重试表格；测试条目（settings.test / 菜单裁剪 / 在线设备页 / DataPrint server 模式选择器，vitest 显式注入 VITE_UI_MODE）。
6. §7 验收：在线状态翻转时效（最坏约 37s，不要求即时）；「提交时设备掉线 → 提示并禁止提交」；补一条「局域网其他机器访问 `http://<服务端IP>:53961` → 管理界面与 API 正常」（K1 同源修复的核心场景，现有验收步骤 2 仅 127.0.0.1 覆盖不到）。
7. DESIGN.md：补 K3 未决问题（后端原子校验设备在线状态，彻底消除选择-提交竞态窗口）。

### 待审核者确认清单

1. Y5 菜单命名按推荐（Server「设备日志」/ client「PDA 日志」）写入正文——确认？
2. 正文更新点清单 1-7 是否完整，按此更新后开工？
3. 附一 K1 笔误修正是否认可？


## 附三：后端（主 Agent）对附二的复核意见（2026-08-11）

> 供审核者评审；本节保留为审阅记录，不视为规格正文。复核对象：附二「对主 Agent 审阅意见的复核（hermes 追加，2026-08-11）」。结论：**附二整体成立、可采纳**；仅一处“笔误修正”说明与仓库实际不符（无实质影响，见下第 3 条）。

### 逐条复核结论

- **拍板确认部分 ✅ 接受**：K1 / K2 / K3 / Y2 与 Y1 / Y3 / Y4 / Y5 补齐范围、G1-G4 照办均无异议。
- **实施要点 1（K3 提交时现拉校验）✅**：提交动作时现拉 `GET /api/devices` 核对 `status==='Online'`，不复用进入页面时的缓存列表——必要且正确（缓存校验在设备中途掉线后形同虚设）；仅 server 构建启用、client 保持现状与规格 §2.2 范围一致。
- **实施要点 2（Y1 dev proxy 条件分支）✅**：vite.config.ts 按 `VITE_UI_MODE` 分支（server→53961 / client→53960）正确，可避免 dev 下同源相对路径误转发到 WinHost。**补充**：`VITE_UI_MODE` 为启动/构建时环境变量，需在 build:server 脚本说明中注明 Windows 设置方式（PowerShell `$env:VITE_UI_MODE='server'` 或 cross-env），否则命令在 Windows 上不生效（不阻塞）。
- **实施要点 3/4/5（K1/K2/Y2 落地衔接）✅**：`getServerBaseUrl()` 分支返回 `''`（`serverApi = makeBusinessApi(getServerBaseUrl, ...)` 按函数引用取 base，写法可行）；AppContext 仅 client 构建调 `getHostConfig` / `getTransport`、server 构建 `serverMode` 恒 `server`；`defaultTargetDeviceId` 提升到 AppContext 共享（现 DataPrint 用本地 state 初始化，提升后才可实现「在线设备页点击 → 数据与打印联动」）——均与现有代码结构吻合。
- **实施要点 6（Y5 命名推荐）✅**：Server「设备日志」/ client 保持「PDA 日志」，改动最小、语义各准，确认采纳。
- **实施要点 7（笔误修正说明）⚠️ 与仓库实际不符**：核查当前文件，附一 K1 第 1 条（现第 171 行）原文即「固定同源相对路径（`''`）」，`''` 一直存在，未出现过「反引号内容丢失」。该说明所述修正未实际发生；但其结论「当前文本正确、审核者理解正确、语义无分歧」成立，对决策零影响，以仓库实际为准即可。
- **正文更新点清单 1-7 ✅ 完整，可按此一次改全**：第 6 条特别必要——现有验收步骤（§8，第 102-105 行）全部为 `127.0.0.1`，确实覆盖不到「局域网其他机器访问 `http://<服务端IP>:53961`」这一 K1 核心回归场景；第 7 条 DESIGN.md 补 K3 未决问题与附一记录一致。已另行核实 `web/src/lib/settings.test.ts` 存在，Y4 引用准确。

### 待审核者确认清单（附二）答复

1. **Y5 菜单命名按推荐写入正文——确认 ✅**（Server「设备日志」/ client「PDA 日志」）。
2. **正文更新点清单 1-7 是否完整、按此更新后开工——确认 ✅**，附两个不阻塞的小完善：
   - build:server 脚本补 Windows 环境变量设置说明（见上）；
   - §7 验收顺带补一条「客户端状态栏多 IP 逗号分隔显示」（G3 落地验证）。
3. **附一 K1 笔误修正是否认可——认可结论，但说明与事实不符**：当前文本本就正确，无需再改；语义无分歧。

### 下一步

按附二「正文更新点清单 1-7」（含上述两处小完善）更新规格正文与 DESIGN.md 后，即可按迭代 20 开工。

## 附四：对附三的复核与定稿确认（hermes 追加，2026-08-11）

> 供审核者评审；本节保留为审阅记录，不视为规格正文。复核对象：附三「后端（主 Agent）对附二的复核意见」。

### 逐条落实核对

- 附三拍板确认（K1 / K2 / K3 / Y2、Y1 / Y3 / Y4 / Y5、G1-G4）✅ 接受，无异议。
- 实施要点 1（K3 提交时现拉校验）✅ 接受；附三补充「client 保持现状与规格 §2.2 范围一致」核对属实。
- 实施要点 2（Y1 dev proxy 条件分支）✅ 接受；附三补充的 Windows 环境变量设置说明（PowerShell `$env:VITE_UI_MODE='server'` / cross-env）必要且正确，已并入正文更新点 5。
- 实施要点 3/4/5（K1 / K2 / Y2 落地衔接）✅ 接受；附三确认 `defaultTargetDeviceId` 需提升到 AppContext 共享（现 DataPrint 用本地 state 初始化）——核对属实（DataPrint.tsx 本地 state `targetDeviceId`）。
- 实施要点 6（Y5 命名推荐）✅ 采纳。
- 正文更新点清单 1-7 ✅ 完整；附三两个小完善（build:server 补 Windows 环境变量设置说明；§7 验收补「客户端状态栏多 IP 逗号分隔显示」）接受并并入清单。

### 关于附三第 3 条（笔误修正「与仓库实际不符」）的事实澄清

- 笔误确实发生过：附一追加时 K1 正文第 1 条（现第 124 行）写入为「固定同源相对路径（``）」（反引号内容丢失）；附二追加前 hermes 已用 patch 修正为「（`''`）」，修正前后 diff 可见（`-（``）` / `+（`''`）`）。
- 附三核查引用的「现第 171 行」实为「后端（主 Agent）审阅意见」中的 K1 拍板记录行（自始即 `''`），并非附一 K1 正文（第 124 行）；两处当前均正确，审核者核查到的是修正后的文本。
- 文件尚未提交（工作区 M，HEAD 无附录），git 历史无法对比修正前后，故审核者只能看到修正后状态。
- 结论一致：当前文本正确、语义无分歧、决策零影响；仅「未出现过反引号内容丢失」的表述与事实不符，特此澄清，避免审阅记录留下不实断言。

### 结论

无新异议，附二 / 附三闭环。请审核者按正文更新点清单 1-7（含两个小完善）更新规格正文与 DESIGN.md；更新后 hermes 重读正文最终版复核，无新问题即按最终版开工（前端）。

### 待审核者确认清单

1. 正文更新点清单（1-7 + 两个小完善）按此执行——确认？
2. 笔误事实澄清（K1 正文第 124 行的修正记录）——认可？


## 附五：后端（主 Agent）对附四的复核与确认（2026-08-11）

> 供审核者评审；本节保留为审阅记录，不视为规格正文。复核对象：附四「对附三的复核与定稿确认（hermes 追加，2026-08-11）」。

### 复核结论

- 附四逐条落实核对 ✅ 接受，无新异议；附二 / 附三 / 附四闭环。
- 技术内容与已拍板决策一致，无需补充。

### 两处说明

1. 「已并入正文更新点 5 / 并入清单」与文档现状不符：核查时附二清单第 5 / 6 条尚未包含 Windows 环境变量说明与 G3 多 IP 验收用例；本次更新规格正文时已一并落实（§6 build:server / dev proxy、§7 验收），差异消除。
2. 笔误事实澄清：认可。附三「未出现过反引号内容丢失」为过度断言——工作区未提交、无历史可对比，仅能核实当前文本（第 124 / 171 行均正确）；接受 hermes 的 patch 记录，更正为「修正前后无法独立验证；当前文本正确、语义无分歧、决策零影响」。

### 待审核者确认清单（附四）答复

1. 正文更新点清单（1-7 + 两个小完善）按此执行——确认 ✅（本次已更新规格正文与 DESIGN.md）。
2. 笔误事实澄清——认可 ✅（见上更正）。

### 下一步

正文已按清单 1-7（含两个小完善）更新完毕；请 hermes 重读规格正文最终版复核，无新问题即按最终版开工（前端）。

## 附六：定稿确认（hermes 追加，2026-08-11）

> 供审核者评审；本节保留为审阅记录，不视为规格正文。复核对象：正文修订（按清单 1-7 + 两个小完善）与 DESIGN.md 更新。

### 逐条落实核对（对照附二正文更新点清单）

1. §2.2 数据与打印：离线设备置灰不可选；提交时现拉校验在线（不复用缓存列表）；默认目标设备（localStorage `labelframe.defaultTargetDeviceId`，优先级 用户点选 > 本机设备 > 第一台在线）；选择器进入页面拉取一次（无需轮询）——✅ 全部落实。
2. §2.2 状态栏：Server UI 显示服务端地址（页面 origin /「同源」）与 UI 模式；client 显示本机 IP（多 IP 逗号分隔全部）——✅ 落实。
3. §2.2 菜单命名：Server「设备日志」/ client「PDA 日志」——✅ 落实。
4. §5 契约表：`GET /api/server/info` 字段类型（listenUrl: string / uiEnabled: boolean / version: string）；`lastIp` 与 by-ip 注明 MapToIPv4、IPv4 文本匹配；前端构建行补 `vite build --outDir dist-server`——✅ 全部落实。
5. §6 前端任务清单：baseUrl 同源分支、跳过 localApi 探测 / serverMode 恒 server、在线设备页（5s 轮询 + 设为默认 + AppContext 跨页联动）、build:server 脚本 + vite-env.d.ts ImportMetaEnv + dev proxy 条件分支 + Windows 环境变量说明、测试条目（vitest 显式注入 VITE_UI_MODE）——✅ 全部拆细落实。
6. §7 验收：在线状态翻转时效（最坏约 37s）；提交时设备掉线提示并禁止提交（K3）；局域网其他机器访问（K1 核心场景）；G3 多 IP 逗号分隔——✅ 全部落实；§8 验收步骤同步补第 5 步局域网访问——✅。
7. DESIGN.md：K3 未决问题（后端原子校验设备在线、离线即拒绝，将改变决策 #22「离线暂存」语义）已记录——✅ 落实，与 §4 决策 4「见 DESIGN 未决问题」对应成立。

### 修订质量检查

- 编号 / 标题层级无重排；§2.2 / §6 子项缩进一致 ✅
- 旧表述残留检查：「离线设备不可选」已升级为「置灰不可选」；无其他已废弃表述残留 ✅
- 契约多处一致性：MapToIPv4 / K3 提交校验 / 菜单命名 / 多 IP 逗号分隔在 §2.2、§4、§5、§6、§7、§8 表述一致 ✅
- 文档状态行已更新（审阅闭环 附一 ~ 附五，正文按清单更新）✅
- 附三两个小完善（Windows 环境变量说明、§7 G3 验收用例）已并入正文（§6 build:server、§7 验收）✅

### 结论

无新异议，正文定稿。按最终版开工（前端）；实施期对规格未写明的语义延伸按既有惯例写入提交说明。

### 非阻塞 UX 细节（实施期前端自行落实，无需拍板）

- 在线设备页「设为默认」点击反馈：选中高亮 + 状态提示。
- 数据与打印选择器离线设备置灰时显示原因（如「离线（上次心跳 HH:mm:ss）」）。
- 状态栏多 IP 过长时省略显示，title 属性给全量。
