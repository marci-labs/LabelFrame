# 迭代 23 规格：客户端插件分发——上传服务端 + 客户端安装 / 卸载

> 状态：已定稿（2026-08-17 用户拍板决策 1A-7A；前端 hermes 终审通过，见附三/附四）。
> 目标：把迭代 22 的传输插件机制做成可分发闭环——插件包可上传到服务端（独立目录 + API + Server UI 管理），客户端在界面里浏览服务端可用插件 → 安装（下载 → 校验 → 解压到插件目录 → 重启生效）→ 查看已安装与状态 → 卸载（删除目录 → 重启生效）。

## 1. 背景

- 迭代 22 已完成传输插件化（统一接口 `ITransportPlugin` / 参数模型 / 注册表 / 外部 DLL 目录加载，决策 #67-69）：插件 DLL 放入 `%ProgramData%\LabelFrame\Client\plugins`（`LABELFRAME_PLUGINS` 可覆盖）→ 重启即出现在可用插件列表；卸载 = 删除插件文件 + 重启生效；插件缺失时宿主启动回退默认连接（附五修复）。
- 迭代 22 已完成客户端下载分发（`client-packages` 目录 + 4 个 API + Server UI「客户端下载」页 + 客户端设置「更新与安装包」卡片，决策 #71）：安装包由服务端集中分发，客户端下载后自行运行安装。
- 迭代 23（2026-08-17 用户提出，范围会话中细化）：把「插件 DLL 手动放目录」升级为可分发闭环——**插件包上传服务端 + 客户端界面安装 / 卸载**；与「更新与安装包」的 UI 关系、插件包格式 / 版本 / 校验、安装覆盖策略在会话中定稿。

## 2. 范围（定稿，2026-08-17 用户拍板）

### 2.1 插件包上传服务端（后端 + 前端）
- **插件包格式（决策 1A 推荐）**：zip 格式，根目录含 `manifest.json` + 插件 DLL（主 DLL 实现 `ITransportPlugin`，伴生依赖 DLL 可同目录 / 子目录）；后缀固定 `.lfplugin`（区别于安装包 `.zip` / `.msi`）。
  - `manifest.json` 字段（必填：pluginId / name / version；可选：description / author / minHostVersion）：
    ```json
    { "pluginId": "sample", "name": "示例插件（测试）", "version": "1.0.0",
      "description": "外部 DLL 目录加载示例", "author": "LabelFrame" }
    ```
  - `minHostVersion` 本轮**仅展示、暂不校验**（hermes 附三观察；避免引入宿主版本比较的额外契约，厂商插件接入迭代 24 再评估）。
  - 服务端**不解压不加载**，仅在列表 / 上传时解析 manifest 展示元数据；客户端安装时再做完整校验与预检。
- **服务端存放与 API（决策 2A 推荐）**：**独立 `plugin-packages` 目录 + 独立 `/api/plugin-packages`**（不复用 `client-packages`——服务端需解析 manifest 展示插件元数据，与「安装包不透明文件」语义不同；混入会让「更新与安装包」列表与「插件管理」互相干扰）。
  - `GET /api/plugin-packages`：列表 `[{ fileName, pluginId?, name?, version?, description?, sizeBytes, modifiedAt, url?, valid, invalidReason? }]`（元数据字段可空——invalid 条目解析失败时缺失；hermes 附三 1.2）；zip / manifest 解析失败 → `valid:false` + 原因，仍列出便于管理删除。
  - `POST /api/plugin-packages`：multipart 上传（`file`）；校验「zip + 根 manifest.json + pluginId/name/version 必填」+ 文件名路径穿越防护（复用 client-packages 的规范化逻辑）；成功 200、失败 400 `ErrorView` + 中文原因；**返回体前端不依赖**（上传成功后重新拉列表，与 ClientPackages 页一致；hermes 附三 1.5）。
  - `GET /api/plugin-packages/{fileName}`：下载（octet-stream + Content-Disposition）；404 = 不存在。
  - `DELETE /api/plugin-packages/{fileName}`：删除；404 = 不存在。
- **Server UI（决策 7A 推荐）**：新增「插件管理」页，与「客户端下载」页**并列**——插件包列表（名称 / 版本 / pluginId / 大小 / 时间 / 解析状态）+ 上传 + 下载 + 删除；invalid 条目红标 + 原因，仍可删除。

