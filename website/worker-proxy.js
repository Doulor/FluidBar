export default {
  async fetch(request) {
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
