import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

// The management API behind /v1: localhost on the host, the management service under compose.
const api = process.env.TABLETOP_API_PROXY_TARGET ?? 'http://localhost:5100';

export default defineConfig({
  plugins: [svelte()],
  define: { 'import.meta.env.VITE_GAME_SOCKET': JSON.stringify(process.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws') },
  // Development is same-origin through the proxy, so management needs no CORS entry for the viewer.
  server: { port: 5174, proxy: { '/v1': { target: api, changeOrigin: false } } },
  preview: { port: 5174, proxy: { '/v1': { target: api, changeOrigin: false } } },
  test: { include: ['tests/**/*.test.ts'], environment: 'node' },
});
