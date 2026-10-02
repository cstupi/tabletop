# Task 1: The project — Vite, Svelte 5, Vitest, and the dev proxy

**Files:**
- Create: `viewer/package.json`, `viewer/.npmrc`, `viewer/.gitignore`, `viewer/svelte.config.js`, `viewer/vite.config.ts`, `viewer/tsconfig.json`, `viewer/index.html`, `viewer/src/main.ts`, `viewer/src/app.css`, `viewer/src/App.svelte`
- Test: `viewer/tests/smoke.test.ts`

**Interfaces:**
- Produces: `npm run dev` (Vite on `0.0.0.0:5174`, proxying `/v1` to `http://localhost:5100`), `npm test` (Vitest, `tests/**/*.test.ts`, Node environment), `npm run check` (`svelte-check`), `npm run build` (static output in `viewer/dist/`). Task 11 defines and reads `VITE_API_BASE` (default `''`, meaning same origin through the proxy) and `VITE_GAME_SOCKET` (default `ws://localhost:5200/ws`).
- Decision — **plain Vite + Svelte 5, not SvelteKit.** `web/` chose SvelteKit because Stytch redirects to `/reset-password?token=…` and it needs real paths and per-page code splitting for the three.js and WASM bundles (web-app plan Task 1). The viewer has one screen, no redirect target, no route to split, and no server-side anything; a single `index.html` is the whole app. `?recording=<url>` is read from `location.search`, which needs no router. Versions are `web/`'s exactly, so one Node install serves both.

Spec: spec → *`viewer/` is a developer web app, separate from `web/`* — "A static page, its own top-level directory, sharing no code and no build with `web/`."

- [ ] **Step 1: Write `package.json`, `.npmrc`, `.gitignore`**

```json
{
  "name": "tabletop-viewer",
  "private": true,
  "version": "0.0.1",
  "type": "module",
  "scripts": {
    "dev": "vite --host 0.0.0.0 --port 5174",
    "build": "vite build",
    "preview": "vite preview --host 0.0.0.0 --port 5174",
    "check": "svelte-check --tsconfig ./tsconfig.json",
    "test": "vitest run",
    "test:watch": "vitest"
  },
  "devDependencies": {
    "@sveltejs/vite-plugin-svelte": "7.3.0",
    "@types/node": "24.19.0",
    "svelte": "5.57.0",
    "svelte-check": "4.7.6",
    "typescript": "5.9.3",
    "vite": "8.2.2",
    "vitest": "5.0.0"
  }
}
```

```
# viewer/.npmrc
save-exact=true
engine-strict=true
```

```
# viewer/.gitignore
node_modules/
dist/
vite.config.ts.timestamp-*
```

- [ ] **Step 2: Write `svelte.config.js`, `vite.config.ts`, `tsconfig.json`**

```js
// viewer/svelte.config.js
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

export default { preprocess: vitePreprocess() };
```

```ts
// viewer/vite.config.ts — defineConfig from vitest/config, because `test` is not part of Vite's own config type.
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [svelte()],
  // Development is same-origin through the proxy, so management needs no CORS entry for the viewer.
  server: { port: 5174, proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  preview: { port: 5174, proxy: { '/v1': { target: 'http://localhost:5100', changeOrigin: false } } },
  test: { include: ['tests/**/*.test.ts'], environment: 'node' },
});
```

```json
{
  "compilerOptions": {
    "strict": true, "noUncheckedIndexedAccess": true, "moduleResolution": "bundler",
    "module": "esnext", "target": "es2022", "lib": ["es2022", "dom", "dom.iterable"],
    "verbatimModuleSyntax": true, "isolatedModules": true, "esModuleInterop": true,
    "skipLibCheck": true, "sourceMap": true, "noEmit": true,
    "types": ["vite/client", "vitest/globals", "node"]
  },
  "include": ["src/**/*.ts", "src/**/*.svelte", "tests/**/*.ts", "vite.config.ts"]
}
```

- [ ] **Step 3: Write the page**

```html
<!-- viewer/index.html -->
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Tabletop viewer</title>
  </head>
  <body>
    <div id="app"></div>
    <script type="module" src="/src/main.ts"></script>
  </body>
</html>
```

```ts
// viewer/src/main.ts
import { mount } from 'svelte';
import App from './App.svelte';
import './app.css';

export default mount(App, { target: document.getElementById('app')! });
```

```svelte
<!-- viewer/src/App.svelte -->
<script lang="ts">
  // Filled in by Task 8 (the recording feed) and Task 11 (the live feed).
</script>

<header><strong>Tabletop viewer</strong> <span class="muted">no recording loaded</span></header>
<main><p class="muted">Load a recording to draw it.</p></main>

<style>
  header { padding: 0.5rem 0.75rem; border-bottom: 1px solid var(--line); }
  main { padding: 0.75rem; }
</style>
```

```css
/* viewer/src/app.css */
:root {
  color-scheme: dark;
  --bg: #14161a; --panel: #1b1e24; --line: #2c313a; --ink: #e6e8ec; --muted: #8b93a1;
  font: 13px/1.45 ui-monospace, SFMono-Regular, Menlo, monospace;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--ink); }
button, input, select { font: inherit; color: inherit; background: var(--panel);
  border: 1px solid var(--line); border-radius: 3px; padding: 0.2rem 0.45rem; }
button:hover:enabled { border-color: var(--muted); cursor: pointer; }
.muted { color: var(--muted); }
```

- [ ] **Step 4: Write the failing smoke test**

```ts
// viewer/tests/smoke.test.ts
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

describe('the viewer project', () => {
  it('is a plain Vite app with no SvelteKit and no dependency on web/', () => {
    const pkg = JSON.parse(readFileSync('package.json', 'utf8'));
    expect(pkg.name).toBe('tabletop-viewer');
    expect(Object.keys(pkg.devDependencies)).not.toContain('@sveltejs/kit');
    expect(pkg.dependencies).toBeUndefined();
  });

  it('proxies /v1 to management and names the game socket nowhere but the config', () => {
    const config = readFileSync('vite.config.ts', 'utf8');
    expect(config).toContain("target: 'http://localhost:5100'");
    expect(config).toContain("port: 5174");
  });
});
```

Both assertions fail until Step 2's `vite.config.ts` is in place. The port is in that file — `server: { port: 5174, … }` and `preview: { port: 5174, … }` — so the config alone answers the second assertion, and the `--port 5174` flags in `package.json`'s scripts are redundant but harmless.

- [ ] **Step 5: Install and run everything**

```bash
cd "$(git rev-parse --show-toplevel)/viewer" && npm install && npm test && npm run check && npm run build
ls dist/index.html
```

Expected: 2 tests passed, `svelte-check` reports 0 errors and 0 warnings, `dist/index.html` exists.

- [ ] **Step 6: Commit**

```bash
cd "$(git rev-parse --show-toplevel)"
git add viewer && git commit -m "Scaffold the developer viewer as a plain Vite and Svelte 5 page"
```
