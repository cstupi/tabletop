# Task 7: The open Ask, who it addresses, and the frame's dice

**Files:**
- Create: `viewer/src/lib/asks/describe.ts`, `viewer/src/lib/components/AskPanel.svelte`, `viewer/src/lib/components/RollPanel.svelte`
- Test: `viewer/tests/describe.test.ts`

**Interfaces:**
- Consumes: `RecordedAsk`, `RecordedAskOption`, `RecordedRoll`, `RecordedModifier`, `RecordedFrame` (Task 2); `teamNames`, `teamOrdinals` (Task 3).
- Produces: `type Addressee = { readonly id: string; readonly label: string; readonly answered: boolean }`; `type AskView = { readonly id: string; readonly heading: string; readonly prompt: string; readonly addressees: readonly Addressee[]; readonly waitingOn: readonly string[]; readonly legal: string; readonly notes: readonly string[]; readonly options: readonly RecordedAskOption[]; readonly pieces: readonly string[]; readonly allowance: number | null }`; `function describeAsk(ask: RecordedAsk, teamLabels: ReadonlyMap<string, string>): AskView`; `function describeLegal(ask: RecordedAsk): string`; `function humanise(question: string): string`.
- `AskPanel.svelte` props `{ asks: readonly RecordedAsk[]; teamLabels: ReadonlyMap<string, string> }`; `RollPanel.svelte` props `{ rolls: readonly RecordedRoll[] }`.
- **Display only.** No control, no answer, no button that sends anything (spec → *Interactive debugging is a door, not a room*: "The viewer sends no command and answers no Ask"). The panel exists because "the recording carries the open Ask at every frame" and showing it is what makes the seam visible without being built on.

Spec: spec → *Interactive debugging is a door, not a room*. Simulation plan Task 5 — `RecordedAsk`'s `LegalKind` is "the legal set's wire kind: options, pieces, allocation, assignment, move_region, token_spend, confirmation" and `Answered` is "the teams that have already answered — a sealed Ask's answers stay hidden until it closes". A token spend is a maximum per model rather than one number, so the recording carries the spender models in `pieces` and the sum of their maxima in `allowance`.

- [ ] **Step 1: Write `viewer/tests/describe.test.ts`**

```ts
import { describe, expect, it } from 'vitest';
import { describeAsk, describeLegal, humanise } from '../src/lib/asks/describe';
import type { RecordedAsk } from '../src/lib/protocol/recording.generated';

const labels = new Map([['t1', 'Team 1'], ['t2', 'Team 2']]);

const ask = (over: Partial<RecordedAsk> = {}): RecordedAsk => ({
  id: 'a1', question: 'act_or_end_phase', prompt: 'Act or end the phase', teams: ['t1'],
  allowPass: false, sealed: false, legalKind: 'options', answered: [],
  options: [{ id: 'o1', label: 'Advance', pieces: ['u1'] }], pieces: [], ...over,
});

describe('describeAsk', () => {
  it('humanises the question and names every team the Ask addresses', () => {
    const view = describeAsk(ask({ teams: ['t1', 't2'], answered: ['t2'] }), labels);
    expect(view.heading).toBe('Act or end phase');
    expect(view.prompt).toBe('Act or end the phase');
    expect(view.addressees).toEqual([
      { id: 't1', label: 'Team 1', answered: false },
      { id: 't2', label: 'Team 2', answered: true },
    ]);
    expect(view.waitingOn).toEqual(['Team 1']);
  });

  it('falls back to a short id for a team the recording does not name', () => {
    expect(describeAsk(ask({ teams: ['0198b7ac-1111-7000-8000-000000000001'] }), labels).addressees[0]!.label)
      .toBe('team 0198b7ac');
  });

  it('describes every legal kind from the fields the schema carries', () => {
    expect(describeLegal(ask())).toBe('1 option');
    expect(describeLegal(ask({ legalKind: 'options', options: [] }))).toBe('0 options');
    expect(describeLegal(ask({ legalKind: 'pieces', options: [], pieces: ['u1', 'u2'] }))).toBe('2 eligible pieces');
    expect(describeLegal(ask({ legalKind: 'allocation', options: [], pieces: ['m1', 'm2', 'm3'] }))).toBe('3 targets to allocate over');
    expect(describeLegal(ask({ legalKind: 'assignment', pieces: ['u1', 'u2'] }))).toBe('1 item over 2 targets');
    expect(describeLegal(ask({ legalKind: 'move_region', options: [], pieces: ['m1', 'm2'], allowance: 6 }))).toBe('2 models within 6"');
    expect(describeLegal(ask({ legalKind: 'token_spend', options: [], pieces: ['m1', 'm2'], allowance: 3 }))).toBe('a token spend of up to 3 over 2 models');
    expect(describeLegal(ask({ legalKind: 'confirmation', options: [] }))).toBe('yes or no');
    expect(describeLegal(ask({ legalKind: 'SomethingNew', options: [] }))).toBe('SomethingNew');
  });

  it('notes a pass, a sealed Ask, a reaction window and the frame it belongs to', () => {
    const view = describeAsk(
      ask({ allowPass: true, sealed: true, windowKind: 'charged', frameId: '0198b7ac-2222-7000-8000-000000000002' }),
      labels,
    );
    expect(view.notes).toEqual([
      'a pass is allowed',
      'sealed — answers stay hidden until it closes',
      'reaction window: charged',
      'in frame 0198b7ac',
    ]);
  });

  it('carries the option, piece and allowance detail through untouched', () => {
    const view = describeAsk(ask({ legalKind: 'move_region', options: [], pieces: ['m1'], allowance: 12 }), labels);
    expect(view.pieces).toEqual(['m1']);
    expect(view.allowance).toBe(12);
    expect(describeAsk(ask(), labels).options).toEqual([{ id: 'o1', label: 'Advance', pieces: ['u1'] }]);
    expect(describeAsk(ask(), labels).allowance).toBeNull();
  });

  it('humanises a snake_case question and leaves anything else alone', () => {
    expect(humanise('deploy_scouts')).toBe('Deploy scouts');
    expect(humanise('confirm')).toBe('Confirm');
    expect(humanise('')).toBe('');
  });
});
```

