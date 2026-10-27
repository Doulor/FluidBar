import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  base: process.env.BASE_PATH || '/',
  server: { port: 4173, strictPort: true },
  preview: { port: 4173, strictPort: true },
  build: { target: 'es2022', chunkSizeWarningLimit: 3000 },
});
