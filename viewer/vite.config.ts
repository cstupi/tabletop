import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [svelte()],
  // Development is same-origin through the proxy, so management needs no CORS entry for the viewer.
  server: { port: 5174, proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  preview: { port: 5174, proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  test: { include: ['tests/**/*.test.ts'], environment: 'node' },
});