- [ ] **Step 2: Run; expect the import to fail. Write `describe.ts`**

```ts
// viewer/src/lib/asks/describe.ts
import type { RecordedAsk, RecordedAskOption } from '../protocol/recording.generated';

export type Addressee = { readonly id: string; readonly label: string; readonly answered: boolean };

export type AskView = {
  readonly id: string;
  readonly heading: string;
  readonly prompt: string;
  readonly addressees: readonly Addressee[];
  readonly waitingOn: readonly string[];
  readonly legal: string;
  readonly notes: readonly string[];
  readonly options: readonly RecordedAskOption[];
  readonly pieces: readonly string[];
  readonly allowance: number | null;
};

export function humanise(question: string): string {
  if (question.length === 0) return '';
  const words = question.split('_').join(' ');
  return words[0]!.toUpperCase() + words.slice(1);
}

const plural = (n: number, one: string, many = `${one}s`): string => `${n} ${n === 1 ? one : many}`;

export function describeLegal(ask: RecordedAsk): string {
  switch (ask.legalKind) {
    case 'options': return plural(ask.options.length, 'option');
    case 'pieces': return `${plural(ask.pieces.length, 'eligible piece')}`;
    case 'allocation': return `${plural(ask.pieces.length, 'target')} to allocate over`;
    case 'assignment': return `${plural(ask.options.length, 'item')} over ${plural(ask.pieces.length, 'target')}`;
    case 'move_region': return `${plural(ask.pieces.length, 'model')} within ${ask.allowance ?? 0}"`;
    // The spend is per model (p.13): the recording carries the spender models in `pieces` and the
    // sum of their maxima in `allowance`, so the line says how much may be spent and by how many models.
    case 'token_spend': return `a token spend of up to ${ask.allowance ?? 0} over ${plural(ask.pieces.length, 'model')}`;
    case 'confirmation': return 'yes or no';
    default: return ask.legalKind;
  }
}

export function describeAsk(ask: RecordedAsk, teamLabels: ReadonlyMap<string, string>): AskView {
  const answered = new Set(ask.answered);
  const addressees = ask.teams.map((id) => ({
    id, label: teamLabels.get(id) ?? `team ${id.slice(0, 8)}`, answered: answered.has(id),
  }));
  const notes: string[] = [];
  if (ask.allowPass) notes.push('a pass is allowed');
  if (ask.sealed) notes.push('sealed — answers stay hidden until it closes');
  if (ask.windowKind !== undefined) notes.push(`reaction window: ${ask.windowKind}`);
  if (ask.frameId !== undefined) notes.push(`in frame ${ask.frameId.slice(0, 8)}`);

  return {
    id: ask.id,
    heading: humanise(ask.question),
    prompt: ask.prompt,
    addressees,
    waitingOn: addressees.filter((a) => !a.answered).map((a) => a.label),
    legal: describeLegal(ask),
    notes,
    options: ask.options,
    pieces: ask.pieces,
    allowance: ask.allowance ?? null,
  };
}
```

- [ ] **Step 3: Write `AskPanel.svelte`**

```svelte
<!-- viewer/src/lib/components/AskPanel.svelte -->
<script lang="ts">
  import type { RecordedAsk } from '../protocol/recording.generated';
  import { describeAsk } from '../asks/describe';

  let { asks, teamLabels }: { asks: readonly RecordedAsk[]; teamLabels: ReadonlyMap<string, string> } = $props();
  const views = $derived(asks.map((a) => describeAsk(a, teamLabels)));
