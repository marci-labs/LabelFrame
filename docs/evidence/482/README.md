# 缺陷 #482 验收证据

Client MSI 直接安装时的两个问题：缺运行时提示不准；缺 .NET Desktop Runtime 或安装目录不在 Program Files 时，启动更新服务约 4 分钟后报 1920。对应 Issue：#482。

## 测试环境

| 项 | 说明 |
|---|---|
| 测试机 | 本机 Hyper-V 虚拟机 `LF-Accept-Win10`：Windows 10 专业版 19045 x64，2 个逻辑处理器，约 3.3 GB 内存。基线还原后没有任何 .NET 10 运行时、没装过 LabelFrame。每轮开始前还原基线，结束后还原并关机 |
| 运行时 | .NET Runtime、ASP.NET Core Runtime、Windows Desktop Runtime 10.0.12 x64 的微软独立安装包 |
| 发布版安装包 | `LabelFrame-Client-1.6.0.msi`（v1.6.0 Release），SHA256 `e905ffd2cad6efcf01a8cad5e46b726a4ff2013d941b8943698b664b398c315c` |
| 修复后安装包 | `LabelFrame-Client-1.6.1.msi`，SHA256 `18188bd07f36d33d3718bf2f336cf514b154f5634c346734d1a24e9736f7ca15`，由本分支源码本地构建。本地裸构建不附带官方插件包，CI 对同一源码构建出的安装包字节会不同 |
| 未覆盖 | Windows 11（两个原因都与系统版本无关，见 Issue「不在范围」）、x86 版运行时、Hosting Bundle 安装包 |

2026-11-24 复现，2026-11-25 验收。证据里的测试机账户名已替换为 `<account>`。

## 文件索引

「包」一列：**发布版** = 1.6.0；**最终** = 上面的 1.6.1 构建产物；**中间构建** = 修复过程中、没有发布过的构建，只用来保存设计依据和缺陷对照。

