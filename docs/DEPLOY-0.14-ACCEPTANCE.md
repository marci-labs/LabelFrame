# 0.14 双包部署与同机联调验收步骤

> 状态：2026-08-11 制定；适用于 LabelFrame-Server-0.14.0.msi + LabelFrame-Client-0.14.0.msi（同机安装，验证服务端 ↔ 客户端连接闭环）。

## 0. 准备
- 目标机需已安装 .NET 10 Desktop Runtime（x64）。
- 若已装旧 0.13.x 单机版：建议先停止/卸载（避免 53960 端口冲突）；停止旧进程：`Stop-Process -Name LabelFrame.WinHost`（确认路径为旧根目录版后再停）。
- 两个安装包：`artifacts\LabelFrame-Server-0.14.0.msi`、`artifacts\LabelFrame-Client-0.14.0.msi`。

## 1. 安装
1. 先装 Server → `C:\Program Files\LabelFrame\Server`（启动为控制台进程，监听 `0.0.0.0:53961`）。
2. 再装 Client → `C:\Program Files\LabelFrame\Client`（托盘程序，监听 `127.0.0.1:53960`，默认 `ServerUrl=http://127.0.0.1:53961`）。
3. 验证监听：
   ```
   netstat -ano | findstr 53960
   netstat -ano | findstr 53961
   ```
   期望：53960（Client，127.0.0.1）、53961（Server，0.0.0.0）。

## 2. 基础连通
- 浏览器打开 `http://127.0.0.1:53961`（Server 测试入口 / Web UI 均可）。
- Client 启动后自动注册：Server 设备目录（`/api/devices` 或 Web UI）应出现本机设备，状态 Online。
- 未出现：检查 Client `host.log`（`%LOCALAPPDATA%\LabelFrame\host.log`）与 Server 日志。

## 3. 模板准备
- Server Web UI → 工作台/设计器 → 新建模板（或导入 `.lfpkg`）；保存后模板库可见。

## 4. 提交作业（目标设备 = 本机 Client）
1. 数据与打印页 →「目标设备」下拉选择本机 Client（在线）。
2. 选模板 → 填数据 → 打印测试（单张）或批量打印。
3. 期望链路：Server 收作业（Pending）→ Client 领取（Claimed）→ 打印 → 回报 Completed。
   - Client 当前连接为 **Log 模拟**：作业进度/`print` 目录 `%LOCALAPPDATA%\LabelFrame\print\{jobId}\` 出现 PNG，host.log 有摘要；
   - 若 Client 配了 **WindowsDriver**（USB 打印机）：真机出纸。
4. Server 作业列表/进度显示 Completed、完成张数。

## 5. 失败与离线场景
- Client 连接改成错误地址/断开打印机 → 提交 → Server 作业显示 Failed + 原因（客户端回报）。
- Client 停止 → 提交 → Server 作业保持 Pending（设备离线暂存）→ 启动 Client → 自动领取并打印（不丢作业）。

## 6. 卸载清理（可选验证）
- 用**全 UI** 卸载（控制面板「程序和功能」→ 卸载，或 `msiexec /x <msi>` 不带静默参数）→ 应弹出「清除用户数据（默认不勾选）」。
- 不勾选：数据保留；勾选：删除默认路径数据（Client：jobs/templates/logs/host.log/connection.json/print + appsettings；Server：server 目录/server.db + appsettings）。升级不触发清理。

## 7. 通过标准
- 设备在线、模板可设计/导入、提交→领取→打印→回报闭环；
- 失败可解释、离线暂存不丢作业；
- 卸载弹窗与清理行为符合预期。