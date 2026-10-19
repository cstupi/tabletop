import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

// The management API behind /v1: localhost on the host, the management service under compose.
const api = process.env.TABLETOP_API_PROXY_TARGET ?? 'http://localhost:5100';
// The runner service behind /suite and /runs (sim serve; it sends no CORS headers).
const suite = process.env.TABLETOP_SUITE_PROXY_TARGET ?? 'http://localhost:5180';
const proxy = {
  '/v1': { target: api, changeOrigin: false },
  '/suite': { target: suite, changeOrigin: false },
  '/runs': { target: suite, changeOrigin: false },
};

// Mode `hosted` (npm run build:hosted) is the production build served under /viewer/ on the website's origin; the socket
// URL then comes from the page's host (src/lib/hosted/hosted.ts), so VITE_GAME_SOCKET is not baked in.
export default defineConfig(({ mode }) => ({
  plugins: [svelte()],
  base: mode === 'hosted' ? '/viewer/' : '/',
  define: mode === 'hosted'
    ? {}
    : { 'import.meta.env.VITE_GAME_SOCKET': JSON.stringify(process.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws') },
  // Development is same-origin through the proxy, so management needs no CORS entry for the viewer.
  server: { port: 5174, proxy },
  preview: { port: 5174, proxy },
  test: { include: ['tests/**/*.test.ts'], environment: 'node' },
}));
