import { svelte } from '@sveltejs/vite-plugin-svelte';
import { fileURLToPath } from 'node:url';
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
  // The packed built-in catalog (what Godot packs): the setup page's fallback when GET /v1/builtin answers 404.
  resolve: { alias: { '@builtin-catalog': fileURLToPath(new URL('../client/content/builtin/catalog.json', import.meta.url)) } },
  // Always under /viewer/: production serves it there, and development through the website's dev server (web/vite.config.ts),
  // so the viewer shares the website's origin and its session.
  base: '/viewer/',
  define: mode === 'hosted'
    ? {}
    : { 'import.meta.env.VITE_GAME_SOCKET': JSON.stringify(process.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws') },
  // Development is same-origin through the proxy, so management needs no CORS entry for the viewer.
  server: { port: 5174, proxy, fs: { allow: ['..'] } },
  preview: { port: 5174, proxy },
  // The WebAssembly runtime's dotnet.js is loaded from disk, not bundled: external, or Vitest warns about its source maps.
  test: { include: ['tests/**/*.test.ts'], environment: 'node', server: { deps: { external: [/_framework\//] } } },
}));
