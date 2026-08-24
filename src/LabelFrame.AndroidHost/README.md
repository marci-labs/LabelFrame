# LabelFrame.AndroidHost

Android / PDA 打印宿主（迭代 5）。

> **实验状态，未随发布构建**：不在 `LabelFrame.slnx` 解决方案中，CI 不构建、无测试覆盖；真机验收未执行、16KB 页适配待验证。排期见 ROADMAP 迭代 25（真机验收通过后再纳入解决方案与自动发布）；此前请勿视为可交付组件。

## 职责

- 前台服务（`PrintHostService`）常驻 + 开机自启（`BootReceiver`，BOOT_COMPLETED / MY_PACKAGE_REPLACED）。
- 本地 HTTP 服务（仅 127.0.0.1:53970，TcpListener 极简实现）：健康检查、提交 / 查询 / 挂起 / 恢复 / 取消作业、打印机状态与测试页。
- IP 9100 打印机传输（复用 Core 的 `Tcp9100PrintTransport`）。
- 向 Server 注册设备并轮询领取定向作业（`ServerPoller`）。
- 中文栅格化：Android.Graphics 渲染为 1bpp 位图（^GF），与 WinHost 同契约。

## 配置（SharedPreferences：labelframe）

| 键 | 默认 | 说明 |
|---|---|---|
| `tcp_host` | 192.168.1.50 | 打印机 IP |
| `server_url` | 空 | Server 地址，为空不启用路由 |
| `device_id` | android-pda-1 | 注册到 Server 的设备标识 |

## 构建

```powershell
.\scripts\build-androidhost.ps1
```

要求：.NET 10 SDK + Android workload、Android SDK（platforms;android-36、build-tools 36.0.0）、JDK 17。
产出：`src\LabelFrame.AndroidHost\bin\Debug\net10.0-android\com.labelframe.androidhost-Signed.apk`。

## 说明

- Android 12+ 从后台启动前台服务受限，开机自启需用户在系统设置允许；厂商 ROM 保活差异见 docs/DESIGN.md §5。
- 真机验收（网页 → 宿主 → IP 打印机、开机自启）待执行。