</script>

{#if views.length === 0}
  <p class="muted">no open Ask</p>
{/if}
{#each views as view (view.id)}
  <section class="ask">
    <h3>{view.heading}</h3>
    <p>{view.prompt}</p>
    <p class="muted">
      asked of
      {#each view.addressees as team, i (team.id)}<span class:answered={team.answered}
        >{i > 0 ? ', ' : ''}{team.label}{team.answered ? ' ✓' : ''}</span>{/each}
    </p>
    {#if view.waitingOn.length > 0}<p class="muted">waiting on {view.waitingOn.join(', ')}</p>{/if}
    <p class="muted">legal set: {view.legal}</p>
    {#each view.notes as note (note)}<p class="muted">· {note}</p>{/each}
    {#if view.options.length > 0}
      <ul>{#each view.options as option (option.id)}<li><code>{option.id}</code> {option.label}</li>{/each}</ul>
    {/if}
    <!-- Display only. The viewer answers no Ask (spec → Interactive debugging is a door, not a room). -->
  </section>
{/each}

<style>
  .ask { border: 1px solid var(--line); border-radius: 3px; padding: 0.4rem 0.5rem; margin-bottom: 0.5rem; }
  h3 { margin: 0 0 0.25rem; font-size: 1rem; }
  p { margin: 0.15rem 0; }
  ul { margin: 0.3rem 0 0; padding-left: 1.1rem; }
  .answered { color: var(--ink); }
</style>
```

- [ ] **Step 4: Write `RollPanel.svelte`**

```svelte
<!-- viewer/src/lib/components/RollPanel.svelte -->
<script lang="ts">
  import type { RecordedRoll } from '../protocol/recording.generated';

  let { rolls }: { rolls: readonly RecordedRoll[] } = $props();
</script>

{#if rolls.length === 0}
  <p class="muted">no dice this frame</p>
{/if}
{#each rolls as roll (roll.id)}
  <section class="roll">
    <h3>{roll.roll}{roll.threshold === undefined ? '' : ` — needs ${roll.threshold}+`}</h3>
    <p class="dice">
      {#each roll.raw as raw, i (i)}
        <span class="die" class:hit={roll.success[i]}>
          {raw}{roll.modified[i] === raw ? '' : `→${roll.modified[i]}`}
        </span>
      {/each}
    </p>
    <p class="muted">
      net {roll.net >= 0 ? `+${roll.net}` : roll.net}
      {#if roll.modifiers.length > 0}
        · {roll.modifiers.map((m) => `${m.source} ${m.amount >= 0 ? `+${m.amount}` : m.amount}`).join(', ')}
      {/if}
      · {roll.success.filter(Boolean).length}/{roll.raw.length} passed
    </p>
  </section>
{/each}

<style>
  .roll { border: 1px solid var(--line); border-radius: 3px; padding: 0.4rem 0.5rem; margin-bottom: 0.5rem; }
  h3 { margin: 0 0 0.25rem; font-size: 1rem; }
  p { margin: 0.15rem 0; }
  .dice { display: flex; flex-wrap: wrap; gap: 0.25rem; }
  .die { border: 1px solid var(--line); border-radius: 3px; padding: 0 0.3rem; color: var(--muted); }
  .die.hit { border-color: #6ddf7c; color: #6ddf7c; }
</style>
```

- [ ] **Step 5: Run the tests; expect 6 more passed**

Run: `cd viewer && npm test && npm run check`

- [ ] **Step 6: Commit**

```bash
git add viewer/src/lib/asks viewer/src/lib/components/AskPanel.svelte viewer/src/lib/components/RollPanel.svelte viewer/tests/describe.test.ts
git commit -m "Show the open Ask, who it addresses, and every die of the frame"
```
