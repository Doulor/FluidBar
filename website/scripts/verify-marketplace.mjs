import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyPluginPackage } from './verify-plugin-package.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const origin = process.argv[2] || 'https://fluidbar.doulor.cn';
const marketDirectory = path.resolve(process.argv[3] || path.join(root, 'public/market'));
const indexBytes = await readFile(path.join(marketDirectory, 'index.json'));
const index = JSON.parse(indexBytes);
const trustedKeys = JSON.parse(await readFile(path.join(marketDirectory, 'keys.json')));
const results = [];

async function verify(relative, json = false) {
  const response = await fetch(new URL(relative, origin), { signal: AbortSignal.timeout(30_000), cache: 'no-store' });
  assert.equal(response.status, 200, relative);
  const bytes = Buffer.from(await response.arrayBuffer());
  const localBytes = await readFile(path.join(path.dirname(marketDirectory), relative.slice(1)));
  assert.ok(bytes.equals(localBytes), `${relative}: deployed static bytes`);
  if (json) {
    assert.match(response.headers.get('content-type'), /^application\/json;\s*charset=utf-8$/i, relative);
    assert.equal(response.headers.get('access-control-allow-origin'), '*', relative);
    assert.ok(bytes.length <= 4 * 1024 * 1024, relative);
    JSON.parse(bytes.toString('utf8'));
  }
  if (relative.endsWith('.png')) assert.match(response.headers.get('content-type'), /^image\/png/i);
  if (relative.endsWith('.html')) assert.match(response.headers.get('content-type'), /^text\/html/i);
  const etag = response.headers.get('etag');
  // Pages canonicalizes listing HTML to directory URLs, which may omit ETag.
  // JSON catalog endpoints and image assets must retain conditional caching.
  if (!relative.endsWith('.html')) assert.ok(etag, `${relative}: ETag`);
  let conditionalStatus = null;
  if (etag) {
    const conditional = await fetch(new URL(relative, origin), { headers: { 'If-None-Match': etag } });
    assert.equal(conditional.status, 304, `${relative}: conditional response`);
    conditionalStatus = conditional.status;
  }
  if (relative === '/market/index.json') assert.ok(bytes.equals(indexBytes), 'signed source bytes');
  results.push({ path: relative, status: 200, bytes: bytes.length, conditional: conditionalStatus });
}

await verify('/market/index.json', true);
await verify('/market/keys.json', true);
for (const folder of ['schemas']) {
  for (const file of await readdir(path.join(path.dirname(marketDirectory), folder))) {
    if (file.endsWith('.json')) await verify(`/${folder}/${file}`, true);
  }
}
for (const plugin of index.plugins) {
  await verify(`/market/plugins/${plugin.id}.json`, true);
  await verify(`/market/listings/${plugin.id}/index.html`);
  await verify(`/market/assets/${plugin.id}/icon.png`);
  const artifact = plugin.latest.artifact;
  const packageResponse = await fetch(artifact.url, { signal: AbortSignal.timeout(60_000) });
  assert.equal(packageResponse.status, 200, `${plugin.id}: package download`);
  assert.ok(artifact.size > 0 && artifact.size <= 128 * 1024 * 1024, 'bounded package size');
  const chunks = [];
  let size = 0;
  for await (const chunk of packageResponse.body) {
    size += chunk.length;
    assert.ok(size <= artifact.size, `${plugin.id}: package size budget`);
    chunks.push(chunk);
  }
  const signatureResponse = await fetch(artifact.signatureUrl, { signal: AbortSignal.timeout(30_000) });
  assert.equal(signatureResponse.status, 200, `${plugin.id}: signature download`);
  verifyPluginPackage(Buffer.concat(chunks), await signatureResponse.json(), artifact, trustedKeys);
  results.push({ plugin: plugin.id, version: plugin.latest.version, packageBytes: size, signature: 'verified' });
}
for (const missing of ['/market/missing-fluidbar.json', '/schemas/missing-fluidbar.json']) {
  assert.equal((await fetch(new URL(missing, origin))).status, 404, missing);
}
console.log(JSON.stringify({ origin, passed: results.length, missingRoutes: 404, results }, null, 2));
