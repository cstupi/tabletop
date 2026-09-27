# Task 16: The army editor — unit default mesh, per-slot mesh, base and height

**Files:**
- Create: `web/src/lib/components/AssetPicker.svelte`, `web/src/lib/armies/patches.ts`, `web/src/routes/armies/[id]/+page.svelte`
- Test: `web/tests/patches.test.ts`

**Interfaces:**
- Consumes: `api.armies.get/patch`, `api.assets.list`, `Army`, `Unit`, `ModelSlot`, `PatchArmyRequest`, `UnitPatch`, `SlotPatch`, `AssetDto`, `BasePicker`, `baseLabel`, `presetFor`, `filterAssets`' `sameBase` rule.
- Produces: `unitDefault(unitId, assetId | null): PatchArmyRequest`, `slotMesh(slotId, assetId | null): PatchArmyRequest` (null → `clearAsset: true`), `slotBase(slotId, base): PatchArmyRequest`, `slotHeight(slotId, height): PatchArmyRequest`, `rename(name): PatchArmyRequest`; `slotSummary(slot): string` (weapons and properties as one line, `Heavy Rifle (24", A1, AP(1))` style, read from the stat keys `Range`/`Attacks` the import writes); `AssetPicker` with props `value: Guid | null`, `base: Base | null` (offers "made for this base" first), `onchange(assetId | null)`; `/armies/[id]`.
- Every edit is one `PATCH` and the page re-renders from the returned `Army`; there is no local draft to lose. Unit default writes every slot's `assetId` (the management repository does that); a slot override is a later `SlotPatch`. The army editor edits what the specs let a user edit: bindings, base shape and height per slot, and the army's name — stats, weapons and properties are the list's and are shown read-only. Unit and slot names are not editable: they are the list's (the army → Where the values come from), and `PATCH /v1/armies/{id}` has no field for them (see *Interfaces needed from other modules*).

Spec: the army → Where the values come from ("the pack or the import supplies a default, the user overrides per slot"), Binding ("The default fills every slot in a unit with the same mesh; overriding individual slots is expected rather than exceptional"), Base shape and height ("Set by the user, per slot"), Assets are a soft reference ("The army holds the hitbox. The asset is only ever appearance"); asset library → The modelled base suggests ("given a slot on a 32mm base, the library can offer the assets built for one"); management plan Task 10 (`PatchArmyRequest(Name?, Units?, Slots?)`, `UnitPatch(UnitId, AssetId)`, `SlotPatch(SlotId, AssetId, ClearAsset, Base, Height)`).

- [ ] **Step 1: Write the failing tests and `patches.ts`**

```ts
// web/tests/patches.test.ts
import { describe, expect, it } from 'vitest';
import { rename, slotBase, slotHeight, slotMesh, slotSummary, unitDefault } from '../src/lib/armies/patches';

describe('patches', () => {
  it('shapes each edit as the PATCH management accepts', () => {
    expect(unitDefault('u1', 'a1')).toEqual({ units: [{ unitId: 'u1', assetId: 'a1' }] });
    expect(unitDefault('u1', null)).toEqual({ units: [{ unitId: 'u1', assetId: null }] });
    expect(slotMesh('s1', 'a2')).toEqual({ slots: [{ slotId: 's1', assetId: 'a2', clearAsset: false, base: null, height: null }] });
    expect(slotMesh('s1', null)).toEqual({ slots: [{ slotId: 's1', assetId: null, clearAsset: true, base: null, height: null }] });
    expect(slotBase('s1', { shape: 'oval', a: 2.36, b: 1.38 })).toEqual({ slots: [{ slotId: 's1', assetId: null, clearAsset: false, base: { shape: 'oval', a: 2.36, b: 1.38 }, height: null }] });
    expect(slotHeight('s1', 2)).toEqual({ slots: [{ slotId: 's1', assetId: null, clearAsset: false, base: null, height: 2 }] });
    expect(rename('Daemons')).toEqual({ name: 'Daemons' });
  });
  it('summarises a slot the way the rulebook prints a profile', () => {
    expect(slotSummary({ id: 's', ordinal: 1, name: 'Rider 1', stats: {}, properties: [{ name: 'Tough', parameter: 3 }], base: { shape: 'circle', a: 1, b: 1 }, height: 1.5,
      weapons: [{ id: 'w', name: 'Heavy Rifle', stats: { Range: '24"', Attacks: '1' }, properties: [{ name: 'AP', parameter: 1 }] }, { id: 'x', name: 'CCW', stats: { Attacks: '2' }, properties: [] }] }))
      .toBe('Heavy Rifle (24", A1, AP(1)), CCW (A2) · Tough(3)');
  });
});
```