### 2.2 客户端安装（后端为主，前端配合）
- **安装目录布局（决策 3A 推荐）**：插件包解压到 `plugins/<pluginId>/`（每插件一目录）——卸载 = 删目录，与决策 2A「卸载 = 删除插件文件 + 重启生效」一致；插件目录加载器扩展为「平铺 `plugins/*.dll`（手动放置，向后兼容）+ 子目录 `plugins/<pluginId>/*.dll`（安装包）」两态扫描，单插件失败隔离不变。
- **客户端安装流程**：设置页新增「插件管理」卡片（决策 7A，与「更新与安装包」并列）→ 浏览服务端可用插件（`serverApi.listPluginPackages`，仅 valid 可安装）→ 点「安装」→ `serverApi.downloadPluginPackage(fileName)` 从服务端下载（blob，复用 makeFetchBlob 统一错误语义；hermes 附三 1.1）→ `localApi.installPlugin(file)`（multipart 上传给本机 WinHost，**保留原始文件名** `form.append('file', blob, fileName)`；hermes 附三 2.2）→ WinHost 校验 + 解压 → 返回成功提示「重启客户端后生效」。
  - 为什么前端中转而非 WinHost 直连服务端下载：WinHost 的 `ServerUrl`（路由用）与 UI 配置的服务端地址（机器级 settings.json）是两套，直连会造成地址不一致；前端中转不引入 WinHost 对服务端的耦合（hermes 附三 2.3 确认维持此方案）。
- **已安装插件查看**：`GET /api/plugins/installed` 列出已安装插件（扫描插件目录 + 读 manifest + 合并注册表加载状态）——字段以 §5.2 为准（`source` 必填、`packageDir?`、`installedAt?`；hermes 附三 1.3 统一 §2.3/§5.2）；`loaded=false` 且 `loadError` 非空 = 加载失败（坏 DLL / 缺依赖）；安装后未重启 = `loaded=false`（待生效）。
- **校验（决策 5A 推荐）**：安装时 WinHost 做三层校验——① zip 完整性（解压 CRC）+ 根 manifest.json 必填字段；② 内置插件 ID 拒绝（决策 6A）；③ **安装预检**：临时 collectible `AssemblyLoadContext` 加载包内主 DLL，确认发现 `ITransportPlugin` 实现且至少一个的 `Id` 与 manifest.pluginId 一致（不一致 / 无实现 → 拒绝安装并提示）。预检通过才写入 `plugins/<pluginId>/`。本轮**不做签名**（无鉴权局域网模型，见 §9）。
- **安装覆盖策略（决策 4A 推荐）**：同 `pluginId` 允许**覆盖安装**（先删旧目录再解压新包，替换文件，重启生效）；**不做版本比较**（manifest.version 仅展示，允许降级覆盖）；内置插件 ID 的包拒绝安装。覆盖安装在 WinHost 运行中若目标插件已被加载（Windows 文件锁），删除旧目录可能失败 → 返回明确提示「插件正在使用中，请重启客户端后重试」（详见 §9 风险）。
- **大小上限（hermes 附三 2.1）**：插件包上传 / 安装上限 **64MB**——Server `POST /api/plugin-packages` 与 WinHost `POST /api/plugins/install` 都做显式限制（Kestrel `MaxRequestBodySize` 同步配置，避免默认 413 无错误体）；前端安装 / 上传前按列表 `sizeBytes` 预检并给中文提示。

### 2.3 客户端卸载（后端为主，前端配合）
- 已安装插件（子目录包，`source:"package"`）行内「卸载」→ `window.confirm` 确认 → `POST /api/plugins/uninstall`（`{ pluginId }`）→ WinHost 删除 `plugins/<pluginId>/` → 返回成功提示「重启客户端后生效」；重启后注册表不再装配（缺失回退默认连接，沿用迭代 22 附五修复）。
- 平铺手动放置的 DLL（`source:"manual"`，无 `plugins/<pluginId>/` 目录）**不在卸载范围**（无归属记录，由管理员手动删除文件，与迭代 22 语义一致）；`GET /api/plugins/installed` 对平铺 DLL 展示为「手动放置」只读，不提供卸载按钮。
- 运行时热卸载 / 热替换仍不做（未决，见 §9）。

## 3. 决策（2026-08-17 用户拍板；以下 7 项全部采纳推荐项）

