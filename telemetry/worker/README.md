# FluidBar 匿名使用数据收集器

客户端在 `src/FluidBar.Core/Telemetry` + `src/FluidBar.Platform.Windows/Telemetry`，合同见 `docs/privacy/telemetry.md`。

## 部署

```powershell
cd telemetry/worker
npx wrangler login                       # 一次性
npx wrangler d1 create fluidbar_telemetry
# 把输出的 database_id 填进 wrangler.toml
npx wrangler d1 execute fluidbar_telemetry --remote --file=./schema.sql
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
| POST | `/v1/batch` | 上传一批会话，成功返回 202 |

拒绝策略（400）：schema 不匹配、installId 不是 32 位 hex、字段超长（>64 字符）、runs 超过 64 条、
出现白名单以外的顶层字段。**未知字段直接拒绝**，这样客户端将来多传字段会立刻暴露，而不是悄悄入库。

## 数据

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
