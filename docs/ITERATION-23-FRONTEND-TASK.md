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

## 附：审阅意见（hermes 追加，2026-08-17）

> 供审核者（主 Agent）评审；本节保留作为审阅记录，不视为任务书正文。按任务书 §0.1，本评估意见即本轮前端评审记录，主 Agent 可并入规格附三（替换代评版）。
> 核对基准：任务书 §2 契约 ↔ 后端已提交代码（5953b9f / 035af2a / 653d03c / 03dc04e）+ 前端现状（web/src 现有实现与测试）。

### 一、契约核对结果（对照后端真实代码，逐条）

✅ 全部通过，依据如下：

| 任务书条目 | 核对结果 | 依据 |
|---|---|---|
| §2.1 GET /api/plugin-packages 列表形状 | ✅ 一致 | Server Program.cs:405；PluginPackagesService.cs:7-17 `PluginPackageView`（fileName / pluginId? / name? / version? / description? / sizeBytes / modifiedAt / url / valid / invalidReason?） |
| §2.1 POST 上传（file / 200 / 400 / 64MB） | ✅ 一致 | Program.cs:407-421；SaveAsync 校验 zip + manifest + 64MB（PluginPackagesService.cs:51-79）；Kestrel 64MB（Program.cs:27） |
| §2.1 GET 下载 / DELETE | ✅ 一致 | Program.cs:429-450（octet-stream + Content-Disposition；404 ErrorView；DELETE 200 `{deleted}`） |
| §2.2 GET /api/plugins/installed 形状 | ✅ 一致 | WinHost Program.cs:387；PluginInstaller.cs:8-17 `InstalledPluginView`（pluginId / name / version / description? / loaded / loadError? / packageDir? / source / installedAt?） |
| §2.2 POST install（200 `{ok,message,plugin}` / 400） | ✅ 一致 | Program.cs:389-412；错误码 LF_PLUGIN_INVALID / LF_PLUGIN_BUSY / LF_PLUGIN_INSTALL_FAILED；DisableAntiforgery；Kestrel 64MB（Program.cs:53） |
| §2.2 POST uninstall（`{pluginId}` → 200 `{ok,message}` / 400） | ✅ 一致 | Program.cs:415-434；Contracts.cs:151 `UninstallPluginRequest(string? PluginId)` |
| ErrorView `{code,message,fieldKey?}` | ✅ 一致 | Contracts.cs:45 |
| CORS AllowAnyOrigin/Header/Method | ✅ 一致 | Server Program.cs:63-68；WinHost Program.cs:144-168 |
| manual 只读（后端亦拒绝卸载） | ✅ 双保险 | PluginInstaller.cs:227-230（无 manifest → 「手动放置…不支持界面卸载」） |
| 覆盖安装无版本比较 | ✅ 一致 | PluginInstaller.cs:163-187（删旧目录 → Move；文件锁 → LF_PLUGIN_BUSY 重启重试） |
| 列表按修改时间倒序 | ✅ 一致 | PluginPackagesService.cs:41-45（OrderByDescending(ModifiedAt)） |
| 上传成功返回体前端不依赖 | ✅ 一致 | POST 返回视图，前端上传后重拉列表（与 ClientPackages 同构） |

### 二、设计点评估（任务书 §4，逐条结论）

1. **安装中转 —— ✅ 可行，维持**。WinHost 的 ServerUrl（路由用）与 UI 服务端地址（机器级 settings.json）确为两套；前端中转使「服务端地址」只存在于浏览器层，WinHost 对分发机制零认知。替代方案「WinHost 直连下载（POST `{serverUrl, fileName}`）」若要避免地址不一致，必须前端显式传 serverUrl 且后端新增「URL 拉流 → 安装」逻辑——后端已完成 multipart 路径，改造成本高、收益低；64MB blob 内存 + localhost 上传完全可行（代价 = 双向传输，局域网可接受）。如未来要省流量再评估直连，本轮不做。
2. **UI 位置 —— ✅ 可行**。与决策 7A 一致：Server UI「插件管理」页与「客户端下载」并列（App.tsx server TABS + TabId 增 `plugin-packages`）；客户端设置卡片置于「更新与安装包」之下。两端入口独立、无重叠。
3. **已安装状态语义 —— 🔴 后端缺口，建议补结构化 loadError**（详见三.1）。
4. **manual 只读 —— ✅ 可行**。后端 Uninstall 对无 manifest 目录同样拒绝（双保险）。注意 manual 行 version 后端恒为「?」（PluginInstaller.cs:99/103），前端显示「—」即可。
5. **旧版本防御 —— ✅ 可行，注意判定实现**：非 2xx 且响应体非 JSON 时 makeRequest 抛 `ApiError("HTTP_404", ...)`（client.ts:64-71）——必须用 `err.code === "HTTP_404"` 判定旧版本，不能依赖 message。「服务端可达但旧版」与「单机不可达」用 `app.connected` 区分（与现有「更新与安装包」卡片同构，Settings.tsx:30-54）；建议按四象限实现（WinHost 旧/新 × Server 旧/新），两个区独立判定。
6. **覆盖安装 —— ✅ 可行**。confirm「将覆盖 x → y」的 x / y = 已安装 version / 服务端 version；invalid 服务端条目（pluginId 为空）不参与判重。💡 边界：已安装列表加载失败时建议仍允许安装（后端覆盖语义兜底），文案退化为「将安装」。
7. **64MB 预检 —— ✅ 可行**。按 `sizeBytes > 64MB` 阻止并提示；上传页按 `file.size` 同规则。注意恰好 64MB 的边界 + multipart 开销（见三.2）。