| # | 决策点 | 推荐（R）/ 替代（A） |
|---|---|---|
| 1 | 插件包格式 | **R-A**：zip（根 manifest.json + 插件 DLL），后缀 `.lfplugin`。A：纯 zip 无 manifest（服务端无法展示元数据，客户端安装信息不全）——不推荐 |
| 2 | 服务端存放与 API | **R-A**：独立 `plugin-packages` 目录 + 独立 `/api/plugin-packages`（需解析 manifest，与 client-packages 语义不同）。A：并入 client-packages（列表混入安装包、无元数据、删除时误伤）——不推荐 |
| 3 | 客户端安装目录布局 | **R-A**：`plugins/<pluginId>/` 每插件一目录（卸载 = 删目录；加载器兼容平铺）。A：平铺文件 + 本地注册表文件（归属脆弱、易残留）——不推荐 |
| 4 | 安装覆盖策略 | **R-A**：同 pluginId 允许覆盖安装，不做版本比较（version 仅展示）。A：版本升序才允许（v1 复杂化，无真实需求）——不推荐 |
| 5 | 校验 / 签名 | **R-A**：zip CRC + manifest 必填 + 安装预检（临时 ALC 发现插件并核对 id）；大小上限 64MB；不做签名（无鉴权局域网，风险记录，正式分发再评估）。A：SHA-256 哈希随列表下发、客户端安装时核对（多一跳且不解决信任问题）——可选，v1 不做 |
| 6 | 外部插件可否覆盖内置插件 ID | **R-A**：禁止——安装预检拒绝内置 id（log/tcp9100/winspool/zebra）；加载器 / 注册表登记时外部插件注册内置 id 跳过并记日志。A：允许（外部包可覆盖内置插件，潜在误装 / 恶意风险）——不推荐 |
| 7 | 与「更新与安装包」UI 关系 | **R-A**：**并列**——客户端设置页新增「插件管理」卡片（与「更新与安装包」并列）；Server UI 新增「插件管理」页（与「客户端下载」并列）。A：归并（两类对象安装位置 / 生效方式 / 操作完全不同，归并会混乱）——不推荐 |

## 4. 不在范围

- 具体厂商插件实现（精成打印机顺延至迭代 24）。
- 传输插件运行时热卸载 / 热替换（未决，沿用 DESIGN 记录）。
- 插件包代码签名 / 服务端鉴权（无鉴权局域网模型，沿用既有风险记录）。
- 客户端自动升级（迭代 22 语义不变：仅提供服务端分发，安装动作由用户执行）。
- 插件包依赖的 native 库 / 复杂第三方依赖的兼容性保证（见 §9 风险）。
- `minHostVersion` 版本门槛校验（本轮仅展示；厂商插件接入迭代 24 再评估）。
- PDA / AndroidHost（延后至迭代 25）。

## 5. 后端契约（API 与配置）

### 5.1 服务端插件包（新增 `/api/plugin-packages`）
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/plugin-packages | 列表 `[{ fileName, pluginId?, name?, version?, description?, sizeBytes, modifiedAt, url?, valid, invalidReason? }]`；按修改时间倒序；invalid 条目仍列出（可删除）；元数据字段解析失败时缺失（前端显示「—」） |
| POST | /api/plugin-packages | multipart 上传（`file`）；zip + 根 manifest.json 校验 + 必填字段 + 文件名路径穿越防护（复用 client-packages 规范化）；**大小上限 64MB**（超出 400 + 中文原因）；成功 200（返回体前端不依赖）、失败 400 `ErrorView` |
| GET | /api/plugin-packages/{fileName} | 下载（`application/octet-stream` + Content-Disposition）；404 = 不存在 / 文件名非法 |
| DELETE | /api/plugin-packages/{fileName} | 删除；404 = 不存在 / 文件名非法 |

- 目录：Windows `%ProgramData%\LabelFrame\server\plugin-packages`；Linux `/var/lib/labelframe/server/plugin-packages`；`LABELFRAME_SERVER_PLUGIN_PACKAGES` 可覆盖（空 = 默认）。
- manifest 解析：只读 zip 内 `manifest.json`（不解压落地）；`pluginId` / `name` / `version` 缺失或非字符串 → `valid:false`。`pluginId` 仅做标识展示，服务端不做内置 id 判断（客户端安装时判断）。

