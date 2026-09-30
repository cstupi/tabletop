# Task 15: Armies — the list, import from Army Forge, re-import, delete

**Files:**
- Create: `web/src/lib/armies/import.ts`, `web/src/routes/armies/+page.svelte`
- Test: `web/tests/armies.test.ts`

**Interfaces:**
- Consumes: `api.armies.import/list/delete`, `ArmySummary`, `Army`, `ApiError`.
- Produces: `normaliseSource(text: string): string | null` (a share link `https://army-forge.onepagerules.com/share?id=…`, an Army Forge list URL carrying `?id=`, or a bare id → the text management accepts; anything else → `null`); `importErrorMessage(e: unknown): string` (`422` → management's detail, e.g. a skirmish or regiment list refused; `502` → "Army Forge did not answer"); `reimport(army: { id; source }): Promise<Army>` — imports the same source as a new army, then deletes the old one; `/armies`.
- Import is `POST /v1/armies/import { source }` and management fetches the list — the browser never calls Army Forge (management API → Army import: "the parser lives in one place, there is no CORS problem"). This plan therefore consumes the export's shape only through the `Army` the API returns; the field mapping is the management plan's Task 9.
- Re-import replaces: the button on a list row says what is lost — every per-slot mesh, base and height override — and asks before it runs; the new army keeps the source and gets the imported name.
- Load more and Delete share one guard: Load more does nothing while a delete or another page load is running, and Delete does nothing while an import, a re-import or a page load is running. Both buttons are disabled for the same states. A failure in either is reported through `importErrorMessage`.

Spec: the army → Where the values come from ("The list determines composition and loadout … imported, and needing no input from the user"), A changed list means a re-import ("import it again. There is no copy operation, no reconciliation … a re-import loses per-slot bindings and base overrides"); management plan Task 10 (`ImportRequest(Source)` → `201 Army`; `ListId` accepts a share link's `id=` or a bare id; `422` for no id or another game system; `502` when Army Forge fails), keyset `Page<ArmySummary>`.

- [ ] **Step 1: Write the failing tests**

```ts
// web/tests/armies.test.ts
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../src/lib/api/client';
import { ApiError } from '../src/lib/api/http';
import { importErrorMessage, normaliseSource, reimport } from '../src/lib/armies/import';
afterEach(() => vi.restoreAllMocks());

describe('normaliseSource', () => {
  it('accepts share links, list URLs with an id, and bare ids', () => {
    expect(normaliseSource(' https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py&name=Wormhole ')).toBe('https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py&name=Wormhole');
    expect(normaliseSource('https://army-forge.onepagerules.com/list?listId=x&id=c2-6YsWRH3py')).toBe('c2-6YsWRH3py');
    expect(normaliseSource('c2-6YsWRH3py')).toBe('c2-6YsWRH3py');
    expect(normaliseSource('https://example.com/nothing')).toBeNull();
    expect(normaliseSource('')).toBeNull();
  });
});

describe('reimport', () => {
  it('imports the same source as a new army and then deletes the old one', async () => {
    const calls: string[] = [];
    vi.spyOn(api.armies, 'import').mockImplementation(async (s) => { calls.push(`import ${s}`); return { id: 'new', name: 'N', gameSystem: 'grimdark-future', source: s, units: [] }; });
    vi.spyOn(api.armies, 'delete').mockImplementation(async (id) => { calls.push(`delete ${id}`); });
    const army = await reimport({ id: 'old', source: 'c2-6YsWRH3py' });
    expect(army.id).toBe('new');
    expect(calls).toEqual(['import c2-6YsWRH3py', 'delete old']);
  });
  it('leaves the old army alone when the import fails', async () => {
    vi.spyOn(api.armies, 'import').mockRejectedValue(new ApiError(502, 'Upstream failure', 'Army Forge answered 404'));
    const del = vi.spyOn(api.armies, 'delete');
    await expect(reimport({ id: 'old', source: 'x' })).rejects.toBeInstanceOf(ApiError);
    expect(del).not.toHaveBeenCalled();
    expect(importErrorMessage(new ApiError(502, 'Upstream failure', 'Army Forge answered 404'))).toMatch(/Army Forge did not answer/);
    expect(importErrorMessage(new ApiError(422, 'Unprocessable', "Only Grimdark Future and Age of Fantasy lists import; this list is 'aofs'."))).toMatch(/Age of Fantasy/);
  });
});
```

- [ ] **Step 2: Run; expect failures. Write `import.ts`:**

```ts
// web/src/lib/armies/import.ts — the browser hands management a source; management fetches and parses the list.
import { api } from '$lib/api/client';
import { ApiError } from '$lib/api/http';
import type { Army } from '$lib/api/types';

const ID = /^[A-Za-z0-9_-]{4,64}$/;

/** A share link is passed through whole (management reads its id= and keeps the link as `source`); other URLs with an id= collapse to the id; a bare id passes. */
export function normaliseSource(text: string): string | null {
  const t = text.trim();
  if (!t) return null;
  if (/^https:\/\/army-forge\.onepagerules\.com\/share\?/.test(t) && /[?&]id=[A-Za-z0-9_-]+/.test(t)) return t;
  const m = /[?&]id=([A-Za-z0-9_-]+)/.exec(t);
  if (m) return m[1]!;
  return ID.test(t) ? t : null;
}

export function importErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return 'Something went wrong. Try again.';
  if (e.status === 502) return `Army Forge did not answer (${e.detail ?? e.title}). Check the link is a public share link and try again.`;
  return e.detail ?? e.title;
}

/** A changed list means a re-import: a new army from the same source, then the old one goes. Nothing is reconciled. */
export async function reimport(army: { id: string; source: string }): Promise<Army> {
  const fresh = await api.armies.import(army.source);
  await api.armies.delete(army.id);
  return fresh;
}
```

- [ ] **Step 3: Write `armies/+page.svelte`**

```svelte
<script lang="ts">
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import type { ArmySummary } from '$lib/api/types';
  import { importErrorMessage, normaliseSource, reimport } from '$lib/armies/import';

  let armies = $state<ArmySummary[]>([]); let next = $state<string | undefined>(); let source = $state(''); let busy = $state(false); let loadingMore = $state(false); let error = $state<string | null>(null);
  const normalised = $derived(normaliseSource(source));

  async function load(more = false) {
    if (more) { if (loadingMore || busy) return; loadingMore = true; }
    try {
      const p = await api.armies.list({ limit: 50, after: more ? next : undefined });
      armies = more ? [...armies, ...p.items] : p.items; next = p.next;
    } catch (e) {
      error = importErrorMessage(e);
    } finally { if (more) loadingMore = false; }
  }
  $effect(() => { void load(); });

  async function doImport() {
    if (!normalised) return;
    busy = true; error = null;
    try { const army = await api.armies.import(normalised); await goto(`/armies/${army.id}`); }
    catch (e) { error = importErrorMessage(e); } finally { busy = false; }
  }
  async function doReimport(a: ArmySummary) {
    if (!confirm(`Re-import "${a.name}" from Army Forge?\n\nThis replaces the army with the list as it is now. Every per-slot mesh, base and height you set is lost; unit defaults are quick to set again.`)) return;
    busy = true; error = null;
    try { const fresh = await reimport(a); await goto(`/armies/${fresh.id}`); } catch (e) { error = importErrorMessage(e); } finally { busy = false; }
  }
  async function remove(a: ArmySummary) {
    if (!confirm(`Delete "${a.name}"? Games already loaded with it keep their own copy.`)) return;
    if (busy || loadingMore) return;
    busy = true;
    try {
      await api.armies.delete(a.id); await load();
    } catch (e) {
      error = importErrorMessage(e);
    } finally { busy = false; }
  }
</script>

<h1>Armies</h1>
<section>
  <label>Army Forge share link or list id <input bind:value={source} placeholder="https://army-forge.onepagerules.com/share?id=…" size="60" /></label>
  <button onclick={doImport} disabled={busy || !normalised}>Import</button>
  {#if source && !normalised}<small>That is not a share link or list id.</small>{/if}
  <p><small>Grimdark Future and Age of Fantasy lists import; the skirmish and regiment variants do not. Management fetches the list; the army then holds its own copy of every value.</small></p>
</section>
{#if error}<p role="alert">{error}</p>{/if}
<table>
  <thead><tr><th>Name</th><th>System</th><th>Units</th><th>Models</th><th></th></tr></thead>
  <tbody>
    {#each armies as a (a.id)}
      <tr><td><a href="/armies/{a.id}">{a.name}</a></td><td>{a.gameSystem}</td><td>{a.units}</td><td>{a.models}</td>
        <td><button onclick={() => doReimport(a)} disabled={busy}>Re-import</button> <button onclick={() => remove(a)} disabled={busy || loadingMore}>Delete</button></td></tr>
    {/each}
  </tbody>
</table>
{#if next}<button onclick={() => load(true)} disabled={loadingMore || busy}>Load more</button>{/if}
```

- [ ] **Step 4: `npm test && npm run check`; 60 passed. Run the app: paste `https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py` (the management plan's fixture list), import, see five units and 33 models in the list; paste an Age of Fantasy link and see it import too (Grimdark Future and Age of Fantasy are the same mechanics on different content, so both import and both land as `grimdark-future`; management plan Task 9); paste an Age of Fantasy Skirmish link and read the `422` message; re-import and confirm the old id is gone. Commit: `Add the armies list with import, re-import and delete`.**
