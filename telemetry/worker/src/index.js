// FluidBar anonymous usage data collector.
//
// Contract: docs/privacy/telemetry.md (client) and this file must agree. The client sends a closed whitelist of
// fields; anything else is rejected, so a future client bug cannot start shipping user content into the database.

const MAX_BODY_BYTES = 32 * 1024;
const MAX_RUNS = 64;
const SCHEMA = 1;

const STOP_REASONS = new Set(['user', 'system', 'crash']);

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === 'OPTIONS') {
      return new Response(null, { status: 204 });
    }

    if (request.method === 'GET' && url.pathname === '/v1/health') {
      return json({ ok: true, schema: SCHEMA });
    }

    if (request.method !== 'POST' || url.pathname !== '/v1/batch') {
      return json({ error: 'not-found' }, 404);
    }

    const declared = Number(request.headers.get('content-length') ?? '0');
    if (declared > MAX_BODY_BYTES) {
      return json({ error: 'too-large' }, 413);
    }

    const body = await request.text();
    if (body.length > MAX_BODY_BYTES) {
      return json({ error: 'too-large' }, 413);
    }

    let payload;
    try {
      payload = JSON.parse(body);
    } catch {
      return json({ error: 'invalid-json' }, 400);
    }

    const problem = validate(payload);
    if (problem) {
      return json({ error: problem }, 400);
    }

    // The install id never lands in the database in the clear: it is salted-hashed, which still allows
    // "same install, different days" counting without storing a stable identifier.
    const installHash = await hash(env.TELEMETRY_SALT ?? 'fluidbar', payload.installId);
    const receivedAt = new Date().toISOString();

    await env.DB.prepare(
      'INSERT INTO batches ' +
        '(received_at, install_hash, version, channel, self_contained, os, arch, locale, run_count, dropped, payload) ' +
        'VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
    )
      .bind(
        receivedAt,
        installHash,
        payload.environment.version,
        payload.environment.channel,
        payload.environment.selfContained ? 1 : 0,
        payload.environment.os,
        payload.environment.arch,
        payload.environment.locale,
        payload.runs.length,
        payload.dropped ?? 0,
        body,
      )
      .run();

    return json({ ok: true }, 202);
  },

  // Daily: raw batches are deleted after the published retention window (30 days).
  async scheduled(_event, env) {
    await env.DB.prepare("DELETE FROM batches WHERE received_at < datetime('now', '-30 days')").run();
  },
};

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' },
  });
}

function text(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= 64;
}

function validate(payload) {
  if (!payload || typeof payload !== 'object') return 'not-an-object';
  if (payload.schema !== SCHEMA) return 'schema';
  if (!text(payload.installId) || !/^[a-f0-9]{32}$/i.test(payload.installId)) return 'install-id';

  const env = payload.environment;
  if (!env || typeof env !== 'object') return 'environment';
  if (!text(env.version) || !text(env.channel) || !text(env.os) || !text(env.arch) || !text(env.locale)) {
    return 'environment';
  }

  if (!Array.isArray(payload.runs) || payload.runs.length > MAX_RUNS) return 'runs';
  for (const run of payload.runs) {
    if (!run || typeof run !== 'object') return 'run';
    if (!text(run.startedAt) || !text(run.reason)) return 'run';
    if (!Number.isInteger(run.uptimeSeconds) || run.uptimeSeconds < 0) return 'run';
    if (!Number.isInteger(run.startupMs) || run.startupMs < 0 || run.startupMs > 120000) return 'run';
    if (typeof run.firstRun !== 'boolean') return 'run';
    if (!STOP_REASONS.has(run.reason)) return 'run';
  }

  if (payload.dropped !== undefined && (!Number.isInteger(payload.dropped) || payload.dropped < 0)) {
    return 'dropped';
  }

  // Reject unknown top-level keys outright: silence here would let a client change ship new fields unnoticed.
  const allowed = new Set(['schema', 'installId', 'sentAt', 'environment', 'runs', 'dropped']);
  for (const key of Object.keys(payload)) {
    if (!allowed.has(key)) return 'unexpected-field';
  }

  return null;
}

async function hash(salt, value) {
  const data = new TextEncoder().encode(salt + ':' + value);
  const digest = await crypto.subtle.digest('SHA-256', data);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
}