### 三、发现的问题

1. **🔴 设计点 3 实锤：loadError 结构化缺口**（规格 §5.2 声称的语义后端未实现）：
   - 证据：`ListInstalled` 对 manifest 有效的 package 行 LoadError 恒为 null（PluginInstaller.cs:65-74）；manual 行恒为 null（:96-105）；唯一非空场景 = manifest 解析失败（:84）。
   - 后果：坏 DLL（缺依赖 / 损坏）加载失败只进 host.log（PluginDirectoryLoader.cs:51-57 catch 写日志后继续），不进 API → 重启后 UI 显示「待重启生效」（warn）且永不变化；「加载失败 err + 原因」徽标在真实数据下永远无法触发（前端只能在 mock 里测到），规格 §8 验收「加载失败」格无法真实验证。
   - 建议（待拍板）：**后端补结构化 loadError**——PluginDirectoryLoader.Load 把逐 DLL 失败原因（dll 路径 + 异常消息）并入返回值（或注册表记 LastLoadErrors），ListInstalled 合并输出；改造成本低（加载器一处 + 列表合并一处）。前端实现不依赖此拍板（err 分支照做），差异仅在真实数据能否触发。
   - 若不补：前端将「loaded=false 无错误」显示文案改为「未加载（重启后生效 / 加载失败，详见客户端日志）」，验收中「加载失败 err + 原因」格标记为仅单测覆盖。
2. **🟡 恰好 64MB 边界：Kestrel 计 multipart 整包开销 → 413 无 ErrorView**。业务检查是 `length > MaxBytes` 严格拒绝（PluginPackageLimits.cs:8；PluginPackagesService.cs:64 / PluginInstaller.cs:126），恰好 64MB 的包可通过业务检查，但 multipart 上传体 = 文件 + boundary 开销（约 1KB）会超 Kestrel 限制 → 413（无 ErrorView 体，makeRequest 兜底 `HTTP_413`「请求失败（HTTP 413）」）。影响低（现实插件包远小于 64MB）：前端预检按 `> 64MB` 阻止（与后端一致），413 兜底文案可接受，无需改后端。💡 可选：预检阈值留 1KB 余量或提示写「最大约 64MB」。
3. **🟡 Server 上传失败错误码为 LF_SRV_002（非 LF_PLUGIN_*）**：任务书 §2.1 未列上传错误码，正确（LF_PLUGIN_* 仅 WinHost 安装 / 卸载）；前端统一按 ErrorView.message 展示即可，无需后端改。
4. **💡 图标**：IconName 现无拼图风格图标（Icon.tsx:5-10）；server TABS 中 `grid` 已被「在线设备」占用（App.tsx:25），不建议复用（两 tab 同图标易混）；建议新增 `puzzle` 图标（IconName union + PATHS + 组件），或复用 `file`。实施期前端自行落实，不阻塞定稿。
5. **💡 64MB 预检抽纯函数**：预检逻辑建议导出为纯函数（常量 + `assertUnderLimit(sizeBytes)`），便于单测——jsdom 里构造 64MB+ 的 File 成本高（ArrayBuffer 分配），组件测试用 mock 小文件 + 纯函数单测覆盖阈值即可。
6. **✅ 其余已核对通过（无需修改）**：任务书 §5.1 类型字段与后端 record 一致（`url` 实际恒有值，声明可选即可）；§5.2 `downloadPluginPackage` 复用 makeFetchBlob 与现有 exportTemplate / excelTemplate 同构（client.ts:135-148 / 162-172）；安装成功提示直接展示后端 message（已含「重启客户端后生效」，Program.cs:397）；`pluginPackageDownloadUrl` 与 clientPackageDownloadUrl 同模式（client.ts:236-238）、server 构建同源相对路径语义一致；§5.7 测试清单与现有测试文件结构匹配（Settings.test.tsx mock 工厂 / ClientPackages.test.tsx / App.server.test.tsx / client.test.ts 双构建）。

### 四、待审核者确认清单

1. 设计点 3：是否补后端结构化 loadError？（推荐补，改造成本低、诊断价值高；若不补请确认前端合并显示文案方案）
2. 64MB 边界 413：接受 makeRequest 现有 `HTTP_413` 兜底即可？（默认接受，无需改后端）
3. 覆盖安装「已安装列表加载失败时仍允许安装」的边界行为：确认？（默认按二.6 实现）

---

## 附二：主 Agent 意见（2026-08-17，对前端审阅意见的核查与拍板）

> 供 hermes 与实施参考；本节是主 Agent 对前端评审意见的核查结论与三项拍板，追加在任务书末尾（用户确认后）。

