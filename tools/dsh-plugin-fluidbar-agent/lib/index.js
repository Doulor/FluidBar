// dsh-plugin-fluidbar-agent — host half.
//
// DeepSeek Harness is a Cordis plugin host. The officially supported extension surface is the agent
// event bus (see dsh-tool-cordis' Plugin API catalog): agent/created, agent/status, agent/error,
// tools/result, user-questions/request, agent/disposed. This plugin subscribes to that bus and writes
// FluidBar schema-v2 agent events into %APPDATA%\\FluidBar\\agent-events; the island's
// AgentInboxWatcher merges them by sessionId (one running -> completed row per DSH session).
//
// The plugin is deliberately best-effort: any I/O failure is swallowed because agent status must never
// break a harness conversation.

import { promises as fsp } from 'node:fs';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import os from 'node:os';

/** Cordis plugin name (also used by the loader entry id in cordis.patch.yml). */
export const name = 'fluidbar-agent-hook';

const DEFAULT_TITLE = 'DeepSeek Harness';

function defaultInboxDir() {
  const appData = process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming');
  return path.join(appData, 'FluidBar', 'agent-events');
}

/**
 * @param {import('@deepseek-ai/cordis').Context} ctx
 * @param {{ inboxDir?: string, title?: string }} [config]
 */
export function apply(ctx, config = {}) {
  const inboxDir = config.inboxDir || process.env.FLUIDBAR_AGENT_INBOX || defaultInboxDir();
  const title = config.title || DEFAULT_TITLE;
  const sessions = new Map();

  async function write(event, sessionId, message, extra = {}) {
    try {
      await fsp.mkdir(inboxDir, { recursive: true });
      const payload = {
        schemaVersion: 2,
        agent: 'dsh',
        event,
        title: extra.title || title,
        message,
        timestamp: new Date().toISOString(),
      };
      if (sessionId) payload.sessionId = sessionId;
      if (extra.startedAt) payload.startedAt = extra.startedAt;
      if (extra.error) payload.error = extra.error;

      const file = path.join(inboxDir, 'agent-' + randomUUID() + '.json');
      await fsp.writeFile(file + '.tmp', JSON.stringify(payload), 'utf8');
      await fsp.rename(file + '.tmp', file);
    } catch {
      // Best-effort only: an unavailable inbox must not break the harness.
    }
  }

  const stateOf = (sessionId) => {
    if (!sessionId) return undefined;
    let state = sessions.get(sessionId);
    if (!state) {
      state = { startedAt: new Date().toISOString() };
      sessions.set(sessionId, state);
    }
    return state;
  };

  ctx.on('agent/created', ({ agent }) => {
    const state = stateOf(agent && agent.id);
    void write('running', agent && agent.id, '会话已启动', { startedAt: state && state.startedAt });
  });

  ctx.on('agent/status', ({ agent, status }) => {
    const sessionId = agent && agent.id;
    const state = stateOf(sessionId);
    if (status === 'running') {
      void write('running', sessionId, '运行中', { startedAt: state && state.startedAt });
    } else {
      void write('completed', sessionId, '已完成', { startedAt: state && state.startedAt });
    }
  });

  ctx.on('agent/error', ({ agent, turn, step, error }) => {
    const sessionId = agent && agent.id;
    const state = sessions.get(sessionId);
    const message = error && error.message ? String(error.message) : String(error || 'agent error');
    void write('failed', sessionId, '第 ' + turn + ' 轮第 ' + step + ' 步失败', {
      startedAt: state && state.startedAt,
      error: message,
    });
  });

  ctx.on('tools/result', (exec) => {
    const tool = exec && (exec.tool || exec.name || (exec.call && exec.call.tool));
    const sessionId = exec && (exec.agent && exec.agent.id ? exec.agent.id : exec.agentId);
    if (!tool) return;
    const state = sessions.get(sessionId);
    void write('running', sessionId, '正在使用 ' + tool, { startedAt: state && state.startedAt });
  });

  // Waterfall observer: announce "waiting for input", then pass the question through untouched.
  ctx.on('user-questions/request', async (request, next) => {
    const sessionId = request && request.agent && request.agent.id;
    const state = sessions.get(sessionId);
    void write('waiting', sessionId, '等待输入', { startedAt: state && state.startedAt });
    try {
      return await next();
    } finally {
      void write('running', sessionId, '已收到输入', { startedAt: state && state.startedAt });
    }
  });

  ctx.on('agent/disposed', ({ agent }) => {
    if (agent && agent.id) sessions.delete(agent.id);
  });
}
