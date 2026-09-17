# Changelog

本文件记录每个迭代的变更。

## 迭代 80：服务端地址切换「保存即生效」落地与连接状态一致性 · 2026-09-17

- **动机与范围（#128；评审 #114 高优先项 C-2（含数据正确性风险）/ B-9 / C-3 同根修复，决议按 #114 终版拍板）**：「保存并生效」后设备并未真正切换——启动时构造的 `ServerJobPoller` 固化 URL、保存仅写机器级配置，打印记录持续发往旧服务器直到重启进程；「服务端」一词在客户端同屏承载三种含义且可互相矛盾；数据与打印页设备加入状态只取一次不随探测刷新。
- **路由热切换（AC-01，决议 1 取「保存成功后重建路由 worker」形态，决策 #141）**：新增 `ServerRoutingCoordinator`（WinHost `Routing/`）持有当前路由独占资源（服务端地址 + HttpClient 连接池 + poller + worker）——`POST /api/host/config` 保存成功后热切换：先按新地址重建 poller / worker 并立即注册（最小化设备不可用窗口），再停旧路由（取消在途长轮询 → 等待收尾 → 释放旧 HttpClient 连接池）；地址未变化幂等无动作（不打断进行中的注册 / 长轮询）；未配置地址时空转，首次保存即可开启路由（覆盖「启动无地址 → 保存后开启」转换）。形态选择理由：「poller 每轮读取配置」无法覆盖无地址启动转换、切换时延受长轮询挂起（最长 20s）影响不确定；重建形态在保存请求内确定性完成、旧连接生命周期独占。协调器不注册为 IHostedService（测试装配移除后台 Worker 后仍可经端点热切换），生命周期 = WinHostApp 启动接线 + `IHostApplicationLifetime` 停机联动清理。
- **三名义（AC-03，决议 2：本机打印服务 / 服务端地址 / 已加入服务端各义一名）**：① 状态栏（client 构建）改「本机打印服务：运行中 / 未运行」——`AppContext` 新增 `localServiceUp`（页面来源 `/healthz` 独立探测，10s 周期），本机 IP / 本机设备名随其显示（本机事实不再依赖远程服务端连通性）；设置页「服务端地址」维持原状（含义②）；② 数据与打印页对服务端的状态一律「已加入 / 未加入服务端」（顶部徽标 + 目标设备行 + 打印按钮提示 + 底部说明，`已连接服务端 / 尚未加入` 全量改「加入」口径）；③ 连接方式面板徽标着色改随本机打印服务（本机事实）；在线设备页空态「安装并连接服务端后出现」改「加入服务端」。残留「连接 / 未连接服务端」仅为含义②语境（设置页安装包 / 插件分发、作业历史数据源提示）。
- **加入状态周期刷新（AC-02，C-3）**：数据与打印页设备列表 / routeMode / hostInList 由「进入页面取一次」改为随探测周期刷新（client 构建 10s，server 构建维持进入拉取一次 + 提交前现拉校验契约）——后台注册状态变化（含保存地址后的热切换）页面在探测周期内自动更新无需切页；周期刷新保留已选目标设备（仍在线时）。
- **测试（全绿）**：WinHost 新增 5 项——协调器单测 4 项（无地址启动空转后首次开启 / 切换后新 poller 注册 + 旧 worker 停止 + 旧 HttpClient 释放 / 同地址幂等不重建 / 启动接线与停机清理）+ HTTP 集成 1 项（生产装配 WinHostApp + 双 Kestrel stub 服务端模拟 AC-01：保存后设备在新服务端注册（`POST /api/devices` 可见）+ 旧服务端 notify 长轮询连接中止）；前端新增 / 更新至 311 项（状态栏三名义三态 + 各义独立探测 + 徽标已加入 / 未加入 / 未注册 + fake timers 周期刷新无需切页）。
- **记账**：DESIGN 决策 #141 + §5.2 交互口径补句；ROADMAP 状态行。

## 迭代 78：插件命令打印实现 2/4——Zebra 编译器·文本（内置字体度量与文本元素编译） · 2026-09-17

- **动机与范围（#120；DESIGN §5.4.8 拆分步 2，依赖迭代 77 宿主链路 `6b72a4a`；契约 §5.4 已定稿含功能裁剪补遗，本轮只落码不改契约）**：Zebra 官方插件 `labelframe-transport-zebra` 实现首批 `ILabelCommandCompiler` 能力——文本元素编译（整页自包含 / `^A` 字体与字高 DPI 换算 / 加粗近似 / `^FO` 定位）+ 内置字体度量接 `ITextWidthMeasurer`（锚定 / 对齐起始坐标尽力近似）+ `printMode` 参数声明 + 前端「无预览，效果以真机为准」提示。
- **Zebra 编译器（AC-02，`LabelFrame.TransportPlugin.Zebra` 新增 `ZebraLabelCompiler`）**：整页自包含结构 `^XA` / `^PW` / `^LL` / `^CI28`（UTF-8）/ `^XZ`（宿主不包装追加）；文本元素 `^FO{x},{y}^A{font}N,{h}[,{w}]^FH^FD{data}^FS`——毫米 → 点按 DPI 换算（与图片模式 `ZplImageEncoder` 同口径），模板保留的 ZPL 内置字体名原样输出（默认缩放字体 0），加粗近似 = 粗体字体变体映射（`0→1`，决策 #49 方案 A 先例，映射表可调）；字段数据 `^FH` 十六进制转义（`^` / `~` / `_`，`_` 先转义防二次替换）。**排版降级（§5.4.1 功能裁剪，不实现不模拟）**：超长文本不换行不缩放直接输出（溢出为预期常态，不做溢出校验）；锚定 / 块内 `TextAlign` / 垂直对齐按内置字体度量编译期换算起始坐标作尽力近似，无法近似即按起始坐标直接输出。**显式拒绝（§5.4.4）**：中文等内置字体未覆盖字符 → `LF_ENC_002`（含首个问题字符码点 + fieldKey）；条码 / 二维码（迭代 79）与图片 / 线元素按编译失败显式拒绝，不自动回退图片；区域元素 = 版式容器（仅参与锚定解析，边框为图片模式能力裁剪）。
- **内置字体度量（决策 #110 编译侧兑现）**：新增 `ZebraTextWidthMeasurer : ITextWidthMeasurer`——缩放字体 0 按 Helvetica 同度量比例字宽表（1/1000 字高）估算，位图字体 A-D 按官方设计点阵（9×5 / 11×7 / 18×10 / 18×10 点）单元格换算；度量仅供锚定近似（尽力项口径，不追求与图片模式等价）。`ZebraBuiltInFontMetrics` 内聚字体知识（品牌知识归属品牌插件，§5.4 动机）。
- **能力与参数声明（AC-01）**：`ZebraTransportPlugin` 同一插件对象实现 `ILabelCommandCompiler`（品牌级能力，与连接实例无关，委托无状态编译器）；参数清单增 Select `printMode`（必填、默认 `image`、Options 图片（默认）/ 原生指令）——注册表装配期能力位自动为真，`GET /api/transport` 经既有 DTO 透出（零前端品牌硬编码）；`Describe` 原生模式附加「（原生指令）」标识。
- **前端提示（AC-04，§5.4.2 口径）**：原生指令模式无指令级预览——连接为 native 时界面出现「原生指令模式无预览，效果以真机为准」提示（新增 `NativePrintModeHint`）：数据与打印页操作区（连接态判断 `isNativePrintMode`，与后端读取口径一致——缺失 / 非法值不视为 native）+ 设置页连接面板（选择即时反馈）；`TransportPluginInfo` 类型增 `supportsDocumentCompile` 增量字段。
- **测试（全绿）**：WinHost 新增 30 项——编译器与度量器 27 项（能力位与参数声明 / 整页结构与 DPI 换算 / 字体与加粗映射 / 转义 / Latin-1 接受 / 超长直出不换行不缩放 / 区域锚定 Start↔End 起点偏移可断言 / 块对齐近似 / 中文与多文种及控制字符 `LF_ENC_002` / 四类范围外元素显式失败 / 度量器确定性口径）+ 宿主集成 3 项（真实 Zebra 插件 native 分派：作业指令字段为原生指令非 `^GF`、逐张内容区分；image / 缺省路径经真实插件连接零回归；中文端到端失败含插件 id 与中文原因）；前端新增 7 项（`isNativePrintMode` 读取口径 / 设置面板提示出现与切回消失 / 数据打印页连接态提示出现与图片模式不出现）。AC-04 另附浏览器自动化走查截图证据（见 Issue #120 回评）。
- **记账**：ROADMAP 状态行；DESIGN 无契约变更（§5.4 未改动，实现细节按 ZPL 官方资料实施）；真机文本对比（AC-06）转 `待验收`。
## 流程卫生：轮值协议补齐 worktree / 本地分支回收闭环 · 2026-09-17

- **动机（#126）**：`C:\work\OpenCode` 累积 18 个 `LabelFrame-wt*` 半删残骸与 18 条已合并本地分支泄漏。根因：① `git worktree remove` 在 Windows 删不动 `web/node_modules`（pnpm junction / 文件锁）会**静默半删**（注册表已清、目录残留），迭代轮值虽有清理指令但无「验证目录消失」与兜底对账；② 验收 worker 自建 worktree（取证 / 结项 PR）用毕无人管，验收协议全文未提 worktree；③ squash 合并使 `git branch -d` 误判未合并，本地分支清理指令不可执行。
- **协议修订（`.zcode/automations/`，随本变更首次入库版本化）**：`iteration-duty.md` v7——「三」合并后回收加固（`remove --force` → 确认目录确实消失 → 残骸 rm -rf 补删 → 汇报；删分支核实 PR 已合并后用 `-D`）；「一」扫描加每轮 worktree 残留对账兜底（prune + 扫兄弟目录 `LabelFrame-wt*` 对照注册表与在途迭代）。`acceptance-duty.md` v6——worker 自建 worktree 用毕回收（进安全边界，与沙箱「用毕清理」同款纪律），漏网由迭代轮值对账兜底。
- **存量清理（本机一次性）**：18 个残骸目录与 18 条已合并本地分支（逐一对到已合并 PR 验证后删）已清除；在途 `wt78`（迭代 78）保留。纯流程 / 文档变更，无代码改动。

## 迭代 77：插件命令打印实现 1/4——宿主链路落码（能力接口与提交分支） · 2026-09-17

- **动机与范围（#119；DESIGN §5.4.8 拆分步 1，契约迭代 76 / #113 已定稿，本轮只落码不改契约）**：把插件命令打印契约的宿主侧链路落成代码——Core 契约类型 + 插件能力位 + `printMode` 连接参数消费 + 提交编译分派 + `LF_ENC` 新码入注册表。本迭代无品牌插件（Zebra 编译器实现归步 2 / 3，#120 / #121），全链路用 fake 编译器单测验证；界面无可见变化（前端零改动，首个真实 `printMode` 参数随步 2 出现）。
- **Core 契约（AC-01，签名与 DESIGN §5.4.1 逐字对齐）**：新增 `ILabelCommandCompiler`（`CompileAsync(LabelDocument, LabelCommandCompileOptions, CancellationToken)`）+ `LabelCommandCompileOptions(Dpi, Context)` + `LabelCommandCompileResult(Command, ErrorCode, ErrorMessage, FieldKey)`（`LabelFrame.Core.Transport.Plugins`，可选能力模式，先例 `ITestableTransport` / `IPrinterStatusProvider`）；`TransportPluginDescriptor` 增能力位 `SupportsDocumentCompile`（默认 false，装配期 `plugin is ILabelCommandCompiler` 判定），注册表新增 `GetCommandCompiler(id)` 取用；`GET /api/transport`（availablePlugins）与 `/api/transport/plugins` 经 `TransportPluginDescriptorDto` 透出该位（向后兼容增量字段 `supportsDocumentCompile`）。
- **printMode 消费（AC-03）**：新增 `TransportPrintMode`（Key `printMode`，`image` 默认 / `native`；读取口径 = 缺失 / 空白 / 非法值回退 image，忽略大小写，#69 兼容演进风格）；保存层校验——`TransportManager.Validate` 拒绝「native + 无能力位插件」（中文提示「请切回图片或更换插件」，走既有校验错误响应）；提交兜底——存量异常配置（手改 connection.json / 插件降级后能力消失）在提交时显式失败 `LF_ENC_003`，**不静默按图片打印**。
- **提交分派（AC-02，本地直连 `POST /api/jobs` 与 Server 路由领取 `HandleClaimedJobAsync` 共用 `JobSubmissionService` 入口）**：契约校验后按当前连接的打印方式分派——`image`（缺省 / 非法回退）→ 既有 Skia 整版渲染 → `^GF` 编码路径**零回归**；`native` → 插件编译器逐张编译（编译输入 = 原始 `LabelDocument`；意外异常由宿主捕获映射为同一失败语义，不透出堆栈），产物写 `LabelJobItem.Zpl`——**字段与 DB 列 `zpl` 不改名**，注释语义泛化为「持久化的打印机指令」；任一标签编译失败**整体拒绝**（直连不建作业 + 码 / 中文原因（含插件 id）/ fieldKey；Server 路由按既有失败回报路径回报 Failed，消息含问题码与中文原因）。
- **幂等不重编译（AC-04）**：native 路径提交前按 requestId 短路（`LabelJobQueue` 新增 `GetByRequestIdAsync`）——同 requestId 重放返回既有作业、fake 编译器调用计数零增加；图片路径维持既有「先渲染后入队幂等」顺序，行为零变化（模板后续修改不影响已入队作业的语义两模式一致）。
- **错误码注册表（Issue #119 待决议 1 用户定案：两新码）**：`LF_ENC_002`（指令编译失败——编译器无法原生表示元素 / 字符等，§5.4.4 口径）+ `LF_ENC_003`（打印方式配置异常——`printMode=native` 但插件无编译能力的提交兜底）入 `JobErrorCodes`，语义可区分、前端可编程分支（对齐 #107 注册表口径）。
- **测试（fake 编译器单测矩阵，全绿；fake 插件仅测试工程使用不进产品装配）**：Core 11 项（装配期能力位判定 + 编译器取用 / 内置插件能力位为假 / Options 携带 DPI 与上下文 / printMode 读取口径 8 例）+ WinHost 15 项（native 逐张编译入 Zpl 且非 `^GF` / 编译失败整体拒绝含码、插件 id、中文原因、fieldKey / 意外异常映射无堆栈 / 无能力兜底 `LF_ENC_003` 不静默 / 缺失与非法回退 image 且编译器零调用 / 幂等重放不重编译 / 契约校验先于编译 / native + Log 兼容传输数据留痕照常不出图 / 路由回报 Failed 含问题码 / 保存校验拒绝（native + 无能力）与放行（native + 有能力，含持久化）/ DTO 能力位透出 + HTTP 层校验拒绝）；既有套件全绿（dotnet 798 项，排除 Perf/Soak；本迭代无前端改动）。
- **记账**：ROADMAP 状态行；DESIGN 无契约缺口（§5.4 未改动）。实现边界备注：编译输入 `LabelDocument.Images` 首版保持缺省——品牌编译器按裁剪原则（§5.4.1）不消费图片元素，首个需要图片资源的实现迭代在提交分派处按需填充，不改契约。

## 迭代 76 补遗：原生指令模式功能裁剪原则——排版与对齐降级（契约补充） · 2026-09-17

- **契约补充（用户 2026-09-17 两轮补充口径；DESIGN §5.4.1 / §5.4.5 + 决策 #137 ⑥）**：确立**功能裁剪总原则**——图片模式是**全功能基准模式**（模板全部能力以图片渲染为实现基准）；原生指令（命令打印）模式是按品牌官方命令支持范围**裁剪的子集**，功能差异属预期行为而非缺陷，实现不了的不强求实现。具体口径：① 自动换行（`wrap`）/ 缩小适应（`fitMode`）/ 行距（`lineHeight`）等排版增强**一律不实现**（指令语法通常不支持等效语义，编译器不模拟）——文本超长溢出为预期常态，不因此编译失败、不做溢出校验；② 对齐类能力（块内 `TextAlign` / 区域锚定 / 垂直对齐）无指令原生等价（ZPL 基本模型 = 定起始坐标后绘制元素）——编译器可按内置字体度量编译期换算起始坐标作**尽力近似**（#110 机制），不强求等价，无法近似即按起始坐标直接输出；③ 效果验收口径同步——溢出与对齐差异不列为验收缺陷（锚定近似效果以观察记录为准，不作阻断项）。
- 纯文档修订，无代码改动；实现迭代（迭代 77 / #119、78 / #120、79 / #121）按修订后 §5.4 执行（#120 的锚定相关 AC 已同步调整）。

## 迭代 75：「PDA 日志 / 设备日志」页面下线——回传链路不存在前的界面收敛 · 2026-09-17

- **动机与范围（#112；两项待决议用户 2026-09-17 拍板，均取建议项——下线范围 = 双形态一起下线 / 代码处置 = 直接删除组件与测试）**：`web/src/pages/PdaLogs.tsx` 同源组件双形态（客户端「PDA 日志」+ Server 管理界面「设备日志」）的数据来源已断供——PDA 自动回传随迭代 40（决策 #95，pc_host 测试模式废止）移除、WinHost 亦无向 Server 回传日志的调用（仅有本地接收端点，迭代 51 决策 #106），两个形态均无写入方、页面永远为空，空态文案面向开发者且与事实不符。纯前端呈现收敛——无公共契约变更（决策 #140）。
- **页面下线**：双形态导航入口 + 页面挂载一并移除（`App.tsx` 两个 TABS 数组的 `logs` 项与页面渲染分支），`TabId` 联合类型移除 `'logs'`；`PdaLogs.tsx` 与 `PdaLogs.test.tsx` 直接删除（git 历史可找回，未来回传立项时按新需求重写）。前端 API client 的 `getLogs` / `LogEntry` 描述层保留（与后端端点同寿命）。
- **后端零改动（AC-03）**：`/api/logs` 接收 / 查询端点（Server 与 WinHost 共享实现，决策 #106）与 logs.db 存储维持现状——未来回传能力的既有地基；为端点存在的服务端测试一律不动。
- **测试同步清理**：客户端 / Server 菜单裁剪守门用例（`App.client.test.tsx` / `App.server.test.tsx`）断言更新为「PDA 日志 / 设备日志」双入口均不存在。
- **验证**：`pnpm lint`（0 错误，6 项既有警告）/ `pnpm test`（300 项全绿——删除 `PdaLogs.test.tsx` 4 项，菜单守门断言更新）/ 双构建（client + server）通过；后端零改动（CI 复跑 dotnet 全量）。
- **记账**：DEPLOY 客户端界面功能清单更新（移除「日志」页）；DESIGN 决策 #140；ROADMAP 状态行。

## 迭代 74：模拟打印作业数据留痕——记录模板名与传入数据集合 · 2026-09-17

- **动机与范围（#109；两项待决议用户 2026-09-17 拍板，均取建议项）**：Log 传输（模拟打印）此前只在宿主日志记「收到 N 字节（^GF 整版位图指令，内容省略）」——接收到作业时的**原始数据（模板名称 + 传入数据集合）不落日志**，PNG 出图只能看渲染结果、看不到作业输入；本轮在 Log 模式提交点补数据留痕（联调 / 排障对照物 =「这个作业带了什么数据进来」）。发送指令（^GF ZPL）摘要留痕明确不做（完整指令已持久化在作业库 `LabelJobItem.Zpl`，用户裁定原始数据才是关注点）。
- **留痕形态（决策 #139）**：记录点 = 宿主层 `JobSubmissionService` 判定 Log 模式处（与 PNG 出图同判定点、互不干扰；插件层 `SendAsync` 只拿到编码后指令字符串、拿不到文档数据）；每次新建作业记**作业 ID、模板名（请求级 `templateName` 优先，回退自包含 `template.name`，均缺省记「未命名」）、标签张数、数据集合（字段键值紧凑形式，键 Ordinal 排序，值内换行转义）**；① **多张标签去重**——相同数据集合记一条 + 重复张数（批量打印不刷屏），不同数据集合各自记录（首次出现顺序）；② **单条上限 2048 字符（≈2KB）**——超限截断数据文本并标注完整长度（防异常大数据刷爆日志）；留痕失败只记一行原因、不影响打印链路（与出图同一防护口径）；幂等重放（同 requestId）不重复留痕；非 Log 模式（tcp9100 / winspool / zebra）不触发，行为零变化。
- **实现**：新增纯函数格式化器 `JobDataLogFormatter`（WinHost `Jobs/`，去重 / 截断 / 转义单点）；`JobSubmissionService` Log 分支在 PNG 出图后调用 `LogJobData` 写宿主日志既有通道（零 DI / 配置变更）。
- **测试（LabelFrame.WinHost.Tests 新增 14 项全绿）**：格式化器 10 项（单张字段键值 / 批量相同去重 + 重复张数 / 相同 + 不同混合三组序 / 键序无关去重 / 超限截断 + 完整长度标注 + 行长不超上限 / 边界长度不截断 / null 与空数据占位去重 / 换行转义单行 / 未命名回退 / 空列表）+ 提交服务 4 项（Log 模式提交留痕含作业 ID / 模板名 / 张数 / 字段键值；批量去重两场景固定；幂等重放不重复留痕；Tcp 模式零留痕且作业照常创建——AC-04）。
- **记账**：DEPLOY §9 模拟打印排障说明补行；DESIGN 决策 #139；ROADMAP 状态行。
## 迭代 73：界面文案用户化——开发者向文案修正与连接配置低频收纳 · 2026-09-17

- **动机与范围（#108；两项待决议用户 2026-09-17 拍板：收纳形态 = 默认折叠 / 技术信息去处 = 直接删除——非提示气泡 / 折叠段收纳）**：不懂打印 / 网络技术的普通用户在首次配置连接、日常打印、查看作业历史时被开发者向术语卡住（英文模式名、系统路径、文件扩展名、端点 / 联调 / 后端渲染等内部机制）；连接配置是低频操作，不应常驻占据设置页主要篇幅。纯前端文案 / 布局迭代——无 API 契约与状态逻辑变更，行为零变化（决策 #138；迭代 76 先合入占用 #137，本条顺延 #138）。
- **连接配置低频收纳（决议 1）**：设置页「连接方式」面板默认折叠为当前连接摘要一行（数据源 = 既有 `formatTransport`——新后端 displayText 优先、旧后端按 mode 用户语格式化）+ 展开箭头，点击面板头一步展开完整编辑区（≤2 步进入编辑）；切换 / 测试 / 保存流程交互与请求行为不变，新增用例覆盖「默认折叠显示摘要 / 点击展开 / 再点收起」。
- **文案用户化（决议 2，逐条审查清单见 PR 描述）**：① 模式标签与连接徽标用户语——`Log（模拟）`→`模拟打印`、`TCP`→`网络打印机（TCP）`、`Zebra`→`Zebra 打印机`，旧后端徽标 `LOG`→`模拟打印`、`TCP …`→`网络打印机 …`、`WindowsDriver …`→`Windows 打印机 …`；② 「调试模式」→「模拟出图」（生成标签图片、不实际打印——保留专业语义），「调试出图 / 出图预览」→「生成图片 / 图片预览」；③ 删除系统路径（服务端地址说明的 `%ProgramData%\LabelFrame\Client\settings.json` 与默认端口）、文件扩展名与技术参数（`.lfpkg` / `.lfplugin` / `MSI / zip` / `仅 .apk` / 测试页字节数 / `条码 LABELFRAME-TEST`）、内部机制术语（端点 / 联调 / 后端渲染 / 心跳 / WinHost / Skia / 同源、PDA 日志页 `POST /api/logs` 手动验证示例、旧客户端「连接管理端点」说明、「插件 DLL 直接放入插件目录」指引）；④ 提示 / 错误文案可行动化（测试打印→确认出纸、旧客户端保存→已存本浏览器建议升级、检查更新→下载安装包运行安装或找管理人员、导入失败→所选文件不是有效模板文件等）；⑤ 表头用户语（`requestId`/`jobId`→请求 / 作业编号、`deviceId`/`lastIp`→设备 ID / 最近 IP、`完成-失败`→完成 / 失败）。后端插件 displayName 为后端数据不在本轮范围（前端回退标签已用户化，两者措辞差异容忍）。
- **验证**：`pnpm lint`（0 错误，6 项既有警告）/ `pnpm test`（304 项全绿——新增折叠收纳用例，既有断言仅同步文案措辞、无行为断言改动）/ 双构建（client + server）通过；后端零改动。
- **记账**：DESIGN 决策 #138；ROADMAP 状态行。

## 迭代 76：插件命令打印契约设计——文档编译能力接口与打印方式参数（纯文档） · 2026-09-17

- **契约先行（强化路径，#113；三项待决议用户拍板，均取建议项——编译失败 = 显式失败不自动回退图片 / 切换粒度 = 仅连接级参数（不做作业级覆盖）/ 接口形态 = 提交时编译并持久化（否决发送时文档直发））**：DESIGN 新增 §5.4「插件命令打印契约」+ 决策 #137——① 插件可选实现 `ILabelCommandCompiler` 文档编译能力（输入 `LabelDocument` + DPI + 插件上下文，输出整页自包含品牌原生指令文本或失败结果；先例 `ITestableTransport` / `IPrinterStatusProvider` 能力模式；编译为纯函数性质；版式解析以品牌内置字体度量驱动，兑现 #110 `ITextWidthMeasurer` 预留）；② 打印方式 = 连接级 `TransportParameterSpec` Select（`printMode`：`image` 默认 / `native`，随 connection.json 先测试后生效；不设全局开关、不做作业级覆盖，#45 教训；`native` + 无能力插件 = 保存校验拒绝 + 提交显式失败兜底）；③ 宿主提交时逐张编译并把指令写入 `LabelJobItem.Zpl`（字段与 DB 列不改名、语义泛化），Worker 发送 / 重启续打 / 失败项重打 / requestId 幂等（不重新编译）/ 挂起恢复 / 批次节流零改动，Server 作业载荷零感知；④ 编译失败 = 显式失败（`LF_ENC` 新码 + 中文原因；直连不建作业 / 路由回报 Server Failed），不自动回退图片，首版不做元素级混合模式；⑤ 效果验收口径：真机双模式并排对比（文本 / 条码 / 二维码 / 区域锚定 #110 不回归 + 出纸速度），参照迭代 55 取证先例。
- **后续实现迭代拆分建议**（Issue 级粒度，§5.4.8 表，可作立项依据）：宿主链路落码（fake 编译器全链路单测）→ Zebra 编译器·文本（SDK 内置字体度量接 `ITextWidthMeasurer`）→ 条码与二维码 → 效果验收收口（可选）。
- **记账**：DESIGN 术语表（编码器 / 新增「命令打印」术语）+ §5.4 + 决策 #137 + §7 边界（PDA 侧待 Windows 验证后评估）；ROADMAP 状态行。本迭代纯文档，无代码改动。

## 迭代 72：模拟打印出图目录保留清理——Log 出图 print 目录超期自动删除 · 2026-09-17

- **动机与范围（#107；两项待决议用户 2026-09-17 拍板，均取建议项）**：Log 传输（模拟打印）每个作业把渲染位图落盘到出图目录（`HostOptions.PrintOutputPath`，默认 `%LOCALAPPDATA%\LabelFrame\print`，结构 `print\<jobId>\label-N.png`，写入点 `JobSubmissionService.SaveLogPrintImages`），该目录此前不在任何清理范围内、无删除 / 封顶 / 过期机制，长期联调 / 演示机器无限累积占盘——本轮补保留清理机制（WinHost 客户端，Windows 窗口与 Linux 无头共用路径），出图目录磁盘占用有界。
- **清理口径（决策 #136，对齐 #108 保留先例）**：**按天保留、默认 31 天**，`LABELFRAME_PRINT_IMAGE_RETENTION_DAYS` 可调（与 `LABELFRAME_APP_LOG_RETENTION_DAYS` 同构），**≤0 = 不清理**（关闭语义）；判龄 = 作业子目录 `print\<jobId>` 的 **LastWriteTime**（与 jobs.db 解耦——删除只影响 Log 模式「查看出图」的目录 / 张数展示，作业历史记录本体不受影响）；触发时机 = **客户端启动时 + 每次模拟打印落盘后顺带执行**（新增 `PrintImageRetentionCleaner`，**不新增常驻后台任务**，对齐 #108「启动与轮转时顺带执行」轻量口径）；清理摘要记宿主日志一行；遇目录被占用 / 权限失败**降级留痕（日志）不抛出**（下次触发再试），不影响启动、打印与出图主链路。非 Log 模式（tcp9100 / winspool / zebra）落盘后清理不触发（调用点位于 Log 分支内），启动清理为目录维护与传输模式无关。
- **实现**：`HostOptions` 新增 `PrintImageRetentionDays`（默认 31）+ `LABELFRAME_PRINT_IMAGE_RETENTION_DAYS` 环境变量覆盖；`Program` 启动时执行一次；`WinHostApp` 装配清理器注入 `JobSubmissionService`（Log 落盘后顺带执行；测试构造缺省 null = 不清理）。
- **测试（LabelFrame.WinHost.Tests 新增 `PrintImageRetentionCleanerTests`，6 项全绿）**：超期删除（含内部 PNG + 摘要留痕一行）/ 保留期内不删（5 天前 + 当日新打）/ 关闭语义（0 与 -1 双参实证）/ 清理失败降级留痕（独占句柄占用超期目录，不抛出、目录留存）+ **主链路不受影响**（占用失败在场的模拟打印提交成功且当日出图正常落盘）。
- **记账**：DEPLOY §9 配置说明补行；DESIGN 决策 #136；ROADMAP 状态行。

## 迭代 70：离线布局安装——布局目录生成与本地源无网首装 · 2026-09-17

- **离线布局目录（offline layout，决策 #135；DESIGN §6.2 / §6.10，对标 VS 安装器 layout 模式）**：内网 / 无外网机器首装产品化——IT 在有网机器把当版全部组件（Server / Client MSI、webui zip、官方插件 `.lfplugin`、runtime 引导器，按 manifest 条目）+ `install-manifest.json`（官方原样字节）+ `latest.json` + 引导 EXE 预下载汇集到一个目录，拷贝分发后目标机**用同一个引导程序**无外网完成首装。组件文件名约定 = urls[0] 路径末段（须含扩展名）+ 查询型直链固定名兜底表（`runtime-webview2` → `MicrosoftEdgeWebView2RuntimeInstallerSimpleX64.exe`，核心库 `OfflineLayoutNaming` 单点，生成与消费共用）；单 EXE 全内嵌（attached container）不做（布局目录满足需求，§7 开放点登记）。
- **生成双形态（用户拍板：两者都做）**：① 独立脚本 `scripts/make-offline-layout.ps1`（IT 手段；引导 EXE 来源 = `-BootstrapperPath` 本地产物，本地缺省时按 `-BootstrapperUrl` 从 Release 下载——引导 EXE 已随 Release 发布（迭代 68 / 决策 #132））；② 引导程序 `--layout <目录>` 参数（产品化；EXE 自复制 `WixBundleOriginalSource`，`--manifest <路径|URL>` 可覆写生成清单源，默认稳定通道；进度窗 + 取消 + 结果对话框，`-passive` 静默）。两者同一语义：逐组件按 urls 顺序下载、sha256 与 manifest 逐字节一致才落位（不符换下一源、全源失败非零退出 fail-closed）、重复生成幂等（已存在且哈希一致复用、篡改 / 旧版残留重下）。引擎原生单横线 `-layout`（`LaunchAction.Layout`）由 BA 路由进同一生成流程（`IBootstrapperCommand.LayoutDirectory`），与双横线 `--layout` 同语义。
- **源解析顺序契约（用户拍板：a) 隐式优先源目录，manifest schema 零修改；#115 联动）**：清单来源为**本地路径**时其所在目录 = 隐式优先源目录，每链包有效源序 = **[布局目录本地文件（在位时）] ++ urls**——失败计数与换源语义不变（本地获取 / 校验失败按既有推进语义落到 urls，全源耗尽 fail-closed）；BA 在 `CacheAcquireBegin` 以 `IEngine.SetLocalSource` 注入本地文件，引擎对本地源副本同样按包内嵌摘要（= manifest sha256）强制校验（#117 口径）——**篡改布局内组件被校验拦截**（在线换 urls 重取干净副本 / 断网源耗尽失败，篡改内容绝不落装）。布局不在位 / URL 清单 → 纯 urls，行为与现状完全一致（AC-03 回归锚点）。
- **离线首装入口（隐式检测）**：引导 EXE 同目录存在 `install-manifest.json` → 欢迎页清单来源默认该本地文件（`WixBundleOriginalSourceFolder` 探测 + 提示条），拷目录双击 EXE 即零网络起步；无邻接清单 → 默认稳定通道（现状不变）。欢迎页离线指引文案随布局目录落地更新（#53 决议 1 方案 A 指引的收口）。
- **实现**：核心库新增 `OfflineLayout/`（`OfflineLayoutNaming` / `OfflineLayoutBuilder` / `LayoutModeArguments`，net48 / net10 双腿）；`CacheSourceFallback` 扩本地源（`SourceFallbackOutcome` 增 `LocalPath` / `IsLocal`，`FromManifest(manifest, layoutDirectory)`）；`WizardSession` 增 `LocalSourceDirectory`；BA 接线（布局生成模式 + `SetLocalSource` + 邻接清单检测 + `LayoutProgressForm` 进度窗）；`InstallManifestLoader.IsHttpUrl` 公开。
- **测试与取证**：Bootstrapper 测试全数保留并新增至 **218 项全绿**（命名约定 / 布局生成 / 参数解析 / 本地源优先与回退 / WizardSession 本地源；既有多源回退用例全数保留通过——AC-03）；走查脚本 `scripts/test-bundle-offline-layout.ps1`（矩阵脚本同构：per-user 测试 Bundle + 真实 BA + 本地测试源）本地同构取证 **31 项断言全过**——G 生成（目录完整 + 哈希一致 + manifest 原样 + EXE 自复制）、I1 零外网首装（测试源在线但访问日志零命中 + 邻接清单默认 + 本地源命中日志）、I2 篡改 + 断网 fail-closed（校验拦截 + 源耗尽 + 篡改内容未落装）、I2b 篡改 + 在线换源重取（有效源序 [本地] ++ urls 实证）、R1 无布局在线回归（纯 urls 下载，行为与现状一致）；`make-offline-layout.ps1` 对本地源同构取证（成功生成 / 源不可达 fail-closed / 幂等复跑零组件下载）；既有下载矩阵脚本复跑通过（S1~S6 全绿）。**AC-02 断网实机口径（断网 VM / 实机 + 抓包）本机无法自证，转待验收**。
- **记账**：DESIGN §6.2（布局目录契约）/ §6.3（offline 预设落地形态）/ §6.4（分发源策略）/ §6.10（本地源优先源解析顺序）+ 决策表 #132 + §7 开放点；ROADMAP 状态行随结项更新。

## 迭代 71：Linux 服务端一键安装——install.sh 与 compose 随发版分发 · 2026-09-17

- **动机与范围（#91；用户 2026-09-15 会话定案「脚本 + compose 分发都做」）**：Linux 服务端部署从「Docker compose 手抄仓库文件 / systemd 裸机 DEPLOY §5 手工步骤（framework-dependent 需自备 runtime——与 Windows 侧 AspNetCore 前置教训 #128/#129 同源）」升级为「一条命令」——脚本即 Linux 的「安装程序」（契约假设 Linux 操作者具备 IT 能力，图形 / CLI 向导不做，#50 边界维持）。三项待决议用户拍板：self-contained 归档为默认（免 runtime 前置）/ 离线模式本轮支持 `--manifest` 本地路径与布局目录（衔接迭代 70 #89）/ runtime 缺失明确报错给官方直链、不自动安装。**Linux 部署契约先行入 DESIGN（决策 #134 + §6.12，强化路径）**。
- **`scripts/install-server-linux.sh`（新）**：消费 install-manifest.json（schemaVersion=1 超范围 fail-closed；sha256 逐源强制 + urls 多源回退，#115/#117/#125 同义）→ 拉取校验 Linux 归档 → staging 解包校验（入口在场 + 形态探测，失败不动存量安装）→ 整体替换 `/opt/labelframe/server`（`appsettings.json` 保留用户版本，对齐 #48；`/var/lib/labelframe` 数据与日志目录不动）→ systemd 单元安装 / 启用 / 重启 → 管理界面 zip 落位 `plugins/web-ui`（默认装，`--no-webui` 关）→ 自检 `/healthz` 并核对 `/api/server/info` 版本。参数：默认官方稳定通道；`--manifest <本地文件 | 布局目录 | URL>`（本地清单所在目录即布局目录，本地文件优先、urls 回退，全量本地命中零外网请求）。归档形态按内容探测（self-contained 含 `System.Private.CoreLib.dll`），FDD 检测 `Microsoft.AspNetCore.App >= 10`，缺失 fail-closed 报官方下载页直链不自动安装。行级 manifest 解析（无 jq 依赖）；libicu 预检（最小化镜像缺失时服务启动即崩，给 apt 提示）。
- **归档默认 self-contained**：`publish-server-linux.ps1` 默认翻转（`-FrameworkDependent` 为可选），Release 附件 `linux-server` 归档不再要求目标机预装 .NET 10；release.yml 调用零变化（默认值在脚本内翻转）。
- **compose 随发版分发**：release job 新增步骤——`compose.yml` 与 `packaging/ubuntu/docker-compose.yml` 同源复制（防漂移，含管理界面 / client-packages / pda-packages / plugin-packages 四组挂载注释）+ `.env`（`LABELFRAME_VERSION` 钉定当版 + 镜像覆盖注释模板）随 Release 附件发布；两者不进 install manifest（部署描述文件而非可安装产物，#116 server-docker「无下载组件」口径维持）。
- **随迭代修复**：`deploy-server-ubuntu.sh` 两处字面 `
` 编码缺陷（`DATA_DIR`/`LOGS_DIR` 拼行 + systemd 单元 LOG_FILE 行粘连——`set -u` 下重跑必挂）。
- **本地验证（Docker 容器自证：ubuntu:24.04 + systemd 特权容器）**：**AC-01 / AC-05**——离线容器（`--network none`）布局目录安装全流程：systemd active + `/healthz` ok + 管理界面 `/` 200（text/html）+ 版本 0.27.1 与清单一致 + 文件日志落盘，零外网请求；**AC-02**——篡改归档一字节 → 本地哈希不符 → urls 回退全败 → 拒绝安装（exit 1），存量服务 / 用户配置无损；**AC-03**——重跑幂等（appsettings 用户值、数据目录标记、三个 SQLite 库保留，staging 替换无陈旧残留，服务重启后版本一致）；在线模式——URL 清单 + 首源 404 回退第二源成功（本机 HTTP 源同构模拟公网通道，代码路径同一 curl 下载链）；FDD 双向——无 runtime 明确报错含官方直链（存量无损）/ 装官方 aspnetcore-runtime 10.0.12 后重跑成功；真实发布清单解析冒烟（gh 下载 v0.27.1 `install-manifest.json` 字节精确——版本 / 归档名 / urls 提取正确）；**AC-04 本地同构演练**——复刻 release job compose 步骤生成 `compose.yml` + `.env`（断言全过），`docker compose config` 解析镜像钉定 `ghcr.io/marci-labs/labelframe-server:0.27.1`。演练抓修两处脚本缺陷（schemaVersion 取值带逗号、最小镜像缺 libicu 预检缺失）。`dotnet build` 0 警告 0 错误；`dotnet test`（排除 Perf/Soak）**709 项全绿**（与迭代 68 基线一致）；前端无改动。
- **记账**：DESIGN 决策 #134 + §6.12 + §6.3 联动；DEPLOY §4 compose 快速启动 / §5 Ubuntu 一键安装推荐（手工路径降高级）/ §8 发布链 / §10 脚本索引；CHANGELOG 本条目；ROADMAP 状态行。真机 Ubuntu 走查与下次 `v*` 发版 Release 附件实证（compose / self-contained 归档公网通道）转 `待验收`。

## 迭代 69：安装卸载收尾对称化——Bundle 卸载清理插件落位目录 · 2026-09-17

- **缺陷与根因（#93；来源 = #55 AC-01 干净 VM 重验外观察项）**：Bundle 卸载时 web-ui 落位目录被清理（且依赖 Server MSI「清除用户数据」rmdir 的附带效应，非 Bundle 自身编排），**zebra 插件落位目录残留**（约 10MB / 29 DLL 含 Zebra SDK 全套伴生依赖）——卸载序列里品牌插件落位包缺少对称清理动作。后果不止垃圾残留：卸载 → 重装不选 Zebra 的机器上，客户端插件扫描仍加载残留目录、Zebra 品牌依然可用，违背 #56「彻底外置」决议与问卷组件集合口径。
- **卸载语义契约入 DESIGN（契约先行，强化路径）**：§6.8 新增「卸载清理」段 + §6.9 链表扩为九包 / 失败处置段修订 + 决策 #133——清理边界 = **只清 Bundle 落位的目录**，**用户手动放置的第三方 DLL 不动**（用户资产；`plugins` 父目录其余内容永不动，父目录仅在清理后已空时移除）；与 #123 覆盖安装、#68「删目录 + 重启生效」（客户端运行时口径不动）、#127 加载生命周期（不改客户端加载机制）的边界关系显式记账。
- **实现载体定案（决策 #133，两项待决议按 Issue #93 用户拍板执行；按 Burn 机制评估、构建实证 WiX v7 WIX0408 后收敛）= 成对清理包**：**落位包（`WebUiPlacement` / `ZebraPluginPlacement`）维持 `Permanent` 与既有语义零改动**——非 Permanent ExePackage 必须有 DetectCondition（WIX0408），而引擎级 Present 跳过会改写「webui 同版重跑覆盖重写 / 插件 #123 版本比较在工具内执行」两条既定语义；清理由链内新增的**非 Permanent 成对清理包**（`WebUiPlacementCleanup` / `ZebraPluginPlacementCleanup`）承担：install = PayloadTool 无参空操作（登记包已装供 Burn 卸载规划），`UninstallArguments` = `-clean -target [落位目标变量]`（PayloadTool 新增 `-clean` 模式，与落位同目标对称）。**否决 BA 卸载路径显式清理**：无包状态机（需自记落位清单）、引擎事务外执行（无回滚语义、与 MSI 卸载时序竞争），违背 #122「引擎负责链装 / 回滚 / 提权 / 日志」立场。
- **落位凭据 = 清理边界的技术保证**：落位成功时 PayloadTool 经 `-version` 在落位目录根写 `.labelframe-bundle-placement`（内容 = Bundle 版本）；BA 启动期（Detect 前）读凭据比对 `WixBundleVersion` 写入 Burn 变量，清理包 DetectCondition 消费——**只有 Bundle 落位的目录携带凭据**（客户端「插件管理」通道安装的官方插件与一切手动资产无此文件，永不被 Bundle 卸载清理）；凭据随目录消亡，无记账漂移；升级（RelatedBundle 移除旧 Bundle）的引擎执行序无论先移除旧链还是先装新链均正确（版本匹配使旧清理包不误删新落位）。
- **清理时机**：Bundle 卸载 / **Modify 改选**（问卷改组件集合即时生效——重跑取消品牌即清理其落位目录）/ 首次安装失败回滚（失败后回到装前状态，较此前「留落位内容」更对称）。容错：目录不存在 = 幂等成功；清理失败日志中文留痕、不阻断卸载主链。落位目标变量改由 BA 启动期写入（ARP 卸载非交互会话的 `[变量]` 引用同样可解析）。机制通用化：后续品牌落位包按同构成对扩展。
- **测试**：新增 `PayloadCleanupTests`（卸载清理断言 / 卸载 → 重装不选品牌组件集合断言 / 手动放置不误伤断言 / 清理后同版重装不拒绝 / 凭据写入与通道区分 / 凭据随目录消亡）+ `PlacementMarker` 探测单测 + `scripts/test-bundle-uninstall-walkthrough.ps1`（per-user 走查：真实 BA 向导 + 真实 PayloadTool 落位 / 凭据 / 清理，S1 装（选 Zebra）→ S2 ARP 形态卸载（目录清理干净 + 凭据断言 + 第三方资产保留 + MSI 卸载正常）→ S3 卸载 → 重装不选 Zebra（zebra 目录不存在对照实证），本地取证不进 CI；清理包 DetectCondition 以 `WixBundleInstalled` 替代 BA 凭据探测变量属测试口径差异，已注释说明）；既有 `PayloadPlacerTests`（#123 版本比较）不回归。

## 迭代 68：流程治理——引导 EXE 随 Release 发布（release.yml 接线）+ 安装引导文档补章 · 2026-09-17

- **动机与范围（#101；收口决策 #121 ④ / #122 ⑤ 两项后置——引导 Bundle 此前只能本机 `build-bundle.ps1` 构建，Release 附件不含，用户测试 / IT 分发都需先具备构建环境）**：推 `v*` tag 后发版链自动构建 `LabelFrame-Bootstrapper-<版本>.exe` 并随 GitHub Release 发布；`docs/DEPLOY.md` 补「安装引导程序」章节、README 安装入口同步（两项待决议用户拍板：签名复用 MSI 自签通道 a / 引导 EXE 列为推荐入口、MSI 降高级路径 a）。引导程序功能与行为零改动（专项 #53~#57 已交付的七页向导 / 升级模式 / 下载链不动）。
- **release.yml 接线（决策 #134）**：新增独立 Windows `bundle` job（needs = package + android——bundle 消费的 manifest 需六类产物在场）：下载 release-assets + android-apk → `generate-install-manifest.ps1` 按与 release job 完全相同参数生成当版 manifest → `build-bundle.ps1 -ManifestSource release/install-manifest.json` 构建引导 EXE（Secrets 在场时 `-Sign`）→ fail-closed 断言 EXE 在场 → 提取 runtime 三条目 sha256 作 job outputs → 上传 artifact；`release` job needs 增 bundle、下载 EXE 后纳入 Release `files`。**manifest 同源机制 = 分阶段生成 + 跨 job 一致性断言**（Issue 点名首要风险）：release job 既有「生成安装清单」「断言安装清单」两步骤 YAML 零变化，追加一致性断言步骤——发布清单与 bundle 消费侧的 runtime 哈希逐条比对，不一致即整次发版失败（防厂商在两次生成之间轮转直链文件导致发布 manifest 哈希 ≠ 引导 EXE 内嵌摘要的错位），重跑自愈。
- **脚本增强**：`generate-install-manifest.ps1` 新增可选 `-RuntimeDownloadDir`（bundle job 与 build-bundle.ps1 缓存目录打通，同 job 内 runtime 只下载一次；默认空 = 既有临时目录行为不变）；`build-bundle.ps1` 新增 `-Sign / -PfxPath / -PfxPassword`（signtool 定位与签名参数与 `build-msi.ps1` 完全对齐，SHA256 + RFC3161 时间戳）。发版时长评估结论：runtime 共约 120MB × 两 job 各一次直连下载，发版低频，Actions 缓存 key 与 .NET 补丁版本 / webview2 轮转联动复杂度不抵收益，维持不加（记入决策 #134 ③）。
- **文档补章**：DEPLOY.md 新增「§2 安装引导程序（推荐安装入口）」——下载入口 / 七页向导与拓扑预设流程 / 运行时前置链说明 / SmartScreen「未知发布者」处置 / Burn 日志位置（`%TEMP%\LabelFrame*.log`）；原 MSI 章节降为「§3 高级路径」，后续章节顺延至 §10（README / release.yml 注释 / AndroidHost README / `InstallTargets.cs` 注释交叉引用同步）；README 快速开始三个安装入口与部署形态对照改为引导 EXE 推荐；§8 自动化发布补引导 EXE 构建链与签名说明。
- **本地同构演练（#97 / #131 先例手法；三项必需检查不执行 release.yml，复核靠本地演练 + YAML 自检）**：以 v0.27.1 真实 Release 六类产物在场模拟 CI——① 分阶段生成：`generate-install-manifest.ps1 -RuntimeDownloadDir artifacts/runtimes` 生成 + 断言 9 组件全过；② bundle 全链：`build-bundle.ps1 -ManifestSource release/install-manifest.json` 三个 runtime **缓存命中零重复下载**（哈希逐条校验通过），Bundle 产出 1.92 MB（与迭代 62 基线一致，4 条警告为既有可接受项）；③ 一致性：release 侧重生成后 runtime 哈希与 bundle 消费侧、官方 v0.27.1 发布 manifest **三方逐条一致**（同源决定性实证）；④ 签名：`create-signing-cert.ps1` 演练证书 + `-Sign` 构建通过，`Get-AuthenticodeSignature` 签署者 CN=LabelFrame（未信任链 UnknownError 属自签预期，与 MSI 同口径）；⑤ fail-closed 反向用例（意外实证）：缺任一产物（演练时漏 linux 归档）manifest 生成即拒绝（缺产物 = 空条目不可能落盘）。**演练抓到并修复一个真实缺陷**：脚本局部变量 `$runtimeDownloadDir` 与新参数 `$RuntimeDownloadDir` 同名——PowerShell 变量名大小写不敏感，参数被首行赋空清掉、恒走临时目录分支（缓存共享失效）；局部变量改名 `$runtimeAcquireDir` 修复。
- **验证**：release.yml YAML 解析自检通过、bundle / release 两 job 步骤序静态复核（五类产物齐备后才生成 manifest、EXE 在场后才创建 Release）；`dotnet build`（0 警告 0 错误）+ `dotnet test`（排除 Perf/Soak）**709 项全绿**（与迭代 62/64 基线一致）；前端无改动。真实发版走查（Release 附件含引导 EXE，AC-02）与干净 Windows 环境下载运行七页向导（AC-03）转 `待验收`。
- **记账**：DESIGN 决策表 #132 + §6.2 生成点补分阶段口径；CHANGELOG 本条目。
## v0.27.1 补丁发布（迭代 64 返修 + 迭代 67） · 2026-09-16

- **打包范围**：v0.27.0 之后合入 master 的两项缺陷修复——迭代 64 返修（PR #96：覆盖升级链依赖降版——发布工件版本固化与降版回归断言，决策 #130）与迭代 67（PR #98：release.yml 步骤序修复——Client MSI 附带插件包，流程治理 #97，决策 #131）。无用户可见功能变更，dotnet / 前端代码零功能改动（仅依赖钉版）。详见各迭代条目。
- **覆盖升级崩溃修复（迭代 64 返修）**：v0.27.0 Client 发布工件较 v0.26.0 依赖降版（SkiaSharp / Microsoft.Extensions.DependencyModel），叠加确定性组件 GUID 跨版本复用触发 Windows Installer 组件规则拒装降版 keyfile → 覆盖升级净结果两文件缺失 → WinHost 首启即崩（全新安装不受影响）。本版将依赖钉至 0.26 实测值（SkiaSharp 3.119.2、Microsoft.Extensions.DependencyModel 10.0.7，全仓统一），并新增 `scripts/assert-client-deps-baseline.ps1` 降版回归断言挂接 `build-msi.ps1`（CI 必需检查与发版链共用入口，降版即构建失败）——0.26 直升与坏 0.27 修复升级两条路径组件版本语义干净。
- **Client MSI 插件包附带修复（迭代 67）**：v0.27.0 发版链步骤序缺陷（插件包构建晚于 Client MSI 打包且静默跳过）致公网 Client MSI 无 `plugin-packages\*.lfplugin`，存量升级的 zebra 自动安装不可能发生；本版插件包构建步骤上移至 Client MSI 打包之前，并加 fail-closed 断言（`scripts/assert-msi-plugin-package.ps1`，缺失即构建失败）——本版起发版 Client MSI 附带 `labelframe-transport-zebra-*.lfplugin`。
- **版本同步**：ServerOptions / HostOptions `ProductVersion` 与稳定版 Compose 默认版本更新为 `0.27.1`（客户端「检查更新」比较口径随升，决策 #126）。
- **发布产物**：与 v0.27.0 同清单（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档、AndroidHost APK、Zebra 官方插件 .lfplugin、install-manifest.json + latest.json）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.27.1` / `labelframe-client:0.27.1`（均含 `latest`）；Client MSI 本版起由发版链断言实证附带插件包。引导程序 Bundle EXE 仍不随发版构建（#122 后置决策），需要时本地 `scripts/build-bundle.ps1` 构建手动上传。

## 迭代 67：流程治理——release.yml 步骤序修复：Client MSI 附带插件包（升级自动安装链修复） · 2026-09-16

- **缺陷与根因（#97；来源 = #57 AC-01 + #56 AC-04 合并升级走查取证，v0.27.0 公网实证）**：release.yml 中「构建 Zebra 官方插件包（.lfplugin）」步骤（PR #80 引入）物理位置在「打包 Client MSI」**之后**，而其步骤注释自称"先于 Client MSI 构建"——`build-msi.ps1` 打包时检测不到 `artifacts\labelframe-transport-zebra-*.lfplugin` 即按设计静默跳过附带 → 公网发版 Client MSI 无 `plugin-packages\*.lfplugin`（File 表 48 文件 0 命中）→ 存量升级的 zebra 自动安装（决策 #123 迁移机制）不可能发生；对照实验已证附带包在位时迁移功能完全正常（自动安装 loaded=true / isExternal=true），缺陷仅在流水线步骤序。
- **修复（流程治理迭代，`.github/workflows/release.yml` 变更已获用户立项批准）**：插件包构建步骤整步上移至「打包 Client MSI」之前（步骤注释与实际顺序对齐）；「打包 Client MSI」后新增 fail-closed 断言步骤——`scripts/assert-msi-plugin-package.ps1`（新增，Windows Installer COM 查 File / Component / Directory 三表，断言 `labelframe-transport-zebra-*.lfplugin` 落在 `plugin-packages` 目录，手法同 `assert-client-deps-baseline.ps1` / 决策 #130），缺失即构建失败。`build-msi.ps1` 的缺包静默跳过行为**保留不动**——本地裸构建与 CI「MSI 结构断言」必需检查依赖该行为（两者本就无插件包产物）。
- **本地同构演练（#97 AC-01 / AC-02 允许口径；三项必需检查不执行 release.yml，复核靠本地演练 + diff 自查）**：release.yml YAML 解析自检通过；AC-01 正向——`build-zebra-plugin.ps1 -Version 0.0.1`（9.88 MB，29 DLL）→ `build-msi.ps1 -Version 0.0.1`（22.6 MB，构建日志「随 MSI 附带官方插件包：labelframe-transport-zebra-0.0.1.lfplugin」）→ 断言通过（exit 0，File 表命中行：`plugin-packages\labelframe-transport-zebra-0.0.1.lfplugin`，组件 `ZebraPluginPackageFile`，目录 `PluginPackagesDir`）；AC-02 反向——移走 `.lfplugin` 后重建（build-msi.ps1 静默跳过，MSI 13.1 MB），断言 exit 1 拦截（fail-closed 实证），恢复插件包重跑构建（22.6 MB）与断言（exit 0）通过。
- **记账**：DESIGN 决策表 #131；v0.27.1 补丁发版由用户在修复合并后推 `v*` tag 触发（发版后走查 Client MSI 附件含插件包交回验收）。dotnet / 前端代码零改动（`dotnet build` / `dotnet test` 全绿仅为 DoD 例行确认）。

## 迭代 64 返修：覆盖升级链依赖降版——发布工件版本固化与降版回归断言（AC-01 验收不通过回流） · 2026-09-16

- **缺陷与根因（决策 #130；#57 AC-01 真实公网覆盖升级走查实证，证据链 `artifacts/accept-upgrade-027/`）**：v0.27.0 Client 发布工件较 v0.26.0 依赖降版（SkiaSharp.dll 3.119.2.0→3.119.1.0、Microsoft.Extensions.DependencyModel.dll 10.0.726.21808→8.0.23.53103），叠加确定性组件 GUID 跨版本复用 → Windows Installer 组件规则拒装降版 keyfile（Client MSI 日志 6 处 Disallowing）→ 同次升级旧产品卸载删旧文件 → 净结果两文件缺失 → WinHost 首启即崩（FileNotFoundException: SkiaSharp）；全新安装不受影响，仅覆盖升级路径命中。根因（两版还原图对称 diff 实证）：0.26 的高版本是 WinHost 当时引用的 Zebra.Printer.SDK 5.0.3685 传递钉定顺带抬升，迭代 63（#123）外置化移出后闭包回落到本仓自钉低版（Rendering 直接引用 SkiaSharp 3.119.1；Serilog.Settings.Configuration 传递 DependencyModel 范围 `>= 8.0.0` 解析取最低版 8.0.0）——非浮动升，是「传递下限消失暴露自钉低版」；全闭包 diff 降版仅三件（含 SkiaSharp.NativeAssets.Win32；原生 libSkiaSharp.dll 无 File 表版本列未触发规则），无其他隐藏降版。
- **依赖版本固化**：Rendering `SkiaSharp` / `SkiaSharp.NativeAssets.Linux` 3.119.1→3.119.2（WinHost / Server 闭包随升，与 AndroidHost / Zebra 插件工程 / WinHost.Tests 毒丸清单全仓统一 3.119.2）；WinHost 显式钉 `Microsoft.Extensions.DependencyModel` 10.0.7（闭包内唯一传递来源为 Serilog 范围引用，显式钉抬下限）；`tools/LabelFrame.PrintImageVerifier` 同步 3.119.2（不入 MSI 闭包，纯仓库一致性防漂移）。钉至 0.26 实测值而非更高最新版——与上一发版工件同版本号，0.26→新版与坏 0.27→新版两条升级路径的组件版本语义最干净；全仓直接引用均精确钉版，还原确定性可复现。
- **降版回归防线**：新增 `scripts/assert-client-deps-baseline.ps1`——对 Client 发布目录（`-PublishDir`）或 MSI File 表（`-MsiPath`，Windows Installer COM 查询）断言关键程序集「不低于上一发版基线」（SkiaSharp.dll ≥ 3.119.2.0、Microsoft.Extensions.DependencyModel.dll ≥ 10.0.726.21808、libSkiaSharp.dll 按在表断言；文件缺失同样判失败），挂接 `build-msi.ps1`（wix build 后、签名前）——CI「MSI 结构断言」必需检查与 release.yml 发版链共用该入口，降版即构建失败，无法再静默出包；基线随发版只升不降维护（`-Snapshot` 抄录，上调即发版检查项）。
- **WiX 组件 GUID 策略评估（决策 #130 ④，不改代码）**：按「相对路径 + 盐」的确定性 GUID 是 MSI 升级 / 引用计数 / Repair 的正确标准做法（改每版本新 GUID 会使旧组件成孤儿、卸载残留）；「同组件 keyfile 版本只升不降」为该模型硬约束，正解是版本单调性（固化 + 断言）而非改 GUID 策略；未来确需降版（依赖替换）须显式处理旧文件清理与重装语义，另行立项。
- **本地验证**：`dotnet build LabelFrame.slnx` 0 警告 0 错误；`dotnet test`（排除 Perf/Soak，过滤器与 CI 一致）**709 项全绿**（0 失败）；Client MSI 本地重建（版本号 0.0.1）File 表实测 SkiaSharp.dll 3.119.2.0、Microsoft.Extensions.DependencyModel.dll 10.0.726.21808 ≥ 0.26 基线，挂接断言随构建通过；**防线反向用例**：固化前工件（SkiaSharp 3.119.1.0 / DependencyModel 8.0.23.53103）在 MSI 与发布目录两模式均被断言拦下（exit 1，两项降版全列）——即 0.27.0 缺陷形态可被自动捕获。前端未改动（web/ 无变更）。
- **记账**：DESIGN 决策表 #130 + §6.11 补记「发布工件版本基线与降版断言」；CHANGELOG 本条目；修复后真实公网覆盖升级走查交回验收轮值（恢复条件 = 下次 `v*` 发版后真机重跑引导升级，含 0.26 直升与坏 0.27 修复升级两条路径）。

## v0.27.0 安装引导专项（迭代 60-64）汇总发布 · 2026-09-15

- **打包范围**：v0.26.0 之后合入 master 的全部迭代与缺陷修复——安装引导专项 4/8~8/8：迭代 60（引导程序骨架——五步问卷与拓扑预设，WiX Burn 形态决策 #122，dry-run 契约）、迭代 61（引导下载体验补全——多源回退决策 #125 与失败分类）、迭代 62（Apply 执行链启用——运行时前置链 + 非 MSI 落位决策 #124；AspNetCore 前置 Server / Client 双链腿两轮返修 #128 / #129）、迭代 63（zebra 传输插件外置化与官方插件体系决策 #123；外置插件 ALC 加载生命周期返修 #127）、迭代 64（升级路径收尾决策 #126——BA 升级清单 + 客户端「检查更新」+ 专项回看记账）。**勘误**：v0.26.0 标题所列「迭代 64」实际晚于 tag v0.26.0 合入（PR #85，2026-09-15），其内容随本版发布。详见各迭代条目。
- **安装编排（本版主题）**：五步问卷向导（欢迎含离线指引 → 拓扑预设（全五种）→ 品牌多选（ZDesigner 驱动名预选 Zebra）→ 管理界面开关 → 确认页含本机版本列）→ 安装 = 运行时前置链（.NET 10 Desktop / AspNetCore / WebView2，缺失才装、已装跳过）→ Server / Client MSI（覆盖升级，配置保留 #48）→ webui 与 zebra 插件落位（新版本覆盖 / 同版幂等 / 拒绝降级）；失败停链 + 引擎逆序回滚 + 中文失败分类报告（源不可达 / 网络中断 / 校验失败）；改选重跑 = Burn Modify 语义；干净机安装三形态（单机一体 / 服务端 / 客户端分离）已经干净 Win10 VM 验收。
- **下载体验**：按 install-manifest `urls` 数组顺序多源回退（获取失败与坏哈希均推进源游标；镜像源落地只需填数组）、缓存命中跳过下载、断网 + 缓存齐备可续装；换源 / 缓存命中 / 逐源进度在进度页标注。
- **插件体系（客户端可感知）**：zebra 传输彻底外置（#123）——WinHost 客户端核心不再内嵌 Zebra SDK（瘦身），官方插件包 `labelframe-transport-zebra`（.lfplugin，约 9.9MB）随 Release 发布并随 Client MSI 附带；存量「已用 zebra 配置」升级本次启动即自动装外置包（连接配置别名兼容读取）；未装外置包该品牌不可用（回退默认连接 + host.log 留痕，不再崩溃）；官方 `labelframe-` 前缀插件安装带版本比较，服务端 `plugin-packages` 放行官方 id。
- **升级路径（客户端可感知）**：重跑引导程序 = 升级模式（检测本机已装组件版本 → 可升级清单「组件 现版本 → 新版本」→ 复用 Apply 链完成覆盖升级；已是最新明确跳过；latest.json 清单新鲜度提示；ARP 卸载 / 静默参数走非交互路径不再卡向导）；客户端设置页「检查更新」提示闭环（发现新版提示到服务端下载或重跑引导，不做应用内自动安装）。
- **版本同步**：`/api/server/info` 版本、客户端 `GET /api/host/config` 只读 `version`（HostOptions.ProductVersion 首次随发版更新，决策 #126）与稳定版 Compose 默认版本更新为 `0.27.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档、AndroidHost APK、**Zebra 官方插件 .lfplugin（首发）**、install-manifest.json（本版起收录 runtime 三条目 + plugin-zebra 共 9 组件）+ latest.json）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.27.0` / `labelframe-client:0.27.0`（均含 `latest`）。引导程序 Bundle EXE 的 CI 发版构建未排期（迭代 60 记录「release.yml 未动」，本地经 `scripts/build-bundle.ps1` 构建），本版 Release 不含该附件——如实记录。

## 迭代 62 二次返修：前置链缺 AspNetCore 运行时在 Client 链腿复发（AC-03 预设 B「追加打印客户端」验收不通过回流） · 2026-09-15

- **缺陷与根因（决策 #129，DESIGN §6.2 / §6.3 / §6.9；AC-03 预设 B 干净 VM 取证实证，证据链 `artifacts/accept-55/ac03b/`）**：WinHost 同为 `Microsoft.NET.Sdk.Web`（隐式 FrameworkReference `Microsoft.AspNetCore.App`），但 client 预设下引导链不装 AspNetCore（`runtime-aspnetcore` 条目 topologies 不含 `client`、Bundle `DotNetAspNetCoreRuntime` `InstallCondition="InstallServer"`、`client-msi` dependsOn 缺该条目）——干净机选「追加打印客户端」安装链全绿（Apply 0x0、分离语义完美），但客户端进程启动即挂（.NET Runtime 1023：No frameworks were found）。#87 / 决策 #128 的同型缺陷在 Client 链腿复发。验收边界实证：手动补装 aspnetcore-runtime 10.0.12（sha256 与 manifest 钉定值逐字节一致）后客户端即正常（53960 监听 + 本地 UI HTTP 200）。
- **client 链腿补齐（三处同步）**：`generate-install-manifest.ps1`——`runtime-aspnetcore` 条目 topologies 补 `client`（standalone / server-win / client / offline，含客户端的预设都需要）、`client-msi` dependsOn 补齐 = runtime-desktop + runtime-aspnetcore + runtime-webview2；`Bundle.wxs`——`DotNetAspNetCoreRuntime` `InstallCondition` 扩为 `InstallServer OR InstallClient`（对齐 `DotNetDesktopRuntime` 双预设条件）。
- **Client MSI 检测修正（同 #128 Server 侧手法）**：`main.wxs` NetCoreCheck `RuntimeType` desktop → **aspnet**（WinHost = Web SDK，隐式主依赖 AspNetCore.App）+ LaunchCondition / RuntimeMissingDlg 文案同步——只装 Desktop 的机器在 Client MSI 检测阶段即被拦截，而非装完进程起不来；取舍如实记录（DESIGN 决策 #129 ②）：NetCoreCheck 单检测单 RuntimeType，WinHost 双框架需求（WinForms 的 WindowsDesktop + Web 托管的 AspNetCore）中 Desktop 腿由引导链 `runtime-desktop` 补装承载。
- **观察项①小修（确认页文案按计划集合过滤，决策 #129 ③）**：BA 确认页「确认安装计划」日志的运行时三腿（Desktop / AspNetCore / WebView2「未装（将安装）」）此前无条件统一输出，与引擎 `execute: None` 计划矛盾（验收实证 client 预设下 AspNetCore 文案偏差）——改为按当轮计划集合过滤（`ConfirmPage.DescribeRuntimeProbes`）；顺带补齐完成页 runtime-aspnetcore 展示名与探测腿映射（#87 遗留缺口：`CompletePage.DescribeOutcome` 误落 WebView2 探测腿、`DescribeComponent` 展示为裸 id——client 链装 aspnetcore 后完成页将实际出现该条目）。
- **观察项②不修（排期提示，决策 #129 ④）**：client 预设装后 `ServerUrl` 默认 `127.0.0.1:53961`（问卷无服务端地址采集项）与装后自启体验涉 #88 未决问题（客户端装后自启），另行排期复核。
- **测试（全解决方案 709 项全绿，与 #87 基线持平——拓扑矩阵改写而非新增）**：fixture `install-manifest.full.json` 同步（runtime-aspnetcore topologies 补 client + client-msi dependsOn）；`TopologyResolverTests` client 预设四用例预期集合改写（含 runtime-aspnetcore + 依赖序不变式扩展到 client-msi）；`WizardSessionTests` 端到端 client 集合断言；`ManifestParsingTests` client-msi dependsOn 断言；standalone 依赖序断言补 aspnetcore 前置于 client-msi。
- **本地验证**：`dotnet build`（0 警告 0 错误）+ 全解决方案 `dotnet test`（排除 Perf/Soak，709 项全绿）+ `pnpm lint / test`（303 项全绿）；全套产物 0.26.1 重建——manifest 生成 + 断言 9 组件全过（runtime-aspnetcore topologies 含 client、client-msi dependsOn 含 aspnetcore；AspNetCore 直链 sha256 `e5d20698…` 与验收方手动补装件逐字节一致）；真 Bundle 七包链重建 1.92 MB（仅既有两条可接受警告）；**Client MSI 定向断言全过**（LaunchCondition / RuntimeMissingDlg 标题与正文 aspnet 口径 + UI 序列 1150 < 1160 < 1297 + Server MSI 维持 aspnet 口径不回归）+ `verify-msi-ui.ps1`（CI 同构）通过；**真 Bundle「追加打印客户端」预设走查**——DryRun（问卷 → 取消 Zebra 预选 → 确认 → 取消，exit 0 零执行）：确认页计划日志 `组件 [runtime-desktop,runtime-aspnetcore,runtime-webview2,client-msi]`（返修前为三组件）；PlanProbe（确认后点安装、Plan 落日志后提权前截断，零包执行）：引擎 `Planned package: DotNetAspNetCoreRuntime, state: Present, default requested: Present, ba requested: Present, execute: None`（入 client 计划 + 已装跳过 = AC-02 语义；返修前为 requested Absent 被排除）+ `ClientMsi … execute: Install` + `ServerMsi … requested: Absent`（分离语义不回归）；server-win 预设走查：组件集合不变（含 aspnetcore）且确认页不再输出计划外的 WebView2 腿文案（观察①过滤实证）。干净机「只装 Desktop 装 Client MSI 被检测拦截」路径无法本机实证（本机双运行时在位）——以 MSI 表断言 + WiX NetCoreCheck 机制论证，交回验收 VM 复验。
- **记账**：DESIGN §6.2（runtime 条目 topologies / dependsOn 口径与示例）/ §6.3（预设映射表三行补 runtime-aspnetcore）/ §6.9（链表 InstallCondition 双预设）/ §6 决策记账行 + 决策表 #129；CHANGELOG 本条目；ROADMAP 状态行维持迭代 62 既有口径（返修不另行）。

## 迭代 62 返修：前置链缺 AspNetCore 运行时致干净机 Server 服务启动失败（AC-01 干净 VM 验收不通过回流） · 2026-09-15

- **缺陷与根因（决策 #128，DESIGN §6.2 / §6.9；AC-01 干净 VM 取证实证，证据链 `artifacts/accept-55/`）**：Server 组件为 `Microsoft.NET.Sdk.Web`（隐式 FrameworkReference `Microsoft.AspNetCore.App`），但引导前置 `runtime-desktop` 条目只装 windowsdesktop-runtime（不含 AspNetCore，两者互不包含），且 Server MSI 的 .NET 检测 `RuntimeType="desktop"`——检测放行而运行时缺框架，干净机 `LabelFrameServer` 服务无法向 SCM 报告状态（30 秒超时 → MSI 1920 → 1603 → Burn 全链回滚）。验收边界实证：手动补装 aspnetcore-runtime 10.0.12 后重跑引导全链成功、终态五项全绿。
- **前置链补齐定案（新增 `runtime-aspnetcore` 条目，厂商直链 + CI 锁哈希，机制同 #115 / runtime-desktop）**：「改用涵盖两者的安装器」否决——微软无「WindowsDesktop + AspNetCore 合一」安装器（Hosting Bundle 不含 WindowsDesktop 且面向 IIS、SDK 含开发工具体积不可接受）。`generate-install-manifest.ps1` 新增条目：钉定与 runtime-desktop 同一 .NET 补丁列车版本（单参数 `-DotNetRuntimePatchVersion` 统管双条目，原 `-DotNetDesktopRuntimeVersion` 随返修并入）、直链 `builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/<版本>/aspnetcore-runtime-<版本>-win-x64.exe`（Runtime 路径段大写）、topologies = standalone / server-win / offline；`server-msi` dependsOn 补齐 = runtime-desktop + runtime-aspnetcore；断言同步（直链白名单 / x.y.z 钉定版本 / silentArgs 非空）。
- **Bundle 链扩为七包（§6.9）**：`DotNetAspNetCoreRuntime` 置于 Desktop 与 WebView2 之间——DetectCondition = `AspNetCoreRuntimeInstalled`、InstallCondition = `InstallServer`、`Permanent="yes"`；`RuntimeProbe` 补 AspNetCore 腿（文件版本探测 `dotnet\shared\Microsoft.AspNetCore.App` ≥ 10.0.0，机制同 Desktop），BA 启动写 Burn 探测变量、确认页标注与日志同步；`build-bundle.ps1` 消费接线（manifest 缺条目即构建失败）。
- **Server MSI 检测修正**：`main-server.wxs` NetCoreCheck `RuntimeType` desktop → **aspnet**（WiX netfx 合法枚举 aspnet / desktop / core，构建实测 WIX0021 后定值）——只装 Desktop 的机器在 MSI 检测阶段即被拦截（LaunchCondition + RuntimeMissingDlg 文案同步），而非装完服务起不来 1920；客户端 MSI（WinForms，desktop）维持不动。
- **观察项裁定（AC-01 验收评论观察 ①，不修代码，决策 #128 ④）**：「品牌页未选 Zebra 时 InstallPluginZebra 仍为 true」非解析器缺陷——standalone 默认集合不含 plugin-zebra（契约测试锚定）；实证机制 = ZDesigner 驱动名预选（`SelectedBrands` 仅驱动预选与复选框勾选两条置位路径；验收自动化品牌页只点「下一步」，Burn 日志「品牌 [zebra]」= 复选框进入页面即已勾选），品牌页提示文案已声明预选语义。AC-01 重验不装品牌插件时在品牌页取消勾选即可。
- **测试（Bootstrapper 177 → 183，全解决方案 709 项全绿）**：`RuntimeProbeTests` 补 AspNetCore 独立探测 / 最低版本矩阵（3 项）；`UpgradeAssessmentTests` 补 runtime-aspnetcore 共享组件语义（更新即满足 / 落后升级 / 未装新装）；fixture `install-manifest.full.json` 收录 runtime-aspnetcore（9 组件）+ server-msi dependsOn，解析 / 拓扑解析 / 变量映射断言同步；`ChainPackageMap` 七包映射。既有用例全保留适配。
- **本地验证**：`dotnet build`（0 警告 0 错误）+ 全解决方案 `dotnet test`（排除 Perf/Soak，709 项全绿）；manifest 生成 + 断言（9 组件，runtime 三条目全过）+ 破坏性用例（版本非法 / 直链白名单外 URL 拦截）；AspNetCore 直链下载实测 sha256 与验收方手动补装件逐字节一致；真 Bundle 七包链重建 + 走查（本机 .NET 10 双运行时在位——跳过路径实证；干净机拦截路径无法本机实证，交回验收 VM 复验）。AC-01 + AC-03 转待验收（恢复条件 = 干净 VM 快照还原后重跑）。
- **记账**：DESIGN §6.2（runtime 条目特殊语义）/ §6.9（链序七包 + 检测口径）/ §6.3（品牌勾选置位路径）/ §6.7 回看表不动（历史快照）+ 决策表 #128；CHANGELOG 本条目；ROADMAP 状态行维持迭代 62 既有口径（返修不另行）。

## 迭代 63 返修：外置插件加载 ALC 生命周期缺陷修复（AC-02 真机回归不通过回流） · 2026-09-15

- **缺陷与根因（决策 #127，DESIGN §6.8「加载生命周期」；宿主级复现器 100% 实证 + 程序集→ALC 归属转储 + Resolving 处理器内引用相等取证）**：装好外置 zebra 插件后首次实际使用（连接测试）抛 `FileLoadException: Could not load 'SdkApi.Core' → InvalidOperationException: AssemblyLoadContext is unloading or was already unloaded`，此后打印 / 状态 / 测试全部被装配护栏回退 log 模拟，与内置不等价。根因：旧 `PluginDirectoryLoader`「逐 DLL 独立 collectible ALC 扫描 + 依赖惰性 Resolving 字节加载」组合下，**插件主体 ALC 对象扫描后被加载器丢弃（无任何托管强引用）**——GC 对 collectible ALC 发起卸载（状态进入 unloading）；存活插件实例使卸载无法完成（程序集 / 代码持续可用）但状态机已破坏，首用惰性解析 `LoadFromStream → VerifyIsAlive` 命中卸载态上下文即抛。静态强引用对照实验证实持根后同一惰性解析路径完全正常。
- **修复（三方向组合：③ 骨架 + ② 消窗口 + ① 断根）**：① **注册表显式强持有插件 ALC**——`PluginDirectoryLoadResult.Plugins` 元素改为 `DiscoveredPlugin`（插件 + 路径 + ALC 根），`TransportPluginRegistry` 随插件实例存根（宿主生命周期不卸载，同 id 重注册旧根释放可回收）；② **安装包子目录装配期预载全部伴生依赖**进插件 ALC（首用零惰性解析窗口；修复后 ALC 转储实证 29 程序集全部预载）；③ **按包目录单 collectible ALC 整体加载探测**（原子单元，跨程序集发现 `ITransportPlugin`，单 DLL 失败只记逐 DLL error 不阻断；无插件产出的目录显式 `Unload()` 确定性丢弃——消灭 29 个抛弃型扫描 ALC 与卸载竞态环境）。平铺手动 DLL 维持逐 DLL ALC（同居互不污染），ALC 根同样经注册表持有；`PluginProbe`（安装预检）同构收敛为单目录单 ALC + 预检完显式 `Unload`，三层校验语义（逐 DLL `LoadErrors`、`BadImageFormatException` 可行动消息）不变。
- **生命周期契约（不变项）**：字节加载不锁插件文件（#73）——卸载 = 删目录、覆盖升级 = 替换目录 + 重启生效（#68）；运行时热卸载维持不做（#68 未决不变）；collectible 保留用于注册表弃置后 GC 回收（升级重装走重启装配，无泄漏路径）。观察项结论（不修）：扫描期 WinRT `ApplicationData.get_Current` FirstChance = CsWinRT 非打包进程回退探测，内部捕获不影响结果，与本缺陷无关；包内 `System.Private.Windows.Core.dll` 的 `GetTypes` 依赖宿主框架件 `System.Formats.Nrbf`（Default 未加载时无候选）——逐程序集容错记 error 不阻断包装配。
- **测试**：新增 `PluginLoadLifecycleTests`（WinHost，生产同构官方包——真实插件 DLL + SDK 全套伴生闭包：三层校验安装 → 「重启」装配（ALC 根只经注册表持有，加载结果即刻弃置 = 生产形态）→ 多轮强制 GC + 延迟 → 首用 TestAsync 不可达端口——断言无 FileLoadException 且返回干净连接失败消息，再 GC 再用锚定重复性）；既有加载 / 安装 / 版本比较 / 迁移 / 传输行为 / 装配护栏（回退留痕）用例全保留通过。全解决方案 `dotnet test`（排除 Perf/Soak）**703 项全绿**（返修基线 652 项 + 迭代 64 汇合 50 项 + 本轮生命周期 1 项，rebase 后全量复跑）。
- **开发自验（宿主级）**：复现器（WinHost 本体口径：同 loader / 注册表 / TestAsync 入口）修复前 3/3 + 10 轮 GC + 30s 延迟均复现 `FileLoadException(VerifyIsAlive)`，修复后同口径全部通过（连接测试返回「连接测试失败：Zebra 打印机不可达（127.0.0.1:1……）」= 加载路径已过，加载失败发生在网络动作之前）。真机三项回归（连接 `~HS` / 真实状态 `GetCurrentStatus` / 出纸）交回验收轮值陪验。
- **记账**：DESIGN §6.8「加载生命周期」+ 决策表 #127；CHANGELOG 本条目；ROADMAP 状态行维持迭代 63 既有口径（返修不另行）。
## 迭代 64 安装引导专项（8/8 收尾）：升级路径——BA 升级清单 + 客户端检查更新 + 专项收尾记账 · 2026-09-14

- **升级模式（决策 #126，DESIGN §6.11 契约先行；范围 = Issue #57「范围修订 v2（缩编重报）」——原「自研升级编排」被 Burn RelatedBundle + 固定 UpgradeCode 既有语义吸收，本轮只做「看」的部分）**：① **本机已装组件版本探测** `LocalInstallProbe`（核心库 net48 / net10 双腿，全程只读）：MSI 注册表（`MsiEnumRelatedProducts` 按 MSI UpgradeCode 枚举 + `MsiGetProductInfo(VersionString)`，同族多产品取最高；UpgradeCode 常量与 packaging/*.wxs 对齐）、官方插件落位目录 manifest.json、runtime 复用 `RuntimeProbe`；② **升级评估** `UpgradeAssessor`（纯函数）：当轮 manifest 组件版本为目标 × 本机快照 → 逐组件动作（新装 / 升级 / 已是最新 / 本机更新 / 不比较）+ 整体「已是最新」判定（只看已装组件，AC-02）；③ **呈现**：欢迎页升级摘要横幅（可升级清单「组件 现版本 → 新版本」/ 已是最新 / 全新安装）+ 清单新鲜度提示（latest.json 消费：稳定通道 URL / 本地同目录推导，清单版本落后通道最新即提示，读取失败静默）+ 确认页新增「本机版本」列与升级 / 已最新横幅（Burn 日志同步留痕供走查断言）；④ **版本比较统一语义** `VersionSemantics`（双方可解析 System.Version（容忍 v 前缀、缺失段视为 0）按其比较，否则 Ordinal）——#123 插件比较委托同一实现（§6.8 / §6.11 单一语义）。
- **升级执行零新机制 + 走查暴露缺陷修复**：升级 = 确认后复用现成 Apply 链（Burn 固定 UpgradeCode 的 RelatedBundle 覆盖升级 + MSI MajorUpgrade 既有语义，BA 无新执行代码路径）。走查实测暴露真实缺陷并修复：**升级链尾段 Burn 以 Uninstall 动作运行旧 Bundle EXE，旧 BA 若一律弹向导将无人应答、升级链卡死**——BA 增加非交互启动路径（`Create` 事件取启动命令：动作非 Install 或显示级别 None / Passive / Embedded → 跳过向导，自动 Detect → Plan → Apply → Quit；Apply 需有效属主窗口句柄，以隐藏窗口承载（out-of-proc BA 传 IntPtr.Zero 实测抛 ArgumentException））——ARP 卸载 / 静默参数同样受益。
- **升级走查脚本（`scripts/test-bundle-upgrade-walkthrough.ps1`，本地脚本化验证不进 CI）**：per-user 测试载体（真实双 MsiPackage 内嵌链 + 真实 BA；测试 MSI 用生产 Server / Client UpgradeCode 使探测按生产口径真实命中，**不带 Upgrade 表**——0.90.1 → 0.90.2 替换由 Burn RelatedBundle 升级驱动（旧 Bundle 卸载其自有 MSI），绝不触碰本机真实安装；生产 MSI 的 MajorUpgrade 为 MSI 既有语义不在 per-user 载体重复）；**双场景三通道断言（Burn 日志 / MSI 注册表族内版本 / 退出码）18 项全绿**：S1 装旧（全新安装 0.90.1）；S2 升新（确认页升级横幅 + BA 可升级清单「服务端 / 打印客户端 0.90.1 → 0.90.2」+ `Detected related bundle` version 0.90.1 + 旧 Bundle 非交互卸载 + 族内最高 0.90.2）；S3 已最新（「已是最新版本，无需重复安装」横幅 + 双包 `state: Present` 跳过 + 版本不变）。实测踩坑入账：MSI 版本属性名为 `VersionString`（"5.0.5VersionString" 返回 1608）；MsiEnumRelatedProducts 的 UpgradeCode 必须带大括号（缺省返回 87 参数无效）；Burn 装的 MSI 带 ARPSYSTEMCOMPONENT=1 不进 ARP（残留清理须走 MSI API 族枚举）。
- **客户端「检查更新」提示闭环（决策 #71 / #72 分发之上，不做应用内自动下载安装）**：WinHost `GET /api/host/config` 响应新增只读 `version`（`HostOptions.ProductVersion` 常量，与 ServerOptions 同口径随发版更新；POST 忽略）；前端设置页「更新与安装包」卡片按发版命名事实解析客户端 MSI 版本（`LabelFrame-Client-<版本>.msi`）取最高与本机比较——**有新版** → 「发现新版本 X.Y.Z（本机 A.B.C）：到服务端下载新版安装包（下方列表），或由 IT 重跑安装引导程序完成升级」；**无新版** → 「已是最新（本机 X.Y.Z）」；本机版本未知（旧客户端）或列表无可解析版本号 → 不比较不显示（无误导）。比较逻辑入 `web/src/lib/update.ts`（`VersionSemantics` 的前端等价实现，数值段比较、段缺失视为 0）。
- **专项收尾记账**：DESIGN §6.7 新增专项回看表（8 迭代关键决策串讲 + **Burn Pivot 完整脉络**：#114 自研向导 → 迭代 60 AC-04 体积实测 51.7MB 超标 → #122 改 WiX Burn 及其对 #54 / #55 / #57 的连锁「范围修订 v2」缩编）；ROADMAP 状态总览补专项行；逐 Issue（#50~#57）AC 状态核对表回写 #57（滞后项确认 `待验收` 标签与恢复条件在位，只读登记不动标签归属）。
- **测试（Bootstrapper 126 → 177，全解决方案 702 项全绿；前端 303 项全绿）**：新增 `UpgradeAssessmentTests`（组件级口径矩阵：未装 / 升级 / 同版（含缺失段） / 本机更新 / 未装不影响已最新判定 / 插件 / runtime 两条 / webui 不比较 + VersionSemantics 理论展开与 #123 委托回归）、`LocalInstallProbeTests`（族内最高 / 按码匹配 / 版本不可得跳过 / 插件 manifest 四态 / runtime 透传）、`LatestPointerTests`（解析负例 / 未知字段兼容 / 来源推导规则 / 新鲜度与呈现三态）、`WizardSessionUpgradeTests`（会话级接线：探测 + 评估 + 同目录 latest.json + 探测异常兜底）；WinHost 端点集成测试补 version 透出与不可写断言；前端 `update.test.ts`（解析 / 比较 / 取最高 / 结论三态）+ Settings 页四用例（有新版 / 已最新 / 版本未知 / 无可解析包）。既有问卷只读契约（#53 AC-03）、多源回退（#54）、落位幂等（#55）契约测试全保留通过。
- **记账**：DESIGN §6.11（升级模式与版本比较）+ §6.5 / §6.9 / §7 相应注记 + 决策表 #126 + §6.7 专项回看；CHANGELOG 本条目；ROADMAP 状态总览补专项行（8 迭代一行式汇总 + Issue 链接）。

## 迭代 61 安装引导专项（5/8）：引导下载体验补全——多源回退与下载行为测试覆盖 · 2026-09-14

- **多源回退（决策 #125，DESIGN §6.10 契约先行；范围 = Issue #54「范围修订 v2（缩编重报）」——原自研下载器范围被 Burn 引擎吸收，本轮补回退消费与体验）**：BA 按 install manifest `urls` 数组顺序（#115：顺序即优先级）逐源回退——WiX v3 `ResolveSource` 在 v7（本仓工具链）的后继 = **`CacheAcquireBegin` / `CacheAcquireComplete` 事件内调用 `IEngine.SetDownloadSource` + 失败时事件返回 `Retry`**（引擎 `AcquireContainerOrPayload` 获取 / 重试循环；`CacheAcquireResolvingEventArgs.DownloadUrl` 在 v7 只读，`SetDownloadSource` 才是覆写入口——引擎源码考古定案）。决策核心入核心库 `CacheSourceFallback`（`src/LabelFrame.Bootstrapper/Downloads/`，net48 / net10 双腿）：每链包失败计数（获取失败与**坏哈希校验失败各计一次**）逐源推进，第 N 次获取提供 `urls[min(N, count-1)]`；**运行时清单是源顺序权威**（每轮 Apply 从当轮清单重建、重试按钮归零从头重试）；源耗尽停止干预并提示「已尝试全部 N 个源」；重试驱动有界（获取失败 Retry / 坏哈希引擎 2 次额度 + BA 追加 RetryAcquisition / 包级兜底 Retry——每次失败推进源游标，无源不空转）；**仅远程载荷参与**（`PayloadContainerId` 空 = 非内嵌容器，伴生 DLL 解压获取不干扰计数）；本地 / 缓存来源不覆写（保住断网续装）。单源清单（首期生产形态）零 schema 变更退化为「失败即耗尽」，**镜像源落地只需填 urls 数组**（#117 镜像位预留兑现为消费链路）。
- **下载侧进度 / 失败事件映射补全（#55 已交付安装链进度，本轮补下载侧细化）**：`CacheAcquireProgress` 的 `Progress/Total` 映射**单包下载百分比**（进度页分组件行 + 阶段行「下载中 X%（源 i/N）」）；**换源提示**行（「主源获取失败，已切换备用下载源（第 i/N 个）：host」）；**缓存命中标注**（获取前哈希跳过校验通过且本包未发生获取 → 「已缓存（跳过下载）」）；**失败分类** `DownloadFailureClassifier`（纯函数入核心库）——HRESULT → 源不可达（404 / 文件不存在 / 域名未解析 / 无法连接）/ 网络中断（超时 / 连接中止 / 重置）/ 校验失败（0x80091007 坏哈希）/ 未分类，差异化中文提示（换源已自动发生 / 检查网络与代理 / 镜像源部署指引 / 勿绕过校验），失败报告与 Burn 日志双通道呈现（分类结论写日志供回溯）。
- **Burn 下载行为测试矩阵（`scripts/test-bundle-download-matrix.ps1`，本地脚本化验证不进 CI）**：per-user 测试 Bundle（双 ExePackage id=ServerMsi/ClientMsi——PayloadTool 无参空操作作载体，规避 MSI 与提权，复刻 #55 方法）+ **真实 BA 七页向导 Win32 UI 自动化**（本地清单注入 → 单机一体 → 确认安装）；双 TcpListener 测试源按 URL 路由内嵌行为（`/ok/` `/404/` `/hang/` `/corrupt/` `/truncate/`，访问日志落盘）；多源 url 序由运行时清单驱动（正是被测消费路径）。**七场景三通道断言（Burn 日志 / 退出码 / 访问日志）本地全绿**：S1 正常下载；S2 主源 404 → 换源镜像成功；S3 主源无响应挂起 → 换源成功；S4a 坏哈希 → 换源重取干净副本成功；S4b 坏哈希单源 → 引擎重取额度内同源重试仍坏 → 失败（fail-closed 不装不明文件，exit 0x80091007）；S5 半量截断中断 → 镜像完整重传成功；S6 缓存命中 + 断网续装（run1 挂起下载中强制中断（**实测口径：缓存阶段失败会被引擎回滚清缓存，进程中断才保留 InProgress 注册与包缓存**）→ run2 双源停机：已缓存包零网络请求 + 未缓存包逐源拒绝 → 换源 2/2 → 源耗尽 + 分类提示）。**Burn 载体下「续传」语义 = 失败后重试 / 换源重新获取**（引擎不做 HTTP Range 断点续传）——与原自研下载器 AC 口径的差异如实记录于 DESIGN §6.10。
- **测试（Bootstrapper 89 → 126 项）**：新增 `CacheSourceFallbackTests`（11 项：首源 / 失败推进 / 校验失败推进 / 耗尽 / 单源退化 / 未知包 / 计数隔离 / Reset / 六包映射 / 三源钳制 / UI 元数据）与 `DownloadFailureClassifierTests`（26 项理论展开：HRESULT 矩阵 + 差异化文案 + 三类互异）；问卷只读契约（#53 AC-03）与 #55 既有契约测试全保留通过。
- **工程注记（矩阵实测踩坑，入 DESIGN §6.10）**：wix build 会在输出目录留包载荷硬链接中间产物——不清除则 Burn 以 Bundle 同目录为本地源直接 copy 跳过下载；WinForms 控件为注册类名（`WindowsForms10.*`），UI 自动化需 `EnumChildWindows` 递归 + 类名子串匹配。
- **记账**：DESIGN §6.10（下载体验与多源回退）+ 决策表 #125 + §6.9 / §6.4 / §7 相应注记；CHANGELOG 本条目；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR）。

## 迭代 62 安装引导专项（6/8）：Apply 启用——运行时前置链与非 MSI 落位 · 2026-09-14

- **Apply 启用（决策 #124，DESIGN §6.9 契约先行；范围 = Issue #55「范围修订 v2」）**：引导 BA 从 dry-run 进入真装——问卷阶段保持 #53 只读契约（零下载 / 零安装 / 零系统改动，测试锚定），**执行边界 = 确认页「安装」**：BA 写入问卷变量 + 运行时探测变量 + 落位目标变量后 `Engine.Plan(Install)` + `Engine.Apply(向导句柄)`；#53「绝不 Apply」口径修订为「确认前绝不 Apply」（确认页横幅文案同步为「确认前只读」）。
- **Bundle 六包链（§6.9 链序，单一回滚边界）**：`DotNetDesktopRuntime` → `WebView2Runtime`（厂商直链、`Permanent="yes"`）→ `ServerMsi` → `ClientMsi`（既有）→ `WebUiPlacement` → `ZebraPluginPlacement`（非 MSI 落位包）；链内 `DetectCondition` / `InstallCondition` 消费 Burn 变量。
- **运行时前置检测口径（AC-02：缺失才装、已装跳过）**：.NET 10 Desktop Runtime = **文件版本探测**（枚举 `dotnet\shared\Microsoft.WindowsDesktop.App` 版本目录 ≥ 10.0.0，latestMajor 前滚对齐 MSI NetCoreCheck；注册表 sharedfx 键实证「已装而无键」不可靠）；WebView2 = EdgeUpdate Clients `pv` 注册表（HKLM+HKCU 对齐 MSI 先例）。探测入核心库 `RuntimeProbe`（委托注入可单测），BA 于 `engine.Detect()` 前写变量，链内 `DetectCondition` 消费——本地实测（真 Bundle）：本机双运行时均 `evaluates to true → state: Present`（跳过）。**修复实测缺陷**：BA 为 32 位进程（AnyCPU/Prefer32Bit），`SOFTWARE\WOW6432Node` 路径被注册表重定向二次映射读空——默认读取器改 `RegistryView.Registry64` 固定 64 位视图。
- **非 MSI 落位机制定案（ExePackage 包装解压工具；Burn 自定义动作否决——无包获取语义且引擎进程内加载受限）**：新工程 `src/LabelFrame.Bootstrapper.PayloadTool`（net48 控制台，随 bundle 压缩内嵌 + 伴生 DLL 以 PayloadGroup 挂载）；被落位 zip / `.lfplugin` 作为子 Payload 远程下载（Burn 下载校验缓存）；落位逻辑入核心库 `PayloadPlacer`——webui 覆盖解压、插件按 #123 版本比较（新版本覆盖 / 同版本幂等跳过 / 降级拒绝，退出码 20）。落位目标 = Burn 变量（`WebUiTargetDir` / `PluginZebraTargetDir`）注入 `InstallArguments`；落位包 `Permanent="yes"`（卸载编排不在本轮，#57）。
- **进度 / 失败 / 完成 UI**：进度页映射 `CacheAcquireProgress` / `ExecuteProgress` 总进度与 `Cache/Execute Package Begin/Complete` 分包状态（多源下载体验完整消费属 #54 修订范围）；失败报告 = 失败步骤（阶段 + 包 + 真实引擎状态码）+ Burn 日志位置（`WixBundleLog`）+ 建议动作（重试（重新 Plan+Apply）/ 查看日志 / 管理员重跑）；完成页 = 结果摘要（组件 + 版本 + 跳过项 + `LabelFrameServer` 服务状态尽力查询）+ 下一步指引（打开客户端 / 打开管理界面 `http://127.0.0.1:53961/` / **PDA 到下载中心扫码**——衔接 #52）+ 重启提示。执行期间锁定「上一步 / 取消」并拦截 Alt+F4 关闭。
- **失败处置与重跑幂等口径入 DESIGN（§6.9，引用 Burn 引擎内建回滚语义，不自造）**：包失败 → 停链 + 逆序回滚本次已执行包；重跑 = MSI 已装跳过 / 覆盖升级（appsettings.json 不覆盖 = #48）、runtime DetectCondition 跳过、webui 覆盖重写、插件 #123 版本比较、改选 = Burn Modify 语义（条件假即卸载）。
- **manifest runtime 条目落地（#115 消费）**：`generate-install-manifest.ps1` 随发版生成 `runtime-desktop`（钉定补丁版本，默认 10.0.12 可覆写；直链 = `builds.dotnet.microsoft.com/dotnet/WindowsDesktop/<版本>/...` 官方稳定模式）/ `runtime-webview2`（version=`evergreen`；go.microsoft.com 固定直链）条目——CI 下载（或 `-RuntimeFilesDir` 预置）实测 sha256 / sizeBytes；MSI 条目 `dependsOn` 补齐（server→runtime-desktop；client→runtime-desktop+runtime-webview2）；断言新增 runtime 专项（厂商直链白名单 / 版本规则 / silentArgs 非空，破坏性用例本地验证拦截）。`build-bundle.ps1` 按 manifest 获取 runtime 安装器（本地缓存 + 哈希校验，不符即构建失败 fail-closed）后交 wix build 内嵌摘要。
- **测试（Bootstrapper 42 → 89 项，全解决方案 614 项全绿）**：新增 `RuntimeProbeTests`（探测矩阵 11 项）、`PayloadPlacerTests`（落位 / 版本比较 / 拒绝降级 / 异常，10 项）、落位目标目录与链包映射契约（2 项）、问卷只读契约重锚定（执行边界文案 + 会话层无执行 API 反射断言）；既有 42 项全保留适配。
- **本地验证（VM 取证 = AC-01/AC-03 待验收欠账，本机无管理员权限）**：`dotnet build`（0 警告 0 错误）+ 全解决方案 `dotnet test`（614 项全绿）；manifest 生成 + 断言（8 组件）+ 破坏性用例（版本非法 / 直链白名单外 URL 均拦截）；**真 Bundle**（1.48 MB）构建 + 只读走查（问卷 → 确认 → 取消，engine exit=0，#53 零改动行为不回归）+ Burn 日志复核（双运行时 `DetectCondition` 真 → Present 跳过）；**per-user 测试 Bundle**（临时产物不进仓，复刻链机制规避提权）——成功链：问卷 → 确认 → Apply → 完成页（组件摘要 / 服务状态 / 下一步指引）+ 落位目录实测产出；失败链（注入 `exit 1` 包）：失败报告（组件 + 真实状态码 0x80070001 + 日志路径 + 建议动作）+ Burn 日志复核引擎回滚序列（exit code -2147024895 透传）。Bundle 构建含两条可接受警告（落位包无 DetectCondition——幂等执行设计内；双落位包共用工具 EXE 的 attached container 重名——仅影响 `wix burn extract` 调试场景）。
- **记账**：DESIGN §6.9（安装执行链与失败处置）+ §6.2 runtime 条目哈希锁定方式 + §6.3 问卷只读契约与变量契约扩展 + 决策表 #124；CHANGELOG 本条目；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR）。

## 迭代 63 安装引导专项（7/8）：品牌传输插件外置化与官方插件体系 · 2026-09-14

- **zebra 传输外置化（决议 1「彻底外置」，决策 #123，DESIGN §6.8 契约先行）**：`ZebraTransportPlugin` / `ZebraPrinterTransport` 从 WinHost 迁入新插件工程 `src/LabelFrame.TransportPlugin.Zebra`（net10.0-windows10.0.26100，Zebra SDK 5.0.3685 与毒丸引用清单随工程迁移），构建为官方 `.lfplugin` 包（`scripts/build-zebra-plugin.ps1`，版本随主版本演进）随 Release 发布；**WinHost 客户端核心不再引用 Zebra.Printer.SDK**（瘦身，SDK 依赖全部随插件包分发）。未装外置包时该品牌不可用（插件列表无此传输；连接引用时回退默认连接 + host.log 中文留痕——`TransportManager` 启动装配护栏，不再因缺插件崩溃）。
- **官方插件 id 与品牌映射（决议 2）**：官方前缀 `labelframe-`，zebra 插件 id = `labelframe-transport-zebra`（manifest.pluginId / DLL 插件 Id / 连接配置 pluginId 三处一致；Core 新增 `TransportPluginIdPolicy` 统一常量）；旧内置时代 id `zebra`（≤0.26）为读取别名（connection.json 读取时内存态映射，不落盘迁移）；brand → manifest 组件 id（plugin-zebra）→ 插件包 pluginId 三段映射表入 DESIGN §6.8，引导侧 `BrandPluginMap` 同构（后续品牌零契约扩展）。
- **存量升级兼容（AC-04，随升级包附带渠道）**：客户端 MSI 附带 `plugin-packages\labelframe-transport-zebra-<版本>.lfplugin`（main.wxs 条件编译块 + build-msi.ps1 自动检测产物传入；本地无产物可跳过不影响裸构建）；启动时 `ZebraPluginMigration` 检测「已用 zebra 配置」（connection.json 官方 id / 旧别名 / 旧 Mode=Zebra，或 appsettings / 环境变量 Transport=Zebra）且插件未装 → 自动从附带包安装（复用 PluginInstaller 三层校验），**本次启动即完成装配**（先于外部插件目录扫描）；无附带包 / 附带包损坏均只留痕引导安装，不阻断启动。
- **覆盖安装版本比较（决议 2，官方插件率先）**：`labelframe-` 前缀插件安装时与已装版本比较——新版本覆盖旧版本、**同版本幂等跳过**（不重复解压）、**降级拒绝**（提示先卸载再装，中文可行动消息）；第三方插件维持「覆盖安装不做版本比较」（决策 #72 4A）。比较语义：双方可解析 `System.Version` 按其比较（缺失段视为 0，1.0 == 1.0.0），否则字符串 Ordinal（Core 新增 `PluginVersionComparer`）。
- **服务端放行策略（AC-05）**：`plugin-packages` 上传校验新增内置传输保留 id（log / tcp9100 / winspool）拒绝——与客户端安装侧「注册表内置即拒绝」互为纵深；官方 id（`labelframe-` 前缀）放行，官方插件可经服务端集中分发、客户端「插件管理」安装。
- **manifest 收录与引导接线（AC-03）**：`scripts/generate-install-manifest.ps1` 收录 `plugin-zebra` 条目（type=lfplugin、version=主版本、topologies=standalone/client/offline、sha256 CI 实测）——本地实测：样例产物目录生成 + 断言通过（6 组件、逐条目哈希复核）；引导侧 `InstallPluginZebra` Burn 变量接线随清单条目生效（`BundleVariableMap` 既有映射，无需改代码），品牌页文案更新为外置语义、确认页 lfplugin 安装位置显示真实插件目录（`plugins\labelframe-transport-zebra`）；Bundle 链内 `.lfplugin` 的下载与安装归 #54 / #55。
- **发布流水线（对齐 #51 混合立项先例，仅改 release.yml）**：package job 新增「构建 Zebra 官方插件包」步骤（先于 Client MSI——MSI 附带该包），release-assets 产物与 GitHub Release 附件均含 `labelframe-transport-zebra-*.lfplugin`；三项必需检查（ci.yml）名称与语义不动。
- **测试（新增 48 个用例（理论展开计），全解决方案 576 项全绿）**：官方 id 策略 / 版本比较（Core，17 项矩阵）；官方包安装（生产同构包——真实插件 DLL + SDK 全套伴生闭包）通过内置冲突校验且重启装配成功（AC-02 装配面）、新版本覆盖 / 同版本幂等 / 降级拒绝 / 第三方同版本仍覆盖（WinHost，5 项）；存量升级迁移 9 场景（旧别名 / 官方 id / 旧 Mode / 环境变量 / 无配置 / 已装幂等 / 无附带包引导 / 附带包损坏不阻断 / 多版本选最新）；服务端放行 4 项（官方 id 放行 + 三内置 id 拒绝）；引导 fixture 升级（current 含 plugin-zebra 六组件 + 新增 pre-plugin 存量形态清单，既有断言同步）。既有 Zebra 传输行为测试（#49 AC-08 口径：连接失败 / 状态离线 / 测试失败消息含目标）随迁移全数保留通过。
- **本地验证**：`dotnet build LabelFrame.slnx -c Release`（0 警告 0 错误）+ `dotnet test`（排除 Perf/Soak，576 项全绿）；插件包实测 **9.88 MB**（29 DLL + manifest.json，含 Zebra SDK 全套伴生依赖，上限 64MB）；MSI 冒烟构建（含附带包与不含附带包两种路径）+ `verify-msi-ui.ps1` UI 契约断言通过 + MSI File 表确认 `plugin-packages\labelframe-transport-zebra-0.27.0.lfplugin` 在内；manifest 生成 + 断言 6 组件全过。
- **记账**：DESIGN §6.8（官方插件体系）+ 决策表 #123 + §6.2 / §6.3 品牌映射与变量契约更新；DEPLOY §6 与 README 传输行同步事实修正；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR）。

## 迭代 60 安装引导专项（4/8）：引导程序骨架——问卷与拓扑预设→组件集合（dry-run） · 2026-09-14

- **形态返工（决策 #122，Issue #53 决议评论用户拍板）**：首版「自研 WinForms self-contained 向导」实测 51.7MB 超 #114 ≤ 20MB 量级（WinForms/WPF self-contained 框架体积下限、裁剪被 SDK 阻断 NETSDK1175），用户权衡「买引擎」后改用 **WiX Burn Bundle + 托管 BA**——AC-04 口径随形态替换为 **Burn Bundle EXE 实测体积（MB 级）**；DESIGN §6.1 契约修订先行（强化路径），决策表 #122 记账；**迭代 61（#54）/ 62（#55）/ 64（#57）大半自研内容可被 Burn 吸收，各 Issue 拾取时重审范围**。
- **BA 技术形态（调研定案入 #122）**：WiX v5.4+（仓库现役 v7.0.0）BA 为 **out-of-proc 独立 EXE**（v3 工厂模型已废弃）——`src/LabelFrame.Bootstrapper.Ba`（net48 WinForms 五步问卷）引用 `WixToolset.BootstrapperApplicationApi` 7.0.0（支持 net462+），入口 `ManagedBootstrapperApplication.Run` 与引擎握手；**目标框架 net48**：.NET Framework 4.8 是 Win10 1809+ / Win11 的 OS 组件，裸机免装运行时（规避「首个 EXE 依赖 runtime」死循环），BA 载荷仅数 MB；.NET 10 SCD 复现 51.7MB、FDD 裸机不可用，均不取。
- **职责重划与复用**：拓扑编排仍自研——核心逻辑库 `src/LabelFrame.Bootstrapper` 重构为类库多目标 `net48;net10.0-windows`（BA 复用 + 测试 / #54 / #55 消费；`TopologyOptions.SelectedBrands` 形状微调 `IReadOnlySet`→`ISet`，net48 无前者、语义不变，§6.3 契约同步）；Manifest / Topology / Printing（ZDesigner → zebra 预选，决议 2）与既有测试全量复用；下载 / 校验 / 链装 / 回滚 / 升级引擎职责改由 Burn 承担。
- **五步问卷 BA**（欢迎含离线全量包指引（决议 1 方案 A）与清单来源本地路径 / URL → 拓扑预设（全五）→ 品牌多选（选项仅为 manifest 已有 plugin 条目）→ 管理界面开关 → 确认页）：确认页展示组件名称 / 版本 / 体积 / 来源 URL / 目标安装位置（对齐 DEPLOY），server-docker 展示 compose 指引。
- **dry-run 契约（AC-03）**：核心会话全程只读（清单获取 = 本地读或单次 GET，无下载 / 写入 / 系统改动代码路径）；BA 侧 dry-run = 写入 Burn 变量（契约 §6.3：`InstallPreset` / `InstallServer` / `InstallClient` / `InstallWebUi` / `InstallPluginZebra`，映射纯函数 `BundleVariableMap` 入核心库可测）后 `Engine.Plan(LaunchAction, BundleScope.Default)` **只计划不执行，绝不 `Engine.Apply`**；确认页「生成安装计划（仅预览）」展示引擎计划结果（每包 RequestState），UI 明示「仅预览：尚未下载、尚未安装」。
- **Bundle 与构建**：`packaging/bootstrapper/Bundle.wxs`（链 = Server / Client MSI，`Compressed="no"` + `DownloadUrl` web bundle——产物不进 EXE；`InstallCondition` 消费问卷变量；UpgradeCode 固定 GUID）；`scripts/build-bundle.ps1` 本地 / 发版构建（wix build 需真实 MSI 提取包元数据，故 Bundle 不进 slnx——按 #53 fallback「仅 PR 构建验证」口径如实记录，release.yml 未动）；核心库 + BA 工程入 `LabelFrame.slnx` 随 CI 构建验证。
- **测试（AC-01 / AC-02 / AC-03，48 项 = 既有 42 全保留 + 新增 6）**：样例 manifest fixture 两份（严格按 DESIGN §6.2 schema——`current` 对齐迭代 58 产物现状、`full` 含 runtime + dependsOn + plugin-zebra 模拟 #55 / #56 后形态）；全矩阵（五预设 × 开关 × 品牌，含依赖闭包顺序不变式、server-docker 空集合 + 双指引、webui 对 client 的 topologies 过滤）；dry-run 断言（探针目录快照一致 + ProgramData 不创建、本地清单零网络请求、URL 清单单次 GET 且组件 URL 不触碰、明示文案断言）；清单校验负例矩阵 + 品牌预选矩阵 + **Burn 变量映射矩阵（新增 6 项：全开 / 默认 / client / server-win / server-docker / 当前清单无插件条目）**。
- **AC-04 实测（新口径达标）**：`LabelFrame-Bootstrapper-0.26.0.exe` = **1.46 MB**（web bundle：EXE 只含 Burn 引擎 + 压缩 BA 载荷，MSI 按需经 DownloadUrl 下载——对比原 WinForms self-contained 方案 51.7MB）；本地冒烟：bundle 启动 → 引擎 Detect 双 MSI（Absent，含与已装 0.25.0 的 MajorUpgrade 关联识别）→ BA 进程（net48）承载问卷窗口，全程无 Apply、无安装动作。
- **记账**：DESIGN §6.1 形态修订 + §6.3 实现要点与变量契约 + 决策表 #122；CHANGELOG 本条目（Burn 形态）；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR，对齐先例）。
- **返修（2026-09-14 验收回流：向导装配缺陷致 AC-06 走查受阻）**：根因为确定性逻辑缺陷（非「异常被吞的宿主交互」）——`WizardForm.Navigate` 采用**增量**语义（`target = _currentIndex + delta`），构造期 `Navigate(0)` 意图「进入首页」实算 `-1 + 0 = -1` 被越界守卫静默 early-return：首页从未装配、索引停留 -1，窗体带空白内容区进入消息循环，点「下一步」即 `_pages[-1]` 越界异常（与验收实测症状逐项吻合，独立最小复现自证）。修复：导航状态机收口为 UI 无关的 **`WizardNavigator<TPage>`**（核心库新类型：绝对索引 + 惰性装配 + 越界拒绝 + 未装配访问显式 `InvalidOperationException` + 工厂异常传播不吞），`WizardForm` 改绝对 `NavigateTo` 并加装配自检（首页未就位即抛，禁止带 -1 进消息循环），BA `Run()` 装配失败显式失败（诊断对话框 + Burn 日志 + 非零退出 1603）+ UI 线程 `ThreadException` 兜底（不吞、写日志、对话框后退出）；新增导航状态机单测 11 项（构造未装配 → 装配首页 → 惰性前进 / 后退复用 / 越界防御 / 同页重进 / 未装配防御 / 工厂异常可重试 / null 工厂），全解决方案 587 项全绿。本地完整走查（Bundle 本地重建 1.46MB，UI 自动化 + 截图 7 张 + Burn 日志）：**复刻验收失败动作（空清单点「下一步」）得中文校验提示而非崩溃** → 本地清单加载自动进步 → 拓扑 / 品牌（ZDesigner 预选 Zebra 生效）/ 管理界面 → 「上一步」往返 → 确认页（五组件清单装载）→ 引擎 Plan 预览 → 正常退出（exit 0，日志全程无 Apply）。

## v0.26.0 迭代 49-59、64、65 汇总发布 · 2026-09-14

- **打包范围**：v0.25.0 之后合入 master 的全部迭代与缺陷修复——迭代 49（PDA 宿主自动化构建与品牌化）、迭代 50（错误响应分类修正）、迭代 51（客户端设备日志链路与前端可观测）、迭代 52（日志基础设施加固）、迭代 53（PDA 可观测性）、迭代 54（区域水平锚定修正）、迭代 55（Zebra 状态映射修正）、迭代 56（双端复用 Zebra 官方 SDK 5.0.3685）、迭代 57（安装引导专项设计契约 + 客户端退出提速）、迭代 58（发布流水线安装清单）、迭代 59（PDA 签名稳定化 + 服务端下载中心）、迭代 65（无字段模板打印测试修复），缺陷 #46（同 IP 双设备号解析最近活跃优先）与 #58（托盘「退出」不生效）。详见各迭代条目。
- **客户端可感知变更**：托盘「退出」与 Web UI 退出即时生效（统一退出路径 + 事件驱动提前退出，卡死路径约 0.5 秒，托盘图标不再残留）；无字段模板（静态标签）可在「数据与打印」页打印测试；自动宽度文本区域水平锚定按实测宽度对齐；Zebra 状态映射按官方字段表修正；WinHost 升级 Zebra 官方 SDK 5.0.3685。
- **服务端可感知变更**：同 IP 双设备号按 IP 解析改最近活跃优先（缺陷 #46，targetIp 投递不再路由到已停用旧设备号）；新增下载中心页（客户端与 PDA 安装包同页分区 + 条目二维码扫码下载，`pda-packages` 目录与 API）；日志按日轮转与业务事件日志。
- **PDA（AndroidHost）**：**APK 首次随 Release 分发**（专用 keystore Release 签名、版本号由 tag 注入、签名 Secrets 缺失即构建失败——换签名设备需卸载重装一次，见 DEPLOY §7）；接入 Zebra 官方 SDK（tcp 默认 / 蓝牙 SPP / USB OTG）；可观测性（HostLog 封装 + 崩溃捕获 + 本地滚动日志）。
- **发布流水线**：Release 附件新增 `install-manifest.json`（当版全部产物 + sha256 + 多源 URL，CI 实测生成与断言，迭代 58 决策 #115 / #120）与 `latest.json` 最新版本指针（稳定通道 `releases/latest/download/latest.json`）——均首次发布。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.26.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档、AndroidHost APK（首发）、install-manifest.json + latest.json（首发））+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.26.0` / `labelframe-client:0.26.0`（均含 `latest`）。

## 迭代 58 安装引导专项（2/8）：CI 自动生成安装清单（install-manifest.json + latest.json） · 2026-09-14

- **release workflow 新增安装清单生成与断言（Issue #51 范围 1~3，契约 = 决策 #115，实现决议 = 决策 #120；流程治理 + 产品混合立项，对齐迭代 49 / #31 先例——只改 release.yml，不动三项必需检查）**：release job 在创建 GitHub Release 前调用新增脚本 `scripts/generate-install-manifest.ps1`，收集当版全部产物（Server MSI、Client MSI、管理界面插件 zip、Linux 归档、PDA APK），实测 sha256（小写 hex）与 sizeBytes，生成 `install-manifest.json`（`schemaVersion=1`；组件条目 id / type / version / dependsOn / urls / sha256 / sizeBytes / silentArgs / topologies / notes 与 DESIGN §6.2 schema 一致；urls 为**多源 URL 数组**，首期仅 GitHub Release 主源一个元素，镜像位按数组形态预留）；runtime / `plugin-*` 条目按 schema 预留、无产物不出现，dependsOn 首版留空（只引用同集合内条目，runtime 随专项 5/8 #55 落地时补）。manifest 由 CI 生成、禁止人工维护（人工改动会被断言的哈希复核拦截）。
- **断言粒度（用户 2026-09-14 决议）：schema 字段完整性 + 产物存在性 + 哈希抽验（AC-03）**：生成阶段任一组件缺产物立即失败（空哈希 / 空条目不可能落盘）；独立断言步骤（`-VerifyOnly`）**重读落盘清单**复核——顶层与条目必填字段齐全性、type / topologies 枚举、sha256 小写 64 位 hex、sizeBytes 正整数、dependsOn 引用存在性、每条 Release 源 URL 对应产物在产物目录存在、逐条目重算 SHA-256 比对（覆盖且强于抽样，5 个产物成本可忽略）；缺产物 / 缺哈希 / schema 不符任一命中即非零退出、job 失败、不创建 Release。
- **latest.json 最新版本指针同步生成（用户 2026-09-14 决议，Issue #51 待决议 1）**：内容 = 最新版本号 + 当版 manifest URL，随 Release 附件发布；专项 8/8（#57 升级检查）直接消费，稳定通道 URL = `releases/latest/download/latest.json`。
- **脚本工程约束**：兼容 Windows PowerShell 5.1（本地自验）与 PowerShell 7（GitHub Actions runner）；JSON 手写序列化 + UTF-8 无 BOM 落盘（跨版本输出一致，引导程序解析不假设 BOM）。本地自验 5 用例全数符合预期——样例产物目录正常生成 + 断言通过（sha256 与 sha256sum 独立交叉复核一致，Python 独立解析 JSON 通过）、缺产物失败（exit 1）、篡改 sha256 哈希抽验拦截、删除 sha256 字段（缺哈希）拦截、断言阶段产物缺失拦截；release.yml 改动过 YAML 语法校验。
- **记账**：DESIGN §6.2 标注 manifest 从契约变实现（生成点 = release workflow 脚本调用）与 latest.json 已实现（含稳定通道 URL），决策表补 #120（实现决议：生成点 / latest.json / 断言粒度）；ROADMAP 状态行本轮不改（轮值结项时另提 docs PR，对齐 #50 / #52 先例）。
- **AC-01 / AC-02 待发版验证**：Release 附件含清单与下载复核 sha256 依赖真实发版 tag，转待验收口径（恢复条件 = 下一次 `v*` tag）。

## 迭代 57 客户端退出提速（事件驱动提前退出替代固定宽限） · 2026-09-11

- **退出看门狗改事件驱动：宿主停止信号 + 500 毫秒稳定窗提前退出（Issue #64，用户已确认方案）**：缺陷 #58 修复引入的固定 2.5 秒优雅宽限在卡死场景（托盘 / 界面消息循环在场、`RunAsync` 不自然返回）每次烧满——用户托盘退出体感 2~3 秒。`HostExitCoordinator` 序列改为：`StopApplication` 后**订阅 `ApplicationStopped`**，宿主已停止而主流程仍未返回（卡死判定）时，仅再留**稳定窗 500 毫秒**（`DefaultStabilizationWindow` 常量，不打配置面）即执行清理 + `Environment.Exit(0)` 强退——卡死路径端到端 ≈ 宿主停止延迟 + 500 毫秒（目标 <1 秒，现状 2.7 秒）；订阅采用「先挂回调再查已置位」双检，覆盖「订阅时宿主已停止」竞态。
- **优雅返回场景不强杀不回归（AC-03）**：`RunAsync` 自然返回的路径（Linux 无头端等）走既有 `finally` 自然收尾——稳定窗内主流程自然退出则看门狗收手（无强退、无重复清理）；宽限上限 2.5 秒**保留为兜底**（`ApplicationStopped` 未在宽限期内到来、宿主停止本身挂死时按现状超时强退），只是不再决定常见路径耗时。
- **host.log 记账补阶段行（AC-02）**：新增「宿主已停止（ApplicationStopped）而主流程未返回，进入稳定窗观察」「稳定窗内主流程自然退出，看门狗收手」「稳定窗届满判定卡死——稳定窗提前退出」三类阶段行，「点了退出没反应」可定位到稳定窗前后。
- **测试（AC-01 / AC-03）**：单测 7 → 10 项——新增 3 项（宿主停止后主流程未返回：稳定窗内清理 + 强退的**时序断言**——下界 ≥ 观察期结构性成立（Task.Delay 不提前）+ 「稳定窗提前退出」/「优雅等待超时」记账行互斥判定路径；稳定窗内自然退出看门狗收手不强杀；**订阅时已停止竞态**即时命中信号走稳定窗而非干等宽限），改写 1 项（原「宽限期超时强退」拆出「宿主停止信号永不到来」的兜底场景——`FakeLifetime` 加停止信号抑制开关）；既有自然退出 / 幂等 / 未绑定 / StopApplication 异常 / 清理幂等与异常用例全数保留。CI 满载（2 vCPU + 测试类并行 + 覆盖率收集）实测线程池调度延迟可达 9 秒级，时序上界断言因此剔除（环境噪声非被测行为），本地连续多轮实测退出点稳定在 500~600 毫秒。
- **沙箱进程级验证（AC-02，无头实例 HTTP 触发）**：真实客户端在运行并持有机器级单例互斥锁，Windows 托盘 TFM 沙箱不可起（会向真实客户端发激活信号）；以无头 net10.0 实例（独立端口 + 临时目录）实测 HTTP 触发退出：**进程退出 238 / 247 毫秒**（退出码 0，`{shuttingDown:true}` 契约不变），host.log 完整走「稳定窗观察 → 稳定窗内主流程自然退出收手」序列。托盘卡死场景的进程级计时以此环境不可行，由 AC-01 单测时序 + #58 真机日志推演自证，真机体感（AC-05，图标 <1 秒消失）留待用户复核。

## 迭代 59 安装引导专项（3/8）：PDA 签名稳定化 + 服务端下载中心（扫码下载） · 2026-09-11

- **`pda-packages` 目录 + API（决策 #119，与 client-packages 模式对称，三项待决议用户确认）**：服务端数据目录新增 `pda-packages`（`LABELFRAME_SERVER_PDA_PACKAGES` 可覆盖；Docker compose 默认挂载 `./pda-packages`）+ `GET/POST /api/pda-packages`、`GET/DELETE /api/pda-packages/{file}` 列表 / 上传 / 下载 / 删除——路径穿越防护共享 `FilePackageService`；**上传仅接受 `.apk`**（目录直放不限制扩展名，照常列出）；**APK 下载响应 MIME 固定 `application/vnd.android.package-archive`**（Android 浏览器识别为安装包直接拉起安装）；错误码增量 `LF_SRV_010`（不存在 404）。API 属服务端本体，无头服务端直放文件即可分发，管理界面（可选插件）只是操作入口。
- **统一「下载中心」页（Server UI，原「客户端下载」页升级）**：客户端安装包与 PDA 安装包同页分区展示（各自上传按钮、空态提示目录直放方式），条目按修改时间**倒序（最新在上）**（页面内排序先行，`latest.json` 落地后再对齐「推荐 / 最新」标记）；**每条目旁展示二维码**（`qrcode-generator` 渲染，内容 = 管理员浏览器正在访问的局域网地址 + 该条目下载路径）——PDA 与服务器同网扫码即得下载 URL；PDA 区常驻「未知来源 / 安装未知应用」授权步骤与同签名覆盖升级提示文案。**client-packages 既有行为不动**：客户端设置页「更新与安装包」卡片与既有端点零变更，仅并入下载中心展示。
- **release.yml 签名稳定化（流程治理授权范围；三项必需检查名称与语义不变）**：移除 debug 签名回退——Release 构建前逐项校验四个签名 Secrets（`ANDROID_KEYSTORE_BASE64` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS` / `ANDROID_KEY_PASSWORD`），任一缺失 `::error::` + exit 1 构建失败，**绝不静默降级**（回退路径 = 两次构建可能签名不一致：用户无法覆盖升级且换签名重置 ANDROID_ID 致设备号漂移，对决策 #104③ 过渡路径收口）；构建步骤签名参数由条件拼接改为恒定传入。日常 CI 第三必需检查「Android 构建（PDA 宿主）」（ci.yml）不动——仍 debug 签名验证可构建，不对外分发。
- **证书管理与换签名影响文档化（README / DEPLOY §7 / AndroidHost README）**：keystore 托管位置（GitHub Secrets + 生成方离线备份，不得入库）、备份要求（丢失 = 无法再发同签名升级包）、换签名影响（卸载重装 + ANDROID_ID 变 → 设备号变，对齐决策 #104 口径）与 PDA 扫码装机路径（DEPLOY §6 新增小节）。
- **测试**：服务端新增 12 项——服务级 7 项（`PdaPackagesServiceTests`：保存 / 列表 / URL、.apk 上传限制（含大小写）、路径穿越拒绝、目录直放非 apk 照常列出、下载路径解析、删除、同名覆盖）+ HTTP 集成 5 项（`PdaPackagesEndpointsTests`：上传后下载 **Content-Type = application/vnd.android.package-archive**、非 apk 上传 400 + 中文消息、缺失 404 + `LF_SRV_010`、目录直放列出、删除后二次 404）；前端新增 `DownloadCenter.test.tsx` 9 项（双分区列表 / 时间排序最新在上 / 二维码 title = origin + 下载路径 / Android 授权文案 / 双空态 / 加载失败 / 双分区上传与刷新 / PDA 删除确认与取消 / 客户端删除），`App.server.test.tsx` 随页更名与入口断言更新。
- **真机验收滞后（AC-02 / AC-03）**：PDA 真机扫码下载与同签名覆盖升级（设备号不变）转 `待验收`（恢复条件：UROVO DT50 等 PDA 与同网服务端可得）；AC-01（Secrets 缺失构建失败）以 workflow 文件审阅 + 逻辑推演自证，真实触发依赖发版 tag 属验收范畴。

## 迭代 57 安装引导专项（1/8）——设计契约：安装清单格式、拓扑预设与信任模型 · 2026-09-11

- **纯文档契约迭代（不写产品代码）**：`docs/DESIGN.md` 新增「安装引导（Bootstrapper）」章节（新 §6，原「风险与未决问题」顺延为 §7，PERF-BASELINE 两处引用同步），决策表新增 #114~#118——专项 2/8（#51 CI manifest）起的实现以此契约为准（AGENTS 强化路径：跨迭代契约先入 DESIGN 再改代码）。
- **引导程序形态与技术选型（决策 #114，用户已确认）**：自研 .NET 轻量向导（WinForms 分步问卷 + self-contained win-x64 单文件，体积量级 ≤ 20MB）；WiX Burn 不采用（迭代 10 #44 已放弃的 Bal 复杂度，且拓扑编排 / 中文问卷无论如何自研）；**Velopack 评估不用于引导程序**（模型错位：单应用安装器 + 应用内增量自更新框架，无多组件拓扑编排层；核心卖点增量自更新为第一期明确不做项；换打包契约与既有 WiX/MSI 体系冲突大）——记为未来「客户端应用内自更新」立项时的首选候选。
- **安装清单 install manifest 格式（决策 #115，AC-01）**：版本化 JSON schema（`schemaVersion` 首版 1，不兼容变更递增、引导程序超范围 fail-closed）；组件条目含 id / type（`msi`·`lfplugin`·`webui-zip`·`apk`·`runtime`·`archive`）/ version / dependsOn / **urls 多源数组（顺序即优先级、逐源回退）** / **sha256 强制（CI 实测、逐源强制校验）** / sizeBytes / silentArgs / topologies 拓扑标记；**由 CI 发版流水线从当次真实产物计算生成、禁止人工维护**（字段完整性断言）；runtime 条目厂商官方直链 + CI 锁哈希（厂商轮转 = fail-closed 接受）；Docker 镜像不进首版条目（registry digest 兜底）；`latest.json` 版本指针预留。附完整示例 JSON（脚本校验语法有效）。
- **拓扑预设（决策 #116，AC-02）**：单机一体 / 服务端（Windows 服务 · Docker · Linux systemd）/ 追加打印客户端 / **离线全量包（一等形态）**——「预设 → 组件集合」映射表 + **仅两项自由开关**（品牌插件多选、是否带管理界面），不提供任意组件勾选（防组合爆炸）；「预设 + 开关 → 组件集合」解析器契约（`ITopologyResolver`）为 #53 / #54 / #55 公共契约。
- **信任模型与分发源（决策 #117，AC-03 显式结论）**：manifest sha256 强制校验 = 公网完整性背书基线（镜像位零信任成本、哈希即背书）；**不推翻决策 #79**（局域网信任、插件不签名——公网引导分发的 `.lfplugin` 受 manifest 哈希背书，强于局域网三层校验）；插件 / manifest 签名升级触发条件显式衔接 #79（公网陌生第三方插件生态出现时先加签名）；manifest 同信道残余风险（信任根 = GitHub 账号 + TLS + tag 不可变）显式记录；已装服务端可作局域网优先下载源（复用 #71 / #72 分发通道）；离线全量包为一等形态。
- **更新策略与代码签名（决策 #118，AC-04）**：第一期 = 检查新版本（latest.json）+ 重跑引导程序升级（MSI 覆盖升级既有语义：appsettings 不覆盖 #48 等）；明确不做无人值守静默更新 / 服务端进程内自更新 / Velopack 增量更新。签名**首期不购证书**——MSI / APK / 引导 EXE 签名现状盘点入表（MSI 自签过渡 #65 / APK 专用自签 keystore #104 ③ / 引导 EXE 无签名风险最高），缓解 = manifest sha256 强制校验 + 发布页哈希与绕过说明；证书采购另行决策（开放点登记）。
- **未决问题同步**：DESIGN §7 新增「安装引导专项开放点」组（镜像源落地 / 证书采购 / 客户端自更新候选 Velopack / manifest 签名触发条件 / Docker digest 收录 / **i18n 不进 setup 问卷**——REQUIREMENTS §7 边界不变）；既有「插件包签名 / 服务端鉴权」条目补引 #117 / #118。
- **专项依赖清单（AC-05 前半）**：8 迭代依赖图与 Issue 链接表（#50~#57 全部已立项）入 DESIGN §6.7 并回链 Issue #50 评论；ROADMAP 状态行按流程留待结项（AC-05 阻断点 = 结项）。
- **本地检查**：纯文档改动；示例 JSON 语法脚本校验通过（含 sha256 64 位 hex 断言）；决策号 #114~#118 无冲突；`dotnet build LabelFrame.slnx` + `dotnet test`（排除 Perf/Soak）通过。

## 缺陷 #46 同 IP 双设备号按 IP 解析命中旧行修复（最近活跃优先） · 2026-09-11

- **排查确认（缺陷成立，本地复现不依赖真机）**：`devices.last_ip` 无唯一约束 / 无索引，设备号变更后新设备号从同 IP 注册**插入新行**，旧行 `last_ip` 无人清除（全仓仅 `ServerDb` 三处写 devices——注册 upsert / notify 心跳 / 领取事务，均单行更新，**无跨行清理路径**）；`FindDeviceByIpAsync` 原为 `WHERE last_ip = $ip COLLATE NOCASE LIMIT 1` **无 ORDER BY**——SQLite 无索引全表扫描按 rowid 序，稳定命中先注册的旧行。修复前实测：同 IP「旧行 stale / 新行活跃」场景，`FindDeviceByIpAsync`、`SubmitJobAsync` targetIp 解析、`GET /api/devices/by-ip/{ip}` 三路全部返回旧设备号（新增 4 项复现测试在未修复代码上全部失败，与用户现象一致）。
- **修复 = 按 IP 解析改「最近活跃优先」（决策 #113，Issue 候选方向 A）**：`FindDeviceByIpAsync` 加 `ORDER BY last_seen_at DESC, registered_at DESC, id`（后两项为确定性平手序）——设备号变更后必命中活跃新行；两个消费方（by-ip 端点与 targetIp 投递解析）共用该方法一并修复，按 IP 投递不再路由到已停用的旧设备号（长期 Pending 收不到）。**不取方向 B（写入时清除同 IP 其他行）**：NAT 共网出口下多台真机合法共用一个出口 IP，B 会让每次心跳抹掉其他设备的 last_ip（目录 IP 显示丢失、by-ip 退化为「最后写者」）且写放大；同 IP 多设备都活跃时 A 跟随最近一次心跳，尽力而为且不破坏目录数据。行为变化仅在「同 IP 多行」场景（先注册行 → 最近活跃行），单行场景零变化；无需数据迁移。
- **测试**：新增 4 项——服务级 3 项（FakeTimeProvider 时间确定：同 IP 旧行 stale / 新行活跃 → by-ip 与 targetIp 均命中新行；同 IP 双活跃设备解析跟随最近心跳）+ 端点集成 1 项（`RemoteIp` 请求头模拟同来源 IP 双注册，`/api/devices/by-ip/{ip}` 命中新行且在线）；`TempServer` 支持注入时间源。顺带修正 `ServerDb` 一处错位的文档注释（`CreateJobAsync` 的 summary 误置于 `FindDeviceByIpAsync` 上）。
## 缺陷 #62 无字段模板打印测试修复（数据与打印页静态标签可打印） · 2026-09-11

- **无字段模板（静态标签）可在「数据与打印」页打印测试（AC-01 / AC-02）**：模板无可填充字段（`contract.fields` 空且版式无字段填充元素，即 `fieldKeys` 为空）时，测试数据面板此前整体只渲染一条「不允许」语义提示（操作区不渲染），且 `testPrint()` 内还有同条件拦截——静态标签（固定文本 / 条码 / 二维码 / 线条 / 图片）完全无法打印测试。修复（纯前端，`web/src/pages/DataPrint.tsx`）：无字段时操作区照常渲染（调试模式复选框 / 「打印测试（单张）」/「出图预览」），提示改为说明性语义——静态标签按版式原样打印、无需填写数据；提交等价空数据 `labels: [{ data: {} }]`（模板包遗留 `testData` 键不外带）；移除 `testPrint()` 的无字段拦截守卫（`!pkg` 空守卫保留）。后端受理（空 `data` 兜底）/ 校验（空 `fields` 直接通过）/ 渲染（literal 全容错）三层本就支持，本轮零后端改动、公共契约零变更。
- **「下载 Excel 模板」维持禁用（AC-01）**：无字段无列可生成，按钮禁用与 tooltip 均不变。
- **测试（AC-02 / AC-03 / AC-04 / AC-05）**：DataPrint 双模式用例更新 + 新增——client：旧「无字段模板」断言随文案同步更新（含 testData 残留键不渲染输入框的守门），新增静态标签场景（操作区渲染可点与说明提示、打印测试提交 `labels: [{ data: {} }]` 且进度面板正常、调试开出图 / 调试关出图预览均空数据不建作业、Excel 模板仍禁用且 tooltip 不变）；server：无字段模板提交单张空数据到所选在线设备（双 base 守门）、出图预览空数据 render-image；有字段模板既有用例全数保留（回归不变）。
- **界面验收滞后（AC-06）**：真实界面打印测试走查（沙箱或真机）留待验收阶段补证，Issue #62 转 `待验收`。

## 缺陷 #58 托盘菜单「退出」不生效修复（统一退出路径 + 幽灵图标清理） · 2026-09-11

- **退出路径统一为「优雅停止 + 限时兜底强退」（决策 #112，AC-01 / AC-02 / AC-04）**：托盘菜单「退出」与 `/api/host/shutdown`（Web UI 设置页「退出程序」）此前一弱一强——托盘回调只调 `StopApplication()`，而 `RunAsync` 在托盘 / 界面消息循环在场时不自然返回（Issue #58 证据链：本机 2/2 复现进程僵死 >2 分钟，主线程停在 Main 的阻塞等待，`finally` 中的 `Environment.Exit` 永不执行）。修复 = 新增 `HostExitCoordinator` 统一入口：`StopApplication` → 有限等待（默认 2.5 秒）主流程自然退出 → 超时执行清理后 `Environment.Exit(0)` 强退兜底；两条入口共用同一序列（HTTP 路径保留 200ms 响应送达缓冲），不再有强弱退差异。
- **强退前显式托盘清理，消除幽灵图标（AC-03）**：托盘退出清理抽为 `TrayQuitSignaler`——向托盘消息循环线程投递 `WM_QUIT` 并限时 `Join`（默认 2 秒），消息循环退出后在托盘线程内执行 `Shell_NotifyIcon(NIM_DELETE)`（此前两条退出路径都到不了 `using` Dispose——托盘线程收不到 WM_QUIT，图标残留，本机已见幽灵 tooltip 累积）；自然退出路径与强退兜底共用同一清理（`uiShell?.Dispose()` 一并纳入），HTTP 退出路径同享收益。
- **退出链路阶段记账（AC-02）**：host.log 可定位「点了退出没反应」的具体阶段——收到退出请求（来源：托盘菜单 / HTTP）、StopApplication 发起（含异常续走兜底）、优雅等待超时与否（含超时秒数）、清理执行 / 完成 / 异常、最终退出方式（自然完成 / 强制 Environment.Exit(0)）。
- **测试（AC-05）**：新增 13 项——`HostExitCoordinator`（自然退出看门狗收手且不重复清理 / 超时强退 + 清理全序列记账 / 幂等重复请求 / 未绑定生命周期仍兜底 / StopApplication 异常仍强退 / 清理幂等 / 清理异常不阻断）+ `TrayQuitSignaler`（WM_QUIT 投递到登记线程 / 未登记不投递 / 幂等 / Dispose 默认路径 / Join 限时）+ `TrayIconService` 真实消息循环退出回收（Start → Dispose → 循环线程限时结束）。
- **真机验收滞后（AC-01 / AC-03）**：全局单实例互斥锁与端口 53960 归用户真机验收（Issue 复现步骤即恢复条件），本轮自动化验证到单测层；Issue #58 转 `待验收`——托盘「退出」≤5 秒进程退出、退出后图标即失不残留。

## 迭代 56 双端复用 Zebra 官方 SDK 5.0.3685（AndroidHost 接入 + WinHost 升级统一） · 2026-09-11

- **AndroidHost 接入官方 SDK（决策 #111，AC-01）**：打印传输从自研裸 socket `Tcp9100PrintTransport` 切换到 Zebra 官方 Link-OS SDK `5.0.3685`（net10.0-android36.0 纯托管 DLL，无 .so / 无 jar）——新 `ZebraSdkTransport` 实现 `IPrintTransport` / `IPrinterStatusProvider` / `ITestableTransport` 三接口（对齐 Core 传输契约，公共契约零变更）。连接类型扩展：`tcp`（**默认且一级交付路径**，SDK `TcpConnection`）/ `bluetooth`（SPP `BluetoothConnection`，MAC 地址手输——预授权决议 1）/ `usb`（OTG `UsbDiscoverer` 自动发现锁定第一台 Zebra 设备，无可选设备给可行动中文错误——预授权决议 2）；每次发送 / 测试 / 状态独立建连，与裸 socket 时代行为一致。**存量 `tcp_host` / `tcp_port` 配置键沿用，已装设备升级后零操作直入 SDK TCP 路径（AC-03 无感迁移）**。
- **PDA 状态 / 连接测试切 SDK `PrinterStatus` 语义（决策 #111 ②，预授权决议 3——不保留 `~HS` 双轨）**：`ZebraPrinterFactory.GetInstance(connection).GetCurrentStatus()` 官方布尔属性（`isPaperOut` / `isPaused`）；缺纸 / 暂停口径与迭代 55（#109）实证结论对齐，Message 最小口径只报缺纸与暂停，不回退其 AC。
- **WinHost 升级 3.0.3355 → 5.0.3685（决策 #111 ③，AC-08）**：`GetInstance` / `GetCurrentStatus` / `UsbDiscoverer.GetZebraUsbPrinters` 在 5.x 均保留，`ZebraPrinterTransport` 适配面极小；顺带把连接测试失败消息补上打印目标（对齐 #108 不泛化）。四模式（log / tcp9100 / winspool / zebra）行为回归由既有测试套件锚定；Core 的 `Tcp9100PrintTransport` 保留为跨平台兜底（Linux 客户端现状不变）。
- **毒丸引用清单入库（决策 #111 ④⑤，AC-01 / AC-02）**：官方支持矩阵「非 MAUI .NET 10 → Android ✗」的实测成因是依赖链毒化，全部 `ExcludeAssets="all"` 逐包排除（ExcludeAssets 不裁剪传递闭包）。**Android 实证版**：`System.Drawing.Common`（net10.0 lib 夹带 `System.Private.Windows.Core.dll`，Android AOT 失败）+ `SkiaSharp.Views.Maui.Controls` / `SkiaSharp.Views` / `SkiaSharp.Views.Maui.Core` + `Microsoft.Maui.*` 全家（含 `.Controls.Build.Tasks` 注入 MAUI AOT profile）；保留 `SkiaSharp` + `SkiaSharp.NativeAssets.Android`。**Windows 本轮验证版**：MAUI 链同 Android 清单**另加** `SkiaSharp.Views.WinUI` / `SkiaSharp.NativeAssets.WinUI` / `Microsoft.Maui.Graphics.Win2D.WinUI.Desktop` / `.Resizetizer` / `Microsoft.Graphics.Win2D` / `Microsoft.WindowsAppSDK` / `Microsoft.Windows.SDK.BuildTools`（后两者 buildTransitive 注入 win10-* RID 图触发 NETSDK1083；不排除则 MAUI 程序集混入编译——WinForms `Label` 二义、与 WebView2 投影类型冲突）；`System.Drawing.Common` Windows 可用不排除。
- **APK 产物收敛（决策 #111 ⑦，AC-02）**：`RuntimeIdentifiers` 收敛为单 `android-arm64`（现役 PDA 均为 arm64）——多 ABI 下 `libSkiaSharp.so` 与 SDK 依赖按 ABI 复制使 APK 达 53MB 超量级，单 arm64 后 **21.7MB < 现状 24.8MB**（对齐预研 19MB 量级）；构建级断言通过——APK 内无 `System.Private.Windows.Core.dll` / MAUI 产物（grep 0 命中）、`libSkiaSharp.so` ELF 全部 `PT_LOAD` 段 p_align=16384（16KB 对齐）。
- **SkiaSharp 原生 pdb 发布清理（Windows，决策 #111 ⑥）**：`SkiaSharp.NativeAssets.Win32` 的 runtimes 资产附带 81MB `libSkiaSharp.pdb`（非编译产物，`-p:DebugType=None` 不影响包内文件），WinHost csproj 加 Publish 后 Delete 目标，发布产物回到基线量级（win-x64 framework-dependent 55MB / 52 文件，MAUI 0 混入）。
- **配置页与权限（决策 #95 结构不变）**：打印机子页连接方式三选一（网线 / 蓝牙 / USB 数据线），参数随类型切换，配置存储 `{ brand, connectionType, ... }` 增量 `bluetooth_mac` 键；本地 HTTP `GET/POST /api/host/config` 增 `bluetoothMac` / 只读 `printerAddress` 字段（向后兼容增量）；AndroidManifest 蓝牙权限（API 31+ `BLUETOOTH_CONNECT` 运行时请求——选蓝牙保存时触发，legacy `BLUETOOTH` / `BLUETOOTH_ADMIN` 限 maxSdk 30）；USB 无需静态权限，首次连接由 SDK 触发系统授权弹窗。
- **许可证分发依据（决策 #111 ⑧）**：Development Tool License §3.3.3 允许以目标码随 Licensee Application 集成分发、禁止单独分发 SDK 本体——SDK 包本体不进任何 GitHub Release 附件。
- **顺带修复**：master 上 CHANGELOG.md 存在迭代 54 squash 合并引入的 Git 冲突残块（`=======` / `>>>>>>>` 重复块，c686342），本轮清理。
- **真机验收滞后（AC-04～07 及 AC-03 / AC-08 真机部分）**：SDK 三连接类型物理出纸与状态语义取证转 `待验收`（Issue #49，恢复条件：蓝牙 / USB 打印机与 192.168.2.121:9100 可得）；本地交付模拟级验证全绿。
- **测试**：新增 WinHost Zebra 传输 2 项（连接测试失败消息含目标 / 状态查询不可达时离线含原因，锚定 5.x 升级行为口径）；既有 Zebra 传输 3 项与传输配置 / 状态矩阵随升级回归通过。

## 迭代 54 区域水平锚定修正（自动宽度文本对齐语义） · 2026-09-11

- **区域水平锚定按实测文本宽度计算（决策 #110，AC-01，三项待决议按 Issue #44 评论用户确认——方案 A / 度量经字宽度量接口注入 / 兼容护栏全部生效）**：文本无显式 `WidthMm` 且锚定区域时，`LabelLayoutResolver.ResolveBounds` 的水平锚定偏移由「（区域宽 − 块宽）× 因子」（块宽被扩为区域全宽 → 偏移恒 ≈ 0，迭代 32 测试发现的缺陷）修正为按**实测文本宽度**计算——锚定宽度 = 实测单行宽度 + 2×有效水平内边距（夹取到区域宽），区域内 Start / Center / End 三种对齐可区分且符合直觉；**块宽仍为区域全宽（减单值内边距），块内 TextAlign / 边框 / 裁剪 / 缩小适应语义不变**（渲染器块内排版逻辑零改动）。三端一致：Web 预览与 ZPL 图片打印（Skia 渲染器）、PDA（Android 渲染器）同一解析结果驱动。
- **字宽度量接口 `ITextWidthMeasurer`（Core.Layout，毫米口径、与渲染 DPI 无关）**：解析器保持纯几何层、不直接依赖 Skia；默认实现 `SkiaTextWidthMeasurer`（字型查找与渲染器同源——抽取 `SkiaTypefaceLookup` 共享（含中文回退），Embolden 与加粗渲染一致），AndroidHost 提供 `AndroidTextWidthMeasurer`（Android.Graphics Paint 同一文本管线）。设计意图：解析几何（含锚定偏移）与渲染方式解耦，未来命令直发打印插件可提供打印机字体度量的接口实现复用同一解析几何（供后续插件模块重构迭代承接）。
- **兼容护栏全部生效（用户拍板接受渲染漂移）**：含 `RegionHAlign` 缺省 Center 的已发布模板——无显式宽度的已锚定文本位置随锚定语义修正而变化（此前水平锚定实际不生效，文本恒贴近块内对齐位置）；垂直锚定（按字高）与显式宽度路径零变化；未注入度量器的纯几何调用方回退既有行为（API 向后兼容）。
- **DESIGN 记账**：决策表新增 #110；「风险与未决问题」迭代 32 遗留的区域锚定条目清账移除。
- **测试**：新增 Core 解析器用例 8 项（自动宽度水平锚定实测宽度计算 / 锚定宽度含水平内边距 / 超宽夹取 / 3×3 水平垂直组合矩阵 / 无度量器回退 / 显式宽度公式不变 / 未锚定绝对坐标 / 非文本元素自然尺寸锚定）+ Skia 渲染端改写 1 项、新增 2 项（自动宽度三种水平锚定墨迹重心递增与文本不越区域、显式宽度块内 TextAlign 防回归）。
- **并行汇合**：与迭代 55（#45）双 worktree 并行开发，代码无重叠；本迭代后合并——DESIGN 决策号让位重排（#109 归迭代 55，本迭代记 #110），CHANGELOG 按合并时间序重排。

## 迭代 55 Zebra 状态映射修正（~HS 官方字段表 + SDK 细状态） · 2026-09-11

- **TCP 9100 `~HS` 解析按官方字段表修正（决策 #109）**：按 Zebra 官方 ZPL 指南 ~HS 字段表修正解析——响应为三行逗号分隔字段（每行 `<STX>…<ETX><CR><LF>`），**缺纸 = 首行第 2 字段、暂停 = 首行第 3 字段**；修正迭代 6 的盲写映射（原「第 2 字段=暂停、第 5 字段=缺纸」两处皆错：真缺纸会被误读为暂停、接收缓冲格式数会被误读为缺纸）；解析剥离 STX / ETX / CR 控制字符并提取为纯函数（`Tcp9100PrintTransport.ParseHostStatus`），PDA 与 WinHost 共用 Core 的 Tcp9100 解析，一处修复两端生效。
- **「无响应 / 超时 / 响应不完整」一等异常态（决策 #109）**：官方声明 MEDIA OUT / RIBBON OUT / HEAD OPEN / 回卷器满 / 打印头过热等状态下打印机可能完全不响应 `~HS`——状态查询超时、空响应、首行字段不足（截断）、标志位非 0/1 一律返回异常态（`IsOnline=false` + 中文原因，含目标地址与超时秒数），**绝不映射为「正常 / 在线」**（原实现超时与空响应均返回 `IsOnline=true`，缺纸场景可能被误报为正常）。
- **WinHost Zebra SDK 路径细状态（决策 #109）**：`GetStatusCore` 改用官方 `ZebraPrinterFactory.GetInstance(connection).GetCurrentStatus()`（`isPaperOut` / `isPaused` 等官方布尔属性，字段映射由 SDK 维护），消除「细状态恒 false + 待真实设备联调」欠账；连接 / 状态读取失败返回 `IsOnline=false` + 含目标与原因的中文消息（对齐决策 #108 不泛化）。**依赖调查结论**：既有 `Zebra.Printer.SDK` 3.0.3355 已具备 `GetCurrentStatus` 与全套 `PrinterStatus` 布尔属性（经 NuGet 包 XML 文档核对 + windows TFM 编译验证，与 5.0.3685 同构），**无需升级**——原代码注释「3.x 无公开状态字段」判断有误；winspool 驱动路径维持「默认在线」（不在范围）。Message 最小口径只报缺纸 / 暂停，`PrinterStatusInfo` 契约不动。
- **测试**：模拟报文单测矩阵 15 项替换原按旧映射构造的用例——解析级（官方三行报文四象限标志位 / 无控制字符单行 / 截断 / 非 0/1 标志 / 空响应两态 / null 入参）+ 端到端 socket（正常 / 缺纸 / 暂停 / 零字节关闭 / 静默超时）。
- **真机取证不在本轮执行（安全边界）**：三场景（正常 / 人工缺纸 / 人工暂停，打印机 192.168.2.121:9100）留待验收阶段用户配合取证（`~HS` 响应原文留存），Issue #45 转 `待验收`；AC-01~04 恢复条件见 Issue 自评表。

## 迭代 52 日志基础设施加固（轮转 / 启动防护 / 业务事件） · 2026-09-11

- **FileLoggerProvider 启动防护（决策 #108，AC-01）**：`LABELFRAME_SERVER_LOG_FILE` 指向无效路径（目录不存在且不可创建 / 磁盘不可用）不再启动即崩——构造不抛异常、跳过文件通道、控制台输出中文告警（含配置项名、失败原因与降级事实），宿主正常启动；运行中防护：每日轮转开新文件失败继续写旧文件（下次写入再试），实际写入失败自我禁用通道并告警一次，`Log` 永不抛出（不打断请求路径）。
- **轮转与保留统一口径「按日 + 默认保留 31 个」（AC-02，三项待决议按 Issue #35 评论用户确认）**：服务端 `server.log` 与客户端 `host.log` 改**按日轮转**——实际文件 `<名>-<yyyyMMdd>.log`（`LABELFRAME_SERVER_LOG_FILE` / `LABELFRAME_HOST_LOG` 仍是基准路径；历史单名文件不迁移不删除），保留上限默认 31、`LABELFRAME_SERVER_LOG_FILE_RETENTION_DAYS` / `LABELFRAME_HOST_LOG_RETENTION_DAYS` 可调（0 或负值 = 不清理；超期清理在启动与每日轮转时执行）；客户端 Serilog `app-*.log` 显式接线 `retainedFileCountLimit`（`LABELFRAME_APP_LOG_RETENTION_DAYS`，≤0 = 不清理；Serilog 包默认本为 31，此前未显式声明未开放配置）。客户端 MSI 卸载「清理用户数据」同步覆盖 `host-*.log` 轮转文件。
- **服务端业务事件日志（AC-03）**：作业创建（requestId → jobId / 目标设备 / 标签数）、设备认领、回报终态三条 **INFO**（`[LoggerMessage]` 源生成、作业粒度——批量作业不逐张一行、幂等重放不重复记录），正向生命周期可在 server.log 追溯（对照客户端双 ID 行）；默认 INFO，经标准 `Logging:LogLevel` 降级（`Logging__LogLevel__LabelFrame.Server.ServerService=Warning`，沿用决策 #102「不新造轮子」）。
- **杂项吞错留痕（AC-04 / AC-05）**：connection.json 读取 / 解析失败回退默认连接时写 host.log（含原因与回退目标——此前静默回退，用户打印机配置「消失」无痕迹）；TCP 连接测试失败原因透传——`TestAsync` 返回具体原因（连接被拒（SocketError）/ 3 秒超时 / 已连上但 ~HS 无响应等，含目标地址），连接测试结果消息不再泛化失败（迭代 50 的 `LF_TRANSPORT_TEST_FAILED` 测试页链路不变）；无界面壳（`LABELFRAME_OPEN_BROWSER=0` 且 `LABELFRAME_TRAY=0`）启动失败提示同时写控制台 / stderr（此前只入 host.log）。
- **范围外登记**：跨组件关联 ID（TraceId / Activity）按 Issue「不在范围」登记 DESIGN「风险与未决问题」（P2-9），不实现。
- **测试**：新增 15 项——Server 7（FileLoggerProvider：无效路径不抛异常 / 按日文件写入 / 启动清理超期 / 非日期文件不动 / 停用后写入不抛异常；集成：无效日志路径宿主正常启动 /healthz、全流程三条业务行落 server-<yyyyMMdd>.log（含幂等不重复））+ WinHost 8（DailyRotatingFileWriter：按日文件 / 清理超期 / 非日期文件不动 / 停用后写入不抛异常；connection.json 损坏与独占锁定回退留痕 2 项；TCP 测试失败原因具体化 2 项）。`dotnet build` / `dotnet test`（排除 Perf/Soak，421 项）通过；轮转跨天与长期清理行为属长测，建议随部署观察。

## 迭代 50 错误响应分类修正 · 2026-09-11

- **请求体反序列化失败分类（决策 #107）**：共享层 `AddLabelFrameExceptionHandler` 固定开启 `RouteHandlerOptions.ThrowOnBadRequest`（框架默认仅 Development 开启，Production 下参数绑定失败被短路为「400 空 body」，无法给出统一 ErrorView）——非法 JSON / 非 UTF-8 / 类型不匹配等绑定失败统一交给共享 `GlobalExceptionHandler` 分类为 **400 + `LF_API_BAD_BODY` + 中文可行动消息**（新增错误码入 `ApiErrorCodes`），不再落入 500 兜底误导业务方排查服务端；原始解析异常（含行 / 列位置）记 Warning 日志供排障，不透出客户端。Server 与 WinHost 双宿主经共享层自动一致。
- **测试页传输错误分类**：`POST /api/printer/test` 发送失败（打印机连接不可达 / 超时等传输故障）由裸 500 改为 **400 + 新增错误码 `LF_TRANSPORT_TEST_FAILED`**，消息含打印目标地址（host:port / 打印机名 / USB 名）与失败原因（口径对齐 `/api/printer/status` 降级信息）；客户端经 ILogger 留痕（目标 / 插件 / 原因）。
- **403 补 ErrorView**：result / progress 端点非归属设备回报（`NotJobOwner` / LF_SRV_004）由**空 body 403** 改为 403 + ErrorView——全系统错误响应契约统一为 `{ code, message }`，前端不再显示裸 `HTTP_403`。
- **`LF_INTERNAL_001` 常量化**：入 `ApiErrorCodes.InternalError`，全仓仅注册表一处字面量，其余引用常量。
- **插件包无效消息中文化（待决议三项按 Issue #33 评论用户确认）**：Core 业务性校验失败统一抛新增的 `PluginPackageException`（消息全中文可行动），四类高频——**非 zip / zip 损坏 / manifest 缺失或非法 / DLL 无效**（`PluginProbe` 增逐 DLL 失败原因区分 BadImageFormatException）——给具体中文消息；其余框架 `InvalidDataException` 在 API 边界（WinHost 插件安装 / 卸载、Server 插件包上传）转通用中文提示，不再直出英文原话（如 `End of Central Directory record could not be found.`）。
- **测试**：`LabelFrame.Api.Tests` 新增共享处理器分类用例（语法错误 / 非 UTF-8 / 类型不匹配 → 400 LF_API_BAD_BODY；未分类异常 → 500 常量码）；`LabelFrame.Server.Tests` 新增错误契约端点用例（绑定失败三类 + result/progress 403 ErrorView + 非 zip 上传中文消息）；`LabelFrame.WinHost.Tests` 新增绑定失败用例、测试页传输失败（connection.json 指向已关闭回环端口 → 400 LF_TRANSPORT_TEST_FAILED + 日志留痕断言）、Log 传输测试页成功路径回归、插件四类中文消息用例；既有插件包用例断言随异常类型同步更新。Debug / Release 全量 `dotnet test`（排除 Perf/Soak，403 项）与 `pnpm lint / test`（260 项）通过。
## 迭代 51 客户端设备日志链路与前端可观测 · 2026-09-11

- **WinHost 补挂设备日志端点（决策 #106，AC-01）**：审计发现 WinHost 装配了 `SqliteLogStore` 却从未调用共享 `MapLogApi`——客户端实例 `GET/POST /api/logs` 命中 SPA 回退返回 `index.html`（HTTP 200 + HTML）。修复 = `WinHostApp` 补挂共享端点（错误码 `LF_API_001`，与宿主其他共享端点一致），客户端实例返回 JSON。补挂后客户端 53960 开放日志写入接口，与决策 #79 局域网信任模型的关系已在 DESIGN 决策 #106 记录（无新增攻击面：Windows 客户端默认仅回环监听，Linux 容器与 Server 同暴露面）。
- **设备日志按行存储（决策 #106，AC-04，共享契约先文档后代码）**：`SqliteLogStore.AppendAsync` 按物理行拆分入库——多行提交（`lines` 多元素或元素内含 `\r\n` / `\n` / `\r`）拆为独立记录（每行一条、同一提交共用时间戳、单事务原子写入、空白行不落库），与前端按行展示对齐；Server 与 WinHost 同源共享实现，一处修复两端生效。既有 logs.db 历史「合并行」数据**不迁移**（用户确认：旧记录仍为含换行符的单条 line，`pre-wrap` 原样多行呈现）。
- **日志页失败显错（AC-02）**：查询失败（非 2xx / 解析失败——如旧版客户端 SPA 回退返回 HTML 的 200）显示明确错误条（复用现有错误横幅样式），解析失败新增数组形状校验（`响应格式异常` 文案），不再静默「暂无日志」；失败后自动刷新保持（5s 继续重试，恢复后错误条消失）。
- **前端全局错误兜底（AC-03）**：`ErrorBoundary`（错误页**仅文案 + 重新加载按钮**，不附错误摘要——用户确认，避免透出内部细节）挂应用根（`main.tsx` 最外层）；`window.onerror` / `unhandledrejection` 统一 `console.error` 留痕（开发线索，不做后端上报）。
- **测试**：新增 / 改写 13 项——WinHost 集成 1（`/api/logs` JSON 响应 + 两行两条独立记录 + 缺参 400 / LF_API_001）+ `SqliteLogStore` 3（多行提交拆为两条 / 元素内换行拆分 / 空白行跳过与全空提交不落库）+ 前端 9（日志页 4：非 2xx 显错 / 解析失败显错 / 成功按行渲染 / 失败后 5s 自动重试恢复；ErrorBoundary 3：错误页 + console.error 留痕不透出细节 / 点击重新加载 / 正常子树透传；全局监听 2：window.onerror / unhandledrejection 各自留痕）。

## 迭代 53 PDA 可观测性（logcat + 崩溃捕获 + 本地滚动日志） · 2026-09-11

- **logcat 日志封装（决策 #105）**：`HostLog` 轻量静态门面（Info / Warn / Error，tag 前缀 `LabelFrame.` + 区域名 Host / Http / Print / Server / Ui / Crash），零新依赖、写失败静默。关键路径埋点：本地 HTTP 请求失败（method / path / 异常消息 + 完整堆栈）、打印循环发送失败（作业 / 项 / 打印机目标 / 原因）与领取失败、Server 轮询与回报失败（目标地址 / 原因）、前台服务生命周期（启动一行含版本 / 设备 / 服务器 / 打印机配置、停止、开机自启广播）、配置页操作失败。
- **本地滚动日志（用户确认的新增交付，原 Issue 默认仅 adb 可查）**：与 logcat 同一封装同步落应用私有目录 `{FilesDir}/logs/host-<yyyyMMdd>-<NNN>.log`；单文件 512KB 上限滚动到下一序号，目录保留最近 6 个（名字序即时间序）——现场无法 adb 时可直接取证。
- **全局崩溃捕获**：Java 层 `SetDefaultUncaughtExceptionHandler`（记录后交回原处理器，保持系统崩溃流程）+ .NET `AppDomain.UnhandledException` 双通道 → logcat Error 完整堆栈（长堆栈分片）+ 崩溃摘要 `{FilesDir}/crash/crash-<时间戳>.txt`（时间 / 来源 / 版本 / 系统 / 设备 / 堆栈，保留最近 3 份）；注册时机 = 新增 `[Application]` 子类 `HostApplication` 进程创建首行（早于一切组件）+ 服务 OnCreate 首行幂等兜底；服务启动检测到上次崩溃摘要记录 Warn 提示（即「下次启动可读」）。崩溃摘要**不回传服务端**——回传管道登记 DESIGN「风险与未决问题」。
- **请求级吞错可见化（语义保持）**：`EmbeddedHttpServer` 每请求 catch 仍吞掉异常（单请求失败不影响服务），只加日志——请求行已解析出的处理期失败记 Error（含 method / path），对端断开 / 畸形请求（未解析出请求行）记 Warn 防噪；`/api/printer/test` 5xx 自身原因补 Error。
- **进度上报失败保持静默**（决策 #101「不告警刷屏」），仅 Server 轮询主循环与终态回报失败记周期 Warn。
- **测试**：AndroidHost 无测试体系（不在范围），AC 以 CI「Android 构建（PDA 宿主）」+ 真机验收为证据；本轮 `dotnet build / test`（排除 Perf/Soak，384 项）全绿（slnx 无涉改动），AndroidHost Release 本地构建通过（0 警告 0 错误）。

## 迭代 49 PDA 宿主自动化构建与品牌化 · 2026-09-10

- **CI 自动化（决策 #104，含流程治理：修改 workflows）**：`ci.yml` 新增「Android 构建（PDA 宿主）」job（ubuntu；JDK 17 + Android SDK 36 + .NET Android workload；Release 配置 + `-p:EmbedAssembliesIntoApk=true`），用户拍板**直接设为第三项必需检查**——master 门禁由双必需升三必需（既有两项名称与语义不变；落地顺序 = 先合入 job 的 PR 验证全绿、合并后立即补进门禁 ruleset）；AndroidHost 仍不进 `LabelFrame.slnx`（CI 单独构建该工程，不拖慢日常全仓构建）。
- **随发版出 APK**：`release.yml` 随 `v*` tag 构建 Release APK 上传 GitHub Release 附件（`LabelFrame-AndroidHost-<版本>.apk`；`ApplicationDisplayVersion` / versionCode 由 tag 注入，csproj 默认 `1.0`/`1` 兜底）。
- **签名（三项待决议用户拍板：自签 keystore + Secrets）**：CI 发版检测到 `ANDROID_KEYSTORE_BASE64` 等 4 个 Secrets 时用专用自签 keystore 签名，缺失回退 debug 签名并告警（对齐 MSI 自签过渡决策 #65 思路）；`scripts/create-android-keystore.ps1` 一次性生成并可选写入 Secrets。**换签名影响（README 已写明）**：已装真机（DT50）从 debug 签名包升级到正式签名包需**卸载重装**——配置清空需重填，且 Android 8+ 设备号（ANDROID_ID）绑定签名密钥、**设备号会变**（Server 设备目录出现新条目、旧条目停留离线）。
- **应用图标（品牌化）**：沿用 L 型品牌体系（主蓝 #1668DC + 白 L，与 MSI / 桌面图标同源）——`generate-android-icons.ps1` 生成各密度启动器位图（方形圆角 / 圆形），API 26+ 自适应图标（矢量前景 + 主蓝底），常驻通知小图标换为白色单色 L 矢量（替换系统默认机器人图标）；Manifest 补 `icon` / `roundIcon`。
- **版本信息（待决议拍板：配置页 + 本地 HTTP）**：配置页「本机信息」子页新增「程序版本」（只读）；本地 HTTP `GET /healthz` 与 `GET /api/host/config` 新增只读 `version` 字段（宿主本地契约向后兼容增量，DESIGN §5.3 已先行更新）。
- **脚本**：`build-androidhost.ps1` 支持 `-Configuration Release` 与 `-Version`（对齐 CI 参数）；`generate-android-icons.ps1`、`create-android-keystore.ps1` 新增。
- **测试**：AndroidHost 无测试体系（不在范围），AC 以构建产物与真机验收为证据；本轮 `dotnet build / test`（排除 Perf/Soak）全绿（slnx 无涉改动），AndroidHost Release 本地构建通过（含图标资源编译）。

## v0.25.0 迭代 48 发布 · 2026-09-10

- **打包范围**：工作台信息架构重构 + 作业历史自动轮询（迭代 48，决策 #103）。详见迭代条目。
- **客户端可感知变更**：工作台模板列表由表格升级为**卡片网格**（缩略图主视觉，自适应多列；搜索 / 分组过滤 / 缩略图灯箱放大 / 双击编辑 / 新建 / 导入 / 导出 / 删除等既有能力全部保留）；作业历史页**存在进行中作业时 1.5s 自动刷新**（列表全终态即停、页面隐藏暂停、恢复立即拉取），配合 v0.24.0 的进度增量上报可实时观察打印进度。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.25.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.25.0` / `labelframe-client:0.25.0`（均含 `latest`）。AndroidHost（PDA）不随 Release 分发，需本地构建安装。

## 迭代 48 工作台信息架构重构 + 作业历史自动轮询 · 2026-09-10

- **工作台信息架构重构（决策 #103，用户定稿方案 A：卡片网格）**：模板列表由表格形态收敛为**自适应卡片网格**（缩略图主视觉，`minmax(196px, 1fr)` 多列自排）——卡片 = 大号缩略图（点击居中灯箱放大，迭代 46 机制复用）+ 名称 / 分组徽标 / 更新日期 + 编辑 / 导出 / 删除操作行，双击卡片进入设计器；既有能力全部保留：模板名搜索、分组过滤（叠加生效）、新建 / 导入 .lfpkg / 导出 / 删除确认、缩略图会话缓存随列表刷新失效。「卡片网格 vs 列表+详情面板」双形态先在本机沙箱（mock 数据 12 模板）实现并截图征求用户定稿，未采纳形态随收敛移除。
- **作业历史自动轮询（决策 #103，用户定稿建议组合）**：作业历史页在**存在进行中（非终态）作业时 1.5s 自动轮询**（对齐「数据与打印」useJobPolling 节奏），列表全终态即停（新提交作业需手动刷新）；**页面隐藏时暂停**、恢复可见立即拉取一次；轮询失败保留既有列表（瞬时错误只出横幅、不清空表格）并 2s 退避重试；手动「刷新」保留。API 跟随服务端 / 单机降级双模式，deviceFilter（客户端只看自己作业）语义不变；配合迭代 47 进度增量上报，停留页面即可见 completedItems 逐步增长。
- **测试**：前端新增 3 项（1.5s 轮询 + 全终态停止 / 页面隐藏暂停 + 恢复立即拉取 / 失败保留列表 + 2s 退避），既有工作台预览测试适配卡片形态（表格行定位 → 卡片定位）；前端 `pnpm lint / test`（260 项）与双构建通过；日常 `dotnet test` 384 项全绿（排除 Perf / Soak，无后端改动）。

## v0.24.0 迭代 40-47 汇总发布 · 2026-09-10

- **打包范围**：PDA 宿主配置与测试体验（迭代 40，含本机 HTTP 配置端点）、PDA 配置界面文案与视觉优化（迭代 41）、服务端 Claimed 作业超时回收（迭代 43）、Windows 客户端窗口化（迭代 44）、批次首张节奏修正 + 设置页布局自适应 + 工作台小修（迭代 45）、工作台模板缩略图预览（迭代 46）、作业可观测性——进度增量上报 + 日志细化 + 缺陷 #14 修复（迭代 47）；迭代 42（工作流重构）与流程治理（worktree 并行）为流程 / 文档变更，不影响产物。详见各迭代条目。
- **服务端行为变更（部署注意）**：Claimed 作业超时回收默认开启（30 分钟；`Server.ClaimedJobTimeoutMinutes` / `LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES`，0 或负值 = 关闭）——宿主失联的领取作业超期终态化 Failed（`LF_SRV_009`，文案明示「结果未知，可能已实际打印」，禁止自动重投）；长挂起场景（如打印机离线挂起数小时）可调大或关闭规避误判。
- **跨端公共契约新增（决策 #101，向后兼容）**：宿主→Server 新增 `POST /api/devices/{deviceId}/jobs/{jobId}/progress` 进度增量上报端点；旧版宿主不调用行为不变，新版宿主对旧版 Server 的 progress 上报收到 404 时静默降级、不影响打印主链路。**进度逐张增长需 Server 与宿主（客户端 / PDA）双侧均升级到本版本**。
- **客户端可感知变更**：客户端安装后为自有应用窗口（WebView2 嵌入、托盘常驻、单实例激活；MSI 自动引导安装 WebView2 运行时）；批次作业每个作业首张不再等待批间间隔（决策 #100，作业内节流语义不变）；日志级别可配置（`LABELFRAME_LOG_LEVEL` / `WinHost:LogLevel`，默认 Information）；修复 Serilog 逐张日志不再落盘缺陷（#14，`app-*.log` 恢复写入）。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.24.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.24.0` / `labelframe-client:0.24.0`（均含 `latest`）。AndroidHost（PDA）不随 Release 分发，需本地构建安装。

## 迭代 47 作业可观测性（进度增量上报 + 日志细化 + 缺陷 #14 修复） · 2026-09-10

- **进度增量上报（决策 #101，跨端公共契约——强化路径）**：Server 新增 `POST /api/devices/{deviceId}/jobs/{jobId}/progress`——请求体 `{ completedItems, failedItems }`（无宿主时间戳，服务端时钟唯一权威）；仅 `Claimed` 接受，计数**按字段取 max 单调递增**（乱序 / 迟到 / 重复上报不回退）；只增不改终态（不写 status / finished_at、不刷新 claimed_at，与决策 #98 超时回收正交）；终态收到 progress 幂等 no-op 返回当前视图、`Pending` 409、错误语义与 result 端点完全一致；result 仍是唯一终态写入者（绝对值覆盖）。打印中查询 `GET /api/jobs/{jobId}` / 作业历史的计数逐步增长，不再终态一次跳变（本机联调实证：10 张节流作业轮询序列 Claimed 1→2→…→9→Completed 10）。
- **宿主接入（WinHost / Linux Client / AndroidHost 同迭代）**：进度上报放在既有回报循环（1s 周期）——**默认 1s 节流 + 计数有变化才发**（无变化零流量，`LABELFRAME_PROGRESS_INTERVAL_MS` 可调）；Server 不可达静默降级（失败不告警刷屏、不阻塞打印主链路，下一轮携带最新计数自然重试）；作业终态后停发、终态只走 result。WinHost 节流判定抽为 `ProgressThrottle`（TimeProvider 注入，FakeTimeProvider 确定性测试）；AndroidHost 同构内联实现。
- **缺陷 #14 修复（Serilog 逐张日志不再落盘）**：根因 = 迭代 32 装配抽取（0aca4dd）后 `UseSerilog` 留在 `Program.Main` 中只用于配置绑定、**从不 Build 的 builder** 上，文件日志配置自 2026-08-25 起即死代码（08-25～09-03 的 `app-*.log` 由更旧安装版写入，此后换装含缺陷构建即停写）。修复：配置收拢为 `SerilogSetup.Apply`，经 `WinHostApp.BuildAsync` 的 `configureBuilder` 扩展点挂到**真实 builder**；启用 SelfLog 落盘（同目录 `serilog-self.log`，sink 失败可见化）+ 启动自检事件；回归测试以生产同装配路径构建应用→写事件→断言 `app-*.log` 创建且含事件，结构性防止「配置挂在错误的 builder 上」复发。
- **日志细化（决策 #102）**：级别可配置——`LABELFRAME_LOG_LEVEL` 环境变量 / `WinHost:LogLevel` 配置节（默认 Information，启动读取生效，非法值回退并可用），Server 沿用标准 `Logging:LogLevel`；失败上下文结构化——发送失败日志在作业 / 项索引 / 原因之外补**传输插件 ID / 打印目标端点 / 错误码（LF_IO_001）**，经 Serilog 文件通道逐字段断言（测试锚定）。
- **测试**：新增 19 项——Server 服务级 3（max 单调 / 不改 claimed_at / 终态与错误语义）+ Server 端点级 2（视图增长 + result 唯一终态 / 409·404·403·400 错误对齐）+ WinHost 路由 3（FakeTimeProvider 节流时间轴 / 进度失败静默降级不阻塞终态回报 / poller progress 端点形状）+ `ProgressThrottle` 3 + Serilog 回归 3（文件创建含事件 / Warning 过滤 Information / 级别解析）+ 失败上下文日志 1，另有级别解析理论项内联；日常 `dotnet test` 384 项全绿（排除 Perf / Soak），前端 `pnpm lint / test` 通过（无前端改动）。

## 迭代 46 工作台模板缩略图预览（预览列内嵌出图） · 2026-09-10

- **预览列内嵌缩略图（验收修订：纯悬停触发不可发现，用户定稿 B 形态）**：工作台新增「预览」列，列表加载后按需调用既有 `POST /api/templates/{name}/preview`（按模板 TestData 渲染 PNG，Server 与 WinHost 双宿主均已实现）拉取全部模板预览，单元格内以 40px 高等比缩略图直出（所见即所得）；**点击缩略图居中灯箱放大**（二次验收修订：行旁 296px 小浮层 → 居中大图模态——图片按真实宽高比约束在视口 75% 且 ≤900px，203 DPI 原生约 560px、再往上为插值放大；Esc / 点背景 / 点 × 关闭，点卡片本体不关闭）。初版为行悬停 400ms 防抖触发的浮层（PR #19），真机验收时用户判定不可发现，当轮修订为预览列形态（验收教训：客户端托管 UI 的验收须先重建 web/dist；preview 请求须显式携带 JSON 头与空体——旧版 Server 对裸 POST 返回 404，PR #22）。前端新增 `previewTemplate(name)` blob 封装（与 renderImage 同构，serverApi / localApi 双 base 自动跟随业务模式——Client 与 Server UI 共享工作台组件，双端同时受益）。
- **会话内缓存与生命周期**：「模板名 → blob URL」会话内缓存，同一列表周期内每模板只请求一次（含在途去重与失败结果缓存，过滤切换 / 放大均不重复请求）；列表刷新（重新 load，如删除 / 导入后）缓存整体失效并释放全部 blob URL（防泄漏），周期代数防护使失效时仍在途的旧请求结果不写入新周期；组件卸载兜底释放。
- **加载 / 失败态**：拉取中单元格显示骨架占位（呼吸动画）；失败（模板已删除、旧宿主 404、网络不可达）显示警示占位（title 携带原因），不影响列表与其余功能；预览图透明底衬浅棋盘格（白底标签不「隐形」）。
- **测试**：前端测试 6 项（修订后）——列表加载即拉取全部（骨架 → 缩略图 blob URL）、单机降级走本机 API、过滤切换缓存命中不重复请求不重复建 blob URL、列表刷新失效（释放旧 URL 并对剩余模板重拉）、失败态占位与同周期失败缓存、点击放大不重复请求 + 遮罩 / Esc 关闭；日常 `dotnet test` 364 项全绿（排除 Perf / Soak），前端 `pnpm lint / test`（client / server 双模式）与双构建通过。

## 迭代 45 批次首张节奏修正 + 设置页布局自适应 + 工作台小修 · 2026-09-10

- **批次首张节奏修正（决策 #100，修正 #74 计数语义）**：WinHost 批间计数 `_sendsSinceBatch` 由「跨作业全局累计（仅重启清零）」改为**按作业重置**——打印循环领取到与上一次发送不同的作业时计数归零（记录 `_lastSentJobId`），**每个作业的首张立即发送、不再等待批间间隔**；作业内节流语义不变（发满 batchSize 整数倍后、下一张发送前暂停 batchIntervalMs，claim-then-delay 结构不动）。根因：原累计语义使任何历史发送都会让下一作业首张触发暂停（batchSize=1 / 间隔 5s 时每个新作业首张前白等 5s）；`BatchPrintPolicy` 纯函数判定与守卫（已发送数 > 0）本就正确，变化的只是喂给它的计数口径。公共契约零变更（PrintSettingsDto / API / 模板包不动），仅 WinHost 打印循环；AndroidHost / PDA 如有同类行为另行评估。既有「跨作业累计」断言的集成测试改写为按作业重置语义（含「B 作业从 0 重新计数」的口径证明）；辅助时间轴通道不再做反向断言（假时间 500ms 步进在调度噪声下会产生假间隔，8 轮稳定性验证通过）。
- **设置页布局自适应（待决议定稿 B：多列网格）**：内容容器由固定 `maxWidth: 640` 左对齐窄列改为 `repeat(auto-fit, minmax(560px, 1fr))` 自适应网格——宽窗自动双列（1600px 下两列 747px：服务端地址｜连接方式 / 打印批次｜打印机 / 更新与安装包｜插件管理），窄窗自动回退单列（800px 下约 700px，无横向溢出、纵向可滚动）；迭代 44 客户端窗口化后窗口可自由拉伸，空间利用随宽度自适应。A/B 两方案在本机沙箱（mock 数据）实现并出宽 / 窄两档截图征求用户后定稿 B。
- **工作台小修**：① 模板名搜索框（页头、搜索图标内嵌）——子串匹配、大小写不敏感、首尾空白忽略，与分组下拉过滤叠加生效（交集），清空恢复完整列表；无匹配时显示专用空态。② 操作列视觉修正（宽窗下操作列右侧无白底空带）：根因是 `.table .actions` 的 `display: flex` 同时命中 `th`，使表头单元格退出表格列宽分配——1600px 宽窗下表头操作列只有 240px（浅白底提前截断）而表体操作列宽 750px，右侧 510px 露出灰底；修正为 flex 只作用于表体按钮容器（`td .actions`）、`th` 保持 table-cell 并文字右对齐，表头 / 表体列缘完全对齐（DOM 几何断言证据）。
- **测试**：新增前端测试 3 项（搜索大小写不敏感子串 / 搜索 + 分组叠加与双清空恢复 / 首尾空白忽略），改写 WinHost 节流集成测试 2 项（跨作业不暂停 + 新作业从 0 重新计数）；日常 `dotnet test` 365 项全绿（排除 Perf / Soak），前端 `pnpm lint / test / build` 通过（250 项）。

## 迭代 44 Windows 客户端窗口化（WebView2 嵌入界面壳） · 2026-09-09

- **界面壳（决策 #99，D1=A）**：Client 界面从「启动弹默认浏览器」升级为**自有应用窗口**——WinHost（仅 Windows 目标）新增窗口壳：独立 STA UI 线程 + WinForms 主窗体（标题 LabelFrame / 应用图标 / 1280×800 起步）+ WebView2 控件加载本地 UI；HTTP 未就绪先显示加载态、就绪后自动导航；前端零改动、`/api/*` 契约零变更、Linux 无头客户端不受影响。本机地址留在窗口内导航，外链（帮助 / 下载等）自动转系统浏览器（防误导航）；WebView2 用户数据目录 `%LOCALAPPDATA%\LabelFrame\webview2`。
- **关窗 / 托盘 / 单实例（D3 / D4）**：关闭窗口 = 隐藏到托盘、服务常驻（真正退出走托盘「退出」）；托盘双击 / 右键「打开界面」与**单实例二次启动**（互斥锁 + 激活事件）共用「显示并前置」路径，二次启动不再报端口占用；`OpenBrowser` 配置语义演进为「启动时显示界面」（默认开），`--autostart` 保持仅托盘不弹窗。
- **WebView2 运行时缺失处理（D2=C，对齐决策 #44 模式）**：MSI 检测 EdgeUpdate Clients 产品键（per-machine / per-user 双视图）——全 UI 显示带官方下载链接的中文对话框，**装完运行时点「重新检测」即可继续，无需重启安装程序**；静默 / 基础 UI 由 LaunchCondition 拦截提示；应用启动时初始化失败（如装后被卸载）自动回退默认浏览器 + host.log 记录原因，功能不受损。
- **启动失败可见化**：端口占用等监听失败以中文消息框提示用户（含 ListenUrl 修改指引），不再只写 host.log 无感知退出；退出路径确认 WebView2 子进程不残留。
- **MSI / 断言**：WebView2Loader.dll 等随发布产物自动进包（MSI 约 +1.5MB）；快捷方式与安装选项页文案去掉「浏览器」表述；MSI 结构断言新增 WebView2 契约（缺失对话框 + 序列先于欢迎页 + 双注册表视图 + LaunchCondition + 重新检测链路），并修复既有运行时序断言因 PowerShell 单行展平形成的空转（`[int]` 转换失败静默回退字符串比较）。
- **本机冒烟实证三个线程 / 布局坑（已固化代码注释）**：UI 线程须显式 `SetApartmentState(STA)`（.NET 默认 MTA → WebView2 报 RPC_E_CHANGED_MODE）；UI 线程须显式安装 WinForms 同步上下文、窗口经消息队列投递显示（泵启动前 Show 使 await 续体落线程池 → WebView2 报 UI-thread-only）；首显须重申 ClientSize（WebView2 初始化与布局竞态使窗口停在未展开尺寸 864×293）。
- **测试**：新增单元测试 13 项（UiUrl 规范化与导航边界 / UiReadiness 就绪等待五路径含探测异常与超时 / SingleInstanceGuard 抢占 / 激活信号 / 启动失败文案端口占用识别）；日常 `dotnet test` 364 项全绿（排除 Perf / Soak）。本机冒烟：窗口首显 1280×800 + UI 加载、关窗隐藏服务存活、二次启动激活、`--autostart` 无窗口、端口占用中文报错框、运行时不可用回退浏览器 + 日志、退出后 WebView2 子进程零残留。

## 迭代 43 Claimed 作业超时回收（宿主重启后终态化） · 2026-09-09

- **Server 侧超时回收（决策 #98，用户拍板）**：作业被设备领取（Claimed）后，宿主崩溃 / 重启会使「本地作业 ↔ Server 作业」内存映射丢失、终态永不回报，此前作业停留 Claimed 直至 30 天历史清理——进度查询失真、失败项不能重打。现按「领取时间 + 超时时长」回收为终态 **Failed**（原因 = 宿主失联超时，错误码 `LF_SRV_009`，文案明确「结果未知，可能已实际打印；需重打请用新 requestId 重发」）。
- **判定语义**：超时默认 **30 分钟**（`Server.ClaimedJobTimeoutMinutes` / `LABELFRAME_SERVER_CLAIMED_TIMEOUT_MINUTES`，0 或负值 = 关闭回收、沿用现状）；只以 `claimed_at` + 服务端时钟计龄、与设备在线状态无关（宿主重启后照常心跳，「在线」无法区分映射丢失与正常打印）；后台独立扫描任务（复用 `ExpirationScanIntervalMinutes` 周期，与 Pending 过期扫描同构）。
- **不重投 + 终态即最终**：回收不自动重新投递（作业可能已实际打出，重投会重复打印）；同一 requestId 重放只返回既有作业；宿主迟到的真实回报按既有幂等重放返回 Failed 视图（200），不覆盖超时判定——与回报的竞态由「status = Claimed」UPDATE 条件收敛，两个方向都不互相覆盖。
- **误判权衡（记入 DESIGN 决策后果）**：宿主活着但长时间未回报（如打印机离线挂起数小时）也会被回收，业务系统据此重发可能与本地恢复续打叠加成重复打印——正常批量分钟级完成、30 分钟余量充足，长挂起场景可调大配置规避。
- **契约文档先行（强化路径）**：`docs/DESIGN.md` 决策表 #98 + §5.1 服务端作业状态机补充 Claimed 超时回收段；§6「宿主重启后 Claimed 作业无终态」未决问题收敛为「Server 侧已修复，客户端持久化映射 + 重启补报不做」。
- **测试**：新增单元（FakeTimeProvider）+ HTTP 集成测试 10 项——失联回收终态 / 正常回报零回归 / 只回收失联 Claimed / 迟到回报幂等重放 / 关闭开关回归 / 配置项默认值与环境变量覆盖 / 端到端（领取 → 失联 → 回收 → 查询终态与原因 → 无二次领取）；日常 `dotnet test` 338 项全绿（排除 Perf / Soak）。

## 迭代 42 工作流重构：Issue 驱动迭代 + PR 门禁 · 2026-09-09

- **工作流切换（决策 #97）**：迭代任务全面迁移 GitHub Issue（新增「迭代任务」模板立项，含 AC-xx 验收标准与启动命令）；变更走短主题分支 + PR + squash 合并；`master` 启用 ruleset 门禁——必须 PR + 双必需检查（「构建与测试（dotnet + 前端）」「MSI 结构断言（安装包 UI 契约）」，strict 目标分支新鲜度）+ 禁强推，管理员无豁免。CI 从推送后「事后发现」变为合并前拦截。
- **新增 docs/WORKFLOW.md**：平台事实、日常迭代流程、事实源唯一映射（Issue = 本轮需求 / 范围 / 决议 / 进度 / 验收）、风险裁剪（轻量 / 标准 / 强化）、门禁演练记录与未启用选配清单——流程的单一权威说明。
- **验收滞后不结项**：新增 PR 模板（自查清单）与 `待验收` 标签——PR 关联 Issue 用 `#N` 普通引用、禁用关闭关键词；真机 / 用户验收滞后时 Issue 保持开放追踪，存量欠账仍见 ACCEPTANCE-BACKLOG.md。
- **ROADMAP 瘦身**：迭代 0-41 详情段（约 1140 行）整体迁至 docs/archive/ROADMAP-ITERATIONS.md；ROADMAP 保留状态总览索引 + 待需求清单，结项只更新一行。
- **AGENTS.md 重写**：迭代方式（Issue 驱动 + PR 门禁）与 DoD（必需检查绿 + squash 合并 + Issue 回写证据）对齐新流程；发布机制不变（记账 PR 合并后推 `v*` tag → release.yml）。
- **门禁演练**：故意失败 PR 验证「检查红 → 合并被阻」，结项记账走首个正式 PR 全绿合并（证据见 docs/WORKFLOW.md §5）。
- 纯流程变更：产品代码与公共契约零改动，不涉及版本发布。

## 迭代 41 PDA 配置界面文案与视觉优化 · 2026-09-08

- **信息架构重组（决策 #96）**：配置页由单页长滚动改为「主页 + 三子页」——主页只放当前状态卡与三个设置入口（① 连接服务器 / ② 连接打印机 / ③ 本机信息，每行带当前值摘要），每屏一个任务；三个子页各自「编辑 → 测试 → 保存并重启服务」，**不设全局草稿**（保存动作紧贴修改处，无「改了忘保存」心智负担；往返导航不丢输入）。实现仍为原生 Activity + C# 代码布局（同一 Activity 内视图切换，零新依赖）。
- **框架评估结论（调研后用户定稿）**：跨端框架（Kotlin/Compose、Flutter、uni-app）会失去与 `LabelFrame.Core` C# 打印链路的代码共享，MAUI 在包体与低端机表现上对一个配置页得不偿失（社区证据：裁剪后仍难低于 ~18MB、低端真机卡顿议题活跃；纯 .NET Android 为 MAUI 团队认可的正式路径）——保留原生；重新评估触发条件 = PDA 业务打印界面立项（届时评估 MAUI 或按决策 #93 由第三方 Web 经 JS 桥承接）。
- **文案全面改写为人话**：禁用专业术语（宿主、服务端→统一改称「服务器」、终态、渲染、生效配置等一律不出现）；每个位置只说三件事之一（这里填什么 / 点这个按钮会发生什么 / 状态意味着什么 + 下一步怎么办）；按钮动词短语；错误信息转译为可行动提示（如「连不上服务器——请检查地址是否正确、PDA 是否连着 WiFi」「没打出来——请检查打印机是否开机、IP 是否正确、是否缺纸卡纸」，原始异常缩为小字附注供排障）；覆盖配置页全部文本、Toast、常驻通知、内置状态页 HTML、应用名（`LabelFrame 打印宿主` → `LabelFrame 标签打印`）。
- **视觉设计系统（代码布局实现）**：浅灰底 + 白色圆角卡片；状态卡语义色摘要（绿 = 一切正常 / 红 = 连不上 / 灰 = 服务未运行）+ 服务器 / 打印机明细行；主按钮「保存并重启服务」通栏蓝底白字（≥52dp）；次级按钮描边样式（≥48dp）；入口行 ≥64dp；设备号等宽字体防误读；保存校验错误红字显示在保存按钮上方（不再写进状态区）；「刷新状态」按钮移除（状态 5 秒自动刷新，卡内小字注明）。
- **诚实性修正**：测试打印走的是已保存的打印机地址（既有行为）——输入地址未保存时点「打印测试标签」提示「先保存再测试——上面填的地址还没保存，现在测试用的还是旧地址」，不再打到旧地址造成困惑（仅 UI 提示，打印链路零改动）。
- **通知 / 状态页**：常驻通知一句话说清（「服务器：已连接 / 连不上（自动重试中）/ 未设置 · 打印机 192.168.x.x」）；状态页 HTML 重排为本机 / 服务器 / 打印机三卡片 + 语义色 + 通栏「打印一张测试标签」大按钮。
- **顺带清零既有警告**：`LabelHostConfig` 两处可空链式调用警告拆为逐条语句；`MainActivity` 重写后 AndroidHost 构建 0 警告（OnBackPressed 的 API 33 过时提示以 pragma 屏蔽并注明理由——未启用 predictive back 前框架仍回调，避免为此引入 AndroidX）。
- **真机走查（UROVO DT50，Release APK）**：主页（绿色状态卡「一切正常，随时可以打印」+ 三入口行摘要）、服务器子页（真实地址回填 + 引导句）、测试连接（绿字「✓ 能连上服务器」）、打印机子页（IP / 端口 / 引导句）、**保存并重启服务（现场实际操作：新地址持久化 + 服务带新配置运行）**、常驻通知新文案（dumpsys 核对）、浏览器状态页（标题 / 副标题 / 按钮 / 加载文案）逐项核验通过；测试打印完整链路 API 确认 **Completed（1/1，真实 Zebra 出纸）**。
- **真机修正（用户反馈）**：主页三个设置入口行显示为巨大空白卡片——入口行容器误用竖向布局，文本列「宽度 0 + weight」在竖向父容器中被压成 0 宽（文字逐字换行、卡片又高又空）；改为横向布局并补齐箭头布局参数后真机复验通过（UI 树与截屏双重确认：三行标题 / 摘要 / › 齐全，高度正常）。修正后屏上打印结果文案 / 本机信息子页 / 未保存提示仍留待日常使用确认。
- **真机修正二（用户反馈）**：① 设备号去掉 `pda-` 前缀，直接用 `Settings.Secure.ANDROID_ID` 原值（如 `b4ee90bd59b33c53`；兜底随机码同样无前缀）——**升级后设备号变化，Server 设备目录会出现新条目、旧 `pda-…` 条目变离线残留，需在服务端删除**；② 本机信息页「设备号」标签去掉括号说明、设备名称提示精简为「服务器设备列表里显示的名字」、主页本机信息摘要只显示设备名称；③ 修复**旋转退回主页**——旋转触发 Activity 销毁重建，`ConfigurationChanges = Orientation | ScreenSize | KeyboardHidden` 声明后不再重建（子页与未保存输入在旋转后保留），真机横屏验证仍停留当前子页。
- **测试**：`dotnet build` 0 警告 0 错误；AndroidHost 构建 0 警告 0 错误；日常 `dotnet test` 328 项全绿（桌面零回归）。

## 迭代 40 PDA 宿主配置与测试体验 · 2026-09-08

- **PDA 宿主定位定稿（决策 #95，用户拍板）**：AndroidHost = 后台打印执行服务（打印入口在业务系统侧，承接 #93），本迭代收敛为 PDA 专项——PC 侧易用性候选项（模板快选 / 打印值记忆 / 失败提示行动化 / ServerUrl 免重启等）全部延期，PDA 业务打印界面（扫码即打 / 模板列表 / 字段表单）明确不做。跨端公共契约零变更（仅 AndroidHost 本地 HTTP 扩展）。
- **原生配置页（唯一 UI）**：点 App 图标打开（替代此前启动即 Finish 的无界面行为）——服务端地址 + 测试连接；打印机按「品牌（默认 Zebra）→ 连接类型（默认网口 TCP）→ IP + 端口（默认 9100）」两级结构（品牌 / 连接类型为未来传输插件路由键）；设备号只读自动展示 + 复制；设备名称可选编辑；运行状态区（服务 / 服务端最近通讯 / 打印机最近出纸或失败）；「测试打印」本地提交内置测试标签走完整链路（校验 → 渲染 → `^GF` → TCP 发送 → 终态）。
- **设备号自动生成**：取 `Settings.Secure.ANDROID_ID`（`pda-<id>`），不可配置——多台设备天然不撞号，消除配置出错导致互相领作业的风险；卸载重装不变、恢复出厂视为新设备；取不到（含 Android 8 前著名坏值）时本地 UUID 兜底。设备名称默认 `PDA-<码后 4 位>`，注册 Server 随 `name` 上报（设备目录 / 目标设备选择器 / `GET /api/devices` 可读）。**注意**：从旧版本升级后设备号会从 `android-pda-1` 变为自动码，Server 目录出现一台新设备、旧条目变离线残留，属预期。
- **保存并应用（免 force-stop）**：写 SharedPreferences 后自动重启宿主服务（同进程 stop/start），配置页等待本地 HTTP 就绪后刷新状态。
- **通知与状态页**：常驻通知点击打开配置页，文案随服务端连接 / 打印机端点状态刷新（5s 一帧、变化才重发）；内置浏览器页（`127.0.0.1:53970/`）收敛为轻量状态页（配置概览 + 打印机探测 + 测试打印）。
- **配置模型结构化**：`{ server_url, printer_brand, connection_type, tcp_host, tcp_port, device_name }`，旧装机 `tcp_host` 自动迁移；`GET/POST /api/host/config` DTO 扩展（TcpPort / PrinterBrand / ConnectionType / DeviceName，保留旧字段）。
- **移除 pc_host 测试模式（决策 #42 废止）**：`PcTemplateClient`、`/api/pc/*` 端点、`PcHostUrl` 配置项删除——状态页收敛后失去唯一消费方；WinHost `/api/logs` 接收端点保留，PDA 不再自动回传日志。
- **构建修复（决策 #95 ⑦）**：构建脚本改用 `-p:EmbedAssembliesIntoApk=true`——.NET Android 36.1.x 起 `AndroidFastDeployment` 属性失效，Debug 产物一度退化为 Fast Deployment 壳（纯 `adb install` 启动即 abort，真机冒烟时发现并修复）。
- **真机冒烟（UROVO DT50 / Android 11，全部通过）**：配置页渲染与旧配置迁移（server_url / tcp_host 读回）；设备号 `pda-b4ee90bd59b33c53` 自动生成、默认名 `PDA-3C53`；`/api/host/config` 新 DTO；状态页 HTML；打印机状态探测（在线 / 有纸 / 未暂停）；**测试打印真实 Zebra（192.168.2.121）物理出纸（1/1 Completed）**；「保存并应用」触发服务自动重启（ServiceRecord 重建验证）且 healthz 恢复；CORS 预检（OPTIONS 204 + 宽松头）。通知点击进配置页与 UI 视觉细节留待用户日常使用确认。
- **测试**：`dotnet build` 0 警告 0 错误；AndroidHost 构建 0 警告 0 错误；日常 `dotnet test` 328 项全绿（桌面零回归）。

## 迭代 25 Android PDA 宿主真机落地 · 2026-09-08

- **PDA 接入边界定界（决策 #93，用户拍板）**：AndroidHost 是 PDA 上唯一打印执行宿主；第三方 PDA 程序统一经既有 HTTP 公共契约集成——路由模式（Server `POST /api/jobs` + targetDeviceId）或直连模式（同机 `127.0.0.1:53970`，即 JS 桥）。零跨端契约变更；不做 Android SDK / Intent / AAR 新契约形态。
- **SQLitePCLRaw 原生库双坑修复（决策 #94）**：`lib.e_sqlite3.android` 定版 **2.1.11**（2.1.12/2.1.13 误装 glibc 构建 so，Android 装载即 LinkageError）；桌面版 `lib.e_sqlite3` 从 Core 下沉到各可执行项目自引（WinHost / Server / 各测试项目）——此前 Core 引用经 RID 回退图把桌面 glibc so 打进 APK、压过 android 包的 NDK so。桌面宿主行为零变化（328 项测试全绿）。
- **AndroidHost 修复**：启动装载 `e_sqlite3` 原生库（否则 SQLite 首开 DllNotFoundException，迭代 5 以来首次真机运行即崩）；本地 HTTP 修复 **POST 带体请求挂死**（StreamReader 预读吞掉请求体，改统一缓冲解析）；构建脚本关闭 Fast Deployment（默认 Debug 产物纯 `adb install` 后启动即 abort）。
- **AndroidHost 补齐（与 WinHost 同构）**：本地 HTTP 宽松 CORS + OPTIONS 预检（JS 桥）；`GET /api/jobs?limit=` 作业列表与 `POST /api/jobs/{id}/items/{index}/retry` 失败项重打端点；Server 轮询升级为 notify 20s 长轮询 + 独立 1s 回报循环（作业到达即领取）；分析器警告清零。
- **16KB 页构建级验证通过**：全部 arm64 so ELF 段对齐 ≥ 16KB + `zipalign -c -P 16` 通过 + 构建无 XA0141；运行时验证待 Android 15+ 设备（保留验收积压表）。
- **真机验收全通过（UROVO DT50 / Android 11）**：注册 / 心跳（notify 保活）、模板下发、作业打印（路由 + 直连双模式、批量 3 张、ZPL 字节级核对：^PW480/^LL320 精确换算 + ^GF 位图 QR 与中文内容可见）、离线恢复（Offline 判定 + Pending 暂存 + 恢复 7s 内领取）、断网重连（挂起 → resume 续打 → retry 补打 → 两端终态一致）、开机自启（重启 10s 内拉起）、前台服务保活。物理出纸以 TCP9100 模拟打印机验证，真实 IP 打印机出纸保留验收积压。
- **遗留（DESIGN 未决）**：宿主重启后本地↔Server 作业映射（内存态）丢失，Server 侧已 Claimed 作业无终态——全宿主既有语义，需跨端方案再立项。
- **测试**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 328 项全绿。
- **追加（同日真打印机联调）**：本地 HTTP 新增 `GET/POST /api/host/config`（tcpHost / serverUrl / deviceId / pcHost 读写，持久化 SharedPreferences，重启宿主生效）——补齐 WinHost 同构端点，第三方程序 / 联调可免 adb 改配置；真 Zebra 打印机（TCP9100）实测：PDA 侧状态探测在线 / 有纸 / 未暂停，直连作业渲染出纸链路 Completed（物理出纸由用户现场确认）。

## v0.23.0 迭代 36-39 汇总发布 · 2026-09-08

- **打包范围**：文档治理与路线图收口（迭代 36）、服务端暂存作业 TTL 过期 + notify 积压即时唤醒（迭代 37）、测试稳定性治理（迭代 38）、性能优化批次（迭代 39）；详见各迭代条目。
- **服务端行为变更（部署注意）**：Pending 作业 TTL 默认开启（12 小时；`Server.PendingJobTtlHours` / `LABELFRAME_SERVER_PENDING_TTL_HOURS`，0 或负值 = 关闭，行为回到「不设过期」）——设备长期离线期间暂存的作业超期进入新终态 **Expired**，幂等语义保持严格（同一 requestId 重放只返回既有作业，重打需新 requestId）；notify 挂起前积压预检消除纯积压清空场景的 20s 长轮询空等。
- **性能优化**：客户端提交到出纸系统侧 p50 205ms → 9ms（Worker 信号量唤醒）；大批量打印每张托管分配 2.3MB → 0.84MB（SKBitmap 池化）；服务端领取路径写事务合批（Touch + Claim 合一）降低高并发锁竞争。作业 / 路由 / 打印行为零变化。
- **版本同步**：`/api/server/info` 版本与稳定版 Compose 默认版本更新为 `0.23.0`。
- **发布产物**：GitHub Release 附件（Server / Client MSI、服务端 webui 插件 zip、linux-x64 归档）+ ghcr 镜像 `ghcr.io/marci-labs/labelframe-server:0.23.0` / `labelframe-client:0.23.0`（均含 `latest`）。

## 迭代 39 性能优化批次（Worker 信号量唤醒 / SQLite 写合批 / SKBitmap 池）· 2026-09-07

- **Worker 信号量唤醒（决策 #92①）**：`LabelJobQueue` 新增「出现新待打项」唤醒信号——新提交 / 恢复 / 失败项重打 / 启动恢复中断四条路径在存储写入提交后发信号（先信号后提交会让 Worker 探测落空且信号被消费，错过唤醒）；`JobPrintWorker` 空转等待由 200ms 轮询改为信号即时唤醒（5s 超时兜底防信号遗漏，正常路径不触发），「EXISTS 轻量探测 → 完整领取」结构、批次节流（TimeProvider 注入保留）、挂起恢复语义零变化；「探测有 Pending 但领取落空」（挂起作业等不可领场景）保留 200ms 周期且同样可被信号提前唤醒。实测：单张全链路 **p50 205ms → 9ms**（p99 79ms 为首张预热），Perf 阈值收紧为 `p50 < 20ms` + `p99 < 500ms`。
- **SQLite 写事务合批——评估 + 实施一项（决策 #92②）**：评估结论记 DESIGN——提交（INSERT OR IGNORE + UNIQUE 兜底）与回报已是单写事务最细粒度，notify 心跳语义独立保持，busy_timeout 5s 为排队上限（调小变 SQLITE_BUSY 错误、调大延长尾部，均无收益）；**架构级合批（写队列串行化 / 提交缓冲 / 换存储）明确不做**——当前局域网规模（≤20 设备）p50 恒 3-4ms 不受影响、无错误无丢失，尾部排队是 SQLite 单写者可预期特征。实施：领取路径「Touch 心跳 + Claim 圈定」两个自动提交写事务合并为一个显式事务（`ServerDb.TouchAndClaimPendingJobsAsync`），20 设备并发每轮领取少一次单写锁排队——本机 ~39% CPU 负载 A/B：无合批 p95 3741ms → 合批 2954-3422ms；回报路径顺带移除 UPDATE 受影响后的冗余 id 回读。
- **SKBitmap 池化（决策 #92③）**：`SkiaLabelRenderer` 整版渲染中间态（SKBitmap 像素内存 + 托管像素暂存 byte[]）按「尺寸 + 暂存长度」匹配池化复用（池上限 4、lock 保护），租用后 Clear 白底全量重置保证与上一张内容无关；输出 LabelBitmap / PNG 始终新分配，对外 API 与渲染结果不变。bench 实测：**每张托管分配降 61%-67%**（整链路 60×40@203 ~990KB → 390KB；100×60@300 ~4.9MB → 1.61MB），批量均摊 2.3MB/张 → **0.84MB/张**（200 张 458MB → 165MB），Gen2 高频回收显著缓解。
- **基线更新**：`docs/PERF-BASELINE.md` 升级 v2——三项优化后新数据 + v1 旧值对照 + 遗留优化机会清单收敛（Worker 唤醒 / SKBitmap 池已了结；架构级写合批记不做）。
- **测试**：新增 Core 单测 3 项（提交即唤醒、幂等重放不发信号、恢复与失败项重打唤醒——确定性等待语义，无睡眠断言）；既有 FakeTimeProvider 批次节流测试零改动通过；Server 领取路径由既有集成测试覆盖（未注册设备 / TTL 过滤 / 领取不重复全绿）。
- **本地验证**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 328 项全绿（Core 108→111）；perf：WinHost 通过新阈值（p50=9ms）、Server 1/5 设备通过（20 设备在本机 ~39% CPU 负载下两臂同超 50ms 阈值——DESIGN §6 既有环境敏感特征、A/B 证实非本次回归，nightly 隔离把关）；soak 5 分钟通过（0 错误 / WAL 有界 / 托管堆稳定）；bench 全套通过。行为零变化：作业 / 路由 / 打印既有测试全部原样通过。
- **范围说明**：不改发布 / CI 工作流；不推 tag；AndroidHost 与跨端契约不在范围。

## 迭代 38 测试稳定性小治理 · 2026-09-07

- **flaky 根因核实与加固**：ci run `34081028327` 的 `DataPrint.test.tsx` 会话保留用例失败定位到 `findByDisplayValue('A-01')` 行——等待语义本身无误（已是 `findBy`），超因是 testing library 默认 1000ms 超时不足：DataPrint 挂载需串行走完「设备探测 → 模板列表 → 模板详情 → testData 预填」多段异步链，CI 高负载（多 worker CPU 争抢）下整链偶发被拖过默认超时。`DataPrint.test.tsx` / `DataPrint.server.test.tsx` 中守卫该挂载链（含页面卸载重挂后的整链重跑）的 `findBy*` / `waitFor` 统一显式放宽到 3000ms（`MOUNT_WAIT` 常量），仍在 vitest 5s 测试预算内；测试框架 / vitest 配置零变更（约定记入 DESIGN 决策 #91）。
- **同类模式排查（按范围仅加固、不批量重写）**：`web/src` 其余组件测试（Settings / JobHistory / Devices / PluginPackages / ClientPackages / App 双模式）均为「先 `findBy` 异步锚点、再同步断言」的正确模式，等待链为单段 fetch，未发现确有竞态风险的同步查询，维持现状。
- **本机构建产物清理**：删除 `src/LabelFrame.WinHost/bin/Debug/net10.0-windows/` 旧 TFM 遗留目录（bin 不入库，仅本机动作）；清理后 `dotnet run --project src/LabelFrame.WinHost -f net10.0-windows` 被 `NETSDK1005` 拒绝（提示该 TFM 不在 TargetFrameworks 内），不再静默运行月龄旧代码（此前联调会表现为端点大面积 404）。风险提示与「使用完整 TFM 名」约定记入 DESIGN「兼容性」。
- **收尾补记（同日，用户要求）——全仓旧 TFM 残留清理**：迭代收尾后按 csproj 现行 TFM 集合清理各项目 bin/obj（Debug / Release）下全部非现行目录——`net8.0`（Core / Core.Tests / Server）、`net8.0-windows` 与旧 `net10.0-windows`（WinHost / WinHost.Tests，后者含验证命令预建的半成品）、Rendering 旧多目标 `net10.0-windows`（Debug / Release）、Server.Tests 旧 `net10.0`，以及已移除项目 `test/LabelFrame.Studio.Tests` 的整目录构建残留（项目本体已随 a61fe60 删除，git 零跟踪）；清理后 `dotnet build` 0 警告 0 错误，git 工作区零变更（均为 gitignore 本机动作）。
- **范围说明**：不改产品代码行为、不改测试框架 / vitest 配置、不动发布 / CI 工作流、不推 tag；范围外发现的其余旧 TFM 残留在迭代收尾后按用户要求一并清理（见收尾补记）。
- **本地验证**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 325 项全绿；web client / server 双模式各连跑 3 轮（共 6 轮 × 247 项）全绿；lint 通过（既有 6 条 warning，0 errors）。

## 迭代 37 服务端暂存作业 TTL 过期 + notify 积压即时唤醒 · 2026-09-07

- **作业模型契约变更（文档先行）**：服务端作业新增终态 **Expired**（过期未投递）——设备离线期间暂存的 Pending 作业超过 TTL 视为「目标不可达」主动放弃，修订决策 #22 的「不设过期」（新决策 #89 / #90 记入 DESIGN）。TTL 只对 Pending 计龄（Claimed / Completed / Failed 豁免，领取后归客户端本地队列管理），过期判定只以服务端时钟为准；幂等语义保持严格——同一 requestId 重放只返回既有 Expired 作业、不重新投递，业务系统重打必须用新 requestId 重发。
- **服务端配置**：`Server.PendingJobTtlHours`（默认 12 小时；0 或负值 = 关闭过期，行为与现状一致）与 `Server.ExpirationScanIntervalMinutes`（默认 5 分钟）；均支持 appsettings 与 `LABELFRAME_SERVER_PENDING_TTL_HOURS` / `LABELFRAME_SERVER_EXPIRATION_SCAN_MINUTES` 环境变量覆盖。
- **领取过滤（正确性兜底）**：设备领取查询按「CreatedAt + TTL」过滤，超期作业一律不下发，不依赖过期扫描周期；notify 积压预检与领取过滤共用同一判定。
- **后台过期扫描**：新增 `PendingJobExpirationService`（与 DataCleanupService 分离的独立 BackgroundService——过期是状态转移需分钟级及时可见，清理是删除历史数据为小时级周期），把超期 Pending 批量标记 Expired 并写入中文失败原因；Expired 作业随既有 30 天历史清理回收。ServerService 时间取值统一到可注入 TimeProvider（测试 FakeTimeProvider 确定性驱动）。
- **notify 积压即时唤醒**：`GET /api/devices/{id}/jobs/notify` 挂起等待前先查一次该设备未过期 Pending 作业，有则立即返回 hasPending=true——消除纯积压清空场景每批 20 秒的长轮询空等（清空吞吐原被钉在约 30 个作业/分钟）。
- **前端**：作业历史页（与数据打印页同一状态映射）新增 Expired =「已过期」中文标签与中性终态徽标，失败原因列透出服务端放弃说明；不新增操作按钮，客户端零改动。
- **测试**：新增 10 项——TTL 单元（FakeTimeProvider：超期不领取 / 未超期照常领取 / Claimed 豁免 / 扫描只标记超期 / TTL 关闭无操作 / 配置默认值与环境变量）+ 完整宿主集成（超期作业在扫描未运行时仍被领取过滤拦截的双保险、扫描标记 Expired、requestId 重放返回 Expired 且不重新投递、notify 有积压立即返回）+ TTL 关闭（0）回归锚点类（超龄 Pending 照常下发，行为与现状完全一致）。
- **本地验证**：Debug / Release 构建 0 警告 0 错误；日常 .NET 测试 325 项全绿（Server 49→59）；web client / server 双模式各 247 项全绿，lint（既有 6 条 warning）与双构建通过。

## 迭代 36 文档治理与路线图收口 · 2026-09-07

- **路线图决定落地（2026-09-07 用户拍板，只改文档）**：迭代 26（Niimbot 蓝牙传输插件）标记「已放弃」（PDA 宿主走 IP 打印、无蓝牙承接需求；传输插件机制保留，有真实蓝牙打印机需求时按插件另行立项），ACCEPTANCE-BACKLOG §3 真机验收项随之取消；迭代 25（Android PDA 宿主）由「延后」改为「下一轮」，蓝牙打印移出其范围；需求 P1「PDA 蓝牙传输」降级至 P2 / 待需求（PDA 宿主本身走 IP 打印、不依赖蓝牙）。
- **Code128 中文校验决策不做**：Code 128 字符集仅 ASCII，中文值在渲染 / 编码层按编码异常拒绝（作业项 Failed + `LF_ENC_001` + 原因）即为正确语义，不加提交前专门校验（DESIGN「暂不做」已记录）——了结 v0.22.1 遗留的「作为独立校验问题后续处理」悬空承诺；历史 CHANGELOG 条目保持原样。
- **文档与代码一致性核对**：`docs/` 根 8 份活跃文档对照 `src/` 逐项核对 API 端点 / 错误码 / 配置与环境变量 / 默认端口（53960 / 53961 / 53970）/ 数据目录 / 作业状态机，主体一致；修正文档滞后项——DESIGN 术语表「编码器 / 传输」（矢量 ZPL 早已删除、蓝牙降级）、架构图 PDA 节点、错误码家族补 `LF_ENC` / `LF_TRANSPORT` / `LF_PLUGIN`；REQUIREMENTS 底线「中文渲染」机制表述；TEST-MATRIX（Niimbot / PDA 行、示例版本 0.22.2）；LINUX-CLIENT-E2E 示例版本；PERF-BASELINE Niimbot 展望；archive README 现行文档清单；CONTRIBUTING 测试命令改日常过滤口径。未发现代码偏离设计契约的问题。
- **注释清理（无行为变更）**：修正 7 处过时 / 失效注释（失效的规格 §9 / 契约 §4 / DESIGN 旧章节 / 迭代号引用；`LabelTextElement` 字体字段与 `SkiaLabelRenderer` 摘要按现行图片打印语义重写）。
- **过程性文档盘点**：`docs/` 根与 `docs/archive/` 无需归档移动；`artifacts/` / `outputs/` / `BenchmarkDotNet.Artifacts/` 已确认 gitignore 且未跟踪。
- **本地验证**：`dotnet build` 0 警告 0 错误；日常 `dotnet test` 315 项全绿。

## v0.22.2 Linux 容器中文字体切换 · 2026-09-07

- **中文字体基线**：Server Ubuntu/Docker 镜像与 Linux Log Client 镜像的预装中文字体从 `fonts-noto-cjk` 切换为 `fonts-wqy-microhei`，中文字符首选匹配 `WenQuanYi Micro Hei Regular`。
- **版本同步**：`/api/server/info` 的服务端版本同步为 `0.22.2`；稳定版 Compose 默认版本更新为 `0.22.2`。
- **范围说明**：不改变模板包、打印 API、作业模型或客户端打印链路；Windows 客户端实际打印仍使用客户端本机字体环境。
- **本地验证**：Release 构建 0 警告 / 0 错误；日常 .NET 测试 315 项全绿；本地候选 Server 与 Linux Client 镜像的中文字符首选均匹配 `WenQuanYi Micro Hei`，Server 中文文本出图预览返回 `HTTP 200`，PNG 11575 字节。

## v0.22.1 Server Docker 中文字体补丁 · 2026-09-03

- **Server 镜像中文字体**：Ubuntu / Docker Server 镜像默认安装 `fontconfig` 与 `fonts-noto-cjk`，并在构建时刷新字体缓存；服务端管理界面的模板预览 / 出图预览在 Linux 容器中具备中文文本渲染基线。
- **版本同步**：`/api/server/info` 的服务端版本同步为 `0.22.1`；稳定版 Compose 默认版本更新为 `0.22.1`。
- **范围说明**：不改变模板包、打印 API、作业模型或客户端打印链路；Code128 条码绑定中文导致的编码异常作为独立校验问题后续处理。
- **本地验证**：Release 构建 0 警告 / 0 错误；日常 .NET 测试 315 项全绿；本地候选 Server 镜像可见 `Noto Sans CJK SC`，中文文本框出图预览返回 `HTTP 200`，PNG 6653 字节。

## 迭代 35 Linux Client 正式发布 + P0/P1 测试补强 · 2026-08-28

- **双镜像发布**：新增正式 `ghcr.io/marci-labs/labelframe-client`（`linux/amd64`、仅 `log`）与只引用同版本镜像的 `compose.release.yaml`；Server / Client 候选各构建一次，Compose E2E 通过后直接推送原镜像的版本号与 `latest` 标签，避免验收制品与发布制品漂移。Server 发布镜像携带当前管理界面，但继续由配置决定是否启用。
- **P0/P1 发布门禁**：E2E 扩展到 Linux 能力裁剪、模板详情 / 列表 / 预览 / 包导入导出、Excel 生成上传、日志回传、幂等、单张 / 3 张、离线暂存、Client 重启持久化与重启后继续领取；逐张复制真实容器 PNG，检查非空白并解码 Code128。精确 `0.22.0` 本地候选双镜像已通过完整组合。
- **测试治理**：新增 `docs/TEST-MATRIX.md`，按 P0/P1/P2 记录功能、自动化层级、发布门禁与真机欠账；Release 与日常 CI 统一排除 Perf / Soak，由 nightly 独立负责性能与稳态判定。
- **Excel 续填实证**：真实下载模板的命名区域仍为 `A1:A2`，在其下续填 A3:A5 后由 Server 管理界面完整识别 4 行，关闭 TemplateFrame.Excel.Simple 2.0.0 升级后的人工冒烟欠账。
- **回归修复**：WinHost 多目标编译后，Windows MSI 发布脚本显式选择 `net10.0-windows10.0.26100`，恢复 Windows 发布与 MSI 流水线。
- **跨平台门禁修复**：独立 PNG 校验器补齐 Linux 原生 Skia 资产，确保 Release 的 Ubuntu runner 能解码候选镜像产出的 PNG；首次 `v0.22.0` 工作流在镜像推送前准确阻断了该工具链缺口。
- **正式发布**：`v0.22.0` Release 全绿，上传 Client / Server MSI、Server Web UI zip 与 Linux Server 归档；同一次候选 E2E 验收后的 Server / Client `0.22.0` 与 `latest` 镜像已推送 GHCR。发布后重新 pull 双镜像并以全新卷运行稳定 Compose，公共端点、能力边界、设备路由、离线暂存、重启持久化及 6 张 PNG 解码全部通过。
- **本地验证**：Release build 0 警告 / 0 错误；日常 .NET 315 项全绿；web client / server 各 246 项全绿，lint（保留既有 6 条 warning）与双构建通过；源码 Compose 与精确版本候选 Compose E2E 均通过。

## 迭代 34 Linux 无头客户端（Log 驱动）+ Docker Compose E2E · 2026-08-28

- **Linux Client Host**：`LabelFrame.WinHost` 增加 `net10.0` 目标并产出 `LabelFrame.ClientHost`；复用队列、Server 路由、Skia 渲染和批次 Worker，Linux 固定只暴露 `log`，不加载 Windows 驱动、Zebra SDK、外部插件、Web UI、浏览器或托盘。
- **容器化测试环境**：新增 Linux Client 多阶段镜像、稳定版 Server `0.21.0` + 当前 UI / Client 候选的 Compose，以及一键 E2E 脚本；数据与 PNG 使用命名卷，默认管理地址 `http://127.0.0.1:53910`。
- **闭环验证**：设备注册 / 心跳、单张与 3 张作业、客户端领取与终态回报、Log PNG 数量、重启恢复全部通过；浏览器在设计器实际新建并保存模板，再完成数据填写、在线设备选择、单张打印与作业历史，控制台无 warning / error。
- **严格补证**：新增 `LabelFrame.PrintImageVerifier`，E2E 从 Client 命名卷直接复制每个 Item 的 PNG，检查非空白并逐张解码预期 Code128；Client 重启后先断言旧作业仍在，再提交新作业并验证领取、出图与终态回报，补齐此前仅验证重新在线的缺口。
- **联调修复**：Log PNG 输出目录改为可配置且作业 API 返回同一目录 / 正确数量；环境变量 ServerUrl 优先于持久化配置；SQLite provider 初始化改为真正线程安全；Server 完整宿主测试按进程环境变量约束串行，消除数据库路径互相覆盖。
- **验证**：Release build 0 警告 / 0 错误；日常 .NET 315 项全绿，Server 5 分钟 soak 通过；web client / server 各 246 项全绿，lint 与双构建通过；Docker / 浏览器 E2E 通过。严格补证轮次中自动化 5 张 Compose PNG 与浏览器作业 PNG 均直接解码成功，重启后新作业 `Completed`。Perf 在低干扰轮次通过，但宿主 CPU 41%–46% 时既有延迟门槛会抖动失败，未降低阈值，风险记入 DESIGN。



## 依赖升级：TemplateFrame.Excel.Simple 1.0.5 → 2.0.0 · 2026-08-25

- **修复 Excel 模板续填行导入丢失**：1.0.5 的 `SimpleExcel.Read` 以命名区域 EndRow 为边界——我们生成的模板命名区域只覆盖「表头 + 示例行」，用户在下方续填的行不扩展区域、导入时被静默丢弃；2.0（含 1.0.7 读取容错修复）数据区顺延到工作表最后一行，续填行完整读入。第三方（WPS / Excel 生成）文件兼容性同步改善（共享字符串表头 / 富文本 / 缺 RowIndex 等修复）。
- **破坏性变更对照无影响**：项目仅用 Read / Write / SimpleExcelTable / SimpleExcelOptions 四个 API，2.0 签名不变、零代码改动；`Read` 损坏流异常统一为 `InvalidOperationException` + 本地化中文消息（导入端点 catch Exception，行为兼容且报错更友好）；基础包层面的破坏性变更（MissingElementPolicy 迁移 / 删除 6 类型等）未使用。
- 变更范围：`Core` 与 `Studio` 两个 csproj 版本号；dotnet build 0 错误、dotnet test 315 全绿；2.0.0 已发布 nuget.org，CI 可正常还原。

## 迭代 33 性能 / 稳定性测试（微基准 / 端到端延迟 / soak / nightly CI）· 2026-08-25

- **微基准**（test/LabelFrame.Benchmarks，BenchmarkDotNet + MemoryDiagnoser）：整链路单张 0.7-3.4ms、分配 1-5MB（面积线性）；50 张批量 36-170ms 远低于 1 秒预算。
- **端到端延迟**（Trait=Perf，`scripts/run-perf.ps1 perf`）：Server 路由 p50 恒 3-4ms，**发现 SQLite 单写者在 20 并发设备下的 p95 尾部特征**（写事务排队 2-3s，不丢不错）——分层阈值；WinHost 单张 p50 ≈ 205ms，**发现延迟主体是 Worker 200ms 空转轮询**（渲染仅 ~1ms），信号量唤醒记为优化机会。
- **稳定性 soak**（Trait=Soak，默认 5 分钟 / LF_SOAK_MINUTES 可调）：混合负载 0 错误、WAL 有界（显式声明 wal_autocheckpoint=1000）、托管堆稳定。
- **CI**：nightly-perf.yml 每周一 04:00 UTC（Perf + 15 分钟 soak + 基准）；日常 ci.yml 不含 Perf/Soak。
- **文档**：docs/PERF-BASELINE.md 首份基线 + 三个优化机会；dotnet test 314 全绿。

## 迭代 32 测试完善（端点集成 / 渲染元素 / TimeProvider 确定性 / 设计器组件 / MSI 断言 CI）· 2026-08-25

- **WinHost 端点集成测试 +10**：WinHostApp 装配抽取（与生产一致，托盘/浏览器/退出留 Main）；HostOptions 新增 ConnectionPath（测试隔离本机连接配置——实际抓到测试读到开发者本机 zebra 配置的缺陷）；覆盖 transport / jobs 全生命周期 / host config 回环 / 插件安装卸载等八组端点。
- **渲染器元素测试 +6**：图片 / 线 / 区域三种此前零测试的元素；发现 RegionHAlign 对自动宽度文本失效（DESIGN 风险，分轴断言绕过）。
- **TimeProvider 确定性（节流测试重写）**：JobPrintWorker Task.Delay 全经 TimeProvider；FakeTimeProvider 驱动消除 300 秒真实等待；移除测试程序集并行关闭——WinHost 套件 17s→2s。
- **设计器组件测试 +27**：PropsPanel/SidePanel 真实渲染（三类元素字段集互斥、onChange 回调、图层选中与层级、双面板主链 harness）。
- **MSI 断言进 CI**：ci.yml msi-verify job——安装包 UI 契约（向导 / 中文 / 选项页 / Run 键）回归提交即发现。
- dotnet test 314 全绿（298→314）；pnpm test 双模式 246 全绿；测试评审结论与基准来源见 ROADMAP 迭代 32。

## v0.21.0 发布（迭代 27-31：工程治理 + Excel 2.0 + 安装向导 UI）· 2026-08-25

- **安装体验**（迭代 31）：WixUI 全中文品牌向导（欢迎 / 许可 / 安装目录 / 完成）+ 品牌位图 + 完成页「立即打开」复选框 + Client「安装选项」页开机自启（默认勾选，--autostart 托盘模式）；控制面板元数据补全。
- **工程治理**（迭代 27-30）：日常 CI（push/PR）；LabelFrame.Api 共享契约库（两端端点/DTO 去重，修复预览 DPI / 错误码漂移）；数据层并发修复（领取原子化 / N+1 / Worker 轮询轻量化 / TransportManager 加锁）+ SQLite WAL；C# 分析器门禁（latest-recommended + 警告即错误）；Server 端点集成测试（发现并修复宿主启动即崩与 403 变 500 两个真 bug）；覆盖率收集；Studio（WPF）与原型移除；文档体系重组（README 三类角色快速开始 / DEPLOY / 归档）。
- **依赖升级**：TemplateFrame.Excel.Simple 1.0.5 → 2.0.0——修复「下载 Excel 模板下方续填行导入丢失」。
- dotnet test 298 全绿；`dotnet build` 0 警告（分析器全开）。

## 迭代 31 安装包 UI 专业化（WixUI 向导 / 品牌位图 / ARP 元数据）· 2026-08-25

- **安装向导**：Client / Server MSI 接入 WixUI_InstallDir——欢迎页（品牌左幅）→ 中文许可协议 → 安装目录（默认 Program Files，可选）→ 安装进度 → 完成页；此前双击 MSI 只有 Windows Installer 默认裸进度窗。
- **品牌资产**：新增 generate-installer-branding.ps1 生成 493×312 欢迎 / 完成页背景与 493×58 内页横幅（主蓝 + L 标，与应用图标同体系）；generate-license-rtf.py 生成中文许可 RTF。
- **修复隐患**：接入 WixUI 后 ExecuteAction 固定 1300，原卸载清数据对话框（1350）会晚于执行阶段弹出导致勾选失效——移至 1290；运行时缺失提示提前至向导之前（1150）。清理动作改为 `[INSTALLFOLDER]` 目录无关（支持自定义安装目录）+ 尽力语义（缺失文件不卡卸载）。
- **其他**：控制面板元数据（产品图标 / 帮助链接）补全；Server 自定义完成小窗由品牌化 ExitDialog 取代；Client 保留「立即打开」TopMost 完成弹窗；新增 verify-msi-ui.ps1 结构校验（COM 校验对话框表 / 序列 / 品牌二进制，关键断言）；release.yml 安装 WixToolset.UI.wixext。
- **视觉验收反馈修正**：① 许可页标题混入「微软雅黑」——RTF 字体表不解析 Unicode 转义序列，字体名改用 ASCII 英文名 Microsoft YaHei（fcharset134 声明中文字符集）重新生成；② 「立即打开」独立 TopMost 弹窗取消——并入向导完成页可选复选框（默认勾选，Finish 按钮按条件 DoAction 异步启动客户端），应用侧 --install-finished 模式与 InstallFinishedPrompt 一并移除。
- **复选框色差修正**：MSI 的 CheckBox 控件不支持透明、以系统按钮面色（240,240,240）绘制自身背景——位图底色由近白（250,251,253）改为精确按钮面色，完成页复选框与周围无缝融合。
- **向导全量中文化**：构建加 `-culture zh-cn`（WixUI 扩展内置中文资源）——欢迎 / 许可 / 目录 / 进度 / 完成各页按钮与引导文字全部中文（下一步(&N)、上一步(&B)、取消……）。
- **开机自启选项（Client）**：新增「安装选项」页（目录页与确认页之间）——「开机自动启动 LabelFrame」复选框默认勾选；勾选时写 HKLM Run 键，以 `--autostart` 参数启动（托盘常驻、不自动打开浏览器，WinHost 新增该参数支持）；未勾选则不写。Server 无需选项（Windows 服务本身开机自启）。
- 本地构建 Client / Server 0.21.0 MSI 断言全部通过；视觉验收（真实安装走一遍向导）待执行（ACCEPTANCE-BACKLOG §6）。

## 迭代 30 注释与文档清理（代码去过程化 / DESIGN 重构）· 2026-08-25

- **代码注释去过程化**（60+ 文件约 165 处）：迭代号 / 决策号 / 章节引用等过程标记与历史叙述移除，注释只保留「是什么 / 为什么」；面向用户的错误消息去掉内部代号；标点残渣校验为零。
- **DESIGN.md 重构**：章节顺序修正（4→6→5 → 1-6）；原型十轮过程笔记归档 docs/archive/DESIGN-PROTOTYPE-NOTES.md；API 契约刷新为现状（Server / WinHost 完整端点分组 + 错误响应约定）；「风险与未决问题」重写为仅现存项（已解决条目删除），全文 212→201 行。
- DEPLOY / README / ACCEPTANCE-BACKLOG 过程引用清理。
- **收尾体检修复**：移除误提交的本地覆盖率报告（TestResults 入 gitignore）；README Rendering 行去掉已删除的 GDI 预览表述；src/LabelFrame.Studio 磁盘残渣删除；web/README 重写（去 Vite 模板样板与历史叙述）；DESIGN 决策表 3 处单元格内未转义管道符修复（表格渲染破坏）。
- dotnet build 0 警告 0 错误；dotnet test 298 全绿。

## 迭代 29 程序优化（SQLite WAL / 数据层基建 / 分析器门禁 / Server 集成测试 / 覆盖率收集）· 2026-08-25

- **SQLite WAL 全库启用**（决策 #80）：读写并发不再互相阻塞（Server 长轮询 / 心跳 / 提交并发受益）；四存储（jobs / templates / logs / server）基建收拢公共 LabelFrame.Core.Data.SqliteSupport，Server 不再复制 provider 初始化；设备心跳合并单条 UPDATE。
- **C# 分析器门禁**：AnalysisLevel=latest-recommended + 警告即错误（AndroidHost 实验性除外）；清零真修——ZPL 输出与端口参数固定 InvariantCulture（区域无关）、LabelJobQueue / ServerService / TransportManager 实现 IDisposable、LoggerMessage 源生成、Serilog formatProvider、传输插件接口参数名对齐；豁免（CA2007 / CA1031 / CA1848 / CA1873 / 测试目录 CA1707 / CA1861）在 .editorconfig 注明理由。
- **死代码清理**：LabelPreviewRenderer（GDI 预览，迭代 27 后零生产消费者）与其测试移除；Rendering 双 TFM 收敛单 net10.0、移除 System.Drawing.Common。
- **Server 端点集成测试**（WebApplicationFactory 全链路：注册 → 提交 → 幂等 → 领取 → 回报 → 403/404），**发现并修复两个真实缺陷**：① 无参 UseExceptionHandler() 缺 AddProblemDetails() 配套——宿主启动即崩（单元测试不拉管道未暴露）；② Results.Forbid() 依赖未注册的认证设施——运行时 500 而非 403（两宿主 4 处改显式 403）。
- **覆盖率收集**：coverlet.collector + CI artifact（不设门禁）；首份基线 Server 88% / Api 63% / WinHost 59% / Core 49% / Rendering 31%（类级均值）。
- dotnet build 0 警告 0 错误（分析器全开）；dotnet test 298 全绿。

## 迭代 28 工程治理 P1/P2（文档归档 / 死重移除 / 数据层并发 / 异常契约 / Program 拆分 / 集成测试 / 安全边界）· 2026-08-25

- **P1 文档**：16 份历史迭代文档归档 docs/archive（核心四件套 + DEPLOY + ACCEPTANCE-BACKLOG）；ROADMAP 章节与总览表按迭代号排序、陈旧状态对齐（12 处标题）；验收清欠清单落地（大部分验收 2026-08-17 已通过，欠账仅 5 项，见 docs/ACCEPTANCE-BACKLOG.md）。
- **P1 移除死重**：Studio（WPF，冻结存档可找回）与其测试、prototypes/web-designer 移除；AndroidHost README 实验性警示；ClientPackages / PluginPackages 抽共享 FilePackageService 基类。
- **P2 数据层（决策 #77）**：服务端领取原子化（UPDATE...RETURNING，多实例不重复领取）；列表 / 回报同连接加载消除 N+1；ServerService 全局信号量收窄到提交路径（多设备并发不再排队）；打印 Worker 空转 EXISTS 轻量探测（空闲不再每 200ms 全量加载作业）；TransportManager 读写加锁。
- **P2 异常契约（决策 #78）**：两宿主共享全局异常处理（未捕获异常 500 + LF_INTERNAL_001，不透堆栈 / 路径）；上传端点确定性错误 400、意外故障 500（原一律 400 且透出 ex.Message）；render 端点 base64 非法 400（原裸 500）。
- **P2 结构**：Program.cs 拆分——WinHost Main 888→372 行、Server 344→141 行，端点按域分组入 Api/ 目录。
- **P2 测试**：新增 LabelFrame.Api.Tests（TestServer HTTP 集成，10 用例）覆盖共享端点层，含预览 DPI / 错误码 / base64 / Excel 回环回归锚点。
- **P2 安全（决策 #79）**：局域网信任模型显式化（攻击面 / 缓解 / 升级触发条件：插件包签名优先于 API 鉴权）；插件包不加下载哈希（三层校验已覆盖损坏，同信道哈希对主动篡改无意义）。
- dotnet build 0 错误 0 警告；dotnet test 300 全绿（Studio 移除后基线 315→300）。

## 迭代 27 工程治理 P0（日常 CI + API 契约与端点去重 + 文档重组）· 2026-08-25

- **日常 CI（ci.yml）**：push master / PR 触发 dotnet 构建 / 测试 + 前端 lint / 双模式测试 / 构建（命令与发版流水线一致），主干回归提交即发现；同分支新推送自动取消旧运行；不改动发布流水线。
- **新增共享库 LabelFrame.Api**：Server / WinHost 重复的 9 个 DTO 与模板 / 调试出图 / Excel / 日志端点（约 250 行）收敛为共享实现（端点经 Options 传入各自错误码，对外错误码不变）；xlsx 文本解析下沉 Core（ExcelTableReader）；ApiErrorCodes 统一错误码注册表（替换 WinHost 端点魔法字符串）。
- **修复两宿主漂移缺陷**：WinHost 模板预览 DPI 硬编码 203 → 取宿主配置（原非 203 DPI 时预览与打印不一致）；预览统一 Skia 与打印同源（原 GDI 不同源）、无请求数据回退模板 testData；模板不存在错误码 LF_JOB_001（误用）→ LF_TPL_001（Server 保持 LF_SRV_006）；ErrorView 统一 Code / Message / FieldKey；render-image(s) 图片资源解析统一（base64 附带优先、按名回退模板库）。
- **文档重组**：README 重写（三类角色快速开始：文员单机 / 管理员多机 / 业务系统开发者；部署形态对照；216 → 约 110 行）；新增 docs/DEPLOY.md（MSI / Docker / Ubuntu / 管理界面插件 / 分发 / 签名 / 配置）。
- dotnet build 0 错误；dotnet test 315 全绿（Core 108 / Server 45 / Studio 25 / WinHost 137）。

## v0.20.2 验证发布 — 2026-08-18

- **流水线验证**：推送 0.20.2 tag 再次验证 GitHub Actions 发布流水线稳定（内容与 v0.20.1 相同，无功能变更）；全流程（测试 / 打包 / ghcr 镜像 / Release）通过，产物同上；ServerOptions 版本号同步 0.20.2。

## v0.20.1 验证发布 — 2026-08-18

- **流水线验证**：推送 0.20.1 tag 验证 GitHub Actions 发布流水线稳定（内容与 v0.20.0 相同，无功能变更）；全流程（测试 / 打包 / ghcr 镜像 / Release）通过，产物同上；ServerOptions 版本号同步 0.20.1。

## v0.20.0 发布（迭代 24：客户端批次作业 Batch Print）— 2026-08-18

- **自动发布**：推送 0.20.0 tag 触发 GitHub Actions——dotnet 全量测试 + 前端双模式测试/构建通过后，Server Docker 镜像推 ghcr.io（ghcr.io/marci-labs/labelframe-server:0.20.0 / latest），Server / Client MSI + 服务端 webui 插件 zip + Linux 归档上传 GitHub Release；ServerOptions 版本号同步 0.20.0（/api/server/info 与 tag 一致）。

## 迭代 24 完成（客户端批次作业 Batch Print）— 2026-08-18

- **迭代状态 ✅ 已完成**：前后端合入 master（67214c3）→ 端到端联调通过（附五）→ Serilog 日志命名待修项修复（pp-20260818.log）；dotnet build / dotnet test 315 全绿、web pnpm test 双模式 219×2 全绿。服务端零改动（跨端契约不变）；下一轮迭代 26（Niimbot 蓝牙打印机插件，顺延自本迭代）。

## 迭代 24 联调通过 + Serilog 日志命名修正 — 2026-08-18

- **端到端联调通过（附五）**：前后端合入 master（67214c3）后联调——API 契约（GET/POST /api/host/print-settings：默认值 / 保存即生效 / 400 校验 / Normalize）/ 设置页「打印批次」卡片（渲染 / 开关联动禁用 / 保存提示 / 旧 WinHost 404 降级）/ E2E（Server 100 张 → 批次 10/500ms → Serilog 100 条逐张日志 + 节流 9 次、批界间隔 ≈ 500ms → 终态 Completed）全部通过，前端零缺陷。
- **后端待修项已修复**：Serilog 日志文件名含字面 {Date}（产物 pp-{Date}20260818.log）——实证 Serilog.Sinks.File 5.0.0 下 {Date} 为字面量（「去掉 rollingInterval 保留 {Date}」同样不替换），已改为 pp-.log + RollingInterval.Day，实际产物 pp-20260818.log 按天滚动正常；§5.6 同步更正。

## 迭代 24 后端实施完成（WinHost）— 2026-08-18

- **PrintSettings（选项模型）与 PrintSettingsStore**：新增 PrintSettings（默认 关闭 / batchSize 10 / batchIntervalMs 500；读取 Normalize：缺失 / 损坏 / 越界统一回默认值——batchSize<1→10、batchIntervalMs<0→500、batchEnabled 非 bool→false；保存校验返回中文原因）+ PrintSettingsStore（%LOCALAPPDATA%\LabelFrame\print-settings.json，原子写：先写临时文件再替换，与 HostConfigStore 同模式）。
- **API**：新增 GET/POST /api/host/print-settings——POST 校验 batchSize≥1 / batchIntervalMs≥0（非法 400 + 中文原因）、仅回环可写（非回环 403）、保存即生效（更新内存单例，无需重启）；PrintSettings 单例注入 JobPrintWorker，读写 lock 保证跨线程可见性（评审 #8）。
- **JobPrintWorker 批次节流**：「发送前暂停（claim-then-delay）」——ClaimNextItemAsync 领取下一张后、SendAsync 前，若 nabled && sendsSinceBatch>0 && sendsSinceBatch%batchSize==0 先 wait Task.Delay(batchIntervalMs, stoppingToken)；发送成功、CompleteItemAsync 后计数 +1（内存态、跨作业全局累计、不持久化，重启清零）；判定抽为 BatchPrintPolicy.ShouldPauseBeforeSend 纯函数保证可测；本机与服务端作业统一生效；测试页直发不计入计数；队列 / 幂等 / 挂起恢复 / 重打语义零改动（LabelJobQueue / JobSubmissionService / ServerRoutingWorker / ServerService / RoutingJson 均未改）。
- **Serilog 文件日志**：WinHost 引入 Serilog.AspNetCore（传递依赖 Serilog.Sinks.File，无需单独引用）→ %LOCALAPPDATA%\LabelFrame\logs\app-20260818.log（app-.log + RollingInterval.Day、时间戳 + 级别），JobPrintWorker 逐张 ILogger 日志落盘带时间戳，供端到端冒烟断言批间间隔；host.log（hostLogWriter）通道不动，两套日志分开文件。
- **测试**：WinHost 新增 50 个（PrintSettings Normalize / 校验、PrintSettingsStore 兜底 / 原子写、API GET 兜底含越界 Normalize / POST 400 / 非回环 403、BatchPrintPolicy、Worker 节流集成 FakeTransport 时间序列——25 张/批 5 → 第 6/11/16/21 张前各停一次共 4 次、跨作业 5+5 → 第 5 张后 B 首张前等待一次、不足一批不等待、禁用无间隔）；dotnet build 0 错误、dotnet test 315 全绿（Core 108 / Server 45 / Studio 25 / WinHost 137）。
- **前端**（web 设置页「打印批次」卡片 + API client + 测试）由并行会话实施；端到端冒烟（Server 100 张 → 批次 10 / 500ms → Serilog 时间戳断言 ≈500ms → 终态 Completed）待两会话合并后执行。

## 迭代 24 设计定稿：客户端批次作业（Batch Print）— 2026-08-18

- **迭代主题调整（用户提出）**：迭代 24 改为「客户端批次作业（Batch Print）」（🔄 进行中）；Niimbot 蓝牙插件顺延至迭代 26；Android PDA 排期以 ROADMAP「延后至迭代 25」为准（DESIGN.md 两处已同步修正）。
- **设计方案定稿**：docs/archive/ITERATION-24-BATCH-DESIGN.md 经 hermes 两轮评审「可定稿」（第一轮 10 条意见 + 第二轮复核；附四落实两处 💡 建议：app-{Date}.log 滚动命名、§4.1 措辞）。
- **关键设计决策**：不拆作业、只在 JobPrintWorker 发送层节流；「发送前暂停（claim-then-delay）」统一语义（100 张/批 10/500ms = 9 次停顿 ≈ 4.5s，末批后不等待）；批次计数全局累计、内存态（重启清零）；设置读取 Normalize（缺失/损坏/越界回默认值）；WinHost 引入 Serilog 文件日志（app-{Date}.log，RollingInterval.Day）解决 ILogger 逐张日志不可见；测试页直发不计入批次计数；服务端零改动。
## 迭代 23 收尾：前端重做完成 + loadError 补充 + 0.19.0 打包 — 2026-08-17

- **前端重做完成（hermes，4dcdbce）**：按任务书 docs/archive/ITERATION-23-FRONTEND-TASK.md（含 hermes 评审附一 + 主 Agent 拍板附二）实施——`types.ts` / `client.ts` 插件包 API（serverApi.list/upload/delete/downloadPluginPackage + localApi.listInstalledPlugins/installPlugin/uninstallPlugin + pluginPackageDownloadUrl）；`pluginLimits.ts` 64MB 预检纯函数；Server UI「插件管理」页（invalid 红标+原因）；客户端设置「插件管理」卡片（可用插件四态 + 覆盖安装确认 + 已安装四态徽标 + manual 只读 + 旧版本 404 区分）；TabId 增 plugin-packages + puzzle 图标；web 212 用例全绿（双模式）。
- **loadError 结构化补充（前端评审三.1 拍板）**：`PluginDirectoryLoader.LoadWithErrors` 透出逐 DLL 失败原因 → `PluginInstaller.ListInstalled` 合并输出 `loadError`（package 按目录 / manual 按路径匹配）；端到端实证坏 DLL 包 loaded=false + loadError=Bad IL format.；dotnet 265 全绿。
- **0.19.0 本地测试包**：Client / Server MSI + `labelframe-server-webui-0.19.0.zip` + 示例 `.lfplugin`（合法 `labelframe-transport-sample-1.0.0.lfplugin` / 坏 DLL 演示包）打包验收；ServerOptions 版本号同步 0.19.0。
- **迭代 24 主题调整（用户提出）**：下一轮改为「Niimbot 蓝牙打印机传输插件实现 + 真机测试」（补需求 P1 蓝牙缺口）；精成打印机顺延。

## 迭代 23 客户端插件分发实施完成 — 2026-08-17

- **范围定稿（2026-08-17 用户拍板决策 1A-7A，规格 [docs/archive/ITERATION-23-SPEC.md](docs/archive/ITERATION-23-SPEC.md)）**：插件包上传服务端（zip + 根 manifest.json + 插件 DLL，后缀 `.lfplugin`；独立 `plugin-packages` 目录 + `/api/plugin-packages`）；客户端安装（设置页「插件管理」卡片浏览服务端可用插件 → 下载 → 三层校验 → 解压到 `plugins/<pluginId>/` → 重启生效）；客户端卸载（删目录 → 重启生效，热卸载仍不做）；包大小上限 64MB；不做签名（局域网无鉴权模型）；与「更新与安装包」UI 并列。
- **Core（插件包 + 加载演进）**：`Transport/Plugins/Package/`——`PluginPackageManifest`（pluginId/name/version 必填 + 可选 description/author/minHostVersion）+ `PluginPackageReader`（zip 读取：根 manifest 校验 / zip-slip 防护 / 安全解压）+ `PluginPackageLimits`（64MB）；`PluginDirectoryLoader` 演进——平铺 + 子目录扫描（`plugins/<pluginId>/` 安装包，决策 3A）+ **字节加载（LoadFromStream，不锁 Windows DLL）**；注册表 `RegisterExternal`（外部插件禁止覆盖内置插件 ID，决策 6A）；`PluginProbe`（安装预检：临时 collectible ALC 字节加载发现插件并核对 id）；`SafeFileName`（由 ClientPackagesService 提取共享的文件名规范化，防路径穿越）。
- **WinHost（插件安装 / 卸载 API）**：`PluginInstaller` 服务（安装三层校验 / 覆盖安装 / 卸载 / 已安装列表合并注册表 loaded 状态）+ `GET /api/plugins/installed`、`POST /api/plugins/install`（multipart）、`POST /api/plugins/uninstall`（失败统一 400 ErrorView）；Kestrel `MaxRequestBodySize` 64MB；卸载当前连接引用插件后重启回退默认连接（复用迭代 22 附五兜底）。
- **Server（插件包 API）**：`PluginPackagesService` + `GET/POST/GET/DELETE /api/plugin-packages`（列表含元数据与 valid/invalid 状态、上传即校验、路径穿越防护）；`ServerOptions.PluginPackagesPath` + `LABELFRAME_SERVER_PLUGIN_PACKAGES`；Kestrel 64MB；`docker-compose.yml` 挂载 `./plugin-packages`。
- **前端（待 hermes 独立实施）**：任务书 docs/archive/ITERATION-23-FRONTEND-TASK.md（契约 / 清单 / 待评估设计点）；主 Agent 代跑提交 9faae3a 已回滚（2026-08-17，协作方式按用户指定：前端由 hermes 独立评估与实施）。
- **测试**：dotnet 259 全绿（Core 104 / Server 45 / WinHost 85 / Studio 25，新增插件包读取 / zip-slip / 安全解压 / SafeFileName / 注册表防覆盖 / 加载器子目录 / PluginInstaller 安装卸载覆盖 / plugin-packages 上传删除路径穿越大小上限）；web 207 全绿（22 文件，+28 用例）。
- **端到端联调冒烟通过（16 步）**：上传 .lfplugin → 服务端列表元数据 → 客户端安装（重启前 loaded=false）→ 重启后装配 / loaded=true → 配置启用（SAMPLE(SMOKE)）→ 卸载成功（**字节加载修复 Windows 文件锁**，决策 #73）→ 重启后消失 → 卸载当前连接插件后重启回退 log（附五兜底）→ Server 删除插件包。
- **关键决策（DESIGN #72/#73）**：插件包分发闭环（格式 / 目录 / 校验 / 覆盖 / UI 并列）；外部插件字节加载（`LoadFromStream` 不锁文件，「卸载 = 删除文件 + 重启生效」在 Windows 下真正可用；插件 `Assembly.Location` 为空，自定位资源改用上下文数据目录）。
- **loadError 结构化补充（2026-08-17 前端评审三.1 拍板）**：`PluginDirectoryLoader.LoadWithErrors` 透出逐 DLL 失败原因 → `PluginInstaller` 合并到 `/api/plugins/installed.loadError`；前端「加载失败 err + 原因」真实可触发（端到端：坏 DLL 包 loaded=false + loadError=Bad IL format.）；dotnet 265 全绿（Core 108 / Server 45 / WinHost 87 / Studio 25）。
## 迭代 22 后端实施完成 — 2026-08-17

- **Core（传输插件机制，决策 #67-69）**：`LabelFrame.Core.Transport.Plugins`——统一接口 `ITransportPlugin`（Id / DisplayName / Description / Parameters / Describe / Create）+ 传输实例继续用 `IPrintTransport` / `IPrinterStatusProvider` / 新增 `ITestableTransport`（连接测试）；参数模型 `TransportParameterSpec` / `TransportPluginParameters` / `ITransportPluginContext`；注册表 `ITransportPluginRegistry`（内置 log / tcp9100 + WinHost 内置 winspool / zebra + 外部 DLL 目录扫描）；`PluginDirectoryLoader`（collectible ALC，单插件失败只记日志，卸载 = 删文件 + 重启生效）。
- **Core（Excel 模板生成，决策 4A）**：`LabelFrame.Core.Excel.ExcelTemplateWriter`（复用 `TemplateFrame.Excel.Simple` 写能力）→ `POST /api/import/excel-template`（Server 与 WinHost 都实现，契约字段 + testData 生成 xlsx）。
- **WinHost（传输插件化）**：`connection.json` 新格式 `{ pluginId, params }` + 旧格式自动迁移（`Mode` / `TcpHost` 等 → pluginId + params）；`TransportManager` 改走注册表装配（校验 / 测试 / 切换 / 持久化）；`GET/POST /api/transport` 扩展（pluginId / displayText / availablePlugins spec，旧字段 mode / availableModes 保留兼容）+ 新增 `GET /api/transport/plugins`；`LABELFRAME_PLUGINS` 插件目录（默认 `%ProgramData%\LabelFrame\Client\plugins`）。
- **Server（权限边界 + 分发）**：`GET /api/jobs?deviceId=` 过滤（客户端只看自己的作业、服务端看全部）；`client-packages` 目录 + 列表 / 上传 / 下载 / 删除 API（文件名路径穿越防护，`LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）；`docker-compose.yml` 挂载 `./client-packages`。
- **测试**：214 全绿（Core 78 / Server 37 / WinHost 74 / Studio 25，新增插件注册表 / 参数模型 / 目录加载器（示例插件 DLL 项目）/ Excel 生成 / TransportConfig 迁移 / 插件切换 / deviceId 过滤 / client-packages 用例）；web 178 全绿。
- **端到端冒烟通过**：上传 / 列表 / 下载 / 删除 client-packages、路径穿越 404、Server/WinHost excel-template（xlsx 200）、jobs 按 deviceId 过滤、/api/transport（availablePlugins + displayText + 外部 sample 插件可装配可测试）、切换失败保留当前连接、旧格式 mode 兼容。
## 迭代 22 联调收尾：后端缺陷修复 — 2026-08-17

- **修复（附四 §三）外部插件删除后宿主启动崩溃**：connection.json 仍引用已删除的外部插件 DLL（决策 2A「卸载 = 删除文件 + 重启生效」）时，`TransportManager.LoadPersisted` 检测插件不在注册表 → 回退默认连接（log）+ host.log 警告，宿主正常启动、不再抛「传输插件不存在」退出；新增回归用例（Startup_should_fall_back_to_log_when_persisted_plugin_missing）。
- **healthz 透出插件信息**：响应新增 `pluginId` / `displayText`（精确反映当前插件；旧字段 `transport` 保留兼容），前端联调观察项落实。
- 前端联调修复（hermes，54e77c9）：Excel 列映射按显示名自动匹配、插件参数全量字符串序列化（修复 HTTP 400）、null 默认值不显示字面量——web 179 用例全绿。
- 测试：dotnet 215 全绿（Core 78 / Server 37 / WinHost 75 / Studio 25）；web 179 全绿。
## 迭代 22 完成 + 迭代 23 计划调整 — 2026-08-17

- **迭代 22 完成（用户确认迭代结束）**：打印测试体验 + 传输插件化 + 客户端下载分发全部交付——前后端实现、联调 8 项场景全过、后端缺陷修复、前端 4 处修复（含剪贴板降级）；dotnet 215 / web 179 全绿；本地 0.18.0 测试包（`LabelFrame-Client-0.18.0.msi` / `LabelFrame-Server-0.18.0.msi` / `labelframe-server-webui-0.18.0.zip`）打包验收通过；ROADMAP 状态更新为已完成。
- **迭代 23 主题调整（2026-08-17 用户提出）**：下一轮改为「客户端插件分发——传输插件包上传服务端 + 客户端选择安装 / 卸载已安装插件」（范围会话中细化）；原「精成打印机传输插件实现 + 真机测试」顺延为迭代 24；Android PDA 宿主延后至迭代 25。
- **版本号**：ServerOptions.ProductVersion 同步 0.18.0（/api/server/info 与 MSI 一致）。
- **打包修复（040e0db，迭代 22 收尾）**：build:server 未设 VITE_UI_MODE 导致 Server UI 插件被打成 client 模式——脚本内联 cross-env + release.yml 产物自检；本地 0.18.0 包已按修复重建（webui 插件为真正 server 模式）。
- **前端 UI 修复（23789ba）**：连接方式插件参数表单必填星号与标签分行（并入同一 flex 子项）；web 187 用例全绿，已纳入重建的 Client 0.18.0 包。
## 迭代 22 规划：范围定稿 + 后端 / 前端分工 — 2026-08-17

- **范围定稿（用户拍板）**：打印测试体验（下载 Excel 模板、客户端仅本机打印测试、显示本机设备名、作业历史按设备可见）+ 传输插件化（统一接口 / 参数模型 / 注册表按需装配）+ 客户端下载分发（服务端 `client-packages` 目录 + 上传下载 API + 管理界面 + 客户端设置默认从服务端获取）。
- **四项决策**：① 客户端本机打印测试 = 在线走服务端路由、未注册 / 离线降级本机直连并提示；② 插件卸载 = 删除文件 + 重启生效（运行时热卸载记未决）；③ 安装包 = 页面上传 + 目录直放都支持，**Ubuntu / Docker 允许挂载 `client-packages`**；④ Excel 模板生成放 Core 共享（复用 `TemplateFrame.Excel.Simple` 写能力）。
- **分工**：后端由主 Agent 负责、前端由 hermes 负责；新增规格文档 `docs/archive/ITERATION-22-SPEC.md`（§5 契约 / §6 后端拆分 / §7 前端拆分 / §8 验收）；后端等待前端评估后并行开工。
- **DESIGN.md 决策 #67-71**：传输插件统一接口与参数模型 / 加载卸载使用 / connection.json 兼容演进 / 打印测试体验与权限边界 / 客户端下载分发；未决新增「传输插件运行时热卸载（ALC）」。
- 不推 tag；仓库内容无公司 / 业务线品牌字样。
## 迭代 21 收尾 + 状态盘点 — 2026-08-17

- **SQLitePCLRaw 升级 2.1.6 → 2.1.13**：修复 GHSA-2m69-gcr7-jv3q（SQLite 原生库漏洞）；Core / Server / AndroidHost 三项目同步升级，移除 NU1903 抑制；构建 0 警告 0 错误，测试 176 全绿。
- **验收状态确认（用户）**：真实打印机验收通过（迭代 2 / 3 / 4、13、14、15）；Studio 界面验收通过（迭代 7 / 8 / 8B / 8C / 8D）；双包 / 无头服务端联调完成（迭代 16 / 18 / 19 / 20）；试点验收通过（扫码枪 50 张 + 连续 100 张压力验证，含重启 / 断网）——ROADMAP 状态表已更新。
- **迭代 12 关闭**：前端 renderLabelImage 按用户决定取消（不再实施）；后端 previewValue / PrintMode 图片打印能力保留。
- **契约字段 Pattern 校验**：列为未来事项（现阶段不处理）。
- **迭代计划调整（2026-08-17 晚）**：用户提出新需求（打印测试体验 / 传输插件化 / 客户端下载分发），迭代 22 主题调整为本轮需求；精成打印机插件排入迭代 23（下一轮）；Android PDA 延后至迭代 24。
- **MSI 签名 Secret**：暂无证书，保持可选（有则自动签名、无则跳过）。

## 迭代 21（自动化发布）— 2026-08-12

- **首次自动发布成功（v0.17.0）**：推送 `v0.17.0` tag 后 CI 全流程通过——dotnet + 前端双模式测试 → 打包 `LabelFrame-Server-0.17.0.msi` / `LabelFrame-Client-0.17.0.msi` / `labelframe-server-webui-0.17.0.zip` / `labelframe-server-0.17.0-linux-x64.tar.gz` → 推送 `ghcr.io/marci-labs/labelframe-server:0.17.0` 与 `:latest` → 创建 GitHub Release（附件齐全）。
- 仓库已转移至组织 `marci-labs` 并转为公开；本地 remote 同步更新。
- CI 稳定性修复：TCP 状态测试改为服务端就绪同步 + 放宽超时；Skia 渲染测试阈值放宽（CJK 字体环境差异）；设备列表测试日期断言时区无关化；SQLitePCLRaw 在测试进程用 `[ModuleInitializer]` 确定性初始化（Server / WinHost / Core 测试）+ `SqliteLogStore` 构造时自行 `SqliteSupport.EnsureInitialized()`（不再依赖宿主先初始化）。
- 打包链修复：WiX 用 dotnet tool 版并安装 NetFx 扩展 + 先接受 OSMF EULA（WIX7015）；artifact 下载路径对齐 `web/dist` 与 `web/dist-server`；插件打包步骤改用 `$?` 判断（脚本内无原生命令时 `LASTEXITCODE` 为空）。
- 待办（2026-08-15 更新）：ghcr 包已设为 Public 并通过匿名 `docker pull ghcr.io/marci-labs/labelframe-server:0.17.0` 验证（注意：组织包可见性不支持 REST API 修改，须先由组织管理员在「组织设置 → Packages」允许公开包，再在包设置页改为 Public）；MSI 签名 Secret 可随时补充（有则自动签名，可选）。

## 迭代 22 后端实施完成 — 2026-08-17

- **Core（传输插件机制，决策 #67-69）**：`LabelFrame.Core.Transport.Plugins`——统一接口 `ITransportPlugin`（Id / DisplayName / Description / Parameters / Describe / Create）+ 传输实例继续用 `IPrintTransport` / `IPrinterStatusProvider` / 新增 `ITestableTransport`（连接测试）；参数模型 `TransportParameterSpec` / `TransportPluginParameters` / `ITransportPluginContext`；注册表 `ITransportPluginRegistry`（内置 log / tcp9100 + WinHost 内置 winspool / zebra + 外部 DLL 目录扫描）；`PluginDirectoryLoader`（collectible ALC，单插件失败只记日志，卸载 = 删文件 + 重启生效）。
- **Core（Excel 模板生成，决策 4A）**：`LabelFrame.Core.Excel.ExcelTemplateWriter`（复用 `TemplateFrame.Excel.Simple` 写能力）→ `POST /api/import/excel-template`（Server 与 WinHost 都实现，契约字段 + testData 生成 xlsx）。
- **WinHost（传输插件化）**：`connection.json` 新格式 `{ pluginId, params }` + 旧格式自动迁移（`Mode` / `TcpHost` 等 → pluginId + params）；`TransportManager` 改走注册表装配（校验 / 测试 / 切换 / 持久化）；`GET/POST /api/transport` 扩展（pluginId / displayText / availablePlugins spec，旧字段 mode / availableModes 保留兼容）+ 新增 `GET /api/transport/plugins`；`LABELFRAME_PLUGINS` 插件目录（默认 `%ProgramData%\LabelFrame\Client\plugins`）。
- **Server（权限边界 + 分发）**：`GET /api/jobs?deviceId=` 过滤（客户端只看自己的作业、服务端看全部）；`client-packages` 目录 + 列表 / 上传 / 下载 / 删除 API（文件名路径穿越防护，`LABELFRAME_SERVER_CLIENT_PACKAGES` 可覆盖）；`docker-compose.yml` 挂载 `./client-packages`。
- **测试**：214 全绿（Core 78 / Server 37 / WinHost 74 / Studio 25，新增插件注册表 / 参数模型 / 目录加载器（示例插件 DLL 项目）/ Excel 生成 / TransportConfig 迁移 / 插件切换 / deviceId 过滤 / client-packages 用例）；web 178 全绿。
- **端到端冒烟通过**：上传 / 列表 / 下载 / 删除 client-packages、路径穿越 404、Server/WinHost excel-template（xlsx 200）、jobs 按 deviceId 过滤、/api/transport（availablePlugins + displayText + 外部 sample 插件可装配可测试）、切换失败保留当前连接、旧格式 mode 兼容。
## 迭代 22 联调收尾：后端缺陷修复 — 2026-08-17

- **修复（附四 §三）外部插件删除后宿主启动崩溃**：connection.json 仍引用已删除的外部插件 DLL（决策 2A「卸载 = 删除文件 + 重启生效」）时，`TransportManager.LoadPersisted` 检测插件不在注册表 → 回退默认连接（log）+ host.log 警告，宿主正常启动、不再抛「传输插件不存在」退出；新增回归用例（Startup_should_fall_back_to_log_when_persisted_plugin_missing）。
- **healthz 透出插件信息**：响应新增 `pluginId` / `displayText`（精确反映当前插件；旧字段 `transport` 保留兼容），前端联调观察项落实。
- 前端联调修复（hermes，54e77c9）：Excel 列映射按显示名自动匹配、插件参数全量字符串序列化（修复 HTTP 400）、null 默认值不显示字面量——web 179 用例全绿。
- 测试：dotnet 215 全绿（Core 78 / Server 37 / WinHost 75 / Studio 25）；web 179 全绿。
## 迭代 22 完成 + 迭代 23 计划调整 — 2026-08-17

- **迭代 22 完成（用户确认迭代结束）**：打印测试体验 + 传输插件化 + 客户端下载分发全部交付——前后端实现、联调 8 项场景全过、后端缺陷修复、前端 4 处修复（含剪贴板降级）；dotnet 215 / web 179 全绿；本地 0.18.0 测试包（`LabelFrame-Client-0.18.0.msi` / `LabelFrame-Server-0.18.0.msi` / `labelframe-server-webui-0.18.0.zip`）打包验收通过；ROADMAP 状态更新为已完成。
- **迭代 23 主题调整（2026-08-17 用户提出）**：下一轮改为「客户端插件分发——传输插件包上传服务端 + 客户端选择安装 / 卸载已安装插件」（范围会话中细化）；原「精成打印机传输插件实现 + 真机测试」顺延为迭代 24；Android PDA 宿主延后至迭代 25。
- **版本号**：ServerOptions.ProductVersion 同步 0.18.0（/api/server/info 与 MSI 一致）。
- **打包修复（040e0db，迭代 22 收尾）**：build:server 未设 VITE_UI_MODE 导致 Server UI 插件被打成 client 模式——脚本内联 cross-env + release.yml 产物自检；本地 0.18.0 包已按修复重建（webui 插件为真正 server 模式）。
- **前端 UI 修复（23789ba）**：连接方式插件参数表单必填星号与标签分行（并入同一 flex 子项）；web 187 用例全绿，已纳入重建的 Client 0.18.0 包。
## 迭代 22 规划：范围定稿 + 后端 / 前端分工 — 2026-08-17

- **范围定稿（用户拍板）**：打印测试体验（下载 Excel 模板、客户端仅本机打印测试、显示本机设备名、作业历史按设备可见）+ 传输插件化（统一接口 / 参数模型 / 注册表按需装配）+ 客户端下载分发（服务端 `client-packages` 目录 + 上传下载 API + 管理界面 + 客户端设置默认从服务端获取）。
- **四项决策**：① 客户端本机打印测试 = 在线走服务端路由、未注册 / 离线降级本机直连并提示；② 插件卸载 = 删除文件 + 重启生效（运行时热卸载记未决）；③ 安装包 = 页面上传 + 目录直放都支持，**Ubuntu / Docker 允许挂载 `client-packages`**；④ Excel 模板生成放 Core 共享（复用 `TemplateFrame.Excel.Simple` 写能力）。
- **分工**：后端由主 Agent 负责、前端由 hermes 负责；新增规格文档 `docs/archive/ITERATION-22-SPEC.md`（§5 契约 / §6 后端拆分 / §7 前端拆分 / §8 验收）；后端等待前端评估后并行开工。
- **DESIGN.md 决策 #67-71**：传输插件统一接口与参数模型 / 加载卸载使用 / connection.json 兼容演进 / 打印测试体验与权限边界 / 客户端下载分发；未决新增「传输插件运行时热卸载（ALC）」。
- 不推 tag；仓库内容无公司 / 业务线品牌字样。
## 迭代 21 收尾 + 状态盘点 — 2026-08-17

- **SQLitePCLRaw 升级 2.1.6 → 2.1.13**：修复 GHSA-2m69-gcr7-jv3q（SQLite 原生库漏洞）；Core / Server / AndroidHost 三项目同步升级，移除 NU1903 抑制；构建 0 警告 0 错误，测试 176 全绿。
- **验收状态确认（用户）**：真实打印机验收通过（迭代 2 / 3 / 4、13、14、15）；Studio 界面验收通过（迭代 7 / 8 / 8B / 8C / 8D）；双包 / 无头服务端联调完成（迭代 16 / 18 / 19 / 20）；试点验收通过（扫码枪 50 张 + 连续 100 张压力验证，含重启 / 断网）——ROADMAP 状态表已更新。
- **迭代 12 关闭**：前端 renderLabelImage 按用户决定取消（不再实施）；后端 previewValue / PrintMode 图片打印能力保留。
- **契约字段 Pattern 校验**：列为未来事项（现阶段不处理）。
- **迭代计划调整（2026-08-17 晚）**：用户提出新需求（打印测试体验 / 传输插件化 / 客户端下载分发），迭代 22 主题调整为本轮需求；精成打印机插件排入迭代 23（下一轮）；Android PDA 延后至迭代 24。
- **MSI 签名 Secret**：暂无证书，保持可选（有则自动签名、无则跳过）。

## 迭代 21（自动化发布）— 2026-08-12（进行中）

- 新增 GitHub Actions 发布流水线 `.github/workflows/release.yml`：推送 `v*` tag 自动测试、打包（Server / Client MSI、管理界面插件 zip、linux-x64 归档）、构建并推送 ghcr.io Docker 镜像、创建 GitHub Release。
- 版本号单一来源：git tag（`v0.17.0` → `0.17.0`），另支持手动触发。
- 打包脚本：新增 `-WixPath` 参数（CI 用 dotnet tool 版 WiX 7.0.0）；MSI 签名证书改走 GitHub Secret（`MSI_SIGN_CERT_BASE64` / `MSI_SIGN_PASSWORD`），有则签名、无则跳过；移除脚本内明文默认密码。
- compose 默认拉取 `ghcr.io/marci-labs/labelframe-server`（`LABELFRAME_VERSION` / `LABELFRAME_IMAGE` 可覆盖）。
- 组织 `marci-labs` 已创建；仓库公开化 / 转移组织待执行。

## 迭代 0（奠基）— 2026-08-08

- 建立文档体系：README（愿景）、AGENTS、DESIGN、REQUIREMENTS、ROADMAP、CHANGELOG。
- 建立解决方案骨架：`LabelFrame.Core` / `LabelFrame.Server` / `LabelFrame.WinHost`（占位），`LabelFrame.AndroidHost` 目录占位。
- 初始化 git 仓库并推送至 GitHub。
## 迭代 1（契约与 ZPL）— 2026-08-09

- `LabelFrame.Core`：契约 / 版式模型（LabelContract、LabelLayout：文本 / 条码 / 二维码 / 图片 / 线，毫米坐标）、LabelDocument。
- 数据校验：必填字段缺失（含空白）拒绝，返回问题码 `LF_VAL_001`。
- ZPL 编码器：文本、Code128（^BC）、图片占位（^FX），毫米 → 点换算（默认 203 dpi）；二维码 / 线元素显式报错待迭代 2。
- 日志传输（模拟打印机）：`LogPrintTransport`。
- 单元测试：库位码 golden test、校验用例、编码器用例、传输用例（14 个，`dotnet test` 全绿）。
- 新增测试项目 `test/LabelFrame.Core.Tests` 并加入解决方案。
## 迭代 2（WinHost 打印闭环）— 2026-08-09

- 全项目升级 .NET 10；WinHost 目标 `net10.0-windows10.0.26100`。
- `LabelFrame.Core`：作业模型 + SQLite 持久化队列（requestId 幂等、逐张状态、挂起 / 恢复 / 取消、批内顺序、重启不丢作业并把在途 Item 重置续打）；LabelBitmap（1bpp）+ ZPL ^GF 位图编码；TCP 9100 传输；版式元素 JSON 转换器（type 判别）。
- `LabelFrame.WinHost`：本地 HTTP API（POST/GET /api/jobs、suspend/resume/cancel、healthz；模板自包含提交）；打印 Worker 串行打印；GDI 中文栅格化（内嵌 / 本地字体优先，回退微软雅黑）；传输：Log / TCP9100 / Windows 驱动（winspool raw）/ Zebra 官方 SDK（TCP / USB 自动发现 / 驱动）。
- 配置：appsettings.json（WinHost 节）+ LABELFRAME_* 环境变量。
- 测试 53 个全绿（队列 / ^GF / TCP / JSON / 栅格化 / raw / Zebra / 提交服务）；端到端冒烟验证通过。
## 迭代 3（Server 路由）— 2026-08-09

- `LabelFrame.Server`：设备注册 / 心跳 / 目录（在线状态）、作业定向投递（requestId 幂等，SQLite 持久化）、宿主轮询领取、结果回报、作业集中查询；测试入口页面；配置 appsettings（Server 节）+ LABELFRAME_SERVER_*。
- `LabelFrame.WinHost`：Server 路由客户端 + 路由 Worker（领取 → 本地队列打印 → 终态回报）。
- 设备离线语义：作业暂存 Pending，上线轮询即领取（不丢作业）。
- 默认端口：WinHost 53960 / Server 53961。
- 测试 65 个全绿；端到端冒烟：提交 → WinHost 领取打印 → 回报 Completed。
## 迭代 4（模板管理 + 预览）— 2026-08-09

- `LabelFrame.Core.Templates`：模板包模型 + zip 导入导出（manifest.json + images/）+ SQLite 模板存储（CRUD / 分组 / 图片资源）。
- `LabelFrame.WinHost`：模板 API（保存 / 列表 / 详情 / 删除 / 导出 / 导入 / 预览）；预览 PNG（GDI 文本与线 + ZXing 条码 / 二维码 + 图片渲染）；ZXing.Net 0.16.11。
- 测试 79 个全绿；冒烟验证：保存 → 预览 PNG → 导出 zip。

## 迭代 5（PDA 宿主）— 2026-08-09

- `LabelFrame.AndroidHost`（net10.0-android）：前台服务 + 开机自启广播、本地 HTTP（127.0.0.1:53970）、IP 9100 传输、Server 注册 / 轮询 / 回报、Android.Graphics 中文栅格化（^GF）、SQLite 作业队列。
- 编译打包成功（Signed APK 约 11MB）；`scripts/build-androidhost.ps1` 一键构建。
- 真机验收（PDA 网页 → Server → 宿主 → IP 打印机、开机自启）待执行；蓝牙在迭代 6。

## 迭代 6（P1 收尾）— 2026-08-09

- 失败项单独重打：`RetryItemAsync`（Failed → Pending，Failed 作业自动恢复）+ API `POST /api/jobs/{jobId}/items/{itemIndex}/retry`。
- 打印机测试页 / 在线状态：`GET /api/printer/status`、`POST /api/printer/test`；TCP `~HS` 基础解析、Zebra 连接即在线、驱动模式不可读回、Log 模拟在线。
- 蓝牙传输随迭代 5 受阻；真实设备字段联调待执行。
## 迭代 7（Studio 模板工具 V1）— 2026-08-09

- `LabelFrame.Studio`（WPF，net10.0-windows）：WinHost 客户端。
  - 连接管理：地址配置、一键启动 / 停止 WinHost、传输模式显示（healthz 新增 transport）。
  - 模板管理：按分组列表、详情（契约字段 + 版式元素）、删除、导出 `.lfpkg`。
  - 模板导入：文件选择 `.lfpkg` → 导入 WinHost 模板库。
  - 测试打印：选模板 → 按契约字段自动生成数据表单 → 预览 PNG → 提交打印作业 → 轮询状态与失败原因。
- 复用 WinHost API，无重复打印逻辑；`StudioClient` 支持注入 HttpClient（可测试）。
- 测试 85 个全绿；界面验收待执行；版式可视化编辑（拖拽画布）为 V2。
## 迭代 8（Studio 版式编排 V2）— 2026-08-09

- `LabelFrame.Studio` 新增版式编辑窗口（EditorWindow）：
  - 画布按 mm 渲染（缩放 50%–250%），元素拖拽移动 / 选中 / 删除。
  - 工具箱添加文本 / 条码 / 二维码 / 图片 / 线元素。
  - 属性面板编辑坐标、尺寸、SourceKey、字体高宽、线宽。
  - 契约字段增删、必填、类型、显示名编辑。
  - 保存（POST /api/templates）+ 刷新预览（WinHost preview PNG）。
- 条码数据仍为纯文本传递，模板元素类型决定条码 / 二维码渲染（无契约变更）。
- 测试 90 个全绿。
## 迭代 8B（Studio 版式增强：字段编辑 / 元素样式 / 区域布局）— 2026-08-09

- 字段编辑：键 Key / 显示名 / 必填 / 类型可编辑；重命名自动同步引用该字段的元素 SourceKey。
- 画布：显眼显示标签尺寸（不随窗口变化）；新元素默认排在上一个下方（上下结构为主）。
- 元素样式（模板包契约扩展，向后兼容）：文本 WidthMm（块宽）/ TextAlign（左/中/右）/ PaddingMm / BorderMm；条码 / 二维码 / 图片 BorderMm。
- 区域（格子）布局：新增 LabelRegionElement 容器；元素可锚定 RegionId + 区域内 H/V 对齐（默认居中）；区域移动元素跟随。
- ZPL 编码：区域边框 ^GB、文本块对齐 ^FB、二维码 ^BQ、线 ^GB（L）；预览渲染同步（共用 LabelLayoutResolver）。
- 测试 99 个全绿。
## 迭代 8C（Studio 界面重构：工作台 + 设计器）— 2026-08-09

- 共享渲染库 `LabelFrame.Rendering`（GDI + ZXing）：预览渲染从 WinHost 抽出，WinHost 与 Studio 共用；Studio 画布 / 预览本地实时渲染。
- 契约扩展：文本 / 条码 / 二维码支持 `Literal` 固定值或 `SourceKey` 字段填充（向后兼容）。
- 作业工作台（主窗口重写）：菜单栏、模板列表、本地预览、数据表单、打印、底部状态栏 + 日志栏。
- 模板设计器（独立窗口）：控件栏点击 / 拖入、画布毫米网格、选择移动、画区域（拖矩形）、元素拖入区域自动锚定居中 / 移出解除锚定、属性分组（位置尺寸 / 文本字体 / 填充 / 内边距边框 / 区域锚定）、测试数据、实时打印预览（节流）、打印测试、底部状态 + 日志。
- 待办（迭代 8D）：拖角缩放、标尺 / 对齐线。
- 测试 105 个全绿。
## 迭代 8D（设计器交互重做）— 2026-08-09

- 设计器重做（`DesignerWindow`）：
  - 设计 / 测试用 Tab 分离：测试 Tab 放测试数据（字段由版式自动推导）、实时打印预览、打印测试。
  - 控件栏改为可拖拽项（文本 / 条码 / 二维码 / 图片 / 线 / 容器），点击添加一次、拖入画布定位，修复“拖拽一次建两个元素”问题。
  - 画布：毫米标尺 + 网格；左键选中、8 手柄拖角缩放、Shift/Ctrl 点击与拖框多选、Delete 删除、中键平移、Ctrl+滚轮缩放（以鼠标为中心）；移动时边缘 / 中心自动吸附到画布与其它元素；右键对齐菜单（左 / 水平居中 / 右 / 上 / 垂直居中 / 下）。
  - 容器控件替代“画区域”：控件栏拖「容器」矩形；元素拖入容器自动锚定居中；属性面板移除 RegionId / 锚定 UI（后台能力保留，模板格式不变）。
  - 属性面板仅在选中元素时显示（默认收起）：单选显示元素属性，多选显示对齐工具。
  - 底部状态 + 日志栏横跨全窗口，日志自动滚动到底、可一键清空。
  - 固定值 / 字段 / 样式修改实时重绘画布并节流刷新打印预览。
- 契约字段后台自动推导：字段集合 = 版式「字段填充」元素 SourceKey 去重（保留旧契约字段顺序与元数据）；移除字段增删 / 重命名 / 显示名 UI；工作台与测试表单统一用 Key 作标签。
- `MainWindow`：数据表单标签改用字段 Key；日志自动滚底 + 清空按钮。
- 测试 109 个全绿（新增字段推导 / 多选删除 / 对齐 / 吸附用例）。
## 迭代 9（Excel 数据导入）— 2026-08-09

- 新增 `ExcelImportService`（Studio 服务层，UI 栈无关）：读取 .xlsx（标题行 + 数据行）、列 → 字段映射建议（Key 忽略大小写匹配）、按行生成标签数据字典；基于 `TemplateFrame.Excel.Simple` 1.0.5。
- 主窗口「导入数据(Excel)…」：选模板 → 选 .xlsx → 映射确认窗口（列 → 字段 Key 可手工调整）→ 批量打印（一次提交多张，复用 `/api/jobs`）→ 轮询作业状态；首行数据自动刷新预览，状态栏显示文件名与行数。
- Web 设计器原型 `prototypes/web-designer/`：Konva 画布（控件栏 / 容器 / 手柄缩放 / 多选对齐 / 中键平移 / Ctrl+滚轮缩放 / 标尺网格）+ WinHost API（连接 / 加载 / 保存 / 预览），用于 UI 技术选型评估（决策 #39）。
- 测试 112 个全绿（新增 Excel 读取 / 映射建议 / 行数据生成用例）。

## 迭代 8E（Web 设计器原型 v2）— 2026-08-09

- 视口缩放模型：画布容器自动铺满视口（随窗口自适应）；Ctrl+滚轮只缩放画布内容（以鼠标为中心）；「适应窗口」/「实际大小」按钮，设计态与真实尺寸预览分离。
- 条码 / 二维码实时渲染：值变化立即渲染真实条码 / 二维码（JsBarcode / qrcode-generator 本地化）；属性面板预留条码（码制 / 底部文字 / 模块宽）与二维码（纠错级别 / 边距）参数分组。
- 智能参考线：拖动时吸附画布边缘 / 中心与其它元素边缘 / 中心并显示参考线（参考 Figma / Konva snapping 做法）。
- 边框修正：边框为矩形元素外框描边，不再描文字。
- 控件栏精简为文本 / 条码 / 二维码（图片 / 线 / 容器移除入口，已有模板仍可加载显示）。
- 文本溢出模式：每元素可配置「自动换行 / 超长截断 / 缩小字体」（参考 BarTender Auto-Fit / Cleverence Label）。
- 原型经 headless 浏览器自测通过（元素添加 / 条码二维码渲染 / 初始化无异常）。

## 迭代 8F（Web 设计器原型 v3）— 2026-08-09

- 画布留白 + 标尺跟随：画布实际大小 = 输入尺寸 + 四周 10mm 留白；标尺以 mm 覆盖整个画布并随画布移动 / 缩放；内容区边缘标蓝刻度。
- 画布平移不越界：中键拖拽 clamp 到可视边界。
- 容器不再手动缩放：默认画布铺满视口；「实际大小」= 1mm=8 点（203dpi 打印比例），可滚动 / 平移查看。
- 文本溢出新增「不限制高度」模式（按内容实际高度显示全部文字）。
- 修复控件拖入不可见：drop 坐标改为基于 clientX/Y 几何换算（原依赖 Konva 指针状态，HTML5 拖拽期间无指针事件导致元素被放到错误 / 越界位置）。
- 核心修复：stage 尺寸 = 逻辑尺寸 × 比例尺（原实现 stage.scale 只缩放绘制内容，canvas 容器仍是逻辑尺寸，放大时内容被 canvas 裁剪 → 网格范围变化 / 拖入元素在裁剪区外不可见 / 标尺错位）。
- 标尺与网格对齐：标尺 0 点与画布左缘对齐（左上角空块布局）；网格覆盖整个画布（含 10mm 留白）并与标尺同比例缩放。
- 适应窗口 / 实际大小统一为「比例尺预设」：设计时按点处理，需要真实比例时再换算点与 mm。
- 第三轮修复：
  - 控件不可见根因：Konva 9.3 的 Text 无 clipFunc，文本渲染抛异常中断 render（网格先画好所以可见）。改为 Group clip 裁剪；条码 / 二维码未绑定显示「未绑定」虚线占位。
  - 标尺画进 Konva（与内容同坐标系），放大 + 平移后标尺 / 网格不再错位；HTML 标尺移除。
  - 中键平移改用原生 DOM + document 级 mouseup 复位（修复松开后仍拖拽的粘滞）。
- 第四轮修复：
  - 吸附 / 位置换算统一用 `getClientRect({ relativeTo: layer })` 逻辑坐标（原用绝对视觉坐标，比例尺放大后吸附与定位偏差 total 倍）。
  - 二维码同步 canvas 渲染（模块遍历手绘，去掉异步 Image 加载）。
  - 属性面板下拉 / 勾选补 commit（修复条码底部文字、码制、纠错等切换不刷新）。
  - 边框 / 内边距通用化（文本 / 条码 / 二维码一致）；文本模式收敛为「缩小适应 / 溢出显示」（文本框 = 遮罩区域，抛弃自动换行 / 不限制高度）。
- 第五轮改进：拉伸文本框不再改变字高（字高独立，遮罩区域变化）；溢出模式文案改「隐藏」；内边距拆上下 / 左右；填充默认固定值，字段填充 = 键名称 + 填充值（预览）；新增 Ctrl+C / Ctrl+V 复制粘贴（偏移 5mm）。
- 第六轮改进：Ctrl+Z / Ctrl+Y 撤销恢复（上限 100 步）；字高调大才撑高文本框；吸附强化（边完全重合 + 参考线醒目）；导出 / 导入设计到剪贴板（labelframe-web-design JSON，Ctrl+Shift+C/V）；控件栏新增矩形控件（边框 + 可选填充，保存映射 region）；文本框基础属性新增高度字段（遮罩高度，与条码 / 二维码一致）。
- 第七轮改进：矩形镂空（仅边框，移除填充色）；新增图层面板（控件列表 / 点击选中同步画布 / 置顶上移下移置底 / 列表 Delete 删除）。
- 第八轮修复：网格吸附兜底（无参考目标时贴最近 1mm 网格，消除 0.2 小数偏移）；字段填充提示明确「打印以外界数据为准，预览值被忽略」。
- 第九轮改进：移除适应窗口 / 实际大小按钮，改为 DPI 选择框（203/300）+「预览打印效果」按钮——按所选 DPI 以真实打印比例显示（203→1mm≈8点、300→1mm≈12点），再点退出回到适应窗口。
- 第十轮改进：文本框自动换行（超过显示区域换行，仍超出整体缩小字体至最小 1.5mm）+ 行间距（默认 1.2）+ 字体选择（雅黑 / 宋体 / 黑体 / 楷体 / Arial / Consolas）；单行保留缩小 / 隐藏。
- 补充：文本垂直对齐（顶端 / 居中 / 底部），配合换行使用。
- 纯前端编辑器化：移除连接 / 模板 / 保存 / 预览 / 导出导入按钮与 IP 等后端元素；导出 / 导入设计保留快捷键（Ctrl+Shift+C / Ctrl+Shift+V）并写入操作提示；顶部仅保留纸张 / DPI / 预览 / 缩放 / 网格；修复中键误触发控件选中（仅左键响应点击）。

## 迭代 11（单机模式，后端部分）— 2026-08-09

- 契约扩展：模板 testData（Core / SQLite / 模板包 / API 全链路，旧库自动迁移）。
- WinHost 演进：Web UI 静态托管（web/dist + SPA fallback）、Excel 导入 API、PDA 日志端点、宽松 CORS。
- AndroidHost 演进：PDA 测试模式（pc_host 配置 / 拉模板列表 / 点击模板用 testData 本地打印 / 终态日志回传 PC / 内置测试页）。
- 前端规格 docs/archive/FRONTEND-SPEC.md 定稿（hermes 两轮审阅全部落定），前端并行开发中。
- 测试 118 个全绿；AndroidHost 编译通过。

## 迭代 10（MSI 安装包）— 2026-08-09

- WinHost 单机 UX：WinExe（无控制台）、启动自动打开浏览器、Log 写 host.log、本机优雅关闭端点。
- 一键打包脚本：publish-winhost.ps1（self-contained + web/dist）+ build-msi.ps1（WiX v7）。
- MSI：桌面 / 开始菜单快捷方式、默认配置、卸载清理；产物 LabelFrame-0.11.0.msi（约 47MB）。
- 发布版冒烟通过；MSI 数据库验证 443 文件 + 2 快捷方式（Target=#WinHostExe）；沙箱无法实际安装 / 签名（Windows Installer 服务与 CryptoAPI 受限），真机验收与签名待执行。
- 名称统一为 LabelFrame（安装目录 / 快捷方式 / 卸载显示）；新增应用图标（蓝底白色 L 型，嵌入 exe 与快捷方式）。
- 新增脚本：generate-icon / create-signing-cert（openssl 自签名 + .NET 重封装）/ cleanup-residue（管理员清理历史残留）。
- 发布改 framework-dependent：MSI 56.5MB → 9.7MB（目标机需 .NET 10 Desktop Runtime）；系统托盘改原生 P/Invoke 实现（无 WinForms 依赖）。
- 安装结构修复：web/dist 与 assets 子目录正确（解决白屏 / JS 404）；安装目录 Program Files\LabelFrame。
- MSI 增加 .NET Desktop Runtime（x64）检测：缺失时全 UI 安装显示带可点击官方下载链接的对话框（MSI Hyperlink 控件，点击直达下载页）；静默 / 基础 UI 由 LaunchCondition 拦截并提示链接；不自动安装（2026-08-10 用户确认放弃 Burn 自动引导方案）。
- 修复运行时误报缺失（2026-08-10）：检测从注册表搜索改为 WiX NetFx 扩展 DotNetCompatibilityCheck（内置官方 NetCoreCheck 自检，检查 x64 Microsoft.WindowsDesktop.App >= 10.0.0、RollForward=latestMajor）。原注册表搜索读 sharedfx 键默认值，而运行时版本号是命名值，且 32 位 MSI 读 32 位视图，导致已装 Desktop Runtime 仍提示未安装；现改为实时自检，装完运行时**无需重启**即可识别。
- 修复托盘 P/Invoke 崩溃（0.11.1，2026-08-10）：`GetCurrentThreadId` / `GetModuleHandle` 被错误声明为从 `user32.dll` 导入（实际在 `kernel32.dll`），托盘线程启动即抛 `EntryPointNotFoundException`，未处理异常直接杀死宿主进程——这是「装完啥也不显示 / 页面打不开」的根因；已改为正确 DLL，并给托盘循环加异常保护：托盘出问题只记日志，不再让宿主退出。
- MSI 改为 **x64 包**（0.11.1，2026-08-10）：此前 MSI 是 32 位包，`ProgramFiles64Folder` 不生效导致装到 `Program Files (x86)`；现 `wix build -arch x64`，安装到 `C:\Program Files\LabelFrame`；版本 0.11.1 支持直接覆盖已装的 0.11.0。
- ZPL 编码器显式输出 `^PW` / `^LL`（0.11.2，2026-08-10）：按模板宽高换算点数（70×50 @203dpi → `^PW559` / `^LL400`），避免打印机沿用旧标签长度导致一张作业走多张纸；新增对应单元测试。
## 迭代 12（模板预览值 + 图片打印，后端部分，2026-08-10）

- 元素 JSON 新增 `previewValue`（字段填充模式预览值持久化，text/barcode/qrcode 非空时输出，旧模板向后兼容）。
- `TemplateStore.SaveAsync` testData 读-改-写：数据库现有值 → 并入显式传入 → 被元素预览值派生覆盖；旧模板显式测试数据不再因前端不传而被清空。
- 新增 `PrintMode`（Vector 默认 / Image）：Image 模式整版渲染 1bpp 位图经 `^GF` 直传打印机，与画布预览同源；`SubmitJobRequest` 支持 `printMode` 覆盖、`template.name` 取模板图片；`/healthz` 返回 `printMode`。
- 修复：预览渲染器文本无显式块宽时被裁成 1px 导致图片打印空白（改为按文本实际宽度绘制）。
- 修复：Log 传输重复打开同一 host.log 触发文件锁、ZPL 被静默丢弃（复用宿主日志写入器）。
- 测试 127 个全绿（新增 previewValue 往返、testData 派生/保留/覆盖、EncodeImage、RenderLabelBitmap、Image 打印模式用例）。
- 产物 `LabelFrame-0.11.3.msi`（2026-08-10）：含迭代 12 前后端合并版（预览值持久化 + 测试默认值 + 打印方式选择 + 图片打印），可覆盖 0.11.x 安装。
- 图片打印调试与清晰度优化（0.11.4，2026-08-10）：打印位图改为单比特网格对齐（去抗锯齿灰度，避免 1bpp 阈值切字发虚）；新增 `POST /api/print/render-image` 与前端「调试：不打印，保存实际打印图片（PNG）」复选框（图片打印方式下显示），用于排查文字清晰度 / 定位是渲染问题还是打印机问题。产物 `LabelFrame-0.11.4.msi`。
- 后端渲染器改为 SkiaSharp（0.11.5，2026-08-10，方案 2）：新增 `SkiaLabelRenderer`（canvas 类 2D 渲染，与前端同源规则：文本超出框宽缩小适应、左中右对齐、内边距/边框、线条/区域、ZXing 条码二维码、模板图片），图片打印与「保存打印图片」均切换；修复 GDI 渲染的 CJK / 右对齐 / 长文本缺失问题（含生僻字开头只匹配小字体、行高为负导致裁剪空矩形）。你的 70×50 模板四个字段（MaterialName / CompanyName / Specification / WarehouseName）端到端验证全部渲染。产物 `LabelFrame-0.11.5.msi`。
- 文本垂直对齐契约与前后端同源（0.11.6，2026-08-10）：文本元素新增 `heightMm` + `verticalAlign`（Top/Middle/Bottom），前端保存时写入元素高度与垂直对齐；Skia / GDI 渲染器按框高垂直对齐绘制，修复「打印比前端预览整体偏上」（此前前端在元素框内垂直居中、后端顶部对齐，且高度未持久化）。端到端验证：MaterialName / CompanyName / WarehouseName 文字均落在框中部。产物 `LabelFrame-0.11.6.msi`。
- 修复 0.11.6 回归（0.11.7，2026-08-10）：无 `heightMm` 的旧模板 + 1mm 内边距时，内框高（字高−2×内边距）被算成负数，裁剪区塌成 1px 导致文字几乎全部消失；现裁剪高度至少一行，旧模板恢复顶部对齐可见，新模板保持居中。产物 `LabelFrame-0.11.7.msi`。

## 迭代 13（文本排版与二维码参数持久化，后端部分，2026-08-10）

- 元素契约补齐第二批字段：文本 `wrap / lineHeight / fitMode / fontFamily`（默认 Microsoft YaHei）、二维码 `qrEcc / qrMargin`（默认 M / 2）、条码 `displayValue`（默认 true）、通用双边内边距 `paddingH / paddingV`（`PaddingHMm / PaddingVMm`，0=未设，缺失时回退 `paddingMm`，`paddingMm` 保留兼容）。
- 决策 A：`VerticalAlign` 默认由 Top 改为 Middle（与前端一致）；`LabelElementJsonConverter` 写规则改「非 Middle 才写」；旧模板无 `heightMm` 时 Skia 渲染器框高兜底 = `max(字高 + 2×最大双边内边距, 10mm)`（与前端读回兜底一致）。
- `LabelElementJsonConverter` 读写：非默认才写（wrap=true、displayValue=false、fitMode 非 shrink、lineHeight 非 1.2、fontFamily 非默认、qrEcc 非 M、qrMargin 非 2、paddingH/V >0），旧模板无新字段读回默认，无数据库迁移（layout 整块 JSON）。
- `SkiaLabelRenderer` 渲染支持：wrap 自动换行 + 行距（lineHeight 倍数）+ 超高整体缩小（最小 1.5mm）、overflow 隐藏裁剪不缩小、fontFamily 字体族（含 CJK 系统回退）、qrEcc / qrMargin 传 ZXing、条码 displayValue 底部数值文字（条码占剩余高度）、文本 / 条码 / 二维码双边内边距内容区（= 元素框减 padding）。
- ZPL 矢量路径不变量：新排版字段不参与 ZPL 编码，矢量输出与现状一致（新增不变量测试）。
- 测试 152 个全绿（新增字段往返 / 省略规则 / paddingMm 兜底 / wrap 换行与超高缩小 / overflow 不缩小 / 字体族 / QR 纠错与静区 / 条码文字 / 双边内边距 / 旧模板默认 Middle / ZPL 不变量）。
## 迭代 13（文本排版与二维码参数持久化，前后端已完成，2026-08-10）

- 前端（hermes）：`convert.ts` 的 `BackendElement` 补齐 `paddingH/paddingV/fontFamily/wrap/lineHeight/fitMode/qrEcc/qrMargin/displayValue`；写方向按契约非默认才写（wrap=true、displayValue=false、verticalAlign 非 Middle、fitMode 非 shrink 等）；读回 `?? 默认`（paddingH/V ?? paddingMm 旧模板兜底）；`ElementNode.tsx` TextContent wrap=true 超高由裁剪改为整体缩小（最小 1.5mm），与后端 Skia 渲染语义一致；convert.test.ts 64 用例全绿（+7 新增）。
- 复现验证：100×60 方案导入 → 保存 → 重开，关键差异清零（wrap / lineHeight / qrEcc / paddingV 均保留；剩余仅默认值显式化，显示一致）。
- 文档归档：`docs/archive/ITERATION-13-SPEC.md` / `docs/archive/ITERATION-13-CONTRACT.md` 标记已完成；ROADMAP 迭代 13 状态更新为「已完成（用户验收待执行）」；DESIGN 决策 #47 更新为前后端完成。
- 产物 `LabelFrame-0.12.0.msi`（2026-08-10）：含迭代 13 前后端合并版（元素契约第二批字段 + Skia 图片打印渲染 + 前端字段映射与 wrap 超高缩小），可覆盖 0.11.x 安装；用户测试验收待执行。
## 迭代 13 前端修复（0.12.1，2026-08-10）

- 修复画布中文长文本字高失真（commit abf58a0）：Konva `wrap='word'` 按空格分词，中文无空格永不换行 → 长文本单行溢出被 shrink 缩小；改为含 CJK 文本用 `wrap='char'` 逐字换行（与 Skia 打印语义一致），纯 ASCII 保持 `word`；shrink 缩小循环按「单行宽 ÷ 内容宽 = 行数」估算换行后总高，只对超高整体缩小（最小 1.5mm），并补 `lineHeight` 依赖。
- 实测：70×50 方案 MaterialName（fontH 3, wrap）修复前单行 1.59mm，修复后两行 2.85mm；64 单测 + build + lint 全绿。
- 产物 `LabelFrame-0.12.1.msi`（2026-08-10）：含该前端修复，可覆盖 0.12.0 / 0.11.x 安装。
## 打包优化（0.12.2，2026-08-10）

- MSI 升级不再覆盖用户配置：`appsettings.json` 从自动文件清单（AppFiles）中剔除，改为 `main.wxs` 中 GUID 固定的独立组件，标记 `NeverOverwrite="yes"`（升级 / 修复不覆盖）+ `Permanent="yes"`（卸载不删除）。新装仍写入默认配置；已改过的配置在后续更新中保留。
- 说明：`appsettings.json` 属于用户数据，卸载时也会保留（与 %LOCALAPPDATA%\LabelFrame 下的数据库一致）；需要全新默认配置时可手动删除该文件后重装。
- 产物 `LabelFrame-0.12.2.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 14（字体加粗 bold 契约，后端部分，2026-08-10）

- 前端（hermes，commit ae16d0d）：属性面板「加粗（打印更清晰）」复选框 + 画布 `fontStyle:'bold'`；convert.ts 契约字段 `bold`（true 才写 / 读回 ?? false）+ 单测；属性面板数字输入受控同步、右侧面板滚动条两项修复。
- 后端：`LabelTextElement.Bold`（bool，默认 false）+ `LabelElementJsonConverter` 写 `bold: true`（true 才写）、读回默认 false，旧模板兼容。
- ZPL（Vector）：新增 `ZplBoldMode`（默认 `FontVariant` 方案 A：粗体字体变体映射 `"0"→"1"`；`WidthScale` 方案 B：宽度 ×1.15 放大兜底）；`ZplEncoder` 构造函数可注入模式与映射表；WinHost `HostOptions.BoldMode` + `LABELFRAME_BOLD_MODE` 环境变量可配置。
- Skia（Image）：测量与绘制字体统一 `SKFont.Embolden = text.Bold`，换行 / shrink 度量按加粗字体计算，与前端预览一致。
- 测试 156 全绿（新增 bold 往返/省略、ZPL 方案 A/B、Skia 加粗墨迹对比）。
## 迭代 14 打包（0.12.3，2026-08-10）

- 含迭代 14 前后端合并版：文本加粗（`bold` 契约 + ZPL 方案 A/B + Skia Embolden）+ 前端加粗设置与属性面板两项修复。
- `appsettings.json` 保留用户配置机制沿用（独立组件 NeverOverwrite + Permanent）。
- 产物 `LabelFrame-0.12.3.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 15（打印设置与会话保留 + 连接管理 + 删除 ZPL，后端部分，2026-08-10）

- 彻底删除矢量 ZPL：移除 `IZplEncoder` / `ZplEncoder.Encode` / `ZplBoldMode` / `PrintMode`（配置 + `LABELFRAME_PRINT_MODE`）/ `SubmitJobRequest.printMode` / `/healthz.printMode` / `ITextRasterizer` / `GdiTextRasterizer` 及对应测试；`^GF` 位图编码重构为 `ZplImageEncoder`；作业项内容统一为整版位图指令（沿用历史列名，无迁移）；README / demo 脚本同步清理。
- 连接管理：`ITransportManager` + `TransportConfig`；`GET /api/transport`、`POST /api/transport`（单一连接、先测试后生效、失败自动回滚、400 沿用 ErrorView、响应统一 `config`=当前生效连接）；持久化 `%LOCALAPPDATA%\LabelFrame\connection.json`（启动优先级 connection.json > appsettings > 默认 Log）；Tcp / Windows 驱动 / Zebra 增加连接测试能力；打印 Worker / 打印机状态 / 测试页统一从管理器取当前连接；测试页改为 Skia 渲染整版位图 ^GF。
- 调试出图：新增 `POST /api/print/render-images`（批量渲染全部行返回 zip，`label-{n}.png`）；保留 `POST /api/print/render-image`（单张 PNG）；调试不建作业、不发驱动、不改作业模型 / SQLite。
- Log 模拟打印：`LogPrintTransport` 只记录摘要（不再写大段指令）；作业层渲染 PNG 保存到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\` 并写 host.log 摘要。
- AndroidHost：新增 `AndroidLabelRenderer`（Android.Graphics + ZXing）整版位图渲染 → `ZplImageEncoder`，替换 ZplEncoder（真机验收待 PDA 联调）。
- 测试 143 全绿（Core 60 / Server 8 / Studio 25 / WinHost 50）；AndroidHost 编译通过；前端（hermes）已实施会话保留 / 连接切换 UI / 调试开关（见下条）。


## 迭代 15（打印设置会话保留 + 连接管理 + 删除 ZPL，前端部分，2026-08-10）

- DataPrint 会话保留（§6.1）：草稿提升全局 AppContext（`printDraft`：selectedName / valuesByTemplate + dirtyKeysByTemplate / debugMode / jobId），sessionStorage 持久化（刷新保留、标签页天然隔离；**禁 localStorage**）；values 按 **key 存在性**合并（用户主动清空的字段不被 testData 顶回）；Excel 数据与列映射不保留。
- 连接管理 UI（§6.2）：AppContext 增 `transportConfig`（GET /api/transport），切换成功后立即用响应 config 更新全局状态；设置页「连接方式」分组（模式单选 Log / TCP / Windows 驱动 / Zebra，只显示当前模式参数，「测试连接」testOnly、「保存并应用」先测试后生效失败回滚）；DataPrint 顶部连接徽标 + 快速切换；状态栏 / 导航徽标显示 mode + 关键参数（TCP 192.168.1.50:9100 等）。
- 调试独立（§6.3）：独立开关（默认关）——开：「调试出图（单张）」走 render-image 下载 PNG、「下载调试图片 zip（N 张）」走 render-images（全部行），不建作业不发驱动，作业进度区提示；关：「打印测试 / 批量打印」正常作业 +「出图预览」即时预览。
- 删除（§3.2）：DataPrint / Settings 的 printMode 下拉与旧调试复选框、`Healthz.printMode` / `SubmitJobRequest.printMode` 类型。
- api client：新增 `getTransport` / `setTransport` / `testTransport` / `renderImages`；下载型端点统一 `fetchBlob`（Content-Disposition 文件名 + ErrorView 错误解析）。
- 测试 91 全绿（新增 27 个：draft 纯逻辑 / 连接切换交互 / 保留与调试按钮行为）；`pnpm build` / `pnpm lint` 通过。

## 迭代 15 打包（0.13.0，2026-08-10）

- 含迭代 15 前后端合并版：彻底删除矢量 ZPL（打印统一 Skia / Android 整版位图 ^GF）；连接管理 `GET/POST /api/transport`（单一连接、先测试后生效、失败回滚、持久化 connection.json）+ Web 设置页 / 数据与打印页连接切换 UI；调试独立（单张 PNG / 批量 zip，不建作业不发驱动）；DataPrint 会话保留（同标签页切视图不丢设置、标签页间不互通）；Log 模拟打印保存 PNG；AndroidHost 图片打印。
- 联调反馈修复：TCP 连接测试加固（IP 直连 + 本地监听开/关回归测试）；前端测试环境 storage 垫片（Node 26 下 jsdom 兼容，91 用例全绿）。
- `appsettings.json` 保留用户配置机制沿用；`%LOCALAPPDATA%\LabelFrame\connection.json` 保存用户连接配置。
- 产物 `LabelFrame-0.13.0.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装。
## 迭代 15 实测修复（连接测试严格化 + Log 输出可见，2026-08-10）

- 连接测试升级：Tcp / Zebra 均改为「连接 + `~HS` 主机状态探测」，无打印机响应判定失败（能连端口 ≠ 打印机），失败不切换、不持久化。
- Log 模拟打印：PNG 保存到 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\`，作业视图新增 `printImageDir` / `printImageCount`，前端作业进度区显示目录与张数（此前用户找不到输出）。
- 新增回归测试：本地监听响应 `~HS` 判定成功、无响应判定失败（两向稳定）。

## 迭代 15 增强（设计器快捷操作说明，2026-08-10）

- 设计器画布顶部常驻核心快捷键提示条（`Ctrl+Z 撤销 · Ctrl+C/V 复制粘贴 · Delete 删除 · 中键平移 · Ctrl+滚轮缩放`），编辑模式随时可见（与预览模式提示同款视觉；预览时自动切换为预览提示）。
- 设计器工具栏新增「快捷键」按钮，弹出完整清单：编辑（撤销重做 / 删除 / 取消放置）、剪贴板（复制粘贴 / 导出导入设计 JSON）、画布（中键平移 / Ctrl+滚轮缩放 / Shift+Ctrl 多选 / 拖拽吸附 / 手柄缩放）三组。
- 测试 95 全绿（新增快捷键清单结构测试 4 个）。

## 迭代 15 打包（0.13.1，2026-08-10）

- 含迭代 15 前后端合并版 + 实测修复：连接测试升级为 `~HS` 打印机探测（能连端口≠打印机，无响应不切换）；Log 模拟打印目录随作业视图显示（`printImageDir` / `printImageCount`，前端进度区展示）；设计器快捷键提示条与「快捷键」弹窗（95 前端用例全绿）。
- 产物 `LabelFrame-0.13.1.msi`（2026-08-10）：可覆盖 0.12.x / 0.11.x 安装；`appsettings.json` 保留机制沿用，连接配置持久化到 connection.json。
## 修复：本地 UI 打开地址规范化（2026-08-10）

- 当 `ListenUrl` 配置为 `0.0.0.0`（局域网访问）时，启动自动开浏览器与托盘「打开主界面」会跳到 `http://0.0.0.0:53960`；新增 `ToLocalUiUrl` 把通配监听地址（`0.0.0.0` / `*` / `+` / `[::]`）规范化为 `127.0.0.1`，本地界面始终可打开。
## 迭代 15 打包（0.13.2，2026-08-11）

- 前端 baseUrl 修复（附九定稿实施）：`getBaseUrl()` 无存储值时默认返回页面自身来源（`window.location.origin`）——PDA 远程访问不再发往自身回环 127.0.0.1；方案 B 自动纠正旧版保存的默认地址残留；新增 `settings.test.ts` 5 用例；`vite.config.ts` dev proxy（`/api` + `/healthz`）配套联调。前端 100 用例全绿。
- 后端（此前已合入）：本地 UI 打开地址规范化（`0.0.0.0` 监听时浏览器/托盘跳 127.0.0.1）、连接测试 `~HS` 探测、Log 模拟打印目录展示。
- 产物 `LabelFrame-0.13.2.msi`（2026-08-11）：可覆盖 0.12.x / 0.11.x 安装；`appsettings.json` 保留机制沿用。
## 迭代 16（服务端 / 客户端拆分，后端骨架，2026-08-11）

- 服务端（LabelFrame.Server）迁入集中能力：模板库（CRUD / 导入导出 / 预览）、作业提交支持 `templateName` 引用（pending 载荷附带模板 + 图片 base64）、调试出图（render-image / render-images，Skia）、设备日志接收与查询、Excel 导入、Web UI 静态托管（SPA fallback）；Server TFM 改 net10.0-windows（引用 Skia 渲染）。
- 客户端（WinHost）配合：`TemplateDto` 增 `Images`（base64），提交服务优先用内联图片否则按 Name 本地加载；路由 Worker 透传 Server 附带模板；`SqliteLogStore` 移至 Core.Logs 供两端共用；单机模式保留。
- 测试 147 全绿（Core 60 / Server 10 / Studio 25 / WinHost 52）；Server 新增 templateName 解析与模板不存在用例。
## 迭代 16/17（服务端 / 客户端拆分，0.14.0，2026-08-11）

- 前端（hermes，e161d81）：移除打印机连接 UI（连接方式 / 打印机分组、连接徽标与快速切换、transport API 与类型）；数据与打印新增目标设备选择（listDevices + targetDeviceId + templateName 提交），404/失败自动降级单机模式；JobView 适配 Server 作业视图；前端 105 用例全绿。
- 双 MSI 打包：`LabelFrame-Server-0.14.0.msi`（→ Program Files\LabelFrame\Server，服务端，默认 0.0.0.0:53961）与 `LabelFrame-Client-0.14.0.msi`（→ Program Files\LabelFrame\Client，打印客户端，默认 ServerUrl=127.0.0.1:53961）；两包 appsettings 保留机制沿用；打包脚本 `build-msi.ps1`（Client）与 `build-server-msi.ps1`（Server），文件清单 GUID 按包加盐避免冲突。
## 打包增强：卸载询问清除用户数据（0.14.0，2026-08-11）

- 两个 MSI 卸载时弹出确认对话框「清除用户数据（默认不勾选）」；勾选则删除本程序产生的数据：
  - Client：`%LOCALAPPDATA%\LabelFrame\` 下 jobs.db / templates.db / logs.db / host.log / connection.json / print 目录 + 安装目录 appsettings.json；
  - Server：`%LOCALAPPDATA%\LabelFrame\server\` 目录 + server.db + 安装目录 appsettings.json。
- 仅手动卸载触发（条件 `REMOVE=ALL AND NOT UPGRADINGPRODUCTCODE`），覆盖升级不会清数据；静默卸载不弹窗、默认保留。

## 迭代 18（决策与规格，进行中，2026-08-11）

- 架构修订（0.15.0）：服务端默认不提供界面（移除 web/dist 托管），客户端（WinHost 127.0.0.1:53960）托管完整 Web UI；模板 / 作业 / 设备投递仍以服务端为中心，作业走服务端队列。
- 服务端 Windows 服务部署（`LabelFrameServer`，LocalSystem）；数据目录默认改 `%ProgramData%\LabelFrame\server`；历史数据定期清理（作业默认保留 30 天、日志默认保留 90 天，可配置，非终态作业不删）。
- 客户端机器级 ServerUrl（WinHost `GET/POST /api/host/config` → `%ProgramData%\LabelFrame\Client\settings.json`）。
- 双 MSI 安装完成弹窗：Server（开机自启 / 立即运行，默认勾选）、Client（立即打开，默认勾选）；升级不触发。
- 规格与任务单：docs/archive/ITERATION-18-SPEC.md；决策登记 docs/DESIGN.md #53-58；架构修订 docs/archive/ARCHITECTURE-SPLIT.md。


## 迭代 18 后端实施（0.15.0，2026-08-11）

- Server 无头化：移除 Web UI 静态托管与测试页（/、/devices、/jobs），仅保留 /healthz 与 API；`GET /api/jobs` 支持 `?limit`（默认 100，上限 500）。
- Server Windows 服务：`builder.Host.UseWindowsService`（服务名 LabelFrameServer，LocalSystem）；控制台模式保留供开发；exe 图标改用 labelframe.ico。
- 数据目录改 `%ProgramData%\LabelFrame\server`（server.db / templates.db / logs.db；服务账户下 LOCALAPPDATA 不可靠）。
- 历史数据定期清理：`DataCleanupService`（启动 60s 后按周期执行，默认 24h）删除终态作业超 `JobRetentionDays`（默认 30 天）与日志超 `LogRetentionDays`（默认 90 天）；非终态作业不删；`ServerDb.DeleteTerminalJobsBeforeAsync` + `SqliteLogStore.DeleteBeforeAsync`。
- WinHost 机器级配置：`GET/POST /api/host/config`（serverUrl + deviceId/deviceName，仅回环可写），持久化 `%ProgramData%\LabelFrame\Client\settings.json`（缺失 / 损坏返回默认值；启动加载覆盖 ServerUrl）。
- WinHost 作业列表：`GET /api/jobs`（limit 默认 100 上限 500），JobView 扩展 CreatedAt / FailedItems / ErrorMessage / TargetDeviceId（本机作业为 null）。
- 双 MSI 0.15.0：Server 安装注册 Windows 服务 + 安装完成弹窗（开机自启 / 立即运行，默认勾选；按勾选 `sc config start= auto` / `net start`，升级不触发）；Client 安装完成弹窗（立即打开，默认勾选；启动客户端开界面）；Server 不再打包 web/dist；卸载清理路径含 ProgramData（Server server 目录 / Client settings.json）。
- 测试 156 全绿（Core 60 / Server 13 / WinHost 58 / Studio 25）；产物 `LabelFrame-Server-0.15.0.msi`（7.6MB）、`LabelFrame-Client-0.15.0.msi`（14.2MB）。


## 修复：Server 安装未注册 Windows 服务（0.15.1，2026-08-11）

- 根因：`ServiceInstall` 放在只有注册表 KeyPath 的独立组件，Windows Installer 拿不到服务二进制路径，服务创建被跳过（其余文件正常安装）。
- 修复：`ServiceInstall / ServiceControl` 移入 `LabelFrame.Server.exe` 所在组件（服务二进制 = 组件 KeyPath）；`generate-files.ps1` 新增 `-ServerServiceName / -ServerServiceDisplayName` 参数。
- 版本升至 0.15.1（MajorUpgrade 可覆盖已装的 0.15.0）；产物 `LabelFrame-Server-0.15.1.msi`（7.6MB）、`LabelFrame-Client-0.15.1.msi`（14.2MB）。
- 覆盖升级（0.15.0 → 0.15.1）不弹完成弹窗、不自动启动，服务注册为手动启动，可 `net start LabelFrameServer` 或服务管理器启动；全新安装仍弹完成弹窗（默认自启 + 立即运行）。


## 修复：安装完成弹窗的「自启 / 立即运行 / 立即打开」动作未触发（0.15.2，2026-08-11）

- 现象：0.15.1 全新安装后服务已注册，但 StartType=Manual 且从未启动（弹窗点确认后后续 InstallUISequence 动作不执行——最后一个对话框 EndDialog 后序列结束）。
- 修复：改为弹窗「确认」按钮点击时通过 `DoAction` 直接触发：Server（SetAutoStart → sc config start= auto；StartServiceNow → net start）、Client（LaunchClient）；移除 InstallUISequence 中的尾部 Custom 动作。
- 版本升至 0.15.2；产物 `LabelFrame-Server-0.15.2.msi`（7.6MB）、`LabelFrame-Client-0.15.2.msi`（14.1MB）。


## 简化：Server 服务安装改为“注册即自动 + 安装时启动”，完成弹窗仅提示（0.15.3，2026-08-11）

- 按用户反馈简化：移除 Server 完成弹窗的「开机自启 / 立即运行」勾选项与 `sc config / net start` 自定义动作；`ServiceInstall Start=auto` + `ServiceControl Start=install`（安装即自动 + 启动），完成弹窗仅提示“服务已注册并自动启动”。
- 实装验证：静默安装 0.15.3 后 `START_TYPE=AUTO_START`、服务 RUNNING、healthz OK。
- 双包版本对齐 0.15.3：`LabelFrame-Server-0.15.3.msi`（7.6MB）、`LabelFrame-Client-0.15.3.msi`（14.1MB，Client 交互不变：完成弹窗仍含「立即打开」勾选）。


## 迭代 18 前端合入与联调（0.15.3 客户端，2026-08-11）

- 前端（hermes，0668d03）：F1-F7 全部完成——双 base（serverApi / localApi）、机器级配置（/api/host/config 启动加载 + 保存即生效）、恢复连接方式（TransportPanel）与打印机分组、数据与打印本机设备默认选中 + 顶部连接徽标、作业历史页（limit=100，空态按模式）、Workbench/Designer/PdaLogs 跟随 serverMode 降级；前端测试 125 全绿、build/lint 通过。
- 后端复核：契约对齐（JobView.createdAt / HostConfig.serverUrl-deviceId-deviceName / TransportConfig / PrinterStatus 字段与后端一致）。
- 客户端 MSI 0.15.3 重新打包（含新前端 dist index-DfhhEvBH.js）；端到端冒烟通过：Client 注册在线 → 提交作业 Pending → Completed 2/2 → Log 模拟 PNG 落盘；页面引用新 bundle。


## 0.15.4：推送等效 + 客户端弹窗关闭修复 + 弹窗文字简化（2026-08-11）

- 服务端推送（长轮询通知）：`GET /api/devices/{deviceId}/jobs/notify?timeout=N`——作业入队立即返回 hasPending=true（等效推送），同时刷新设备心跳；客户端 `ServerRoutingWorker` 改为长轮询等待 → 立即领取，打印等待从最多 5s 轮询降为 <1s；网络异常回退间隔重试。
- 客户端安装完成弹窗无法关闭：`LaunchClient` 改为 `cmd /c start` 非阻塞启动（msiexec 不再等待 GUI 进程退出）。
- 弹窗文字去掉「（默认勾选）」「（默认不勾选）」括号说明。
- 测试 162 全绿（Server 17 / WinHost 60 / Core 60 / Studio 25）；产物 `LabelFrame-Server-0.15.4.msi`（7.6MB）、`LabelFrame-Client-0.15.4.msi`（14.1MB）。


## 迭代 19：Ubuntu 服务端部署 + 跨机验证（2026-08-11，进行中）

- Rendering / Server 多目标框架 `net10.0;net10.0-windows`：GDI 预览（LabelPreviewRenderer）仅 Windows（#if WINDOWS）；Server `UseWindowsService` / 应用图标 / WindowsServices 包仅 Windows；Linux 用 systemd。
- SkiaSharp Linux 原生库：新增 `SkiaSharp.NativeAssets.Linux`（net10.0）；发布产物含 `libSkiaSharp.so` / `libe_sqlite3.so`。
- Server 数据目录按平台默认：Windows `%ProgramData%\LabelFrame\server` / Linux `/var/lib/labelframe/server`；`LABELFRAME_SERVER_*` 覆盖。
- 交付：`scripts/publish-server-linux.ps1`（framework-dependent / self-contained，tar.gz 归档）、`scripts/deploy-server-ubuntu.sh`（用户/目录/systemd 自启/防火墙提示）、`packaging/ubuntu/labelframe-server.service`、`packaging/ubuntu/Dockerfile`。
- 测试 162 全绿；Windows Server MSI 打包回归正常；linux-x64 产物 6.7MB（归档）。
- 跨机验证（服务端 Linux + 客户端 Windows）待真机 / 容器执行：验证清单见 docs/archive/ITERATION-19-SPEC.md §5。


## 迭代 19 增补：Docker 镜像交付 + 容器跨机验证（2026-08-11）

- 构建 `labelframe-server:0.15.4`（aspnet:10.0 基础镜像 + libfontconfig/libfreetype/libharfbuzz Skia 依赖），导出离线包 `artifacts\labelframe-server-0.15.4.docker.tar`（106MB）；新增 `packaging/ubuntu/docker-compose.yml`（端口 + 数据卷 + 重启策略）。
- 容器内验证（Linux）：healthz、SQLite（数据落 /var/lib/labelframe/server）、Skia 单张/批量出图全部通过（修复 Skia 缺 fontconfig 依赖）。
- 跨机闭环验证：Windows Client 指向 Linux 容器服务端 → 设备注册 Online → 提交作业 Pending→Claimed 131ms（推送通知）。
- 已知行为：客户端「服务端地址」修改后需重启 Client（打印 Worker 连接使用启动时地址），文档已注明。


## 迭代 19 增补：服务端文本日志 + 日志目录挂载（2026-08-11）

- 新增 `LABELFRAME_SERVER_LOG_FILE` 环境变量：设置后服务端把 ILogger 输出追加写入 UTF-8 文本文件（极简 FileLoggerProvider，含时间/级别/分类）。
- Docker compose / systemd 默认挂载日志目录并启用：容器 `/var/lib/labelframe/logs` → 宿主机目录（compose 默认 `./logs`，生产建议 `/opt/store/labelframe/logs`），`tail -f server.log` 直接查看。
- 已重建 `labelframe-server:0.15.4` 镜像与离线包 `labelframe-server-0.15.4.docker.tar`（容器内验证：日志落盘宿主机挂载点）。

## 迭代 19 增补：安装包先停运行程序 + 作业完成回报不再等长轮询（2026-08-11）

- 安装 / 覆盖升级 / 卸载前先停止运行中的程序：
  - Server MSI 新增 `StopServerService` 自定义动作（`sc.exe stop LabelFrameServer`，安装 / 卸载均先停服务再执行 MSI 自带 StopServices / DeleteServices），避免卸载后服务仍显示运行；
  - Server 停机超时从默认 30s 缩短为 5s（`HostOptions.ShutdownTimeout`），避免客户端长轮询请求拖慢服务停止 / 卸载 / 升级；
  - Client MSI 新增 `KillWinHost` 自定义动作（`taskkill /F /IM LabelFrame.WinHost.exe`，位于安装序列最前），覆盖安装 / 卸载前强制结束托盘程序，避免 exe 占用导致覆盖失败或卸载后残留进程。双 MSI 显式声明 `Codepage="65001"`，保证任意系统区域设置下中文打包。
- 作业完成回报改为独立循环（`ServerRoutingWorker.ReportFinishedLoopAsync`，1s 周期）：本地作业终态后约 1s 内回报 Server，不再被 20s 长轮询阻塞——「已领取 → 已完成」不再延迟；新增回归测试 `Worker_should_report_finished_job_without_waiting_for_long_poll`。

## 迭代 20 文档：服务端管理界面（插件式 UI）+ 设备 IP（2026-08-11）

- 起草 `docs/archive/ITERATION-20-SPEC.md`：客户端状态栏显示本机 IP；服务端可选管理界面以插件形式提供（静态前端包放入 `plugins/web-ui` 即生效，无需重启，默认仍无头）；Server UI 去除打印机相关内容，保留工作台 / 设计器，新增在线设备页，数据与打印复用并改为“在线设备选择器”发送打印测试。
- 契约：`DeviceView.lastIp`、`GET /api/devices/by-ip/{ip}`、`POST /api/jobs` 可选 `targetIp`、`GET /api/host/config` 增加 `ips`、新增 `GET /api/server/info`、前端 `VITE_UI_MODE=client|server` 双构建。
- DESIGN 决策 #61/#62/#63；ROADMAP 新增迭代 20 条目。


## 迭代 20 后端实施（0.16.0，2026-08-11）

- 设备 IP 记录与查找：`devices` 表新增 `last_ip`（旧库 ALTER TABLE 迁移，已存在列静默跳过）；设备注册 / 心跳（notify / pending）从 `RemoteIpAddress` 记录 / 刷新来源 IP，IPv4-mapped IPv6（`::ffff:a.b.c.d`）统一为 IPv4 文本（MapToIPv4）；`DeviceView.lastIp`；新增 `GET /api/devices/by-ip/{ip}`（忽略大小写，未找到 404）；`POST /api/jobs` 支持可选 `targetIp`（与 `targetDeviceId` 二选一，同时提供时 `targetDeviceId` 优先，按 IP 未找到 404）。
- 服务端管理界面插件（默认仍无头，不推翻决策 #53）：`Server.WebUiPath`（默认 Windows `%ProgramData%\LabelFrame\server\plugins\web-ui` / Linux `/var/lib/labelframe/server/plugins/web-ui`，`LABELFRAME_SERVER_WEB_UI` 覆盖、为空显式禁用）；静态托管中间件启动确保目录存在（创建失败降级无头不崩），每次请求运行时检测——放入 `index.html` 即托管（UseDefaultFiles + UseStaticFiles + SPA fallback），移除即恢复无头；fallback 不拦截 `/api/*` 与 `/healthz`；新增 `GET /api/server/info`（`listenUrl / uiEnabled / version`）。
- 插件交付：`scripts/package-server-webui.ps1` 把 `web/dist-server` 打包为 `artifacts/labelframe-server-webui-<version>.zip`；Docker compose 增加可选卷挂载示例（`./plugins/web-ui`）。
- WinHost：`GET /api/host/config` 响应增加 `ips`（本机启用网卡的 IPv4 列表，过滤回环 / 隧道，去重；状态栏展示用）。
- 测试 176 全绿（Core 60 / Server 29 / Studio 25 / WinHost 62，新增迁移 / by-ip / targetIp / IP 规范化 / 插件开关 / ips 枚举用例）；端到端冒烟通过（lastIp 记录、by-ip、targetIp 解析、插件放入即托管、移除恢复无头、插件启用时 API 不受影响）。


## 迭代 20 前端合入与打包（0.16.0，2026-08-11）

- 前端（hermes，be87548）：双构建 `VITE_UI_MODE=client|server`——`build`（web/dist，Client 包）/ `build:server`（web/dist-server，插件包），`uiMode.ts` + `vite-env.d.ts` ImportMetaEnv 声明 + dev proxy 按模式分支 + vitest 显式注入；K1 `getServerBaseUrl()` server 构建恒返回同源相对路径 `''`（不读 localStorage / 机器级配置）；K2 server 构建跳过 localApi 探测、`serverMode` 恒 server 无 standalone；Server UI 菜单裁剪（移除设置与打印机相关内容，新增「在线设备」页，日志页更名「设备日志」）；在线设备页（每 5s 轮询、点击设默认目标 localStorage `labelframe.defaultTargetDeviceId` 跨页联动、离线拦截）；数据与打印在线设备选择器（仅在线可选、离线置灰显示上次心跳、默认目标优先级 用户点选 > 本机设备 > 第一台在线、提交时现拉校验在线（K3 掉线禁止提交不排队）、隐藏逐张重试表格）；客户端状态栏连接后显示本机 IP（/api/host/config.ips，多 IP 逗号分隔、过长省略 title 给全量）。前端测试 151 全绿（client / server 双分支）。
- 打包 0.16.0：`artifacts/LabelFrame-Server-0.16.0.msi`（7.6MB）、`artifacts/LabelFrame-Client-0.16.0.msi`（14.1MB）、`artifacts/labelframe-server-webui-0.16.0.zip`（0.2MB，由 `scripts/package-server-webui.ps1` 打包 web/dist-server）；插件端到端验证：解压到插件目录 → 服务端根路径返回管理界面、静态资源与 /api 正常、`/api/server/info.uiEnabled=true`。


## 迭代 20 打包修复（0.16.0，2026-08-11）

- 修复 Client 安装报错 2732（Directory Manager not initialized）：`KillWinHost` 自定义动作引用 `SystemFolder` 目录属性，却排在 `Before=FindRelatedProducts`（CostInitialize 之前，目录管理器未初始化）；改为 `After=InstallInitialize`，与 Server 包 `StopServerService` 一致。已用详细安装日志复现（2732 于 KillWinHost 处触发）并验证修复（升级安装 Client 0.16.0 成功，KillWinHost 正常执行）。
- 修复包内版本漂移：`main.wxs` / `main-server.wxs` 的 Package Version 硬编码 0.15.5（此前产物文件名 0.16.0 但安装后显示 0.15.5）；改为 `$(var.Version)`，打包脚本 `-d Version=$Version` 传入，避免今后再次漂移。
- 已验证：本机 Client / Server 0.15.5 → 0.16.0 提权升级安装成功，DisplayVersion=0.16.0，Server 服务 Running；产物 `artifacts/LabelFrame-Client-0.16.0.msi`（14.1MB）、`artifacts/LabelFrame-Server-0.16.0.msi`（7.6MB）。


## 迭代 20 收尾：Docker 0.16.0 + 安装完成弹窗优化 + 未来规划（2026-08-12）

- Docker 交付 0.16.0：镜像 `labelframe-server:0.16.0`（393MB，基础镜像 aspnet:10.0 + Skia 依赖）、离线包 `artifacts/labelframe-server-0.16.0.docker.tar`（105.9MB）；`docker-compose.yml` 升级镜像标签并默认挂载插件目录 `./plugins/web-ui:/var/lib/labelframe/server/plugins/web-ui`（README 已写明挂载目录与操作：解压 `labelframe-server-webui-0.16.0.zip` 到该目录即生效、移除即无头）；容器内验证 healthz / `/api/server/info`（uiEnabled）/ 插件页面 200 全部通过。
- Client 安装完成弹窗优化：MSI 原生完成弹窗会被 Windows 焦点策略挡到后台 / 任务栏闪烁——改为安装完成后拉起 WinHost 的 `--install-finished` TopMost 弹窗（默认置前，勾选「立即打开」重启宿主并打开界面）；修复弹窗「确认」点击后不关闭、进程挂起的问题（Application.Run 非模态下 DialogResult 不自动关闭，改为 ShowDialog + 显式 Close，重启宿主用 CreateProcess）；弹窗仅全新安装且非静默（/qn）时出现；已实测全新安装弹窗 TopMost 出现、安装成功。
- 未来规划（仅文档，不实施）：打印机连接方式插件化——把 Log / TCP9100 / Windows 驱动 / Zebra SDK 抽象为传输插件（接口 + 注册表按需装配），第三方厂商可自研接入；见 ROADMAP 待需求 / DESIGN 未决问题。
