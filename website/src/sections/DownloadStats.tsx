import { useEffect, useState } from 'react';

interface Stats {
  schema: number;
  downloads: { count: number | null; updatedAt: string | null };
  activeInstalls: { count: number | null; windowHours: number; updatedAt: string };
}
const format = (value: number | null | undefined) =>
  Number.isSafeInteger(value) && value! >= 0 ? value!.toLocaleString('zh-CN') : '—';

export function DownloadStats() {
  const [stats, setStats] = useState<Stats | null>(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    const controller = new AbortController();
    async function update() {
      try {
        const response = await fetch('/t/v1/public-stats', {
          signal: AbortSignal.any([controller.signal, AbortSignal.timeout(12_000)]),
        });
        if (!response.ok) throw new Error('Stats unavailable');
        const data: Stats = await response.json();
        if (data.schema !== 1 || !data.downloads || data.activeInstalls?.windowHours !== 24) throw new Error('Invalid stats');
        if (!controller.signal.aborted) setStats(data);
      } catch {
        // Never keep showing an old activity count as if it were current.
        if (!controller.signal.aborted) setStats(null);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }
    void update();
    const timer = window.setInterval(() => { if (!document.hidden) void update(); }, 60_000);
    return () => { controller.abort(); window.clearInterval(timer); };
  }, []);
  return <div className="download-stats" aria-label="下载与使用统计" aria-busy={loading}>
    <dl>
      <div><dt>累计下载次数</dt><dd data-stat="downloads">{format(stats?.downloads.count)}</dd><dd className="download-stats__source">GitHub 应用发布包{stats?.downloads.updatedAt && Number.isFinite(Date.parse(stats.downloads.updatedAt)) ? <> · 截至 <time dateTime={stats.downloads.updatedAt}>{new Date(stats.downloads.updatedAt).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false })}</time></> : ' · 每小时更新'}</dd></div>
      <div><dt>近 24 小时活跃安装</dt><dd data-stat="active">{format(stats?.activeInstalls.count)}</dd><dd className="download-stats__source">匿名遥测上报 · 按安装去重</dd></div>
    </dl>
    <p className="download-stats__note">下载次数包含重复下载，不等于安装人数。活跃安装仅统计近 24 小时上报匿名遥测的安装，不代表实时在线；关闭遥测的安装不计入。数据暂不可用时显示 —。</p>
  </div>;
}
