export default {
  async fetch(request, env) {
    const incoming = new URL(request.url);
    if (incoming.pathname.startsWith('/t/')) {
      // The dedicated telemetry Worker owns validation, salted install ids, D1 writes and retention. Keeping it
      // behind the already-working site domain also works in networks where workers.dev and new subdomains are
      // unavailable. Everything outside /t/ remains the unchanged website proxy below.
      const telemetry = new URL(request.url);
      telemetry.pathname = incoming.pathname.slice('/t'.length);
      return env.TELEMETRY.fetch(new Request(telemetry.toString(), request));
    }

    const target = new URL(request.url);
    target.protocol = 'https:';
    target.hostname = 'fluidbar.pages.dev';

    const headers = new Headers(request.headers);
    headers.delete('host');

    const init = {
      method: request.method,
      headers,
      redirect: 'manual',
    };
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      init.body = request.body;
    }

    return fetch(target.toString(), init);
  },
};