| 文件 | 包 | 内容 |
|---|---|---|
| `released-1.6.0-only-aspnetcore-runtime.txt` | 发布版 | 只装 ASP.NET Core Runtime：安装前被拦，提示「本机未检测到 ASP.NET Core Runtime」（检测值 12289） |
| `released-1.6.0-no-desktop-runtime.txt` | 发布版 | .NET Runtime + ASP.NET Core Runtime、无 .NET Desktop Runtime：检测通过，**4 分 02 秒**后返回 1603，错误 1920；服务管理器连续 6 次「等待服务的连接超时」 |
| `released-1.6.0-all-runtimes.txt` | 发布版 | 三项齐全：3 秒装完，更新服务以 LocalSystem 运行 |
| `released-1.6.0-custom-install-dir.txt` | 发布版 | 装到 `C:\LabelFrame\Client`：**4 分 11 秒**后返回 1603，错误 1920；同一程序在 Program Files 之外以 `--update-service` 运行时自检抛「自动安装仅支持管理员部署在 Program Files 下的客户端」 |
| `updater-fails-fast-but-msi-retries.txt` | 中间构建 | 设计依据：服务自检失败后 1 秒内就停了（退出码 1064），但安装器内置的「启动服务」动作仍然反复重试——所以安装包必须自己启动服务并等待结果 |
| `service-reports-start-failure-detail.txt` | 中间构建 | 设计依据：服务把失败阶段与原因写进事件日志；启动入口按系统代码页输出的中文被安装日志读成乱码——结论行改用 ASCII，中文写到单独的 UTF-8 明细日志 |
| `final-wizard-runtime-check-values.txt` | 最终 | 向导三种运行时状态下两项检测的取值：0 = 可用，12289 / 12290 = 不可用；完整向导下 `UILevel = 5` |
| `final-wizard-prompt-no-runtime.txt`、`wizard-runtime-missing.png` | 最终 | 向导：没有任何运行时 → 对话框「未检测到可用的 .NET 10 运行时（x64）……两项」，带官方下载链接，只有「退出」按钮 |
| `final-wizard-prompt-only-aspnetcore-runtime.txt`、`wizard-runtime-missing-only-aspnetcore-runtime.png` | 最终 | 向导：只装 ASP.NET Core Runtime → 同上，不再说 ASP.NET Core Runtime 未安装 |
| `final-wizard-prompt-no-desktop-runtime.txt`、`wizard-desktop-runtime-missing.png` | 最终 | 向导：有 ASP.NET Core 与 .NET Runtime、缺 .NET Desktop Runtime → 「未检测到可用的 .NET Desktop Runtime 10（x64）……」 |
| `wizard-exit-leads-to-interrupted-page.png` | 最终 | 点「退出」之后的标准「安装向导中断」页 |
| `final-wizard-installfolder-intercepted.txt`、`wizard-installfolder-intercepted.png`、`wizard-installfolder-then-early-end.png` | 最终 | 向导：命令行传 `INSTALLFOLDER` → 对话框写明「固定安装到 C:\Program Files\LabelFrame\Client，不支持通过 INSTALLFOLDER 指定安装目录」；点「确定」后是带安装日志路径的「提前结束」页；没有安装 |
| `final-wizard-full-install.txt`、`wizard-welcome.png`、`wizard-license.png`、`wizard-install-location.png`、`wizard-install-complete.png` | 最终 | 向导从欢迎页走到装完（不传 `/L`）：路径、只读的安装目录页、完成页、自动生成的安装日志、服务运行 |
| `final-wizard-back-navigation.txt`、`wizard-options.png`、`wizard-ready-to-install.png`、`wizard-cancel-confirmation.png`、`wizard-cancelled.png` | 最终 | 向导在安装目录页前进、后退各两次，最后取消退出 |
| `final-wizard-install-failure-prompt.txt`、`wizard-failure-then-early-end.png` | 最终 | 向导：安装目录预先建成普通用户可写 → 点「安装」后约 5 秒进入「提前结束」页，页面上写明安装日志路径；没有安装残留 |
| `final-silent-runtime-missing.txt` | 最终 | 静默安装的四种运行时状态（R0–R3）与再次安装（R4） |
| `final-silent-acceptance.txt` | 最终 | 静默：干净安装、卸载、从 1.6.0 升级、自检失败、`INSTALLFOLDER`、启动入口手工调用、CPU 满载 |
| `final-runtime-dialog-tables.txt` | 中间构建、最终 | 「缺少 .NET 运行时」对话框的控件表对照：中间构建三段说明默认可见，最终构建默认隐藏 |
| `final-exit-page-controls.txt` | 最终 | 取消确认、提前结束、中断、完成四个收尾页在包里的控件文字 |
| `structure-assertions.txt` | 全部 | `scripts/verify-msi-ui.ps1` 的判定：最终包通过；四个从最终源码故意改错的变异包、三个中间构建、发布版 1.6.0 被拒 |
| `host-build-and-tests.txt` | — | 本地构建与测试，以及 CI 的结果 |

## 根因复现（发布版 1.6.0，2026-11-24）

全部为静默安装 `msiexec /i … /qn /norestart`，运行时 10.0.12 x64。

| 场景 | 结果 |
|---|---|
| 仅装 ASP.NET Core Runtime | 仍被拦截，提示「本机未检测到 ASP.NET Core Runtime」。该安装包只含 `Microsoft.AspNetCore.App`，连 `dotnet.exe` 和宿主都没有，两项检测都不满足 |
| .NET Runtime + ASP.NET Core Runtime，无 .NET Desktop Runtime | 检测通过，4 分 02 秒后返回 1603（错误 1920） |
| 三项齐全 | 3 秒装完 |
| 三项齐全，装到 `C:\LabelFrame\Client` | 4 分 11 秒后返回 1603（错误 1920） |

## 验收标准逐条结果

下表数字都来自最终包。

