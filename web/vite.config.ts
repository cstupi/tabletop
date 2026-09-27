import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [sveltekit()],
  envPrefix: ['VITE_', 'PUBLIC_'],   // Vite only exposes VITE_* on import.meta.env; PUBLIC_API_BASE is read there, not through $env
  server: { proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  preview: { proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  worker: { format: 'es' },
  optimizeDeps: { exclude: ['meshoptimizer'] },
  test: { include: ['tests/**/*.test.ts'], environment: 'node', testTimeout: 60_000 },
});