```ts
// web/src/lib/armies/patches.ts
import type { Base, Guid, ModelSlot, PatchArmyRequest, PropertyInstance } from '$lib/api/types';

const slot = (slotId: Guid, over: Partial<{ assetId: Guid | null; clearAsset: boolean; base: Base | null; height: number | null }>): PatchArmyRequest =>
  ({ slots: [{ slotId, assetId: null, clearAsset: false, base: null, height: null, ...over }] });

export const unitDefault = (unitId: Guid, assetId: Guid | null): PatchArmyRequest => ({ units: [{ unitId, assetId }] });
export const slotMesh = (slotId: Guid, assetId: Guid | null): PatchArmyRequest => slot(slotId, assetId ? { assetId } : { clearAsset: true });
export const slotBase = (slotId: Guid, base: Base): PatchArmyRequest => slot(slotId, { base });
export const slotHeight = (slotId: Guid, height: number): PatchArmyRequest => slot(slotId, { height });
export const rename = (name: string): PatchArmyRequest => ({ name });

const prop = (p: PropertyInstance) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`);
export function slotSummary(s: ModelSlot): string {
  const weapons = s.weapons.map((w) => `${w.name} (${[w.stats['Range'], w.stats['Attacks'] ? `A${w.stats['Attacks']}` : undefined, ...w.properties.map(prop)].filter(Boolean).join(', ')})`).join(', ');
  const props = s.properties.map(prop).join(', ');
  return [weapons, props].filter(Boolean).join(' · ');
}
```

- [ ] **Step 2: Run; expect failures then pass. Write `AssetPicker.svelte`:**

```svelte
<script lang="ts">
  import { api } from '$lib/api/client';
  import type { AssetDto, Base, Guid } from '$lib/api/types';
  import { baseLabel } from '$lib/units/bases';
  let { value, base, onchange }: { value: Guid | null; base: Base | null; onchange: (id: Guid | null) => void } = $props();

  // one load per page: the library is small enough to hold, and filtering "made for this base" is client-side
  let all = $state<AssetDto[]>([]);
  $effect(() => { (async () => { const out: AssetDto[] = []; for (let after: string | undefined; ;) { const p = await api.assets.list({ limit: 200, after, kind: 'miniature' }); out.push(...p.items); if (!p.next) break; after = p.next; } all = out.filter((a) => a.uploaded); })(); });
  const fits = (a: AssetDto) => !!base && !!a.modelledBase && a.modelledBase.shape === base.shape && Math.abs(a.modelledBase.a - base.a) < 0.005 && Math.abs(a.modelledBase.b - base.b) < 0.005;
  const fitting = $derived(all.filter(fits).sort((a, b) => a.name.localeCompare(b.name)));
  const others = $derived(all.filter((a) => !fits(a)).sort((a, b) => a.name.localeCompare(b.name)));
</script>

