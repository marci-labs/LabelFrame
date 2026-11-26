# 缺陷 #486 验收证据

服务端 MSI 固定安装到 `C:\Program Files\LabelFrame\Server`：向导的目录页只读，命令行传入 `INSTALLFOLDER` 时拦下并说明原因。对应 Issue：[#486](https://github.com/marci-labs/LabelFrame/issues/486)。

## 测试环境

| 项 | 说明 |
|---|---|
| 测试机 | 本机 Hyper-V 虚拟机 `LF-Accept-Win10`（Windows 10，与 [#482 的取证](../482/README.md) 是同一个基线还原点 `baseline-clean-winrm`：没有 .NET 10 运行时、没装过 LabelFrame）。每轮开始前还原基线，结束后还原并关机 |
| 运行时 | 微软 .NET Runtime 10.0.12 x64、ASP.NET Core Runtime 10.0.12 x64 的独立安装包（服务端需要的两项） |
| 旧版安装包 | `LabelFrame-Server-1.6.0.msi`（1.6.0 Release），SHA256 `6c398c3dbd2f1e294cb26ed0dcc618631434284ec0226a14f5a4ec4bd69e930c` |
| 被测安装包 | `LabelFrame-Server-1.6.1.msi`，SHA256 `aacd03283de3d08d68712485450e8c286fc7558b82f4894407b27d2a9bff1a82`，由本分支提交 `43127ac` 的源码本地构建（构建时设置 `VERSION=1.6.1`）。合并采用 squash，合并后的提交哈希不同，但安装包源码与脚本在 `43127ac` 之后没有改动 |
| 未覆盖 | Windows 11；引导程序与离线包的完整链路（见下文「与引导程序、离线包的等价关系」） |

2026-11-26 取证。证据里的测试机账户名已替换为 `<account>`。带界面的轮次用临时自动登录在交互桌面里走查，结束时已随基线还原清除。

## 文件索引

每个 `*.txt` 是一轮的宿主机侧记录：客机脚本的每一行输出按顺序带时间戳。

| 文件 | 轮次 | 内容 |
|---|---|---|
| `rehearsal-round.txt` | 第 0 轮 | 演练，不安装任何东西：参数按名字绑定、重启等待可靠 |
| `silent-fresh-install.txt` | 静默全新安装 | 不带属性的安装、功能冒烟、改端口并重启服务、重启系统、两种卸载 |
| `silent-installfolder-refused.txt` | 静默拦截 | 命令行分别传入自定义目录和固定目录本身，两次都应被拒 |
| `silent-upgrade-from-1.6.0-default-dir.txt` | 静默升级 | 旧版在固定目录，端口已改到 53970 |
| `silent-upgrade-from-1.6.0-custom-dir.txt` | 静默升级 | 旧版在 `C:\LabelFrame Test\Server`，升级前不处理配置 |
| `silent-upgrade-from-1.6.0-custom-dir-with-config-copied-first.txt` | 静默升级 | 旧版在 `C:\标签 服务\LabelFrameServer`，升级前先把配置复制到固定目录 |
| `wizard-fresh-install.txt`、`wizard-fresh-*.png` | 向导全新安装 | 走查记录与截图（欢迎、许可、安装目录、确认、安装中、完成） |
| `wizard-upgrade-from-1.6.0.txt`、`wizard-upgrade-*.png` | 向导升级 | 覆盖固定目录里的 1.6.0 |
| `wizard-installfolder-refused.txt`、`wizard-intercept-*.png` | 向导拦截 | 拦截对话框与「提前结束」页的截图 |
| `installer-log-excerpts.txt` | 全部 | 从测试机取回的 Windows Installer 日志的关键行（带行号） |
| `structure-assertion.txt` | 本机 | 结构断言对最终包与几类应当被拒的输入的结论 |
| `structure-assertion-mutants.txt` | 本机 | 27 个变异包各改坏一处，断言逐个拒绝的原因 |
| `structure-assertion-evaluator.txt` | 本机 | 断言脚本里条件求值器的 30 项单元检查 |
| `host-build-and-tests.txt` | 本机 | `dotnet build` / `dotnet test` 结果 |
| `chain-log.txt` | 全部 | 轮次链记录：自检、内存礼让等待、每轮结果 |

## 验收标准逐条结果

| 编号 | 结果 | 证据与要点 |
|---|---|---|
| AC-01 向导全新安装 | 通过 | `wizard-fresh-install.txt`、`wizard-fresh-03-Location.png`：原「目标文件夹」页变为只读的「安装目录」页，页面上只有标题「安装目录」和路径，以及「上一步 / 下一步 / 取消」；没有路径输入框，没有「更改」按钮。走查逐个枚举了页面控件（两次进入各一次）。路径显示为 `C:\Program Files\LabelFrame\Server\`——安装器给目录属性格式化时带一个尾反斜杠，与客户端的同一页一致。点「上一步」回许可页、再前进，页面相同。安装完成后服务从固定目录运行，冒烟 33 项全过 |
| AC-02 静默全新安装 | 通过 | `silent-fresh-install.txt`：4.1 秒完成，退出码 0。固定目录下 30 个文件、无子目录，`C:\Program Files\LabelFrame` 下只有 `Server`；服务 `LabelFrameServer` 的程序路径是 `C:\Program Files\LabelFrame\Server\LabelFrame.Server.exe`，自动启动、LocalSystem；监听 `0.0.0.0:53961`；数据在 `C:\ProgramData\LabelFrame\server` |
| AC-03 命令行传入 INSTALLFOLDER | 通过 | 静默（`silent-installfolder-refused.txt`）：传自定义目录、传固定目录本身，两次都在 1.0 秒内返回 1603；之后两个目录都不存在，没有服务、没有产品登记、没有进行中的安装登记。安装日志里 `LaunchConditions` 返回 3 并写明原因（`installer-log-excerpts.txt`）。随后不带属性的安装照常成功。向导（`wizard-installfolder-refused.txt`、`wizard-intercept-01-InstallFolderBox.png`）：第一个窗口就是拦截对话框，只有「确定」，文字是「LabelFrame 服务端固定安装到 C:\Program Files\LabelFrame\Server，不支持通过 INSTALLFOLDER 指定安装目录。请去掉该参数后重新运行安装程序。」；点「确定」后是「安装向导提前结束」页（`wizard-intercept-02-FatalError.png`），退出码 1603，机器上没有任何变化 |
| AC-04 功能冒烟 | 通过 | 33 项全部通过。在被测包上共七次：全新安装后、重启系统后（`silent-fresh-install.txt`）；三次静默升级之后；两次向导安装之后。另有四次是升级前对旧版 1.6.0 做的，同样全过。33 项 = 健康检查与版本信息 2、设备注册 3、作业提交 / 查询 / 领取 / 进度 / 回执 / 历史 7、通知 1、调试出图 2、模板库 5、日志 2、安装包上传下载删除 5、其余分发与更新端点 6 |
| AC-05 配置与重启 | 通过 | `silent-fresh-install.txt`：把 `appsettings.json` 的监听端口改到 53970 后重启服务，53970 开始监听、53961 关闭；恢复配置再重启，回到 53961。重启系统后服务自动恢复（等待 0.4 秒就绪，30 秒内进程稳定），冒烟 33 项全过 |
| AC-06 从 1.6.0（固定目录）升级 | 通过 | 静默（`silent-upgrade-from-1.6.0-default-dir.txt`）：升级前把端口改到 53970 并造了作业、设备、模板；不带目录的静默升级 4.1 秒完成。服务仍指向固定目录、版本 1.6.1；`appsettings.json` 的 SHA-256 升级前后相同（`6cd33709…`）；端口保持 53970；升级前的数据都能读到；冒烟 33 项全过。安装日志（`installer-log-excerpts.txt`）里清除用户数据的三个动作都被跳过，没有出现组件规则的降级拒绝。向导（`wizard-upgrade-from-1.6.0.txt`、`wizard-upgrade-03-Location.png`）：只读页显示固定路径，结果与静默升级相同 |
| AC-07 卸载 | 通过 | `silent-fresh-install.txt`：不勾选清除——服务注销，固定目录下的 `appsettings.json` 保留（哈希不变），数据保留；再次安装后服务直接用保留的配置在 53970 上启动。勾选清除——固定目录下的 `appsettings.json` 被删除，数据目录清空，服务注销。两种情况下清除动作是执行还是跳过见 `installer-log-excerpts.txt` |
| AC-08 旧版装在自定义目录时升级 | 通过 | 不处理配置（`silent-upgrade-from-1.6.0-custom-dir.txt`）：旧版在 `C:\LabelFrame Test\Server`、端口 53970；升级 4.1 秒成功，新版本在固定目录、版本 1.6.1，按默认配置监听 53961；旧目录只剩那份改过的 `appsettings.json`；数据保留，冒烟 33 项全过。升级提示里的处理办法（`silent-upgrade-from-1.6.0-custom-dir-with-config-copied-first.txt`）：旧版在含中文和空格的路径；升级前把 `appsettings.json` 复制到 `C:\Program Files\LabelFrame\Server\`；升级后端口保持 53970，固定目录里的配置与复制过去的逐字节相同——安装包没有覆盖它 |
| AC-09 结构断言 | 通过 | `structure-assertion.txt`：断言脚本对最终包通过；对已发布的 1.6.0 包、误给的客户端包、去掉旧页面登记的副本、去掉许可门控的副本各自因对应的原因被拒。`structure-assertion-mutants.txt`：27 个变异包（每个只改坏一处）全部因预期的原因被拒，断言里的每条检查都有能触发它的反例 |
| AC-10 文档 | 通过 | CHANGELOG（修复一条、升级注意两条）、DEPLOY、README、DESIGN 决策表与未决问题、`docs/design/server.md`、ROADMAP 状态总览 |

## 与引导程序、离线包的等价关系

按 Issue 已决议 3，升级实测用等价的静默安装，没有另外构建引导程序与离线包走完整链路。等价关系来自代码核对：

- 引导程序 `packaging/bootstrapper/Bundle.wxs`：`ServerMsi` 是不带 `MsiProperty` 的 `MsiPackage`，调用服务端 MSI 时不传任何安装目录。
- 离线包 `packaging/offline-win/install-server.ps1`：安装是 `msiexec /i <msi> /qn /norestart /l*v <日志>`，修复是 `/fa`，同样不传安装目录。

这与三个静默升级轮次的调用形式一致。

## 验收中发现并处理的问题

都不在产品里，是取证过程自身的缺陷；下面各项都在对应轮次定稿之前改掉并重跑。

**一、第一次构建的测试包不能用来升级。** 本地构建时漏设 `VERSION` 环境变量，安装包版本是 1.6.1，包内程序文件版本却是 1.0.0.0。这样的包从 1.6.0 升级时会被组件规则拒绝。发现时只有「静默全新安装」一轮用过它（不涉及升级，结论不受影响），该轮已用重建的包重跑。物料自检增加了「包内程序文件版本必须等于安装包版本」，并留了一份缺陷构建做反向对照。

**二、开机前自检自身有漏洞。** 做反向验证时发现：参数检查会静默跳过不存在的客机脚本，也不检查经 `& (Join-Path …)` 引用的宿主脚本；物料检查在「哈希不符」这条路上抛异常而不是报告问题。三处都已修正，并用故意放入的错误逐项确认能拦住。

**三、轮次链的「测试机已在运行也算有条件」从未生效。** 链把 `True` 当成了非法值，所以宿主机被别的进程占满、测试机被手动开起来之后，链仍然在等内存。规则改对后加了 14 个场景的自检；同时补了一条：被内存压力挤扁的运行中虚拟机不算有条件，因为还原基线需要的内存拿不出来。

**四、向导走查对拦截对话框的识别。** 本机替身把拦截提示模拟成系统消息框，而真实安装器显示的是它自己的对话框，文字里又含「安装目录」，第一次被走查误认成安装目录页（产品行为本身是对的：对话框出现了，机器上没有变化）。按真实形态改了替身和识别规则后重跑该轮，上表 AC-03 的向导部分取自重跑。

## 说明

- 事件日志里记录到的错误与警告都是 Windows 自身的（碎片整理、时间服务、激活、DCOM 10016），与 [#482 的对照组](../482/README.md) 相同，没有来自 LabelFrame 的条目。
- Server MSI 的结构断言本轮只对最终包本地执行，接入 CI 是一笔欠账，由流程治理 Issue 承接。
