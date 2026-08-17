# 迭代 23 前端任务书（hermes）— 客户端插件分发

> 状态：2026-08-17 主 Agent 产出，交由 hermes 独立评估与实施。
> 协作说明：本轮「后端 = 主 Agent、前端 = hermes」。后端已按定稿规格（docs/ITERATION-23-SPEC.md，决策 1A-7A 用户拍板）完成并提交，**后端契约已就绪、可直接联调**（dotnet 259 全绿）。前端此前由主 Agent 代跑的子代理做过一版，已回滚（git revert 9faae3a），本任务书以规格为准重新实施。
> 规格附三的「前端评审意见」是主 Agent 代评产物，**仅供你参考、不构成约束**——请按本任务书独立评估设计（§4），如有异议直接提出。

## 0. 你的任务

1. **先评估 §4 的设计点**，给出结论（可行 / 问题 / 替代建议）——你的评估意见就是本轮前端评审记录，主 Agent 会并入规格附三（替换代评版）；
2. 按 §3 契约 + §5 清单实施前端；
3. 跑通 pnpm test / build，按 §7 提交并回报。

## 1. 背景

- 迭代 22 已交付：传输插件化（可用插件列表 + 参数表单动态渲染、「连接方式」切插件先测试后生效）、客户端下载分发（client-packages + Server UI「客户端下载」页 + 客户端设置「更新与安装包」卡片）。
- 迭代 23 目标：把迭代 22 的传输插件机制做成**可分发闭环**——插件包上传服务端（独立 plugin-packages 目录 + API + Server UI 管理）、客户端设置页浏览服务端可用插件 → 安装 / 卸载（重启生效）。

## 2. 后端契约（已就绪，直接对接；失败统一 400 `ErrorView { code, message, fieldKey? }`）

### 2.1 服务端插件包 `/api/plugin-packages`（Server）
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/plugin-packages | 列表 `[{ fileName, pluginId?, name?, version?, description?, sizeBytes, modifiedAt, url?, valid, invalidReason? }]`；invalid 条目元数据字段缺失（前端显示「—」）、仍可删除 |
| POST | /api/plugin-packages | multipart 上传（字段名 `file`）；zip + 根 manifest 校验、64MB 上限；成功 200（返回体前端不依赖，上传后重拉列表）；失败 400 |
| GET | /api/plugin-packages/{fileName} | 下载（octet-stream + Content-Disposition）；404 = 不存在 |
| DELETE | /api/plugin-packages/{fileName} | 删除；404 = 不存在 |

### 2.2 客户端插件 `/api/plugins`（WinHost）
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/plugins/installed | 已安装插件列表 `[{ pluginId, name, version, description, loaded, loadError?, packageDir?, source: "package"\|"manual", installedAt? }]`；`loaded=false` 且 `loadError` 非空 = 加载失败；`loaded=false` 无错误 = 待重启生效 / 未装配；`source:"package"` 才可卸载 |
| POST | /api/plugins/install | multipart 上传插件包（字段名 `file`）；三层校验（zip + manifest 必填 / 内置插件 id 拒绝 / 临时 ALC 预检核对插件 id）；成功 200 `{ ok:true, message, plugin }`；失败 400（LF_PLUGIN_INVALID / LF_PLUGIN_BUSY「正在使用中，请重启客户端后重试」/ LF_PLUGIN_INSTALL_FAILED） |
| POST | /api/plugins/uninstall | 请求 `{ pluginId }`；成功 200 `{ ok:true, message }`；失败 400（LF_PLUGIN_INVALID） |

- **CORS**：Server / WinHost 均为 AllowAnyOrigin/Header/Method（无 credentials），跨域 GET 下载与 POST multipart 可用。
- **64MB**：前端按列表 `sizeBytes` 预检并给中文提示；后端 Kestrel 已配置同值（超限返回 400 而非 413）。

## 3. 插件包格式（前端仅展示，不解析）

`.lfplugin`（zip）：根 `manifest.json`（pluginId / name / version 必填 + 可选 description / author / minHostVersion）+ 插件 DLL。前端只消费服务端列表解析出的元数据字段，不需要自己解 zip。

## 4. 请前端评估的设计点（有异议请直接提出，主 Agent 按你的意见修订）

1. **安装中转**：客户端安装 = 前端从服务端下载 blob → POST 本机 WinHost（`/api/plugins/install`）。理由：WinHost 的 `ServerUrl`（路由用）与 UI 配置的服务端地址（机器级 settings.json）是两套，WinHost 直连会造成地址不一致。替代方案：WinHost 直连服务端下载（POST `{ serverUrl, fileName }`）。
2. **UI 位置**：客户端用设置页「插件管理」卡片、与「更新与安装包」**并列**（决策 7A）；Server UI 用独立「插件管理」页与「客户端下载」并列。替代：客户端独立「插件」页。
3. **已安装状态语义**：`loaded=false` 且无 `loadError` = 「待重启生效 / 未装配」——无法区分「坏 DLL 加载失败」与「未重启」（坏 DLL 的加载失败只记在 host.log，未结构化暴露给 API）。是否需要在后端补结构化 loadError？
4. **manual 只读**：平铺手动 DLL（`source:"manual"`）不支持界面卸载（无安装包归属记录，删除动作由管理员手动执行）。
5. **旧版本防御**：旧 WinHost（无 `/api/plugins`）/ 旧 Server（无 `/api/plugin-packages`）按 HTTP 404 判定并显示版本提示（「当前客户端版本不支持插件管理」/「服务端不支持插件管理（旧版本）」）。
6. **覆盖安装**：同 pluginId 覆盖不比较版本（决策 4A），UI 仅 confirm 提示「将覆盖 x → y」。
7. **64MB 预检**：前端按列表 `sizeBytes` 预检，与后端一致。

