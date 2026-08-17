# 迭代 22 规格：打印测试体验 + 传输插件化 + 客户端下载分发

> 状态：2026-08-17 范围讨论会定稿（用户拍板决策 1-4，见 §3）；后端（主 Agent）负责后端实施，前端（hermes）负责前端实施，本文档为双方契约；**等待前端（hermes）评估后并行开工**。
> 目标：围绕「易用性 / 权限边界 / 插件化 / 分发」四项收口——打印测试更好上手、客户端与服务端边界明确、传输方式插件化、客户端安装包由服务端分发。

## 1. 背景

- 0.17.0 已交付：自动化发布（ghcr + GitHub Release + MSI 签名通道）、服务端管理界面插件（迭代 20）、传输仍为 `ITransport` + `TransportConfig` 硬编码四模式（Log / Tcp / WindowsDriver / Zebra）。
- 迭代 22（2026-08-17 用户提出，范围讨论会定稿）：
  1. 打印测试体验：客户端与服务端「数据与打印」可下载 Excel 模板直接套用；客户端只能本机打印测试、显示本机设备名；客户端只看自己的作业历史、服务端看全部。
  2. 传输插件化：把连接方式抽象为插件——统一接口（连接 / 发送 / 状态 / 测试）+ 参数模型 + 注册表按需装配（配置指定插件与参数即启用）；第三方厂商可自研插件接入（TSPL / CPCL、蓝牙、云打印等）。本轮完成机制，下一轮（迭代 23）实现具体厂商插件（精成打印机）并真机测试。
  3. 客户端下载分发：服务端提供客户端安装包上传 / 下载（管理界面 + 目录），客户端更新安装包默认从服务端获取。

## 2. 范围（定稿）

### 2.1 打印测试体验（后端 + 前端）
- **下载 Excel 模板**：客户端 + 服务端「数据与打印」页，Excel 导入区旁新增「下载 Excel 模板」按钮 → 按当前选中模板的契约字段生成 `.xlsx`（列 = 字段显示名，首行示例值 = 模板 testData；无 testData 时示例行为空），用户拿到后直接套用 Excel 导入做打印测试。
  - 后端：新增 `POST /api/import/excel-template`（Server 与 WinHost 都实现；请求体 = 字段键列表 + 显示名 + 示例值 → 返回 xlsx 文件流）。生成逻辑放 Core 共享（`LabelFrame.Core.Excel.ExcelTemplateWriter`），复用已有 `TemplateFrame.Excel.Simple` 的 `SimpleExcel.Write`（1.0.5 已确认提供写能力，不新增依赖）。
- **权限边界**：
  - 客户端 UI（非 Server 构建）：目标设备**固定为本机**——只显示「本机（{deviceName}）」标签，不再有设备选择器；提交时 `targetDeviceId` = 本机 deviceId（`GET /api/host/config` 取）。
  - 服务端 UI：保持现状（可自由选在线设备打印测试）。
  - 本机路由（决策 1A）：本机已注册且在线 → 走服务端路由（作业进服务端历史，管理员可见）；本机未注册 / 离线 → 自动降级本机直连（WinHost 直接打印，作业只在本机历史），并提示原因。
- **客户端显示本机设备名称**：客户端状态栏增加「本机：{deviceName}」（与本机 IP 并列）；DataPrint 目标标签显示「本机（{deviceName}）」。
- **作业历史可见性**：`GET /api/jobs` 新增可选 `deviceId` 过滤参数（Server 端过滤）；客户端「作业历史」页在服务端模式下传本机 deviceId → 只看自己的作业；服务端 UI 不传 → 看全部。