| 编号 | 结果 | 证据与要点 |
|---|---|---|
| AC-1 只装 ASP.NET Core Runtime | 通过 | 向导：`final-wizard-prompt-only-aspnetcore-runtime.txt`——「未检测到可用的 .NET 10 运行时（x64）。LabelFrame 需要 .NET Desktop Runtime 10 和 ASP.NET Core Runtime 10 两项，请都安装 x64 版本……」。静默：`final-silent-runtime-missing.txt` R1，1.0 秒拦截，事件 10005 为同一段文字 |
| AC-2 缺 .NET Desktop Runtime | 通过 | 静默：`final-silent-runtime-missing.txt` R2，1.0 秒拦截，提示「未检测到可用的 .NET Desktop Runtime 10（x64）……」，日志里没有 `StartUpdaterService`（原来约 4 分钟）。向导：`final-wizard-prompt-no-desktop-runtime.txt` |
| AC-3 三项齐全 | 通过 | 静默：`final-silent-acceptance.txt` F1，4.1 秒，服务 Running（LocalSystem，自动启动）。向导：`final-wizard-full-install.txt`，退出码 0 |
| AC-4 向导目录页 | 通过 | `final-wizard-full-install.txt`、`wizard-install-location.png`：安装目录页只有标题「安装目录」、路径和「上一步 / 下一步 / 取消」，没有说明文字、输入框与「更改」。`final-wizard-back-navigation.txt`：许可页 → 安装目录页 →「上一步」回许可页 → 前进到选项页 →「上一步」回安装目录页 → 前进到确认页 |
| AC-5 命令行传 `INSTALLFOLDER` | 通过 | 向导：`final-wizard-installfolder-intercepted.txt`——对话框与安装日志都写明原因，退出码 1603，未安装。静默：`final-silent-acceptance.txt` F5，1.0 秒拦截，事件 10005 与安装日志写明原因，没有进入 `StartUpdaterService` |
| AC-6 自检失败 | 通过 | 静默：`final-silent-acceptance.txt` F4，3.1 秒返回 1603，安装日志输出 `FAILED - the service did not stay running … Details: …\MSI7a124.LOG.updater-service.log`，明细日志与事件日志写明「更新服务启动失败，停在阶段「安装目录自检」：更新路径允许非管理员写入，请重新以管理员身份安装客户端」。向导：`final-wizard-install-failure-prompt.txt` |
| AC-7 启动较慢但自检通过 | 通过 | `final-silent-acceptance.txt` F7：CPU 100% 饱和下安装成功，启动步骤 3.4 秒就绪（空载 1.4 秒），兜底超时没有触发 |
| AC-8 启动停滞 | 通过（自动化测试覆盖） | `UpdaterServiceStarterTests.Service_that_never_becomes_ready_ends_with_the_backstop_timeout`、`UpdaterStartupTests.Backstop_timeout_is_returned_when_neither_ready_nor_failure_arrives`、`UpdaterStartupTests.Stopping_before_ready_is_a_failure_that_names_the_last_stage`、`UpdaterServiceStarterTests.After_a_failed_verdict_a_service_that_is_still_alive_is_stopped_and_waited_for` |
| AC-9 安装日志 | 通过 | 向导安装（不传 `/L`）：`final-wizard-full-install.txt` 末尾列出 `%TEMP%\MSI36dde.LOG` 与 `MSI36dde.LOG.updater-service.log`；静默安装：`final-silent-acceptance.txt` F1。失败时给出日志路径：向导的「提前结束」页上写明（`final-wizard-install-failure-prompt.txt`、`final-wizard-installfolder-intercepted.txt`）；安装日志的结论行里给出明细日志路径（F4）。停在哪一步：明细日志与事件日志（F4） |
| AC-10 引导 EXE 链 | 通过（代码核对） | `packaging/bootstrapper/Bundle.wxs`：链序 .NET Desktop Runtime → ASP.NET Core Runtime → WebView2 → Server MSI → Client MSI，没有向客户端 MSI 传 `INSTALLFOLDER`。运行时齐全时的静默安装见 AC-3 |
| AC-11 升级路径 | 通过 | `final-silent-acceptance.txt` F3：发布版 1.6.0 装在默认位置 → 修复包覆盖升级，6.1 秒成功，服务运行 |
| AC-12 回归 | 通过（有两项说明） | `host-build-and-tests.txt`：本机 SDK 10.0.402 下构建 0 警告 0 错误；测试 1622 通过、0 失败、1 跳过——这是显式排除 1 项既有测试后的数字，该项受宿主机安全软件影响、在未改动的 master 上同样失败，在 CI 上通过。CI（.NET 11 SDK）四项检查全部通过，测试 1623 通过、0 失败、1 跳过。结构断言见 `structure-assertions.txt`。另有一次本地全量运行出现 2 项与本次改动无关的偶发失败，见该文件 |
| AC-13 文档与证据 | 通过 | 本目录；文档改动见 PR |

