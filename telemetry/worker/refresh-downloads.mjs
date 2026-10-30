// Bootstrap/recover the shared cache using actual GitHub release asset counts.
// Run: node telemetry/worker/refresh-downloads.mjs
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { writeFileSync, unlinkSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { countDownloads } from './src/public-stats.js';

const result = await countDownloads();
if (!Number.isSafeInteger(result.count) || result.count < 0) throw new Error('Invalid total');
const timestamp = new Date(result.updatedAt).toISOString();
const sql = `INSERT INTO public_download_stats (id, count, updated_at) VALUES (1, ${result.count}, '${timestamp}') ON CONFLICT(id) DO UPDATE SET count = excluded.count, updated_at = excluded.updated_at`;
const filename = `.stats-seed-${randomUUID()}.sql`;
const directory = fileURLToPath(new URL('.', import.meta.url));
const file = new URL(filename, import.meta.url);
writeFileSync(file, sql, { flag: 'wx' });
let child;
try {
  child = spawnSync('wrangler', ['d1', 'execute', 'fluidbar_telemetry', '--remote', '--config', 'wrangler.toml', '--file', filename], {
    cwd: directory, stdio: 'inherit', shell: process.platform === 'win32',
  });
} finally { unlinkSync(file); }
if (child.status !== 0) process.exit(child.status ?? 1);
console.log(`Verified GitHub downloads: ${result.count}, as of ${timestamp}`);
