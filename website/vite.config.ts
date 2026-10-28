import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

function siteUrlPlugin(): Plugin {
  const siteUrl = (process.env.SITE_URL || 'https://fluidbar.doulor.cn').replace(/\/+$/, '');
  return {
    name: 'fluidbar-site-url',
    transformIndexHtml(html) {
      return html.replaceAll('__SITE_URL__', siteUrl);
    },
  };
}

export default defineConfig({
  plugins: [react(), siteUrlPlugin()],
  base: '/',
  server: { port: 4173, strictPort: true },
  preview: { port: 4173, strictPort: true },
  build: { target: 'es2022', chunkSizeWarningLimit: 3000 },
});