### 5.2 客户端插件安装 / 卸载（新增 `/api/plugins`，WinHost）
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/plugins/installed | 已安装插件列表（扫描插件目录：子目录包 + 平铺 DLL 合并注册表状态）：`[{ pluginId, name, version, description, loaded, loadError?, packageDir?, source: "package"\|"manual", installedAt? }]`；`loaded` = 注册表已装配（与 `/api/transport/plugins` 交集）；`source:"package"` 才可卸载；`loaded=false` 且 `loadError` 非空 = 加载失败，`loaded=false` 无错误 = 待重启生效 |
| POST | /api/plugins/install | multipart 上传插件包（`file`）；校验（zip CRC / 根 manifest / 内置 id 拒绝 / 临时 ALC 预检发现插件且 id 匹配）→ 删除旧 `plugins/<pluginId>/`（文件锁失败 → 400「插件正在使用中，请重启客户端后重试」）→ 解压到 `plugins/<pluginId>/`（含 manifest.json）；**大小上限 64MB**；成功 200 `{ ok:true, message, plugin }`（message 含「重启客户端后生效」），失败 400 `ErrorView` + 中文原因 |
| POST | /api/plugins/uninstall | 请求 `{ pluginId }`；仅允许 `source:"package"`（子目录包）→ 删除 `plugins/<pluginId>/`（文件锁失败 → 400 提示重启后重试）；成功 200 `{ ok:true, message }`（message 含「重启客户端后生效」），失败 400 `ErrorView` + 中文原因 |

- HTTP 语义统一（hermes 附三 1.4）：安装 / 卸载失败一律 **400 + `ErrorView`**（与 uninstall / client-packages 上传一致），成功 200 + `{ ok:true, ... }`；前端 makeRequest 非 2xx 抛 `ApiError`、2xx 读 `ok`，两条路径都可处理，但契约定死为 400 ErrorView。
- 与现有 `/api/transport/plugins`（已装配插件列表，排障用）并存不冲突：一个看「加载态」，一个看「安装态 + 加载态」。
- 安装 / 卸载**不触发宿主重启**（重启动作由用户执行）；期间不触碰当前连接配置（若卸载的是当前连接引用的插件，重启后由迭代 22 附五回退逻辑兜底）。
- 安全：`pluginId` 作为目录名需做安全校验（拒绝路径分隔符 / `..` / 非法字符，防解压路径穿越）；zip 解压条目拒绝 `..` / 绝对路径 / 盘符（zip-slip 防护）。

### 5.3 配置项
- WinHost：`LABELFRAME_PLUGINS`（插件目录，不变；安装时目录不存在自动创建）。
- Server：`LABELFRAME_SERVER_PLUGIN_PACKAGES`（插件包目录，默认数据目录下 `plugin-packages`）。
- `packaging/ubuntu/docker-compose.yml`：新增 `./plugin-packages:/var/lib/labelframe/server/plugin-packages` 卷挂载（注释说明可选，与 client-packages 一致）。

## 6. 后端实施拆分（主 Agent）

1. **Core**：
   - `Transport/Plugins/Package/`——`PluginPackageManifest`（pluginId/name/version/description/author?/minHostVersion?）+ JSON 解析、`PluginPackageReader`（zip 读取：校验 zip 完整性 / 根 manifest / 必填字段 / 列出包内 DLL；**zip-slip 防护**：拒绝 `..` / 绝对路径 / 盘符条目；不落地解压）。
   - `PluginDirectoryLoader` 演进——扫描平铺 `*.dll` + 子目录 `*/ *.dll`（ALC 命名含子目录避免冲突；`ResolvePluginDependency` 对子目录伴生 DLL 天然兼容）；**外部插件注册内置插件 id 时跳过并记日志**（决策 6A，在登记边界统一处理）。
   - **共享文件名规范化**：把 `ClientPackagesService.NormalizeFileName` 提取为 Core 共享 `SafeFileName`（两服务复用，避免规则漂移）。
   - `PluginProbe`（复用加载发现逻辑）：临时 collectible ALC + 临时目录加载包内 DLL 并返回发现的插件 id（安装预检与目录加载共用同一发现逻辑）。
