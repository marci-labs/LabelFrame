# DT50 同签名能力基线与备份

2026-10-27，任务 [#474](https://github.com/marci-labs/LabelFrame/issues/474)。本次仅完成能力基线覆盖和数据保留核查，不是产品下载实升、ART 候选验签或系统确认安装验收。

## 设备与恢复条件

- 唯一设备 `01792423013057`，DT50；操作前原宿主 `1.5.0 / 10500`、DEBUGGABLE、stopped=true、无 PID。
- 原配置为空 ServerUrl，品牌 `validation-simulation`，TCP `127.0.0.1:59999`，设备名 `LabelFrame隔离升级验证`。没有生产连接或物理打印。
- 原 APK 与停止状态下的完整 `files`、`shared_prefs` 已保存至本机 `C:/Users/Marci/AppData/Local/Temp/LabelFrame-android-device-474-20261027`。数据 tar 为 211293696 字节，包含数据库及 WAL/SHM、插件、配置、历史迁移备份与日志；抽取主库与 WAL/SHM 后只读 `integrity_check=ok`。
- 原 APK SHA256：`58576F598556956D3683F23C34E824D111AA057855DB62616A6088DA81999DAA`；数据 tar SHA256：`16F3A4E157A65FECE4B2EB16CF443907681475CC8CD4A438CE7A639783D5A140`。
- 官方 apksigner 读取原证书为 `27fc986d5450b7d73fe842de76b91bfe76576818911907e0a4d818b023423235`（LabelFrame Isolated Validation），与交接中的本机 Debug 证书不同。因此未直接安装交接 APK。
- 通过 #462 原证据定位到保留的隔离验证 keystore，使用同一证书重新签署本轮非调试 APK；密钥及密码未入库。备份用于诊断与获授权后的恢复，不承诺可直接降级旧 APK 或恢复陈旧数据；失败时保留现场，不卸载、不清数据。

## 实际部署结果

- 非调试能力基线 `1.5.2 / 10502`，SHA256 `EA8BEFD0312B7D8E127D661E2419112381117C9B0D0DE1A7070BD87C89AD531B`，官方校验 v1/v2/v3 成功、单 signer；aapt2 无 debuggable，arm64-v8a。
- ADB 同签名覆盖返回 Success，仅用于首次获得更新能力，不计作产品升级验收。启动后真实 `/healthz` 返回 `LabelFrame.AndroidHost / ok / 1.5.2`。
- 配置 API 仍返回原空 ServerUrl、原品牌/回环连接/设备名；设备 ID `ca83fcf02f67e521`。
- 6 个原作业保留：Cancelled 1、Completed 2、Failed 1、Suspended 2；原 `interrupted` 与 `suspended` 挂起未解除，未重打。`validation-simulation` 插件 Loaded=true、LoadError=null。
- 构建下一候选 `1.5.3 / 10503`：Release/AOT 0 警告 0 错误；同隔离证书签署后 SHA256 `FDF97EFFBFEA047DD6EF7665814BA1FBB2238C3C15964EB94B44E48E89689D8F`。仅存本机候选，未 ADB 安装、未受控发布。

## 验收阻断与下一步

设备截图全黑，唤醒/解除锁屏组合命令被执行策略拒绝，命令没有执行；未绕过限制。该命令中配置写入亦未执行，随后 GET 再次确认 ServerUrl 仍为空。需用户手动唤醒并解锁设备后继续原生界面验收。

临时 Server 只监听 `127.0.0.1:54975`，数据库/包/日志均在上述临时目录的 `server` 子目录；仅验证健康接口，没有设备注册或发布。真机 ART 校验、系统确认/取消/进程重建、人工恢复、健康链及业务负向仍未完成，不能据本页标记 AC-07/08/09/10 通过。

## 用户解锁与配置后的继续验证

- 用户手动解锁并填写隔离地址后，真实设备于 14:40 注册为 Online，配置 API 返回 `http://127.0.0.1:54975`，仍仅 USB 回环转发。
- 发布 `7c25dee710054f6290b91a41937b718b` 只指定设备 `ca83fcf02f67e521`；候选为上述同签名 10503，23865413 字节。未用 ADB 安装候选。
- 14:41:30 设备报告 ready；当时仍在连接服务器页，证明下载不依赖更新页。随后真实更新页展示当前 1.5.2、候选 1.5.3、已下载等待确认、业务未冻结、系统安装许可尚未允许。根据 ready 的实现路径，已完成设备 ART 官方验签桥、Manifest/ABI/协议/摘要独立核验；不等同系统安装已成功。
- 首次无候选时，设备曾报告缓存父目录不存在的 `IO_PathNotFound_Path`。修复只忽略删除候选时的 DirectoryNotFoundException，其他 IO 错误继续上报；新增首次无目录与重复清理/保留旁文件两项测试。Android 托管回归 121 项全过；修复尚未部署到真机，需后续候选复验。
- 系统安装许可须用户显式授予。尚未提交安装，两个原挂起作业未修改。当前保留隔离服务及 USB 转发供继续验收，不将本节当 AC-07/08/09/10 全部完成。

## 挂起保护与系统取消

- 用户手动授予系统安装许可后，更新页显示已允许。首次按②并等待确认键启用后提交，14:46:42 设备日志明确拒绝：仍有作业、未决路由、配置编辑或维护事务。版本保持 1.5.2、两个作业仍 Suspended、业务未冻结，未创建安装事务。
- 真实界面发现阻断原因仅入日志，Toast 只有通用错误。已修改准备操作将原诊断持续显示在更新页；Release/AOT 1.5.4/10504 构建 0 警告 0 错误，尚未部署，不冒充界面修复已验收。
- 为继续正向验证，经正常取消 API 结束两个已备份历史隔离夹具 `validation-interrupted`、`validation-suspended`；作业记录和已完成/失败项保留，不删数据、不恢复陈旧库、不新增发送。此后它们为 Cancelled，不再声称原状态未变化。
- 第二次准备真实创建事务 `21d718cfd0e640798849b98f817bf1ce`，设备报告 confirmation。点击③显示 Android 原生「要为此应用安装更新吗？您现有的数据将不会丢失」对话框，未走公共 Download 或 ADB 安装。
- 点击系统取消，收到 `INSTALL_FAILED_ABORTED: User rejected permissions`，随后页面恢复未冻结、②可用，实际版本仍 1.5.2。第三次准备继续用于用户最终安装确认；更新后新进程健康尚待验证。

## 产品实升与新进程健康通过

- 用户明确点击第二次系统确认框的「安装」。PackageManager 实际 `lastUpdateTime=2026-10-27 14:55:01`，versionCode=10503、versionName=1.5.3，pkgFlags 不含 DEBUGGABLE。候选没有经 ADB 安装。
- 旧进程 PID13749 → 新进程 PID14100；14:55:05.395 日志记录收到 `android.intent.action.MY_PACKAGE_REPLACED` 拉起打印服务，14:55:06.272 完成初始化、开放 HTTP 与 Worker。取证之前没有人为启动新版本来代替自启验证。
- 实际本地 `/healthz` 返回 `LabelFrame.AndroidHost / ok / 1.5.3`；事务 `b596471e04ed421bac9a98585e030f59` 在 14:55:47 自报 succeeded、reason=null、实际版本10503。该终态由设备新进程执行实际签名/版本、原配置摘要、插件文件摘要与加载、品牌、本地 API 和原服务端注册健康链后形成，不只依赖系统安装成功回执。
- 独立复读配置：设备 ID、设备名、隔离 ServerUrl、validation-simulation 品牌、tcp/127.0.0.1:59999 均保留。插件 Loaded=true、LoadError=null；6 条作业及总项数、已完成数与本次安装前一致（两个挂起夹具已按上一节正常取消）。服务端同一设备持续 Online，14:56:30 有真实注册更新。
- 14:57 人为打开更新页做视觉验收，实际显示「更新及健康核验通过」、版本1.5.3、业务未冻结。没有以模拟打印或物理出纸证明健康。
- 边界：这是非调试 APK＋原隔离验证证书的同签名实升，不是正式发行私钥验收。人工恢复、确认期间进程重建、中英文全界面、整批/编辑负向与最新修复候选仍需继续；完整回归、PR三门禁及逐AC结项未完成。

![Android 系统确认](device-system-confirmation.png)

![新版本健康通过](device-update-success.png)
