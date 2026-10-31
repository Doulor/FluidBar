import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createHash, generateKeyPairSync, sign } from 'node:crypto';
import { verifyPluginPackage } from '../verify-plugin-package.mjs';

function fixture() {
  const { publicKey, privateKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const bytes = Buffer.from('immutable plugin bytes');
  const artifact = { sha256: createHash('sha256').update(bytes).digest('hex'), size: bytes.length, keyId: 'market-key' };
  const trusted = { keyId: 'market-key', role: 'marketplace', publicKey: publicKey.export({ format: 'der', type: 'spki' }).toString('base64') };
  const signature = { package: { sha256: artifact.sha256, size: bytes.length }, signatures: [{ ...trusted, algorithm: 'ECDSA-P256-SHA256', value: sign('sha256', bytes, { key: privateKey, dsaEncoding: 'ieee-p1363' }).toString('base64') }] };
  return { bytes, artifact, trusted, signature };
}
test('Checks the same SHA-256 and IEEE P1363 marketplace signature as the C# installer', () => {
  const f = fixture();
  assert.equal(verifyPluginPackage(f.bytes, f.signature, f.artifact, [f.trusted]), f.artifact.sha256);
});
test('Refuses the observed stale-index/new-release mismatch', () => {
  const f = fixture();
  assert.throws(() => verifyPluginPackage(Buffer.from('replacement package'), f.signature, f.artifact, [f.trusted]), /signed index hash/);
});
test('Refuses a signature from a different key and a revoked trusted key', () => {
  const f = fixture();
  const other = fixture();
  assert.throws(() => verifyPluginPackage(f.bytes, f.signature, f.artifact, [other.trusted]), /signature/);
  assert.throws(() => verifyPluginPackage(f.bytes, f.signature, f.artifact, [{ ...f.trusted, revokedAt: '2026-01-01T00:00:00Z' }]), /active/);
});