2. **WinHost**：`PluginInstaller` 服务（安装：三层校验 → 临时目录解压 → 预检 → 覆盖替换 `plugins/<pluginId>/`；卸载：删目录；列表：扫描 + manifest 读取 + 与注册表合并 loaded 状态）；`HostOptions.PluginsPath` 目录懒创建；API 三个端点（5.2）；Kestrel `MaxRequestBodySize` 调至 64MB；消息文案含「重启生效」。
3. **Server**：`PluginPackagesService`（目录管理 + manifest 解析 + 校验 + 列表/上传/下载/删除，路径穿越防护复用 Core `SafeFileName`）；`ServerOptions` 增 `PluginPackagesPath` + `LABELFRAME_SERVER_PLUGIN_PACKAGES`；Kestrel `MaxRequestBodySize` 调至 64MB；docker-compose 挂载。
4. **测试**：Core（manifest 解析 / zip 校验 / zip-slip / 目录加载器子目录扫描 / 内置 id 防覆盖——用 `LabelFrame.TransportPlugin.Sample` 打包装做集成验证）；WinHost（合法包安装 / 非法包拒绝 / 内置 id 拒绝 / id 不匹配拒绝 / 覆盖安装 / 卸载 / 列表 loaded 状态 / 平铺只读——用临时插件目录 + Sample 包 fixture）；Server（plugin-packages 上传下载删除 / 路径穿越 / 非法 zip 拒绝 / 大小上限 / 列表元数据与 valid 状态）。

## 7. 前端实施拆分（hermes）

1. `lib/api/types.ts`：新增 `PluginPackageInfo`（**`pluginId?/name?/version?/description?` 可空**、`url?` 可选，invalid 条目缺失字段显示「—」；含 `valid`/`invalidReason?`）、`InstalledPluginInfo`（含 `source`/`loaded`/`loadError?`/`packageDir?`/`installedAt?`）、`PluginInstallResult` / `PluginUninstallResult`。
2. `lib/api/client.ts`：
   - `serverApi.listPluginPackages / uploadPluginPackage(file) / deletePluginPackage(fileName)`（与 ClientPackages 同构，走 serverRequest）。
   - `serverApi.downloadPluginPackage(fileName): Promise<{ blob, filename }>`（复用内部 makeFetchBlob，fallbackName=fileName，统一错误语义；hermes 附三 1.1）。
   - `localApi.listInstalledPlugins / installPlugin(file: File)`（multipart；**保留原始文件名**——内部 `form.append('file', blob, fileName)`，fileName 取服务端列表项；hermes 附三 2.2）/ `uninstallPlugin(pluginId)`。
   - `pluginPackageDownloadUrl(fileName)`（与 clientPackageDownloadUrl 同模式；server 构建同源相对路径）。
   - 安装 / 上传前按 `sizeBytes` 预检 64MB 上限并给中文提示（hermes 附三 2.1）。
3. Server UI：新增 `pages/PluginPackages.tsx`（插件管理页：列表（名称 / 版本 / pluginId / 大小 / 时间 / valid 状态，invalid 红标 + 原因）+ 上传 + 下载 + 删除）；`App.tsx` 菜单与路由（`TabId` 增 `'plugin-packages'`，与「客户端下载」并列）；菜单图标新增插件风格图标或复用 file/grid（hermes 附三 4.1）。
4. Client UI：`Settings.tsx` 新增「插件管理」卡片（与「更新与安装包」并列，置于其下）：
   - **服务端可用插件区**（`serverApi.listPluginPackages`，仅 valid 可安装）：状态四态——加载中 / 空 / 错误（含「服务端可达但旧服务端无该端点 404」与「单机模式不可达」区分展示）/ 单机模式（提示需先连接服务端，**该区才隐藏/提示**）；行内「安装」per-row loading → 成功后自动刷新已安装列表（新行显示「待重启生效」）；**覆盖安装确认**（已安装同 pluginId 时 `window.confirm`「将覆盖 x → y」）；invalid 条目安装按钮禁用 + 红标原因。
   - **已安装插件区**（`localApi.listInstalledPlugins`，**始终渲染**——单机模式下也可查看 / 卸载，不依赖服务端；hermes 附三 3.2）：行显示 pluginId / name / version / 状态徽标（已加载 ok / 待重启生效 warn / 加载失败 err + 原因 / 手动放置 默认 badge）；`source:"package"` 行提供「卸载」（`window.confirm` + per-row deleting + 成功后刷新）；`source:"manual"` 只读无卸载按钮；「刷新」按钮（loaded 状态重启后变化，需手动刷新确认）。
   - 卡片底部常驻 hint「安装 / 卸载后需重启客户端生效」；旧 WinHost（0.18 无 `/api/plugins`）接口 404 时已安装区显示「当前客户端版本不支持插件管理」防御提示（hermes 附三观察）。
   - 与 TransportPanel（连接方式）无直接交互。

