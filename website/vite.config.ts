import { defineConfig, type Plugin, type ViteDevServer } from 'vite';
import react from '@vitejs/plugin-react';
import { stat } from 'node:fs/promises';
import path from 'node:path';
import { RELEASE } from './src/data/release';

// Match Pages' real static directory routes during development and preview.
// Missing catalog/schema URLs must never become the marketing SPA.
function marketplaceRoutes(): Plugin {
  const configure = (server: Pick<ViteDevServer, 'middlewares' | 'config'>) => {
    server.middlewares.use(async (request, response, next) => {
      const url = new URL(request.url || '/', 'http://localhost');
      if (!/^\/(market|schemas)(\/|$)/.test(url.pathname)) return next();
      try {
        const publicRoot = path.resolve(server.config.publicDir);
        let pathname = decodeURIComponent(url.pathname);
        let target = path.resolve(publicRoot, `.${pathname}`);
        if (!target.startsWith(publicRoot + path.sep)) throw new Error('Invalid static path');
        if ((await stat(target)).isDirectory()) {
          if (!pathname.endsWith('/')) {
            response.writeHead(301, { Location: `${url.pathname}/${url.search}` });
            response.end();
            return;
          }
          pathname += 'index.html';
          target = path.join(target, 'index.html');
        }
        if (!(await stat(target)).isFile()) throw new Error('Missing static file');
        request.url = encodeURI(pathname) + url.search;
        next();
      } catch {
        response.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
        response.end('Not found');
      }
    });
  };
  return { name: 'marketplace-static-routes', configureServer: configure,
    configurePreviewServer(server) {
      // Preview serves the built public output, not the source public folder.
      const publicDir = path.resolve(server.config.root, server.config.build.outDir);
      configure({ ...server, config: { ...server.config, publicDir } });
    },
  };
}

function siteUrlPlugin(): Plugin {
  const siteUrl = (process.env.SITE_URL || 'https://fluidbar.doulor.cn').replace(/\/+$/, '');
  return {
    name: 'fluidbar-site-url',
    transformIndexHtml: {
      // Substitute before Vite resolves URLs, or it prefixes placeholders with "/".
      order: 'pre',
      handler(html) {
        return html.replaceAll('__SITE_URL__', siteUrl)
          .replaceAll('__RELEASE_VERSION__', RELEASE.version)
          .replaceAll('__RELEASE_DOWNLOAD__', RELEASE.download);
      },
    },
  };
}

export default defineConfig({
  plugins: [react(), siteUrlPlugin(), marketplaceRoutes()],
  base: '/',
  server: {
    port: 4173, strictPort: true,
    proxy: { '/t/': { target: 'https://fluidbar.doulor.cn', changeOrigin: true } },
  },
  preview: { port: 4173, strictPort: true },
  build: { target: 'es2022', chunkSizeWarningLimit: 3000 },
});
