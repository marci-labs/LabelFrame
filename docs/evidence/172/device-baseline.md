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