### 2.2 传输插件化（后端为主，前端配合）
- **统一接口**（Core，`LabelFrame.Core.Transport.Plugins`）：`ITransportPlugin`（描述 + 参数模型 + 工厂）→ 返回 `IPrintTransport`（发送，现有接口不变）+ 可选 `IPrinterStatusProvider`（状态）+ 可选 `ITestableTransport`（连接测试）。
- **参数模型**：`TransportParameterSpec`（Key / 中文标签 / 类型 String|Int|Bool|Select / 必填 / 默认 / 枚举 / 提示）+ `TransportPluginParameters`（弱类型字典强类型取值）+ `ITransportPluginContext`（宿主日志写入器 + 数据目录）。
- **注册表按需装配**：`ITransportPluginRegistry`（ListPlugins / GetPlugin / CreateTransport）。内置插件（Log、TCP 9100、Windows 驱动、Zebra）用同一接口实现并注册；第三方插件 DLL 从插件目录扫描加载（collectible `AssemblyLoadContext`，单个插件失败只记日志、不影响宿主启动）。
- **加载 / 卸载 / 使用**（决策 2A）：
  - 加载：启动时扫描插件目录（默认 `%ProgramData%\LabelFrame\Client\plugins`，`LABELFRAME_PLUGINS` 可覆盖）下 `*.dll`，反射发现 `ITransportPlugin`。
  - 使用：配置 `pluginId + params` 即启用；`TransportManager` 从注册表创建实例 → 校验 / 测试（`ITestableTransport`）→ 切换 + 持久化；作业 Worker / 打印机状态 / 测试页统一从 `CurrentTransport` 取，现有链路零改动。
  - 卸载：从插件目录删除对应 dll / 文件夹 → 重启宿主后不再加载；或配置切回内置插件即不再使用。**本轮不做运行时热卸载 / 热替换（ALC unload），记 DESIGN 未决**（迭代 23 接厂商插件时如确有需要再评估）。
- **配置演进与向后兼容**（决策见 §3）：`connection.json` 新格式 `{ "pluginId": "tcp9100", "params": { "host": "...", "port": "9100" } }`；旧 `{ Mode, TcpHost, ... }` 读取时自动映射（Log→`log`、Tcp→`tcp9100`、WindowsDriver→`winspool`、Zebra→`zebra`）；`LABELFRAME_TRANSPORT` 环境变量同样映射，老配置零迁移。