## 5. 前端实施清单（web/src，参照 docs/ITERATION-23-SPEC.md §7）

1. `lib/api/types.ts`：新增 `PluginPackageInfo` / `InstalledPluginInfo` / `PluginInstallResult` / `PluginUninstallResult`（字段按 §2.1/§2.2；元数据字段可空、`url?` 可选）。
2. `lib/api/client.ts`：
   - `serverApi.listPluginPackages / uploadPluginPackage(file) / deletePluginPackage(fileName)`；
   - `serverApi.downloadPluginPackage(fileName): Promise<{ blob, filename }>`（复用内部 makeFetchBlob，统一错误语义）；
   - `localApi.listInstalledPlugins / installPlugin(file: File)`（multipart；**保留原始文件名**——内部 `new File([blob], fileName)`，fileName 取服务端列表项）/ `uninstallPlugin(pluginId)`（POST JSON `{ pluginId }`）；
   - `pluginPackageDownloadUrl(fileName)`（与 `clientPackageDownloadUrl` 同模式）。
3. `state/types.ts`：`TabId` 增 `'plugin-packages'`（注释补迭代 23）。
4. `pages/PluginPackages.tsx`（Server UI 新增「插件管理」页，仿 `ClientPackages.tsx`）：列表（名称 / 版本 / pluginId / 大小 / 时间 / valid 状态，invalid 红标 + 原因）+ 上传（64MB 预检）+ 下载 + 删除（confirm）+ 空态 / 加载态 / 错误态。
5. `App.tsx`：server TABS 增「插件管理」（与「客户端下载」并列；图标可新增 puzzle 拼图图标或复用现有）。
6. `pages/Settings.tsx` 新增「插件管理」卡片（置于「更新与安装包」之下）：
   - **服务端可用插件区**（`serverApi.listPluginPackages`，仅 valid 可安装）：状态四态——加载中 / 空 / 错误（「服务端可达但旧 Server 无端点 404」与「单机模式不可达」区分展示）/ 单机模式（提示需先连接服务端，**该区才隐藏/提示**）；行内「安装」per-row loading → `downloadPluginPackage` → `installPlugin` → 成功提示「重启客户端后生效」+ 刷新已安装列表；覆盖安装确认（已装同 pluginId 时 `window.confirm`「将覆盖 x → y」）；invalid 条目安装按钮禁用 + 红标原因；64MB 预检提示。
   - **已安装插件区**（`localApi.listInstalledPlugins`，**始终渲染**——单机模式下也可查看 / 卸载，不依赖服务端）：行显示 pluginId / name / version + 状态徽标（已加载 ok / 待重启生效 warn / 加载失败 err + 原因 / 手动放置 默认 badge）；`source:"package"` 行「卸载」（`window.confirm` + per-row deleting + 成功后刷新）；`source:"manual"` 只读无卸载按钮；「刷新」按钮；旧 WinHost 404 → 显示「当前客户端版本不支持插件管理」。
   - 卡片底部常驻 hint「安装 / 卸载后需重启客户端生效」。
7. 测试（参照规格 §8）：
   - `Settings.test.tsx` **必改**：vi.mock 工厂补 `listPluginPackages` / `downloadPluginPackage` / `listInstalledPlugins` / `installPlugin` / `uninstallPlugin` / `pluginPackageDownloadUrl` / 64MB 预检函数，beforeEach mockResolvedValue([])；新增「插件管理」describe（可用列表渲染、invalid 禁用、安装流程断言文件名、覆盖确认、单机模式语义、四态徽标、手动放置无卸载、卸载 confirm、加载失败错误态）。
   - 新增 `PluginPackages.test.tsx`（仿 ClientPackages.test.tsx）。
   - `App.server.test.tsx`：mock 扩展 + 「插件管理」菜单 / 路由断言。
   - `client.test.ts`：`pluginPackageDownloadUrl` 双构建（client 绝对 URL / server 同源相对路径）用例。

## 6. 不在前端范围

- 后端（src/、test/ 除 web 外）不做任何修改；如发现契约与实现不一致，回报主 Agent 由后端修订。
- 不做插件包 zip 解析 / 校验（后端负责）；不做签名 / 鉴权。

## 7. 验收与提交

- `pnpm test` 全绿（新增用例计入）；`pnpm build` 与 `VITE_UI_MODE=server pnpm build:server` 通过。
- 提交用 Conventional Commits（`feat(web):` / `test(web):`，中文说明为主）。
- 回报：改动文件清单、测试数、评估结论（§4 每条）、假设清单。

## 8. 参考

- 规格：docs/ITERATION-23-SPEC.md（§5 后端契约 / §7 前端拆分 / §8 验收 / §9 风险）
- 现有前端：web/src/pages/Settings.tsx（「更新与安装包」卡片）、web/src/pages/ClientPackages.tsx（Server UI 客户端下载页）、web/src/lib/api/client.ts + types.ts、web/src/App.tsx、web/src/lib/uiMode.ts