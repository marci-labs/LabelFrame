# LabelFrame.AndroidHost

Android / PDA 打印宿主（迭代 5 立项，迭代 25 真机落地）。

> **真机验收已通过（2026-09-08，UROVO DT50 / Android 11）**：注册 / 心跳 / 模板下发 / 作业打印（路由 + 直连）/ 离线恢复 / 断网重连（挂起 → 续打 → 失败项重打）/ 开机自启 / 前台服务保活 / JS 桥 CORS 全部通过。仍不在 `LabelFrame.slnx` 解决方案中、不随发布构建（纳入自动发布另行排期，见 ROADMAP 迭代 25）；遗留验收项（真实打印机物理出纸、16KB 运行时验证）见 [ACCEPTANCE-BACKLOG.md](../../docs/ACCEPTANCE-BACKLOG.md)。

## 职责

- 前台服务（`PrintHostService`）常驻 + 开机自启（`BootReceiver`，BOOT_COMPLETED / MY_PACKAGE_REPLACED）。
- 本地 HTTP 服务（仅 127.0.0.1:53970，TcpListener 极简实现）：健康检查、提交 / 列表 / 查询 / 挂起 / 恢复 / 取消作业、失败项重打、打印机状态与测试页；宽松 CORS + OPTIONS 预检（JS 桥）。
- IP 9100 打印机传输（复用 Core 的 `Tcp9100PrintTransport`）。
- 向 Server 注册设备并经 notify 长轮询（20s）领取定向作业 + 独立 1s 回报循环（与 WinHost `ServerRoutingWorker` 同构）。
- 中文栅格化：Android.Graphics 渲染为 1bpp 位图（^GF），与 WinHost 同契约。

## 第三方 PDA 程序集成（决策 #93）

本宿主是 PDA 上唯一打印执行宿主；第三方程序不嵌入 LabelFrame 代码，统一走 HTTP 公共契约：

- **路由模式（主）**：任意程序 → Server `POST /api/jobs`（`targetDeviceId` 指向本 PDA 的 device_id）→ 宿主自动领取打印 → Server 查询终态。
- **直连模式（就近单张）**：与 PDA 同机的 App / WebView / 浏览器页面 → `http://127.0.0.1:53970`（即「JS 桥」，fetch 直接调用，跨源已放开）。

不做 Android SDK / Intent / 广播等原生契约形态（有真实需求先更新 DESIGN 再实施）。

## 配置（SharedPreferences：labelframe）

| 键 | 默认 | 说明 |
|---|---|---|
| `tcp_host` | 192.168.1.50 | 打印机 IP |
| `server_url` | 空 | Server 地址，为空不启用路由 |
| `device_id` | android-pda-1 | 注册到 Server 的设备标识 |
| `pc_host` | 空 | PC 单机服务地址（PDA 测试模式，决策 #42） |

## 构建

```powershell
.\scripts\build-androidhost.ps1
```

要求：.NET 10 SDK + Android workload、Android SDK（platforms;android-36、build-tools 36.0.0）、JDK 17。
产出：`src\LabelFrame.AndroidHost\bin\Debug\net10.0-android\com.labelframe.androidhost-Signed.apk`（脚本已关闭 Fast Deployment，可脱离开发环境独立 `adb install`）。

注意：交付真机请用 **Release** 配置（`dotnet build -c Release`，约 25MB 自包含 APK）；Debug 默认 Fast Deployment 的产物不含程序集。

## 原生库注意（决策 #94）

- `SQLitePCLRaw.lib.e_sqlite3.android` 定版 **2.1.11**：2.1.12/2.1.13 误装 glibc 构建的 so（装载即 LinkageError）；升级前必须核对 android 包 so 为 NDK 构建（DT_NEEDED 应为 liblog/libc/libm 等，而非 libc.so.6）。
- 桌面版 `SQLitePCLRaw.lib.e_sqlite3` 不得回到 Core 引用——经 RID 回退图（android-arm64 → linux-arm64）会把 glibc so 打进 APK 压过 android 包。
- 服务启动已先 `JavaSystem.LoadLibrary("e_sqlite3")`（Android 链接器命名空间要求）。

## 16KB 页

构建级验证通过（全部 arm64 so ELF 段对齐 ≥ 16KB、`zipalign -c -P 16`、无 XA0141）；运行时验证需 Android 15+ 16KB 内核设备（见 ACCEPTANCE-BACKLOG）。

## 说明

- Android 12+ 从后台启动前台服务受限，开机自启需用户在系统设置允许；UROVO DT50（Android 11）实测默认策略下前台服务常驻。
- 宿主重启后本地↔Server 作业映射（内存态）丢失，Server 侧已 Claimed 作业停留 Claimed——与 WinHost / Linux Client 同构的既有语义，见 DESIGN「未决问题」。
