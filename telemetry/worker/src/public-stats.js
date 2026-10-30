const RELEASES = 'https://api.github.com/repos/Doulor/FluidBar/releases?per_page=100';
const CACHE_URL = 'https://fluidbar.doulor.cn/t/cache/application-downloads';

export async function countDownloads(fetcher = fetch) {
  let url = RELEASES;
  let total = 0;
  const timestamp = new Date().toISOString();
  for (let page = 0; page < 10; page++) {
    const response = await fetcher(url, {
      headers: { 'User-Agent': 'FluidBar-Public-Stats', Accept: 'application/vnd.github+json' },
      signal: AbortSignal.timeout(8000),
    });
    if (!response.ok) throw new Error(`GitHub unavailable (${response.status})`);
    const releases = await response.json();
    if (!Array.isArray(releases)) throw new Error('Invalid releases');
    for (const release of releases) {
      if (release.draft || release.tag_name.startsWith('plugin-')) continue;
      for (const asset of release.assets) {
        if (!/^FluidBar[^/]*\.(zip|exe|msi|msix|msixbundle)$/i.test(asset.name)) continue;
        if (!Number.isSafeInteger(asset.download_count) || asset.download_count < 0) throw new Error('Invalid count');
        total += asset.download_count;
      }
    }
    const next = response.headers.get('link')?.match(/<([^>]+)>;\s*rel="next"/);
    if (!next) return { count: total, updatedAt: timestamp };
    const target = new URL(next[1]);
    if (target.origin !== 'https://api.github.com' || target.pathname !== '/repos/Doulor/FluidBar/releases') throw new Error('Invalid next page');
    url = target.toString();
  }
  throw new Error('Release pagination limit');
}

export async function storedDownloads(env, force = false) {
  const saved = await env.DB.prepare('SELECT count, updated_at AS updatedAt FROM public_download_stats WHERE id = 1').first();
  if (!force && saved && Date.now() - Date.parse(saved.updatedAt) < 3600_000) return saved;
  try {
    const current = await countDownloads();
    await env.DB.prepare('INSERT INTO public_download_stats (id, count, updated_at) VALUES (1, ?, ?) ON CONFLICT(id) DO UPDATE SET count = excluded.count, updated_at = excluded.updated_at')
      .bind(current.count, current.updatedAt).run();
    return current;
  } catch (error) {
    if (saved) return saved;
    throw error;
  }
}

async function downloads(env, cache) {
  const key = new Request(CACHE_URL);
  const saved = await cache?.match(key).catch(() => undefined);
  if (saved) return saved.json();
  const result = await storedDownloads(env);
  await cache?.put(key, new Response(JSON.stringify(result), {
    headers: { 'content-type': 'application/json', 'cache-control': 'public, max-age=3600' },
  })).catch(() => {});
  return result;
}

export async function publicStats(env, cache) {
  const now = new Date();
  const since = new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString();
  const [downloadResult, activityResult] = await Promise.allSettled([
    downloads(env, cache),
    env.DB.prepare('SELECT COUNT(DISTINCT install_hash) AS count FROM batches WHERE received_at >= ? AND received_at <= ?')
      .bind(since, now.toISOString()).first(),
  ]);
  const activeCount = activityResult.status === 'fulfilled' ? activityResult.value?.count : null;
  if (downloadResult.status === 'rejected') console.warn('Public download statistics unavailable:', downloadResult.reason?.message);
  return new Response(JSON.stringify({
    schema: 1,
    downloads: downloadResult.status === 'fulfilled' ? downloadResult.value : { count: null, updatedAt: null },
    activeInstalls: {
      count: Number.isSafeInteger(activeCount) && activeCount >= 0 ? activeCount : null,
      windowHours: 24,
      updatedAt: now.toISOString(),
    },
  }), { headers: {
    'content-type': 'application/json; charset=utf-8',
    'cache-control': 'public, max-age=60',
    'x-content-type-options': 'nosniff',
  } });
}
