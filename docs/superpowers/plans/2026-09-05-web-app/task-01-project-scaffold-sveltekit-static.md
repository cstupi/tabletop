# Task 1: Project scaffold — SvelteKit as a static SPA, Vitest, the dev proxy

**Files:**
- Create: `web/package.json`, `web/svelte.config.js`, `web/vite.config.ts`, `web/tsconfig.json`, `web/.gitignore`, `web/.npmrc`, `web/src/app.html`, `web/src/app.d.ts`, `web/src/app.css`, `web/src/routes/+layout.ts`, `web/src/routes/+layout.svelte`, `web/src/routes/+page.ts`, `web/tests/smoke.test.ts`

**Interfaces:**
- Produces: `npm run dev` (Vite on `0.0.0.0:5173`, proxying `/v1` to `http://localhost:5100`), `npm test` (Vitest, Node environment, `tests/**/*.test.ts`), `npm run check` (svelte-check), `npm run build` (static output in `web/build/` with `index.html` fallback); `PUBLIC_API_BASE` (default `''` — same origin through the proxy).
- Decision — **SvelteKit + adapter-static, not plain Vite + Svelte**: the app needs real paths (`/reset-password?token=` is what Stytch redirects to, and a hash router would put the query before the `#`), per-page code splitting for the three.js and WASM bundles, and file-based routing; `ssr = false` everywhere keeps it a pure SPA — no server, no `load` on the server, static files only (web app → Deliberately not decided: "framework details beyond it being a Svelte SPA" — this settles them). The `/v1` proxy keeps development same-origin; management also answers CORS for `TABLETOP_WEB_URL` (management plan Task 3, this plan's request under *Interfaces needed*), so a built SPA served from another origin works with `PUBLIC_API_BASE` set to management's URL; presigned MinIO URLs are absolute and MinIO answers CORS itself.

Spec: `architecture.md` → Deliverables (`web/`, "Static (Svelte SPA)"), Repository layout ("The Svelte app shares no code with the .NET projects"); management plan Task 1 (`TABLETOP_WEB_URL=http://localhost:5173`).

- [ ] **Step 1: Write `package.json`, `.npmrc`, `.gitignore`**

```json
{
  "name": "tabletop-web",
  "private": true,
  "version": "0.0.1",
  "type": "module",
  "scripts": {
    "dev": "vite dev --host 0.0.0.0 --port 5173",
    "build": "vite build",
    "preview": "vite preview --host 0.0.0.0 --port 5173",
    "check": "svelte-kit sync && svelte-check --tsconfig ./tsconfig.json",
    "test": "vitest run",
    "test:watch": "vitest",
    "fixtures": "node tests/fixtures/make-fixtures.ts"
  },
  "devDependencies": {
    "@sveltejs/adapter-static": "3.0.10",
    "@sveltejs/kit": "2.70.3",
    "@sveltejs/vite-plugin-svelte": "7.3.0",
    "@types/three": "0.185.4",
    "svelte": "5.57.0",
    "svelte-check": "4.7.6",
    "typescript": "5.9.3",
    "vite": "8.2.2",
    "vitest": "5.0.0"
  },
  "dependencies": {
    "@gltf-transform/core": "4.5.0",
    "meshoptimizer": "1.2.0",
    "three": "0.185.1"
  }
}
```

```
# web/.npmrc
save-exact=true
engine-strict=true
```

```
# web/.gitignore
node_modules/
build/
.svelte-kit/
vite.config.ts.timestamp-*
```

- [ ] **Step 2: Write `svelte.config.js`, `vite.config.ts`, `tsconfig.json`**

```js
// web/svelte.config.js
import adapter from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
export default {
  preprocess: vitePreprocess(),
  kit: {
    adapter: adapter({ pages: 'build', assets: 'build', fallback: 'index.html', strict: false }),
    alias: { $lib: 'src/lib' },
  },
};
```

```ts
// web/vite.config.ts — defineConfig comes from vitest/config, because `test` is not part of Vite's own config type.
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
```

```json
{
  "extends": "./.svelte-kit/tsconfig.json",
  "compilerOptions": {
    "strict": true, "noUncheckedIndexedAccess": true, "moduleResolution": "bundler",
    "module": "esnext", "target": "es2022", "lib": ["es2022", "dom", "dom.iterable", "webworker"],
    "allowJs": true, "checkJs": true, "esModuleInterop": true, "skipLibCheck": true, "sourceMap": true,
    "types": ["vitest/globals"]
  }
}
```

- [ ] **Step 3: Write the app shell**

```html
<!-- web/src/app.html -->
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Tabletop</title>
    %sveltekit.head%
  </head>
  <body data-sveltekit-preload-data="hover">
    <div style="display: contents">%sveltekit.body%</div>
  </body>
</html>
```

```ts
// web/src/app.d.ts
declare global { namespace App { interface Error { message: string } } }
export {};
```

```ts
// web/src/routes/+layout.ts
export const ssr = false;
export const prerender = false;
```

```svelte
<!-- web/src/routes/+layout.svelte -->
<script lang="ts">
  import '../app.css';
  let { children } = $props();
</script>

<main>{@render children()}</main>
```

```ts
// web/src/routes/+page.ts
import { redirect } from '@sveltejs/kit';
export const load = () => { redirect(307, '/armies'); };
```

`app.css`: `:root { color-scheme: light dark; font: 14px/1.4 system-ui, sans-serif; } main { max-width: 72rem; margin: 0 auto; padding: 1rem; } button, input, select { font: inherit; }`.

- [ ] **Step 4: Write the failing smoke test**

```ts
// web/tests/smoke.test.ts
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

describe('project', () => {
  it('is a static SPA with ssr off', () => {
    expect(readFileSync('src/routes/+layout.ts', 'utf8')).toContain('export const ssr = false');
    expect(readFileSync('svelte.config.js', 'utf8')).toContain("fallback: 'index.html'");
  });
});
```

- [ ] **Step 5: Install, run, check**

```bash
cd /home/cstupi/src/tabletop/web && npm install && npm test && npm run check && npm run build
ls build/index.html
```

Expected: 1 test passed; svelte-check reports 0 errors; `build/index.html` exists. `npm run dev` then `curl -s localhost:5173/armies | head -c 200` returns the shell HTML (the redirect happens client-side).

- [ ] **Step 6: Commit**

```bash
git add web && git commit -m "Scaffold the web app as a SvelteKit static SPA with Vitest"
```
