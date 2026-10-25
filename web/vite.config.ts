import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

// The management API behind /v1: localhost on the host, the management service under compose.
const api = process.env.TABLETOP_API_PROXY_TARGET ?? 'http://localhost:5100';
// The viewer's dev server behind /viewer/, so it runs on this origin and reads the website's session, as production serves it.
const viewer = process.env.TABLETOP_VIEWER_PROXY_TARGET ?? 'http://localhost:5174';

export default defineConfig({
  plugins: [sveltekit()],
  envPrefix: ['VITE_', 'PUBLIC_'],   // Vite only exposes VITE_* on import.meta.env; PUBLIC_API_BASE is read there, not through $env
  server: { proxy: { '/v1': { target: api, changeOrigin: false }, '/viewer': { target: viewer, changeOrigin: false, ws: true } } },
  preview: { proxy: { '/v1': { target: api, changeOrigin: false } } },
  worker: { format: 'es' },
  optimizeDeps: { exclude: ['meshoptimizer'] },
  test: { include: ['tests/**/*.test.ts'], environment: 'node', testTimeout: 60_000 },
});
