# 匿名使用数据（Telemetry）

Status: **shipped in 4.0.0-preview.4** · Owner: lead · Client: `src/FluidBar.Core/Telemetry`,
`src/FluidBar.Platform.Windows/Telemetry`, `src/FluidBar.App/Hosting/TelemetryService.cs` ·
Server: `telemetry/worker`

## 开关

设置 → 通用 → **匿名使用数据**，**默认开启**。关闭时本地排队数据立即删除（`TelemetryQueue.Clear`），
之后不再发送任何内容。设置项：`general.telemetry`（`FluidBarSettings.General.Telemetry`）。

设置页有两个按钮：

- **查看将要发送的数据** — 把下一批的**原始字节**写到 `%TEMP%\fluidbar-telemetry-preview.json` 并打开，
  所见即所发，没有隐藏字段。
- **重置匿名 ID** — 换掉安装 ID 并清空队列。

## 发送什么（完整字段表）

一批（`POST https://t.fluidbar.doulor.cn/v1/batch`）最多 32 KB、最多 32 条会话：

```json
{
  "schema": 1,
  "installId": "9f2c… 本机随机 GUID，32 位 hex",
  "sentAt": "2026-10-30T00:00:00+00:00",
  "environment": {
    "version": "4.0.0-preview.4",
    "channel": "preview",
    "selfContained": true,
    "os": "10.0.22631",
    "arch": "x64",
    "locale": "zh-Hans"
  },
  "runs": [
    { "startedAt": "2026-10-29T23:00:00+00:00", "uptimeSeconds": 3612, "startupMs": 240, "firstRun": false, "reason": "user" }
  ],
  "dropped": 0
}
```

| 字段 | 含义 |
|---|---|
| `installId` | 本机随机 GUID；重置即变；不来自硬件、用户名或机器名 |
| `version` / `channel` | 版本号与通道（stable/preview） |
| `selfContained` | 是否自带运行时的单文件包（定位"缺少 .NET 运行时"一类问题） |
| `os` / `arch` | Windows 版本号（major.minor.build）与进程架构 |
| `locale` | 界面语言 |
| `startedAt` / `uptimeSeconds` | 会话起止（时长封顶 7 天） |
| `startupMs` | 进程启动到灵动岛可用的耗时（封顶 120 s） |
| `firstRun` | 是否为该安装的首次会话 |
| `reason` | `user` / `system` / `crash`（上次会话没正常结束 → 下次启动补记 crash） |
| `dropped` | 本机队列因长期离线丢弃的条数（上报健康度） |

## 绝不发送

歌名、歌手、专辑、封面图、歌词文本、通知标题/正文/应用名、剪贴板、窗口标题、文件路径、用户名、
主机名、天气城市、任务/待办内容、Agent 会话、插件配置内容、IP 地址、硬件指纹。

实现约束（有测试守着）：payload 由**手写白名单**序列化（`TelemetryPayload.Serialize`）；
Worker 收到**白名单以外的新顶层字段直接 400** —— 客户端将来多传字段会立刻暴露，而不是悄悄入库。

## 服务端

`telemetry/worker`（Cloudflare Worker + D1）。数据库**只存** `installId` 的加盐 SHA-256（`install_hash`），
不存明文 ID、不存 IP/UA。原始行保留 **30 天**，每日 cron 清理；聚合结果长期保留。

## 本地文件

| 路径 | 内容 |
|---|---|
| `%APPDATA%\FluidBar\telemetry\install-id` | 匿名 ID |
| `%APPDATA%\FluidBar\telemetry\queue.tsv` | 待上传会话（容量 256 条，超出丢最旧） |
| `%APPDATA%\FluidBar\telemetry\session.tsv` | 当前会话开始时间（判定上次是否崩溃） |
| `%TEMP%\fluidbar-telemetry-preview.json` | "查看将要发送的数据"生成的文件 |

## 已知边界

- 目前只做**心跳级**统计（启动数、时长、启动耗时、崩溃）。功能级计数（封面/歌词命中来源等）尚未启用；
  启用时必须同样走白名单，并同步更新本文档与 Worker 校验。
- 中国大陆网络下若 `t.fluidbar.doulor.cn` 不可达，客户端重试三次后静默放弃，队列留到下次启动；不影响任何功能。
