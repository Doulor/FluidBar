# FluidBar 匿名使用数据收集器

客户端在 `src/FluidBar.Core/Telemetry` + `src/FluidBar.Platform.Windows/Telemetry`，合同见 `docs/privacy/telemetry.md`。

## 部署

```powershell
cd telemetry/worker
npx wrangler login                       # 一次性
npx wrangler d1 create fluidbar_telemetry
# 把输出的 database_id 填进 wrangler.toml
npx wrangler d1 execute fluidbar_telemetry --remote --file=./schema.sql
npx wrangler d1 execute fluidbar_telemetry --remote --file=./public-stats.sql
npx wrangler secret put TELEMETRY_SALT   # 随便一串长随机字符串
npx wrangler deploy
```

部署完自检：

```powershell
curl.exe https://fluidbar.doulor.cn/t/v1/health
# {"ok":true,"schema":1}
```

生产流量通过官网 Worker 的 `/t/*` 路径进入（`website/wrangler.worker.toml` 的 `TELEMETRY` 服务绑定），
不依赖 `workers.dev` 或新子域在当前网络的可达性。

## 接口

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/v1/health` | 存活检查 |
| GET | `/v1/public-stats` | 官网公开聚合统计：应用包下载次数、近 24 小时上报的去重安装数 |
| POST | `/v1/batch` | 上传一批会话，成功返回 202 |

拒绝策略（400）：schema 不匹配、installId 不是 32 位 hex、字段超长（>64 字符）、runs 超过 64 条、
出现白名单以外的顶层字段。**未知字段直接拒绝**，这样客户端将来多传字段会立刻暴露，而不是悄悄入库。

## 数据

公开统计经官网 `/t/v1/public-stats` 访问，只返回数字和统计时间，不返回匿名 ID、环境或会话明细。
活跃安装以服务端 `received_at` 的最近 24 小时窗口按 `install_hash` 去重，包含延迟上传，
不代表实时在线，也不覆盖关闭遥测的安装。无需客户端新心跳。
部署公开统计前需执行 `public-stats.sql`（仅新增一张保存下载总数的表，不修改遥测行）。
下载次数汇总 GitHub 所有应用 Releases 的 FluidBar ZIP/EXE/MSI/MSIX 包，排除插件发布、签名和校验文件，
包含重复下载和应用更新，不等于安装人数。成功结果存入 D1，共享给各节点；每小时 cron 尝试更新，
上游失败时保留最后一次真实结果和原更新时间，官网显示“截至”时间。可运行
`node telemetry/worker/refresh-downloads.mjs` 从本地 GitHub API 核实并更新共享缓存。
边缘下载缓存 1 小时，聚合接口缓存 60 秒；无可用结果时对应数值为 `null`，官网显示 `—`，真实零值仍显示 `0`。

- `batches` 一行一批：`received_at`、`install_hash`（salt + installId 的 SHA-256，**不存原始 ID**）、
  `version`、`channel`、`self_contained`、`os`、`arch`、`locale`、`run_count`、`dropped`、`payload`（原始 JSON）。
- 不落 IP、不落 UA、不落 Referer；本 Worker 不写访问日志。
- 保留期：每日 cron 删除 30 天前的行。

## 常用查询

```sql
-- 各版本活跃安装数（按 install_hash 去重）
SELECT version, COUNT(DISTINCT install_hash) AS installs, COUNT(*) AS batches
FROM batches WHERE received_at >= datetime('now','-7 days') GROUP BY version ORDER BY installs DESC;

-- 启动耗时分布（500ms 一档）
SELECT version,
       CAST(json_extract(run.value,'$.startupMs') / 500 AS INT) * 500 AS bucket_ms,
       COUNT(*) AS n
FROM batches, json_each(batches.payload, '$.runs') AS run
WHERE batches.received_at >= datetime('now','-7 days')
GROUP BY version, bucket_ms ORDER BY version, bucket_ms;

-- 异常退出占比
SELECT version,
       SUM(json_extract(run.value,'$.reason') = 'crash') * 1.0 / COUNT(*) AS crash_rate
FROM batches, json_each(batches.payload, '$.runs') AS run
WHERE batches.received_at >= datetime('now','-7 days')
GROUP BY version;
```
