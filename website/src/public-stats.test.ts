import { afterEach, describe, expect, it, vi } from 'vitest';
// @ts-expect-error The deployed Cloudflare Worker is a JavaScript module.
import { countDownloads, publicStats, storedDownloads } from '../../telemetry/worker/src/public-stats.js';

afterEach(() => vi.unstubAllGlobals());
const release = (tag: string, assets: [string, number][]) => ({
  tag_name: tag, draft: false, assets: assets.map(([name, download_count]) => ({ name, download_count })),
});
const database = (count: number | null, fail = false) => ({ DB: { prepare: vi.fn(() => ({
  bind: vi.fn(() => ({ first: () => fail ? Promise.reject(new Error('DB down')) : Promise.resolve({ count }) })),
})) } });

describe('public usage statistics', () => {
  it('shares verified downloads across locations and preserves their timestamp during upstream outages', async () => {
    const saved = { count: 175, updatedAt: new Date().toISOString() };
    const env = { DB: { prepare: () => ({ first: async () => saved }) } };
    const fetcher = vi.fn().mockRejectedValue(new Error('rate limited'));
    vi.stubGlobal('fetch', fetcher);
    expect(await storedDownloads(env)).toEqual(saved);
    expect(fetcher).not.toHaveBeenCalled();
    expect(await storedDownloads(env, true)).toEqual(saved);
    expect(fetcher).toHaveBeenCalledOnce();
  });
  it('paginates app downloads and excludes plugins, signatures and checksums', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([
        release('v4', [['FluidBar.exe', 12], ['FluidBar-win-x64.zip', 7], ['publish.json', 99], ['FluidBar.exe.sig.json', 80]]),
        release('plugin-demo', [['FluidBar-plugin.zip', 50]]),
      ]), { headers: { link: '<https://api.github.com/repos/Doulor/FluidBar/releases?page=2>; rel="next"' } }))
      .mockResolvedValueOnce(Response.json([release('v1', [['FluidBar-v1.zip', 3]])]));
    expect((await countDownloads(fetcher)).count).toBe(22);
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it('does not present incomplete or invalid release data as a download total', async () => {
    await expect(countDownloads(vi.fn().mockResolvedValue(new Response('', { status: 403 })))).rejects.toThrow();
    await expect(countDownloads(vi.fn().mockResolvedValue(Response.json([release('v1', [['FluidBar.exe', -1]])])))).rejects.toThrow();
  });

  it('returns only aggregate counts, uses the last 24 hours, and reuses cached downloads', async () => {
    const env = database(2);
    const cache = { match: vi.fn().mockResolvedValue(Response.json({ count: 31, updatedAt: '2026-10-30T00:00:00Z' })) };
    const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
    const response = await publicStats(env, cache);
    const value = await response.json();
    expect(value.downloads.count).toBe(31);
    expect(value.activeInstalls.count).toBe(2);
    expect(value.activeInstalls.windowHours).toBe(24);
    expect(env.DB.prepare).toHaveBeenCalledWith(expect.stringContaining('COUNT(DISTINCT install_hash)'));
    const binds = env.DB.prepare.mock.results[0].value.bind.mock.calls[0] as unknown as string[];
    expect(Date.parse(binds[1]) - Date.parse(binds[0])).toBe(86_400_000);
    expect(Object.keys(value).sort()).toEqual(['activeInstalls', 'downloads', 'schema']);
    expect(fetcher).not.toHaveBeenCalled();
    expect(response.headers.get('cache-control')).toBe('public, max-age=60');
  });

  it('keeps true zero counts and isolates failures instead of inventing zero', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('GitHub down')));
    const value = await (await publicStats(database(0))).json();
    expect(value.downloads.count).toBeNull();
    expect(value.activeInstalls.count).toBe(0);
    const cache = { match: vi.fn().mockResolvedValue(Response.json({ count: 31, updatedAt: 'now' })) };
    const failed = await (await publicStats(database(null, true), cache)).json();
    expect(failed.downloads.count).toBe(31);
    expect(failed.activeInstalls.count).toBeNull();
  });
});