<select value={value ?? ''} onchange={(e) => onchange(e.currentTarget.value || null)}>
  <option value="">— none (draws the hitbox) —</option>
  {#if fitting.length}<optgroup label="Made for this base">{#each fitting as a (a.id)}<option value={a.id}>{a.name}</option>{/each}</optgroup>{/if}
  <optgroup label={fitting.length ? 'Other miniatures' : 'Miniatures'}>{#each others as a (a.id)}<option value={a.id}>{a.name}{#if a.modelledBase} · {baseLabel(a.modelledBase)}{/if}</option>{/each}</optgroup>
</select>
```

- [ ] **Step 3: Write `armies/[id]/+page.svelte`**

```svelte
<script lang="ts">
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import type { Army, PatchArmyRequest } from '$lib/api/types';
  import AssetPicker from '$lib/components/AssetPicker.svelte';
  import BasePicker from '$lib/components/BasePicker.svelte';
  import { rename, slotBase, slotHeight, slotMesh, slotSummary, unitDefault } from '$lib/armies/patches';
  import { baseLabel } from '$lib/units/bases';

  const id = $derived(page.params.id!);
  let army = $state<Army | null>(null); let name = $state(''); let error = $state<string | null>(null); let open = $state<Record<string, boolean>>({});

  $effect(() => { api.armies.get(id).then((a) => { army = a; name = a.name; }).catch((e) => (error = String(e))); });
  const apply = (p: PatchArmyRequest) => api.armies.patch(id, p).then((a) => (army = a)).catch((e) => (error = e instanceof Error ? e.message : String(e)));
  const props = (ps: { name: string; parameter?: number }[]) => ps.map((p) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`)).join(', ');
</script>

{#if army}
  <header>
    <input bind:value={name} onchange={() => name.trim() && name !== army!.name && apply(rename(name.trim()))} aria-label="Army name" class="title" />
    <p><small>{army.gameSystem} · imported from <a href={army.source.startsWith('http') ? army.source : `https://army-forge.onepagerules.com/share?id=${army.source}`} target="_blank" rel="noopener">Army Forge</a>. Stats, weapons and rules are the list's; meshes, bases and heights are yours.</small></p>
  </header>
  {#each army.units as unit (unit.id)}
    <section class="unit">
      <h2><button onclick={() => (open[unit.id] = !open[unit.id])}>{open[unit.id] ? '▾' : '▸'}</button> {unit.name} <small>[{unit.models.length}] · {Object.entries(unit.stats).map(([k, v]) => `${k} ${v}`).join(' · ')}{#if unit.properties.length} · {props(unit.properties)}{/if}</small></h2>
      <label>Mesh for every model <AssetPicker value={unit.models.every((m) => m.assetId === unit.models[0]?.assetId) ? (unit.models[0]?.assetId ?? null) : null} base={unit.models[0]?.base ?? null} onchange={(a) => apply(unitDefault(unit.id, a))} /></label>
      {#if open[unit.id]}
        <table>
          <thead><tr><th>Model</th><th>Loadout and rules</th><th>Mesh</th><th>Base (hitbox)</th><th>Height (in)</th></tr></thead>
          <tbody>
            {#each unit.models as m (m.id)}
              <tr>
                <td>{m.name}</td>
                <td><small>{slotSummary(m)}</small></td>
                <td><AssetPicker value={m.assetId ?? null} base={m.base} onchange={(a) => apply(slotMesh(m.id, a))} /></td>
                <td><BasePicker value={m.base} allowNone={false} onchangevalue={(b) => b && apply(slotBase(m.id, b))} /> <small>{baseLabel(m.base)}</small></td>
                <td><input type="number" step="0.25" min="0.25" value={m.height} onchange={(e) => apply(slotHeight(m.id, Number(e.currentTarget.value)))} /></td>
              </tr>
            {/each}
          </tbody>
        </table>
      {/if}
    </section>
  {/each}
{/if}
{#if error}<p role="alert">{error}</p>{/if}
<style>.title { font-size: 1.5rem; font-weight: bold; border: none; background: none; width: 100% } .unit { border-top: 1px solid #8884; padding: .5rem 0 } td, th { text-align: left; vertical-align: top; padding: .25rem .5rem }</style>
```


- [ ] **Step 4: `npm test && npm run check`; 45 passed. Run the app on the imported army: set a unit default mesh (every row shows it), override one slot with another mesh, set that slot to a 60 × 35 mm oval and height 2, reload — every value came back from the server; clear the slot's mesh and the picker reads "none (draws the hitbox)". Commit: `Add the army editor: unit default mesh, per-slot mesh, base and height`.**
