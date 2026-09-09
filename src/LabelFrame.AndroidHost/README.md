# LabelFrame.AndroidHost

Android / PDA 打印宿主（迭代 5 立项，迭代 25 真机落地，迭代 40 配置面补齐）。

> **真机验收已通过（2026-09-08，UROVO DT50 / Android 11）**：注册 / 心跳 / 模板下发 / 作业打印（路由 + 直连）/ 离线恢复 / 断网重连（挂起 → 续打 → 失败项重打）/ 开机自启 / 前台服务保活 / JS 桥 CORS 全部通过。仍不在 `LabelFrame.slnx` 解决方案中、不随发布构建（纳入自动发布另行排期，见 ROADMAP 迭代 25）；遗留验收项（16KB 运行时验证）见 [ACCEPTANCE-BACKLOG.md](../../docs/ACCEPTANCE-BACKLOG.md)。

## 定位（决策 #95 / #96）

**后台打印执行服务**：打印入口在业务系统侧（Server 路由或第三方程序经 JS 桥），宿主自身不提供业务打印界面；唯一 UI 是原生配置页（点 App 图标打开），组织为「主页 + 三子页」（每屏一个任务，文案面向不懂技术的仓库用户）：

- **主页**：当前状态卡（语义色摘要——绿「一切正常」/ 红「连不上」/ 灰「服务未运行」+ 服务器 / 打印机明细）与三个设置入口（每行带当前值摘要）。
- **① 连接服务器**子页：地址 + 「测试连接」（用的是输入框地址，不用先保存）。
- **② 连接打印机**子页：IP + 端口（默认 9100）+ 「打印测试标签」——本地提交内置测试标签走完整链路（校验 → 渲染 → `^GF` → TCP 发送 → 终态）；输入地址未保存时提示「先保存再测试」（测试打印走已保存地址）。品牌 / 连接类型仍是未来传输插件的路由键（存配置，不在界面占位）。
- **③ 本机信息**子页：设备号取系统唯一码（ANDROID_ID）自动生成只读展示（等宽字体 + 复制）；设备名称可选编辑（默认 `PDA-<码后 4 位>`，注册 Server 随 name 上报）。
- 每个子页自带「**保存并重启服务**」：写配置后自动重启宿主服务生效，无需 force-stop。
- 常驻通知点击打开配置页，一句话文案（服务器状态 + 打印机地址）；浏览器打开 `http://127.0.0.1:53970` 为轻量状态页（本机 / 服务器 / 打印机三卡片 + 测试打印）。

## 职责

- 前台服务（`PrintHostService`）常驻 + 开机自启（`BootReceiver`，BOOT_COMPLETED / MY_PACKAGE_REPLACED）。
- 本地 HTTP 服务（仅 127.0.0.1:53970，TcpListener 极简实现）：健康检查、提交 / 列表 / 查询 / 挂起 / 恢复 / 取消作业、失败项重打、打印机状态与测试、宿主配置、测试打印、状态页；宽松 CORS + OPTIONS 预检（JS 桥）。
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
| `server_url` | 空 | Server 地址，为空不启用路由 |
| `printer_brand` | zebra | 打印机品牌（未来插件路由键） |
| `connection_type` | tcp | 连接类型（未来蓝牙等扩展） |
| `tcp_host` | 192.168.1.50 | 打印机 IP |
| `tcp_port` | 9100 | 打印机端口 |
| `device_name` | PDA-xxxx | 设备名称（注册 Server 展示；xxxx 为设备码后 4 位） |
| `device_uuid` | — | 设备号兜底（仅 ANDROID_ID 取不到时生成一次） |

设备号自动取 `Settings.Secure.ANDROID_ID` 原值（2026-09-09 起不再加 `pda-` 前缀），不接受配置（多台设备天然不撞号；恢复出厂后变化，视为新设备）。读写入口：配置页 UI 或 `GET/POST /api/host/config`（POST 持久化，重启宿主生效——配置页「保存并重启服务」自动完成重启）。

## 构建

```powershell
.\scripts\build-androidhost.ps1
```

要求：.NET 10 SDK + Android workload、Android SDK（platforms;android-36、build-tools 36.0.0）、JDK 17。
产出：`src\LabelFrame.AndroidHost\bin\Debug\net10.0-android\com.labelframe.androidhost-Signed.apk`（脚本已传 `-p:EmbedAssembliesIntoApk=true`，程序集打包进 APK，可脱离开发环境独立 `adb install`）。

注意：交付真机请用 **Release** 配置（`dotnet build -c Release`，约 25MB 自包含 APK）；Debug + 嵌入程序集约 74MB。`.NET Android 36.1.x` 起 `AndroidFastDeployment` 属性已失效（决策 #95 ⑦），关闭 Fast Deployment 必须用 `EmbedAssembliesIntoApk`。

## 原生库注意（决策 #94）

- `SQLitePCLRaw.lib.e_sqlite3.android` 定版 **2.1.11**：2.1.12/2.1.13 误装 glibc 构建的 so（装载即 LinkageError）；升级前必须核对 android 包 so 为 NDK 构建（DT_NEEDED 应为 liblog/libc/libm 等，而非 libc.so.6）。
- 桌面版 `SQLitePCLRaw.lib.e_sqlite3` 不得回到 Core 引用——经 RID 回退图（android-arm64 → linux-arm64）会把 glibc so 打进 APK 压过 android 包。
- 服务启动已先 `JavaSystem.LoadLibrary("e_sqlite3")`（Android 链接器命名空间要求）。

## 16KB 页

构建级验证通过（全部 arm64 so ELF 段对齐 ≥ 16KB、`zipalign -c -P 16`、无 XA0141）；运行时验证需 Android 15+ 16KB 内核设备（见 ACCEPTANCE-BACKLOG）。

## 说明

- Android 12+ 从后台启动前台服务受限，开机自启需用户在系统设置允许；UROVO DT50（Android 11）实测默认策略下前台服务常驻。
- 宿主重启后本地↔Server 作业映射（内存态）丢失，Server 侧已 Claimed 作业停留 Claimed——与 WinHost / Linux Client 同构的既有语义，见 DESIGN「未决问题」。
