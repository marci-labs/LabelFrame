# 迭代 19 规格：Ubuntu 服务端部署 + 跨机验证

> 状态：2026-08-11 制定；Windows 服务端 0.15.4 已验收（0.15.x 系列）。
> 目标：服务端可部署到 Ubuntu（systemd），客户端仍为 Windows（打印 / 托盘 / UI 在 Windows），验证“服务端 Linux + 客户端 Windows”跨机功能正常。

## 1. 背景与目标

- Server 目前 TFM `net10.0-windows`，依赖 Windows 服务托管与 GDI（Rendering）。Windows 部署已验证（0.15.x）。
- 目标：Server 可发布到 Ubuntu（linux-x64）并以 systemd 服务运行；Windows Client 通过 HTTP 指向 Linux Server，全链路（设备注册 / 模板库 / 作业 / 推送通知 / 调试出图 / 日志 / 历史清理）保持正常。
- 打印仍在 Windows Client（Skia 渲染 → ^GF → 驱动）；Server 无打印机依赖（架构已保证）。

## 2. 范围

- Rendering 多目标：`net10.0;net10.0-windows`；GDI `LabelPreviewRenderer`（System.Drawing）仅 Windows；Skia 渲染跨平台。
- Server 多目标：`net10.0;net10.0-windows`；`UseWindowsService` / 应用图标 / WindowsServices 包仅 Windows；Linux 用 systemd。
- Server 数据目录按平台默认：Windows `%ProgramData%\LabelFrame\server`；Linux `/var/lib/labelframe/server`；`LABELFRAME_SERVER_*` 环境变量优先（已有）。
- 发布脚本：`scripts/publish-server-linux.ps1`（linux-x64，framework-dependent 默认，`-SelfContained` 可选）。
- systemd 单元 `packaging/ubuntu/labelframe-server.service` + 部署脚本 `scripts/deploy-server-ubuntu.sh`（建用户/目录、复制发布物、安装单元、自启、启动、防火墙提示）。
- 可选 `packaging/ubuntu/Dockerfile`（便于容器化验证/部署）。
- 跨机验证：Windows Client 设置服务端地址指向 Linux Server → 全链路验证（见 §5）。

## 3. 不在范围

- Linux 客户端（打印仍 Windows）。
- Android / PDA（延后）。
- 高可用 / 负载均衡 / TLS / 鉴权。
- Windows 服务端行为变更（保持 0.15.4）。

## 4. 平台差异与决策

1. **Rendering 多目标**：`net10.0`（Linux，Skia + ZXing，无 System.Drawing）；`net10.0-windows`（含 GDI 预览）。Server 仅用 Skia 渲染。
2. **Server 多目标**：`net10.0;net10.0-windows`；`#if WINDOWS` 包住 `UseWindowsService` 与 WindowsServices 包引用；应用图标仅 Windows。
3. **数据目录默认**：Windows `%ProgramData%\LabelFrame\server`；Linux `/var/lib/labelframe/server`；环境变量覆盖优先。
4. **服务托管**：Windows 用 Windows 服务；Linux 用 systemd `Type=simple`（直接跑 `LabelFrame.Server`），不引 WindowsServices 包。
5. **监听**：appsettings `Server.ListenUrl=http://0.0.0.0:53961`（跨平台）；systemd 单元可用 `LABELFRAME_SERVER_LISTEN` 显式覆盖。
6. **.NET 运行时**：默认 framework-dependent（目标机装 ASP.NET Core Runtime 10）；`-SelfContained` 可发布免运行时包。
7. **端口**：53961/TCP；Ubuntu 需放行（`ufw allow 53961/tcp`）。

## 5. 跨机验证清单（服务端 Ubuntu / 客户端 Windows）

1. Ubuntu 安装 .NET 10 ASP.NET Core Runtime → 上传发布产物 → 运行部署脚本 → `systemctl status labelframe-server` 为 active(running)。
2. Ubuntu 本机冒烟：`curl http://127.0.0.1:53961/healthz` → ok；`curl http://127.0.0.1:53961/api/jobs?limit=10` → `[]`。
3. Windows Client 设置页「服务端地址」填 `http://<Ubuntu-IP>:53961` → 测试连接成功（机器级保存）。
4. 设备注册：Server `/api/devices` 出现 Online 设备（Windows 机器名）。
5. 模板：Windows Client 设计器新建/保存模板 → Ubuntu Server 模板库可见。
6. 作业：Client 数据与打印选本机设备 → 打印测试（Log 模拟）→ 推送通知 <1s 领取 → Completed；PNG 落在 Windows Client 本地。
7. 调试出图：Client 下载单张 PNG / 批量 zip（Server Skia 渲染）。
8. 日志：Client 日志页可见（/api/logs）。
9. 失败 / 离线：停 Client → 提交 → Pending 暂存；启动 Client → notify 长轮询恢复自动领取。
10. 历史清理：作业历史页可见；保留期配置生效（`LABELFRAME_SERVER_JOB_RETENTION_DAYS` 等环境变量）。

## 6. 验收标准

- `dotnet test` 全绿（Windows）。
- linux-x64 publish 产物包含 Linux 原生依赖：`runtimes/linux-x64/native/e_sqlite3.so`、Skia 的 `libSkiaSharp.so`（如主包不含则加 `SkiaSharp.NativeAssets.Linux`）。
- Ubuntu 上 `systemctl start labelframe-server` → healthz ok；数据落在 `/var/lib/labelframe/server`。
- Windows Client 指向 Linux Server 全链路（设备 / 模板 / 作业 / 推送 / 出图 / 日志）通过（容器或真机）。
- 文档：README 增加 Ubuntu 部署章节；ARCHITECTURE-SPLIT 注明平台差异；ROADMAP / CHANGELOG 更新。

## 7. 不在范围外的风险

- SkiaSharp Linux 原生库：主包可能不含 Linux native，发布后校验，缺失则添加 `SkiaSharp.NativeAssets.Linux`。
- SQLitePCLRaw `libe_sqlite3.so`：随 publish 的 runtimes/linux-x64 输出，需校验存在。
- 本机 Docker / WSL 若不可用，跨机验证走真机或交付验证清单。

## 8. 启动命令

> 继续 LabelFrame 迭代 19（Ubuntu 服务端部署）。先读 AGENTS.md、docs/DESIGN.md、docs/REQUIREMENTS.md、docs/ROADMAP.md、docs/ARCHITECTURE-SPLIT.md、docs/ITERATION-19-SPEC.md；按范围实施；提交用 Conventional Commits；不推 tag；仓库内容不得出现公司 / 业务线品牌字样。