## 验收中发现并修复的问题

两个都只出现在没有发布过的中间构建里，都是在测试机上看到真实界面才发现的。

**一、向导的「缺少 .NET 运行时」对话框显示错段说明。**

三段说明共用同一个位置，靠各自的显示条件择一显示。Windows Installer 的控件显示条件只在条件为真时把控件显示出来，条件为假时并不隐藏；而三段文字默认都是可见的，于是全部叠在同一处，最后创建的一段（ASP.NET Core）盖在最上面。结果在没有安装任何运行时的机器上，向导显示的是「缺少可用的 ASP.NET Core Runtime 10（x64）」——正是本缺陷要纠正的那种提示。

- 修复：三段文字改为默认隐藏（`final-runtime-dialog-tables.txt`）。
- 结构断言：补上「必须默认隐藏」、条件原文精确比对，并把条件翻译后对四种运行时状态逐一求值，要求向导恰好显示该显示的一段、静默安装恰好命中第一条该命中的启动条件。

**二、向导安装失败时，用户看不到安装日志的位置。**

启动服务失败时安装器直接回滚并显示界面库的标准「提前结束」页，中间不出现错误对话框，所以原先放进错误表的那句「原因见安装日志」根本不会被显示。

- 修复：删掉错误表里那条文案；用本地化覆盖（`packaging/wixui-zh-cn-override.wxl`）改写「提前结束」页的两段说明，在其中写明安装日志路径（`final-exit-page-controls.txt`、`wizard-failure-then-early-end.png`）。
- 结构断言：检查该页的说明文字包含 `[MsiLogFileLocation]`。

**按用户目视反馈做的一处调整**：安装目录页原先有一句解释（固定安装到下面的位置、不能修改及原因），按用户要求去掉，页面只留标题「安装目录」和路径；结构断言要求这一页只有标题与路径两个文字控件。

结构断言对以上问题的判定见 `structure-assertions.txt`：最终包通过；四个从最终源码故意改错的变异包被拒（三段说明去掉默认隐藏；一段说明的条件被换成别的；首条启动条件要求两项都可用，即发布版的写法；失败页去掉日志路径）；三个中间构建被拒；发布版 1.6.0 被拒。

## 观察与已知边界

- **CPU 饱和时整次安装 85.8 秒**，但时间不在启动服务上：包内原有的「关闭运行中的客户端」PowerShell 动作和安装器自身的校验、复制、提交占了绝大部分。启动更新服务只占 3.4 秒。
- 向导里点「退出」（或拦截后点「确定」）会进入标准的「安装向导中断 / 提前结束」页，需要再点一次「完成」。这是 Windows Installer 的标准流程，与包里原有的 WebView2 缺失对话框一致。
- 卸载后安装目录下保留 `Client\appsettings.json`（不随卸载删除的配置组件）和空目录 `ClientUpdateState`。发布版 1.6.0 卸载后同样保留一个文件，属于既有行为。
- CI 实际使用的 SDK 是 .NET 11，而不是工作流声明的 10.x：`DOTNET_VERSION` 只保证 runner 上装了 .NET 10 SDK，仓库没有 `global.json`，`dotnet` 会选用镜像上最高的 SDK。本 PR 最初因此被新分析器规则（CA2026 / CA2027）挡住，与本次改动无关；已由 PR #484 改写那些调用并合并，本 PR rebase 后检查全部通过。是否用 `global.json` 固定 SDK 版本仍未决定，记录在 `docs/DESIGN.md` 的「风险与未决问题」。