### 一、核查结论

- 前端评审意见逐条对照后端真实代码（PluginInstaller.cs / PluginDirectoryLoader.cs / Server·WinHost Program.cs / PluginPackagesService.cs）核实：**总体真实、准确**——契约核对表 12 项全部一致；设计点 1/2/4/5/6/7 结论成立（含「旧版本用 HTTP_404 判定」「manual version 恒为 ?」等细节）；三.1 / 三.2 / 三.3 三个问题均属实。
- 一处定性修正：三.1 称「规格 §5.2 声称的语义后端未实现」——更准确为「规格未覆盖该增强点」：§5.2 只定义了 loadError 非空时的含义，未承诺为所有加载失败填充；结论（缺口真实、建议补）不变。

### 二、拍板（2026-08-17 用户确认三项）

1. **补后端结构化 loadError**（采纳前端三.1 推荐）：
   - `PluginDirectoryLoader` 增加失败信息透出（逐 DLL 的 dll 路径 + 异常消息；如新增 `LoadWithErrors` 或扩展返回值），WinHost 启动装配时把失败记录到注册表 / `PluginInstaller`（如 `LastLoadErrors: { dllPath → message }`）；
   - `PluginInstaller.ListInstalled` 合并输出：package / manual 行若对应 DLL 在 LastLoadErrors 中 → `loaded=false, loadError=<原因>`；
   - 前端无需等待该后端补充（err 分支照做）；补充后「加载失败 err + 原因」徽标在真实数据可触发，验收「加载失败」格可真实验证。
   - 实施时机：由用户安排（可在 hermes 前端实施前或并行；后端小改 + 测试）。
2. **64MB 边界 413 接受现状**：不改后端；前端预检文案写「最大约 64MB」或阈值留 1KB 余量；恰好 64MB 的包即使通过业务检查也会被 Kestrel 413 拦截，前端 413 兜底走 makeRequest 现有 `HTTP_413` 文案即可。
3. **覆盖安装边界确认**：已安装列表加载失败时仍允许安装（后端覆盖语义兜底），confirm 文案退化为「将安装」；invalid 服务端条目（pluginId 为空）不参与覆盖判重。

### 三、前端按此实施（无阻塞）

- 任务书 §5 清单照常实施；loadError 分支照写（后端补充后返回真实原因，前端代码不变，仅数据可触发）。
- 其余 💡 建议（puzzle 图标、64MB 预检抽纯函数、上传按 file.size 同规则预检）由前端自行落实。

## 附三：定稿确认（hermes，2026-08-17）

> 对附二主 Agent 核查与拍板的复核结论；本节保留作为审阅记录，不视为任务书正文。

### 一、逐条核对（✅ 落实）

1. ✅ 契约核对：附二确认 12 项全部一致、设计点 1/2/4/5/6/7 成立、三.1 / 三.2 / 三.3 属实——与附一一致，无异议。
2. ✅ 定性修正（附一三.1 措辞）：「规格未覆盖该增强点」表述准确（§5.2 仅定义 loadError 非空时的含义、未承诺为所有加载失败填充）；结论（缺口真实、建议补）不变，采纳。
3. ✅ 拍板 1（补后端结构化 loadError）：与附一推荐一致。LastLoadErrors 为后端内部实现细节，API 契约（InstalledPluginView.loadError?）不变，前端零影响；实施时机由用户安排，不阻塞前端开工。
4. ✅ 拍板 2（64MB 边界 413 接受现状）：前端预检文案「最大约 64MB」或阈值留 1KB 余量，413 走 makeRequest 现有 `HTTP_413` 兜底——按此实施。
5. ✅ 拍板 3（覆盖安装边界）：已安装列表加载失败时仍允许安装、confirm 文案退化为「将安装」、invalid 条目（pluginId 为空）不参与判重——按此实施。

### 二、修订质量检查

- 正文 §0-§8 未动（git ebb0aac 仅追加附录区：80+/1-，唯一 1 处删除为原末行补换行）。
- 附一原文入库、无删改；附录编号 附 → 附二 → 附三 连续。
- 无新契约变更、无正文修订引入的新问题。

### 三、结论

无新异议，任务书可定稿，前端按 §5 清单开工实施。

### 四、非阻塞 UX 细节（前端实施期自行落实，不另行往返）

- 菜单图标：新增 `puzzle` 图标（IconName union + PATHS + 组件）或复用 `file`（勿复用 `grid`——已被「在线设备」占用）。
- 64MB 预检抽纯函数（常量 + 断言函数），组件测试用 mock 小文件 + 纯函数单测覆盖阈值；Server UI 上传页按 file.size 同规则预检。
- manual 行 version 恒为「?」（后端），前端显示「—」。
- 旧版本 404 判定用 `err.code === "HTTP_404"`；WinHost 旧/新 × Server 旧/新四象限组合两个区独立判定。
- 安装成功提示直接展示后端 message（已含「重启客户端后生效」）。
- 验收提示：规格 §8「加载失败 err + 原因」徽标的真实联调验证需等后端 loadError 补充落地（前端单测先行覆盖该分支）。