## 8. 验收

- `dotnet build LabelFrame.slnx` 通过；`dotnet test` 全绿（含新增用例）。
- web `pnpm test`（vitest）全绿；`pnpm build` / `VITE_UI_MODE=server pnpm build:server` 通过。
- 功能冒烟（联调）：
  - 服务端 UI「插件管理」：上传合法 `.lfplugin`（用 Sample 插件打包装）→ 列表显示名称 / 版本 / pluginId / valid；上传非法 zip / 缺 manifest / 缺必填字段 / 超 64MB → 400 + 中文原因；下载 / 删除；路径穿越（`../`）被拒；invalid 条目红标 + 原因且可删除。
  - 客户端「插件管理」：服务端可用插件列表可见（仅 valid 可安装）→ 安装（含覆盖安装确认）→ 提示重启生效 → 重启 WinHost → 插件出现在「连接方式」可用插件列表并可配置启用；「已安装插件」显示已加载；手动刷新。
  - 卸载：已安装插件卸载（确认）→ 提示重启生效 → 重启后插件消失、连接方式列表不再包含；若卸载的是当前连接引用的插件，重启后回退默认连接（log）+ 日志警告（复用迭代 22 附五）；平铺手动 DLL 显示「手动放置」且无卸载按钮。
  - 边界：内置 id 的包（pluginId=log）拒绝安装；manifest.pluginId 与 DLL 实际 id 不一致拒绝安装；覆盖安装（同 pluginId 不同 version）成功且重启后为新包；坏 DLL 不影响宿主启动；已加载插件覆盖 / 卸载被文件锁挡住时提示「重启客户端后重试」。
  - 单机模式（服务端不可达）：可用插件区提示需先连接服务端；已安装区仍可查看 / 卸载。
  - Ubuntu / Docker：`docker compose` 挂载 `plugin-packages` 后上传 / 列表正常。
- 前端测试（hermes 附三 §5）：`Settings.test.tsx` 必改（mock 工厂补 `listPluginPackages` / `listInstalledPlugins` / `pluginPackageDownloadUrl`）+ 新增「插件管理」describe；新增 `PluginPackages.test.tsx`（仿 ClientPackages.test.tsx）；`App.server.test.tsx` 扩展 mock + 「插件管理」菜单断言；`client.test.ts` 补 `pluginPackageDownloadUrl` 双构建用例。
- 文档：ROADMAP（迭代 23 状态）、CHANGELOG、DESIGN（决策 #72+ 与未决）更新。

## 9. 风险与未决

- 插件包代码签名 / 服务端鉴权：局域网无鉴权模型（沿用迭代 22 风险记录）；插件 DLL 与宿主同权限运行——安装通道只接受「服务端列表里 valid 的包」+ 安装预检，文档注明「只安装可信来源插件包」；正式对外分发需评估签名（未决）。
- 运行时热卸载 / 热替换：仍不做（安装 / 卸载 = 写文件 + 重启生效，沿用决策 2A；DESIGN 未决）。
- **覆盖安装 / 卸载的 Windows 文件锁**：WinHost 运行中已加载插件的 DLL 可能被进程锁定（`AssemblyLoadContext.LoadFromAssemblyPath` 的文件共享行为待实施时用 .NET 10 实测确认）——若删除旧目录 / 覆盖失败，返回「插件正在使用中，请重启客户端后重试」，不静默失败；不因此引入运行时热卸载。
- **请求体大小与 413**：Kestrel 默认 `MaxRequestBodySize`（约 30MB）超出返回 413 且无 ErrorView——本轮为 `POST /api/plugin-packages` 与 `POST /api/plugins/install` 显式调至 64MB；**client-packages 上传（迭代 22）存在同一隐患**，一并记入风险，本轮不扩大范围处理（hermes 附三 2.1 观察）。
- 插件包依赖复杂性：包内伴生托管 DLL 可被加载器解析（迭代 22 已支持），但 native 依赖（非托管 DLL / COM）与宿主版本冲突未覆盖，真实厂商插件接入（迭代 24）时验证（风险记录）。
- 安装预检用临时 collectible ALC 加载包内 DLL：与插件目录加载器同一 ALC 机制；预检仅做发现（不 Create 传输实例），静态初始化副作用风险可控（风险记录）。
- 旧版本错位：旧 WinHost（0.18 无 `/api/plugins`）上已安装区 404 → 前端防御提示「当前客户端版本不支持插件管理」（成本低，已入 §7.4）；Server 旧版本无 `/api/plugin-packages` → 可用插件区 404 与「单机模式不可达」区分展示（已入 §7.4）。

