# Linux Client 与容器 E2E

## 定位

Linux Client 是无头发布候选测试宿主，复用 Windows Client 的作业队列、Server 轮询、Skia 渲染、`^GF` 编码与结果回报链路。首版仅注册 `log` 传输：不连接物理打印机，渲染结果保存为 PNG。

Compose 默认组合：

- Server：稳定发布版 `ghcr.io/marci-labs/labelframe-server:0.21.0`；
- Server UI：从当前仓库前端源码构建，装入稳定 Server 镜像；
- Linux Client：从当前仓库源码构建，是待验证的发布候选。

因此测试结论是「稳定 Server 基线 + 当前 Client 候选」，Linux Client 正式发布前不能标记为双端稳定版。

## 启动与冒烟

```powershell
# 一次完成镜像构建、启动、设备上线、单张 / 多张、PNG 与重启恢复验证
.\scripts\test-linux-client-e2e.ps1
```

脚本通过后保留环境，管理界面为 `http://127.0.0.1:53910`。端口被占用时：

```powershell
.\scripts\test-linux-client-e2e.ps1 -ServerPort 53963
```

只启动环境：

```powershell
docker compose -f packaging/e2e/compose.yaml up -d --build
```

查看 Client 健康状态与 Log 输出：

```powershell
docker compose -f packaging/e2e/compose.yaml exec labelframe-client curl -s http://127.0.0.1:53960/healthz
docker compose -f packaging/e2e/compose.yaml exec labelframe-client find /var/lib/labelframe/client/print -name '*.png'
docker compose -f packaging/e2e/compose.yaml logs -f labelframe-client
```

停止环境：

```powershell
docker compose -f packaging/e2e/compose.yaml down
```

默认不删除数据卷，便于复查作业和 PNG。确认不再需要数据后，显式执行 `docker compose -f packaging/e2e/compose.yaml down --volumes`。

## 测试大纲

| 层级 | 检查项 | 判定 |
|---|---|---|
| 镜像 | Linux 目标发布、进程启动、健康检查 | `/healthz` 返回 `platform=linux`、`headless=true` |
| 能力边界 | 传输列表、插件与 UI | 仅 `log`；插件安装端点与 Client 根页面为 404 |
| 路由 | 注册、心跳、领取、回报 | Server 设备在线；作业最终 `Completed` |
| 渲染 | 单张 / 多张、中文、条码 | 每个 Item 生成一张非空 PNG，数量一致 |
| 可靠性 | 幂等、Client 重启、持久化 | 不重复建作业；重启后重新心跳并继续领取 |
| 浏览器 E2E | 模板保存、数据填写、设备选择、打印 | Server UI 主链可完成，页面无阻断性错误 |
| 回归 | Windows Client 与全仓测试 | 既有构建 / 测试全绿，Windows 专属传输仍可编译 |

## 已知边界

- Linux Client 不提供图形界面，配置全部来自 `LABELFRAME_*` 环境变量。
- 首版不支持 TCP 9100、USB、Windows 驱动、Zebra SDK 或第三方传输插件。
- Log 成功只证明软件链路与渲染输出正确，不替代真实打印机的走纸、扫码、缺纸和状态回读验收。
