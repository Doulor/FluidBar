import assert from 'node:assert/strict';
import { createHash, createPublicKey, verify } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** Package verification uses trusted keys supplied by the locally verified publishing snapshot. */
export function verifyPluginPackage(bytes, signature, artifact, trustedKeys) {
  const hash = createHash('sha256').update(bytes).digest('hex');
  assert.equal(hash, artifact.sha256, 'downloaded package must match the signed index hash');
  assert.equal(bytes.length, artifact.size, 'downloaded package must match the signed index size');
  assert.equal(signature.package.sha256, hash, 'signature covers the same package hash');
  assert.equal(signature.package.size, bytes.length, 'signature covers the same package size');
  const trusted = trustedKeys.find(key => key.keyId === artifact.keyId && key.role === 'marketplace');
  assert.ok(trusted && !trusted.revokedAt, 'marketplace signing key must be trusted and active');
  assert.ok(!trusted.notAfter || new Date(trusted.notAfter) > new Date(), 'marketplace key has not expired');
  const key = createPublicKey({ key: Buffer.from(trusted.publicKey, 'base64'), format: 'der', type: 'spki' });
  assert.ok(signature.signatures.some(entry => entry.role === 'marketplace'
    && entry.keyId === trusted.keyId && entry.publicKey === trusted.publicKey
    && entry.algorithm === 'ECDSA-P256-SHA256'
    && verify('sha256', bytes, { key, dsaEncoding: 'ieee-p1363' }, Buffer.from(entry.value, 'base64'))), 'valid marketplace package signature is required');
  return hash;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [indexPath, id, packagePath, signaturePath] = process.argv.slice(2);
  const index = JSON.parse(await readFile(indexPath));
  const entry = index.plugins.find(plugin => plugin.id === id);
  assert.ok(entry, `plugin ${id} is registered in the signed index`);
  const keys = JSON.parse(await readFile(path.join(path.dirname(indexPath), 'keys.json')));
  const hash = verifyPluginPackage(await readFile(packagePath), JSON.parse(await readFile(signaturePath)), entry.latest.artifact, keys);
  console.log(`verified ${id}: ${hash}`);
}