---

## 附三：前端评审意见（代评参考，2026-08-17；前端重做由 hermes 独立评审）

> 状态（2026-08-17 修订）：本表为**主 Agent 代评（子代理）**产物——7 项意见已并入正文（UI / 契约细节仍有效），但因前端由用户指定 hermes 独立实施，代跑前端提交已回滚（git revert 9faae3a），**前端评审与实施以 docs/ITERATION-23-FRONTEND-TASK.md 为准**；hermes 的评估意见（任务书 §4）将回填本附，替换代评版。

| # | 意见 | 处理 |
|---|---|---|
| 1.1 | §7.2 安装流程需下载函数（避免 Settings 裸 fetch 绕过 makeFetchBlob 统一错误处理） | 已并入 §2.2/§7.2：新增 `serverApi.downloadPluginPackage` |
| 1.2 | PluginPackageInfo 元数据字段可空、url 可选（invalid 条目） | 已并入 §2.1/§5.1/§7.1 |
| 1.3 | §2.3 与 §5.2 的 GET /api/plugins/installed 字段不一致 | 已统一以 §5.2 为准（source 必填、packageDir?、installedAt?），同步 §2.3 |
| 1.4 | POST /api/plugins/install 失败 HTTP 语义未定义 | 已定死：失败 400 `ErrorView`、成功 200 `{ ok:true, message, plugin }`（§5.2） |
| 1.5 | POST /api/plugin-packages 返回体前端不依赖 | 已注明返回体后端自定、前端上传后重拉列表（§2.1/§5.1） |
| 2.1 | blob 驻留内存 + Kestrel 默认 30MB 413 无错误体 | 已并入：大小上限 64MB（Server/WinHost 显式配置）+ 前端预检（§2.2/§5/§9）；client-packages 同隐患记 §9 |
| 2.2 | installPlugin 需保留原始文件名（multipart 部件元数据） | 已并入 §2.2/§7.2：`form.append('file', blob, fileName)` |
| 2.3 | blob 中转方案确认可行、维持；不引入 WinHost 直连 | 已并入 §2.2 方案说明 |
| 3.1 | 卡片状态矩阵（加载中/空/错误/单机模式）+ 刷新按钮 | 已并入 §7.4 |
| 3.2 | 单机模式仅可用插件区提示、已安装区始终渲染 | 已并入 §7.4 |
| 3.3 | 交互补齐：per-row loading / 覆盖确认 / confirm 卸载 / 常驻 hint / invalid 禁用 | 已并入 §7.4 |
| 4.1 | 菜单图标（IconName 无插件风格图标） | 已并入 §7.3：新增图标或复用 file/grid |
| 4.2 | App.server.test.tsx mock 需扩展（新增菜单用例） | 已并入 §8 前端测试 |
| 5.1/5.2 | Settings.test.tsx 必改 + 新增用例清单 | 已并入 §8 前端测试 |
| 观察 | minHostVersion 未被消费 / 旧 WinHost 防御提示 / client-packages 413 | 已并入 §2.1、§7.4、§9 |

## 附四：后端自审意见（主 Agent，2026-08-17）

> 评审对象：规格初稿 §5/§6 后端契约与拆分，对照迭代 22 实现（PluginDirectoryLoader / TransportPluginRegistry / TransportManager / ClientPackagesService / ServerOptions / docker-compose）做兼容演进核对。