### 2.3 客户端下载分发（后端 + 前端）
- **服务端上传安装包目录 + 管理**：数据目录下新增 `client-packages`（Windows `%ProgramData%\LabelFrame\server\client-packages`；Linux `/var/lib/labelframe/server/client-packages`；`LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）。目录直放文件与 API 上传两种方式都支持（决策 3A）。
- **API**：`GET /api/client-packages`（列表：文件名 / 大小 / 修改时间 / 下载 URL）、`POST /api/client-packages`（multipart 上传）、`GET /api/client-packages/{file}`（下载，`application/octet-stream` + Content-Disposition）、`DELETE /api/client-packages/{file}`（删除）。所有文件名参数做路径穿越防护（只允许文件名，不允许 `..` / 子目录）。
- **Ubuntu / Docker 部署允许挂载**（用户强调）：`packaging/ubuntu/docker-compose.yml` 增加可选卷挂载 `./client-packages:/var/lib/labelframe/server/client-packages`，与 `plugins/web-ui` 一致；README 补充挂载说明。
- **服务端 UI 新增「客户端下载」页**（hermes）：安装包列表 + 上传 + 下载 + 删除。
- **客户端设置「更新与安装包」卡片**（hermes）：列出服务端可用安装包（`serverApi.listClientPackages`），下载按钮指向 `{serverBaseUrl}/api/client-packages/{file}`；单机模式（服务端不可达）提示需先连接服务端。

## 3. 决策（2026-08-17 用户拍板）

| # | 决策点 | 结论 |
|---|---|---|
| 1 | 客户端本机打印测试路由 | **A**：本机在线 → 服务端路由（作业进服务端历史）；本机未注册 / 离线 → 降级本机直连 + 提示原因 |
| 2 | 插件卸载语义 | **A**：删除插件目录文件 + 重启生效；本轮不做运行时热卸载（记未决） |
| 3 | 安装包上传方式 | **A**：Server UI 页面上传（API）+ 目录直放文件两种都支持；**Ubuntu 部署允许挂载 `client-packages` 目录** |
| 4 | Excel 模板生成位置 | **A**：放 Core 共享（`LabelFrame.Core.Excel`，引用 `TemplateFrame.Excel.Simple`；AndroidHost 编译成本可接受） |

## 4. 不在范围

- 具体厂商打印机插件实现（迭代 23 精成打印机 + 真机测试）。
- PDA / AndroidHost（延后至迭代 24）。
- 传输插件运行时热卸载 / 热替换（未决，迭代 23 评估）。
- 服务端 UI 鉴权 / 多用户（与现有 API 一致，局域网内无鉴权；沿用既有风险记录）。
- 客户端自动升级（仅「从服务端下载安装包」，安装动作仍由用户执行）。

## 5. 后端契约（API 与配置）

### 5.1 传输插件
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/transport | 响应扩展：`{ pluginId, displayName, params, displayText, availablePlugins:[{ id, displayName, description, parameters: spec[] }] }`；旧字段 `mode / availableModes` 保留兼容旧前端 |
| POST | /api/transport | 请求：`{ pluginId, params: {...}, testOnly? }`（保留旧字段名兼容）；校验 → 测试（testOnly 只测不存）→ 切换 + 持久化；响应同 GET |
| GET | /api/transport/plugins | 已加载插件列表（含来源：内置 / 外部 DLL），排障用 |

- 内置插件注册表（id → 参数键）：
  - `log`：无参数。
  - `tcp9100`：`host`（必填）/ `port`（Int，默认 9100）/ `timeoutSeconds`（Int，默认 10）。
  - `winspool`（Windows 驱动）：`printerName`（必填）。
  - `zebra`：`kind`（Select：Tcp / Usb / Driver）+ `host` / `port` / `printerName` / `usbName`（按 kind 必填）。
- 旧配置映射表：`Mode=Tcp → pluginId=tcp9100`、`WindowsDriver → winspool`、`Zebra → zebra`、`Log → log`；旧参数字段映射到新参数键（`TcpHost→host`、`TcpPort→port`、`PrinterName→printerName`、`ZebraKind→kind`、`ZebraUsbName→usbName`）。

### 5.2 Excel 模板下载
| 方法 | 路径 | 说明 |
|---|---|---|
| POST | /api/import/excel-template | Server 与 WinHost 都实现；请求体 `{ columns: [{ key, displayName }], sampleRow: { key: value } }` → 返回 xlsx（application/vnd.openxmlformats-officedocument.spreadsheetml.sheet），第一行 = 显示名表头，第二行 = 示例值 |

### 5.3 作业历史可见性
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/jobs?deviceId=xxx | 新增可选 `deviceId` 过滤；不传 = 全部（Server UI / 管理员），传 = 仅该设备（客户端 UI 传本机 deviceId） |

### 5.4 客户端下载分发
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/client-packages | 列表 `[{ fileName, sizeBytes, modifiedAt, url }]` |
| POST | /api/client-packages | multipart 上传（`file` 字段）；文件名路径穿越防护 |
| GET | /api/client-packages/{fileName} | 下载（octet-stream + Content-Disposition）；404 = 不存在 |
| DELETE | /api/client-packages/{fileName} | 删除；404 = 不存在 |

### 5.5 配置项
- WinHost：`LABELFRAME_PLUGINS`（插件目录，默认 `%ProgramData%\LabelFrame\Client\plugins`）；`connection.json` 新格式 + 旧格式兼容映射。
- Server：`LABELFRAME_SERVER_CLIENT_PACKAGES`（安装包目录，默认数据目录下 `client-packages`）。
- `packaging/ubuntu/docker-compose.yml`：新增 `./client-packages:/var/lib/labelframe/server/client-packages` 卷挂载（注释说明可选）。

## 6. 后端实施拆分（主 Agent）

1. **Core**：`Transport/Plugins/`——`ITransportPlugin`、`TransportParameterSpec`、`TransportParameterType`、`TransportParameterOption`、`TransportPluginParameters`、`ITransportPluginContext`、`ITestableTransport`、`ITransportPluginRegistry`、`BuiltinTransportPlugins`（log / tcp9100）、`PluginDirectoryLoader`（collectible ALC 扫描 + 单插件失败隔离）；`Excel/ExcelTemplateWriter`（SimpleExcel.Write 生成模板，引用 TemplateFrame.Excel.Simple）。
2. **WinHost**：`TransportConfig` 演进（pluginId + params，旧格式兼容映射）；`TransportManager` 改走注册表（校验 / 测试 / 切换 / 持久化）；内置注册表补 winspool / zebra；传输 API 扩展（availablePlugins / displayText / plugins 列表）；`POST /api/import/excel-template`；`GET /api/host/config` 已含 deviceId / deviceName（复用）。
3. **Server**：`GET /api/jobs` 支持 `deviceId` 过滤；`ClientPackages` 服务 + 4 个端点（路径穿越防护）；`POST /api/import/excel-template`；`ServerOptions` 增 `ClientPackagesPath` + 环境变量；docker-compose 挂载。
4. **测试**：Core（参数模型 / 注册表 / Excel 生成 / 目录加载器——新增测试插件 DLL 项目做集成验证）；WinHost（TransportManager 兼容迁移 / 插件切换 / excel-template）；Server（deviceId 过滤 / client-packages 上传下载删除 / 路径穿越 / 404）。

## 7. 前端实施拆分（hermes）

1. `transport.ts` / `TransportPanel.tsx`：按 `availablePlugins` 动态渲染插件表单（spec 驱动：文本 / 数字 / 开关 / 下拉）；连接徽标用后端 `displayText`；无 `availablePlugins` 回退内置 4 模式。
2. `DataPrint.tsx`：新增「下载 Excel 模板」按钮（调 `POST /api/import/excel-template`，按当前模板契约字段 + testData）；客户端构建目标设备固定本机（只显示「本机（{deviceName}）」，提交 `targetDeviceId` = 本机 deviceId；本机未注册 / 离线 → 降级本机直连 + 提示）。
3. `JobHistory.tsx`：客户端构建在服务端模式下传本机 deviceId（只看自己）；服务端构建不传（看全部）。
4. `Settings.tsx`：新增「更新与安装包」卡片（服务端安装包列表 + 下载链接；单机模式提示）。
5. Server UI：新增「客户端下载」页（列表 + 上传 + 下载 + 删除）+ App.tsx 菜单与路由。
6. 客户端状态栏：显示本机设备名称（`hostConfig.deviceName`，与本机 IP 并列）。

## 8. 验收

- `dotnet build LabelFrame.slnx` 通过；`dotnet test` 全绿（含新增用例）。
- web `pnpm test`（vitest）全绿。
- 功能冒烟（联调）：
  - 下载 Excel 模板 → Excel 导入 → 批量打印测试（客户端与服务端两种模式）。
  - 客户端 UI 只能选本机（无其他设备选项）；本机离线时降级直连并提示；服务端 UI 可自由选在线设备。
  - 客户端作业历史只显示本机作业；服务端 UI 显示全部。
  - 安装包上传 → 列表 → 下载 → 删除；路径穿越（`../`）被拒；删除不存在返回 404。
  - 插件：内置 4 插件可切换（先测试后生效）；外部 DLL 放入插件目录 → 重启 → 出现在可用插件列表并可配置启用；删除 DLL → 重启后消失；坏 DLL 不影响宿主启动。
  - Ubuntu / Docker：`docker compose` 挂载 `client-packages` 后上传 / 下载正常。
- 文档：ROADMAP（迭代 22 状态）、CHANGELOG、DESIGN（决策 #67-71 + 未决）更新。

## 9. 风险与未决

- 传输插件运行时热卸载 / 热替换（ALC unload）：依赖固定与线程安全问题，本轮不做；迭代 23 接厂商插件时评估（DESIGN 未决）。
- 外部插件加载安全：插件 DLL 与宿主同权限运行（局域网内无鉴权）；仅从受控插件目录加载，文档注明「只放可信插件」。
- client-packages 上传无鉴权（局域网）：沿用现有 API 无鉴权模型，风险记录；后续如需再排期。


---

## 附三：前端实施说明与待确认清单（hermes 追加，2026-08-17）

> 本节由前端开发者 hermes 在迭代 22 前端实施完成后追加，**供审核者（主 agent）联调时参考**；不视为规格正文。

### 一、前端实施范围（已提交，web pnpm test 178 用例全绿、pnpm build / pnpm build:server / oxlint 通过）

1. 传输插件化：web/src/lib/api/types.ts（TransportParameterSpec / TransportPluginInfo / PluginParams）+ web/src/lib/transport.ts（displayText 优先徽标、mode↔pluginId 映射、spec 默认值 / Select 枚举兼容解析）+ web/src/components/TransportPanel.tsx（availablePlugins 存在时按 spec 动态渲染文本 / 数字 / 开关 / 下拉表单，设置页与 DataPrint 快速切换共用；旧后端无 availablePlugins 时回退内置 4 模式）。
2. 下载 Excel 模板：DataPrint 页头「下载 Excel 模板」按钮，POST /api/import/excel-template（columns = 契约字段 key + displayName，sampleRow = testData 中对应键值；无字段模板按钮禁用）。
3. 权限边界（决策 1A）：客户端构建目标设备固定本机——只显示「本机（{deviceName}）」标签，无设备选择器；本机已注册且在线 → 服务端路由（serverApi + templateName + targetDeviceId=本机 deviceId）；本机未注册 / 离线 → 降级本机直连（localApi 自包含 template，作业仅本机历史）并提示原因（区分「未注册 / 离线」两种文案）。服务端构建保持在线设备选择器不变。
4. 作业历史可见性：客户端构建服务端模式下 GET /api/jobs?limit=100&deviceId={本机 deviceId}；服务端构建不传 deviceId。
5. 客户端下载分发：serverApi.listClientPackages / uploadClientPackage / deleteClientPackage + 下载 URL 构造（{serverBaseUrl}/api/client-packages/{file}，encodeURIComponent）；Server UI 新增「客户端下载」页（列表 / 上传 / 下载 / 删除，删除有确认）；客户端设置页新增「更新与安装包」卡片（服务端可达列出安装包与下载链接，单机模式提示需先连接服务端）。
6. 客户端状态栏：服务端已连接时显示「本机：{deviceName}」（/api/host/config.deviceName，与本机 IP 并列）。

### 二、与规格契约的偏差（均为前端防御性兼容，无后端契约改动）

- TransportParameterSpec.options 与 defaultValue 的 JSON 形状未在契约中精确到字段级：前端**兼容两种序列化**——options 支持 { value, label? }[] 或 string[]；defaultValue 按 type 防御解析（Bool 接受 true / 'true'，Int 接受数字 / 数字字符串）。
- GET /api/transport 新响应中旧 params（平铺 TransportParams）是否仍返回未明示：前端 TransportConfig.params 类型放宽为 TransportParams | PluginParams，插件模式下按 spec 键从配置中提取（未命中用 spec 默认值），新旧后端均可用。
- POST /api/client-packages 上传响应体未定义：前端不依赖响应体，上传成功后重新拉取列表。

### 三、联调时需要的后端配合点

1. **GET /api/transport**：请确保新响应同时携带 pluginId / displayName / params（字典）/ displayText / availablePlugins，且 availablePlugins[].parameters[].options 采用 { value, label? }[] 或 string[] 任一形式（前端两者兼容）；旧字段 mode / availableModes 继续返回。
2. **POST /api/transport**：接受 { pluginId, params: {...}, testOnly? }；切换失败（测试不通过）返回 200 + { ok:false, message, config=当前生效连接 }（与迭代 15 语义一致），前端据此不更新全局连接状态。
3. **GET /api/jobs?deviceId=**：Server 端过滤实现后，客户端作业历史即只显示本机作业；当前前端已按契约传参，后端未实现前该参数被忽略时客户端仍能看到全部（联调确认过滤生效）。
4. **POST /api/import/excel-template**：响应建议带 Content-Disposition: attachment; filename=...（前端解析文件名，否则回退 excel-template.xlsx）；请求体 { columns: [{ key, displayName }], sampleRow: { key: value } } 按契约。
5. **POST /api/client-packages**：multipart 字段名 file；文件名路径穿越防护与 404 语义按规格（前端删除 / 下载均 encodeURIComponent 文件名）。
6. **GET /api/host/config**：确认返回 deviceName（非空），客户端状态栏与 DataPrint 本机标签依赖该字段；旧客户端无此字段时前端显示「未知」并降级直连。

---

## 附四：迭代 22 联调验收报告（hermes 前端实施方，2026-08-17）

> 本节由前端开发者 hermes 联调验收后追加，供审核者（主 agent）确认；验收环境：临时数据目录（%TEMP%\lf-e2e，未污染本机真实数据）、测试 Server 127.0.0.1:53961、测试 WinHost 127.0.0.1:53962（设备 lf-e2e-client / E2E测试本机，与生产客户端 53960 隔离）。

### 一、验证结果清单（按任务 §8 验收逐项）

| # | 场景 | 结果 | 证据 |
|---|---|---|---|
| 1 | 下载 Excel 模板 → 导入 → 批量打印 | ✅ | 客户端与服务端 POST /api/import/excel-template 均返回 xlsx（Content-Disposition 含文件名，表头=显示名、第二行=testData 示例值）；UI 全链路：下载 → Excel 导入 → 列映射自动匹配 → 批量打印 1 张 → 作业已完成 1/1（服务端可见） |
| 2 | 权限边界 | ✅ | 客户端 UI 目标设备只显示「本机（E2E测试本机）」、无设备选择器；本机在线提示「作业经服务端投递」；停掉测试 Server 后 UI 自动降级单机模式并提示（状态栏「服务端未连接（单机模式可用）」）；服务端 UI 目标设备为在线设备选择器（离线设备禁用，提交前现拉校验） |
| 3 | 作业历史可见性 | ✅ | GET /api/jobs?deviceId= 过滤精确（本机 1 条 / 另一设备 1 条 / 不传 2 条）；客户端 UI 作业历史只显示本机 2 条（lf-e2e-client），服务端 UI 显示全部 3 条（含 lf-e2e-client-2 排队中作业） |
| 4 | 客户端状态栏 | ✅ | 「本机：E2E测试本机」与本机 IP 并列显示（/api/host/config.deviceName） |
| 5 | 传输插件 | ✅ | 设置页「连接方式」按 availablePlugins 动态渲染（文本/数字/开关/下拉 spec 驱动）；切换先测试后生效、失败提示且不切换（当前生效连接保持 LOG）；内置 4 插件 + 外部 sample 加载（isExternal=true，GET /api/transport/plugins 返回 5 个）；删除 sample dll 重启后消失（4 个）；坏 dll 不影响宿主启动（日志记录「传输插件加载失败（BadPlugin.dll）：Bad IL format」）；重启后按 connection.json 恢复插件配置 |
| 6 | 客户端下载分发 | ✅ | 上传（multipart 字段 file）→ 列表（fileName/sizeBytes/modifiedAt/url）→ 下载（octet-stream + Content-Disposition）→ 删除（200，磁盘消失，再下载 404）；目录直放文件出现在列表；路径穿越拒绝（..%2f / ../ / ..%5c 均 404）；删除不存在 404；服务端 UI「客户端下载」页列表/上传/删除可用；客户端设置「更新与安装包」列出服务端安装包与下载链接 |
| 7 | 契约核对（附三 §三 6 项） | ✅ | GET /api/transport 同时含 pluginId/displayName/params(字典)/displayText/availablePlugins，options 为 {value,label?}[]（zebra kind），旧字段 mode/availableModes 保留；POST 失败返回 200 + {ok:false,message,config=当前生效连接}；excel-template 返回 Content-Disposition attachment 含文件名；client-packages 上传字段名 file；/api/host/config 返回 deviceName |
| 8 | Ubuntu/Docker | ✅ | 本地构建 linux-x64 镜像 + docker run 挂载 ./client-packages：容器内 API 上传 → 文件落盘宿主机挂载目录；宿主机直放文件 → 容器列表可见；下载内容一致 |

### 二、前端缺陷（联调发现，本轮已修复并补测试）

1. **Excel 导入列映射自动匹配失效**（web/src/lib/excel/mapping.ts + pages/DataPrint.tsx）
   - 实际：下载的 Excel 模板表头为契约字段**显示名**（规格 §5.2），suggestMapping 只按字段**键**归一化匹配 → 中文显示名（库位码）匹配不上英文键（locationCode），三列全部「— 不映射 —」、自动匹配按钮禁用、批量打印不可用。
   - 期望：显示名表头应自动映射到字段键。
   - 修复：MappingField 支持 {key, displayName}，suggestMapping 同时按键与显示名匹配；DataPrint 传契约字段（含显示名）给映射弹窗，选项显示「显示名（键）」；无契约字段（版式推导）时仍按键匹配（兼容）。新增 mapping.test.ts 用例（按显示名匹配 + 键兼容）。
2. **插件参数提交 HTTP 400**（web/src/components/TransportPanel.tsx）
   - 实际：Int/Bool 参数前端发数字/布尔（如 {"port":9100}），后端 TransportApplyRequest.Params 为 `IReadOnlyDictionary<string,string>` → 模型绑定失败 400（无错误体），UI 显示「请求失败（HTTP 400）」而非后端错误信息。
   - 期望：参数按字符串提交，失败时展示后端 ok:false + message。
   - 修复：buildRequest 插件模式把所有参数值序列化为字符串（Bool → "true"/"false"）。UI 复测：TCP 9100 测试连接显示「连接测试失败：无法连接打印机（超时或地址不可达）。」
3. **String 参数默认值显示字面量 "null"**（web/src/lib/transport.ts + lib/api/types.ts）
   - 实际：后端 defaultValue 为 null（如 tcp9100 host）时 specDefaultValue 返回 String(null)="null"，输入框显示 "null"。
   - 期望：显示为空。
   - 修复：null/undefined → ''；TransportParameterSpec.defaultValue 类型补 `| null`。新增 transport.test.ts 用例。

### 三、后端缺陷（只记录，未改后端代码）

1. **外部插件删除后宿主启动崩溃**（违反决策 2A「卸载 = 删除插件目录文件 + 重启生效」）
   - 复现：设置页/API 切换到外部插件 sample → 从插件目录删除 LabelFrame.TransportPlugin.Sample.dll → 重启 WinHost → **启动崩溃退出**（未捕获异常：`System.InvalidOperationException: 传输插件不存在：sample`，TransportManager.CreateTransport → TransportPluginRegistry.CreateTransport，Program.cs:70）。
   - 实际：宿主直接退出（exit 127），服务不可用。
   - 期望：插件缺失时回退默认传输（如 log）+ 日志警告，宿主正常启动，插件列表不再包含该插件。
   - 位置：src/LabelFrame.WinHost/Transport/TransportManager.cs（LoadPersisted 已处理参数缺失回退与 JSON 损坏，未处理插件 id 不在注册表）；src/LabelFrame.Core/Transport/Plugins/TransportPluginRegistry.cs:40 抛异常。
   - 契约归属：后端（WinHost 健壮性缺口；附三 §三 未见此场景约束，属规格「卸载 = 删除文件 + 重启生效」的隐含要求）。

### 四、其他观察（非阻断）

- healthz 响应的 `transport` 字段在切换插件后仍显示 "Log"（旧字段，未跟随当前插件）；前端不依赖该字段，不影响功能；如需精确可让 healthz 透出 displayText。
- 服务端 UI 无打印机/传输相关页面（规格如此）；client-packages 上传无鉴权（规格已记录风险）。

### 五、测试与构建

- web `pnpm test`：**179 用例全绿**（20 文件；迭代 22 前端原 178 + 本轮新增 1 用例、1 断言）。
- web `pnpm build` / `VITE_UI_MODE=server pnpm build:server`：通过。
- `dotnet build LabelFrame.slnx`：0 警告 0 错误；`dotnet test`：**214 用例全绿**（Core 78 / Server 37 / WinHost 74 / Studio 25）。
- 本轮提交：前端 3 处修复（见 §二）+ 本验收报告；后端缺陷待主 agent 修复（见 §三）。
