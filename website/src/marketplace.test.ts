import { readFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const publicDir = resolve('public');
const signedBytes = readFileSync(resolve('../marketplace/index.json'));
const source = JSON.parse(signedBytes.toString('utf8'));

describe('marketplace static contract', () => {
  it('serves the signed source bytes without reserializing them', () => {
    expect(readFileSync(resolve(publicDir, 'market/index.json')).equals(signedBytes)).toBe(true);
    // MarketplaceCatalogClientOptions.MaxResponseBytes is 4 MiB.
    expect(signedBytes.byteLength).toBeLessThanOrEqual(4 * 1024 * 1024);
    expect(existsSync(resolve(publicDir, '404.html'))).toBe(true);
  });

  it('keeps version shards and listing download links aligned with the signed index', () => {
    for (const plugin of source.plugins) {
      const shard = JSON.parse(readFileSync(resolve(publicDir, `market/plugins/${plugin.id}.json`), 'utf8'));
      expect(shard.versions[0]).toEqual(plugin.latest);
      const listing = readFileSync(resolve(publicDir, `market/listings/${plugin.id}/index.html`), 'utf8');
      expect(listing).toContain(plugin.latest.artifact.url);
      expect(listing).toContain(plugin.latest.artifact.sha256);
      expect(existsSync(resolve(publicDir, `market/assets/${plugin.id}/icon.png`))).toBe(true);
    }
  });
});