| # | 意见 | 处理 |
|---|---|---|
| 1 | `PluginDirectoryLoader` 现只扫平铺 `*.dll`（`Directory.GetFiles(directory, "*.dll")`）；子目录扫描需注意 ALC 命名含子目录避免重名冲突；`ResolvePluginDependency` 用 `Path.GetDirectoryName(dll)` 解析伴生 DLL，对子目录天然兼容 | 已并入 §6.1 |
| 2 | 注册表防覆盖（决策 6A）应在登记边界统一处理（Program.cs 登记循环或注册表方法），用宿主日志记录跳过 | 已并入 §6.1（登记边界统一处理） |
| 3 | `ClientPackagesService.NormalizeFileName` 为私有 static，两服务复用需提取共享（避免规则漂移） | 已并入 §6.1：Core `SafeFileName` |
| 4 | `plugins/<pluginId>/` 的 pluginId 来自 manifest，需目录名安全校验（路径分隔符 / `..` / 非法字符）防解压路径穿越 | 已并入 §5.2 安全节 |
| 5 | zip 解压需 zip-slip 防护（条目 `..` / 绝对路径 / 盘符） | 已并入 §5.2 / §6.1 |
| 6 | 安装预检与目录加载共用同一发现逻辑（提取 `PluginProbe`），预检用临时目录 + collectible ALC、加载后释放 | 已并入 §6.1 |
| 7 | 覆盖安装 / 卸载在运行中可能被 Windows 文件锁挡住（已加载 DLL）；处理 = 失败提示「重启客户端后重试」，实施时实测 .NET 10 `LoadFromAssemblyPath` 共享删除行为 | 已并入 §2.2 / §9 |
| 8 | Server `List()` 每次解析各 zip 的 manifest（小目录可接受，v1 不做缓存） | 已在 §5.1 隐含（解析失败 → valid:false）；实施时按此实现 |
| 9 | 卸载当前连接引用插件 → 重启回退默认连接：迭代 22 附五 `TransportManager.LoadPersisted` 已实现，直接复用 | 已并入 §2.3 / §5.2（兜底确认） |
| 10 | 依赖核对：Core 已有 `System.IO.Compression` zip 能力（模板包复用，不新增依赖）；Kestrel `MaxRequestBodySize` 需在 Server 与 WinHost 两处配置 | 已并入 §6.2/§6.3 / §9 |
| 11 | docker-compose 现有 client-packages 挂载模式可照搬扩展 plugin-packages | 已并入 §5.3 / §6.3 |

---

## 附五：实施与联调验收记录（主 Agent + hermes，2026-08-17）

> 供审核者参考；不视为规格正文。阶段二按定稿实施完成，端到端联调冒烟通过。

- **后端（主 Agent）**：Core（`PluginPackageManifest` / `PluginPackageReader` / `PluginPackageLimits` / `SafeFileName` / `PluginProbe`；`PluginDirectoryLoader` 平铺 + 子目录扫描 + 字节加载；`RegisterExternal` 防内置覆盖）；WinHost（`PluginInstaller` + `/api/plugins/installed|install|uninstall` + Kestrel 64MB）；Server（`PluginPackagesService` + `/api/plugin-packages` 4 端点 + `ServerOptions.PluginPackagesPath` + docker-compose 挂载）。
- **前端（待 hermes 实施）**：代跑提交 9faae3a 已回滚；前端任务书 docs/ITERATION-23-FRONTEND-TASK.md（契约 §2、清单 §5、待评估设计点 §4）。
- **联调冒烟（16 步全过，API 层）**：上传 .lfplugin → 服务端列表元数据 → 客户端安装（重启前 loaded=false）→ 重启后装配 / loaded=true → 配置启用（SAMPLE(SMOKE)）→ 卸载 → 重启后消失 → 卸载当前连接插件后重启回退 log → Server 删除插件包。前端 UI 联调待 hermes 实施后执行。
- **关键发现（规格 §9 风险实证并修复）**：Windows 下 `LoadFromAssemblyPath` 会锁已加载插件 DLL，导致「卸载 = 删除文件 + 重启生效」对已加载插件永远失败（重启又加载）；改为字节加载（`LoadFromStream`）后不锁文件——卸载 / 覆盖安装立即成功，运行中进程用内存镜像、重启按新文件装配（决策 #73）。副作用：插件 `Assembly.Location` 为空，自定位资源改用 `ITransportPluginContext.DataDirectory`（文档已注明）。
- **测试基线**：dotnet 259 全绿（Core 104 / Server 45 / WinHost 85 / Studio 25）；web 207 全绿（22 文件）。