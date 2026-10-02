import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [svelte()],
  define: { 'import.meta.env.VITE_GAME_SOCKET': JSON.stringify(process.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws') },
  // Development is same-origin through the proxy, so management needs no CORS entry for the viewer.
  server: { port: 5174, proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  preview: { port: 5174, proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  test: { include: ['tests/**/*.test.ts'], environment: 'node' },
});
