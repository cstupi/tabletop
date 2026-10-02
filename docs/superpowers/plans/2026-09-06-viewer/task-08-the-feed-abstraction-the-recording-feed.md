# Task 8: The feed abstraction, the recording feed, and the whole page

**Files:**
- Create: `viewer/src/lib/feed/feed.ts`, `viewer/src/lib/feed/recordingFeed.ts`, `viewer/src/lib/components/SourcePicker.svelte`, `viewer/src/lib/components/Scoreboard.svelte`
- Modify: `viewer/src/App.svelte` (Task 1 left it a placeholder), `viewer/src/app.css`
- Test: `viewer/tests/feed.test.ts`

**Interfaces:**
- Consumes: `Recording`, `RecordingMeta`, `RecordedBoard`, `RecordedFrame`, `RecordedTeam` (Task 2); `RecordingSource`, `fromFile`, `fromUrl`, `recordingUrlFrom`, `teamNames` (Task 3); `sceneOf`, `teamColour` (Task 4); `Board.svelte` (Task 5); `Timeline.svelte`, `LogPanel.svelte`, `EMPTY`, `reduce`, `frameOf` (Task 6); `AskPanel.svelte`, `RollPanel.svelte` (Task 7).

**Where the score and the passed teams go.** `RecordedFrame` carries `scores` and `passed`, and neither belongs in a tab. The score is the one number a person watching wants continuously, so it may not cost a tab change; and which teams have passed is a scheduler fact that reads as nonsense apart from the round and the active team. So both live in one `Scoreboard.svelte` in the **header**, above the board and outside the tabbed pane: the round, then one chip per team in ordinal order carrying its colour, its name, its score, and — when it applies — `active` or `passed`. That is four scheduler facts in one strip and no new pane, which keeps the layout the two-pane one the plan already committed to (ruling 5). The board itself gains nothing: a score is not geometry, and `Scene` is unchanged.

`scores` is the schema's only dictionary, so it reads as `frame.scores[team.id] ?? 0` — the generated type is `{ readonly [key: string]: number }` and its Guid keys are canonical lowercase strings, because neither `RecordingJson.Options` nor `WireJson.Options` sets a dictionary key policy. `?? 0` is not a fallback for a missing feature: `noUncheckedIndexedAccess` makes every index read `number | undefined`, and a team the projection has not scored for is genuinely absent.
- Produces: `type FeedStatus = 'idle' | 'loading' | 'open' | 'closed' | 'error'`; `type FeedState = { readonly label: string; readonly status: FeedStatus; readonly detail: string | null; readonly meta: RecordingMeta | null; readonly board: RecordedBoard | null; readonly frames: readonly RecordedFrame[] }`; `const IDLE: FeedState`; `type ControlChannel`; `interface Feed { readonly kind: 'recording' | 'live'; readonly control: ControlChannel | null; subscribe(listener: (state: FeedState) => void): () => void; close(): void }`; `class Emitter` (the shared subscribe/emit base); `function detachFeed(previous: Feed | null, unsubscribe: (() => void) | null): void`; `function attachFeed(previous: Feed | null, unsubscribe: (() => void) | null, next: Feed, listener: (state: FeedState) => void): () => void`; `function recordingFeed(source: RecordingSource): Feed`.
- **Swapping feeds is a function, not a habit.** `attachFeed` owns the whole sequence — drop the old subscription, close the old feed, subscribe to the new one, return the new unsubscribe — so `App.svelte` holds what it returns and has no ordering logic of its own, and Task 11's test drives the same function the component does rather than a copy of its steps. The order is load-bearing: a live feed's socket closes asynchronously, so a listener left attached across a swap can fire afterwards and overwrite the new feed's state with the old feed's frames.
- **The control channel is the seam and stays empty.** `ControlChannel` is declared, both feeds return `null` for it, and `feed.test.ts` asserts that. Spec → *Interactive debugging is a door, not a room*: "the viewer's feed abstraction has room for a control channel. None of that is built yet."

Spec: spec → *Two feeds*; *The recording is the artifact* — "A recording, loaded from a file. The primary case, and the only one that works with no server running."

- [ ] **Step 1: Write `viewer/tests/feed.test.ts`**

```ts
import { describe, expect, it } from 'vitest';
import { IDLE, recordingFeed } from '../src/lib/feed/recordingFeed';
import type { FeedState } from '../src/lib/feed/feed';
import type { Recording } from '../src/lib/protocol/recording.generated';
import { SCHEMA_VERSION } from '../src/lib/protocol/recording.generated';

const recording = {
  schemaVersion: SCHEMA_VERSION,
  meta: { scenario: 'skirmish', seed: 3, driver: 'in-process', gameRules: 'grimdark-future',
    propertiesHash: 'sha256:x', schedulerShape: 'alternating_activation', enforcement: 'strict',
    teams: [], outcome: 'decided', rounds: 2, lastSeq: 2, startedAt: '1970-01-01T00:00:00+00:00', durationMs: 4 },
  board: { width: 72, length: 48, texture: 'grass', terrain: [], markers: [], zones: [], models: [] },
  frames: [
    { index: 0, seq: 1, kind: 'phase_started', round: 0, line: 'Phase: deployment', models: [], units: [], markers: [], openAsks: [], rolls: [] },
    { index: 1, seq: 2, kind: 'round_started', round: 1, line: 'Round 1 started', models: [], units: [], markers: [], openAsks: [], rolls: [] },
  ],
} as unknown as Recording;

describe('the recording feed', () => {
  it('starts idle with nothing to draw', () => {
    expect(IDLE).toEqual({ label: '', status: 'idle', detail: null, meta: null, board: null, frames: [] });
  });

  it('hands a subscriber the whole recording at once and stays open', () => {
    const seen: FeedState[] = [];
    const feed = recordingFeed({ label: 'run.json', recording });
    feed.subscribe((s) => seen.push(s));
    expect(seen).toHaveLength(1);
    expect(seen[0]).toMatchObject({ label: 'run.json', status: 'open', frames: recording.frames });
    expect(seen[0]!.board!.width).toBe(72);
    expect(feed.kind).toBe('recording');
  });

  it('leaves the control channel empty — the viewer sends nothing', () => {
    expect(recordingFeed({ label: 'run.json', recording }).control).toBeNull();
  });

  it('stops emitting once unsubscribed and once closed', () => {
    const feed = recordingFeed({ label: 'run.json', recording });
    const seen: FeedState[] = [];
    const stop = feed.subscribe((s) => seen.push(s));
    stop();
    feed.close();
    expect(seen).toHaveLength(1);
    const after: FeedState[] = [];
    feed.subscribe((s) => after.push(s));
    expect(after[0]!.status).toBe('closed');
  });
});
```

- [ ] **Step 2: Run; expect the import to fail. Write `feed.ts`**

```ts
// viewer/src/lib/feed/feed.ts
import type { RecordedBoard, RecordedFrame, RecordingMeta } from '../protocol/recording.generated';

export type FeedStatus = 'idle' | 'loading' | 'open' | 'closed' | 'error';

/** What every feed produces: the recording schema and nothing else, so the renderer cannot tell them apart. */
export type FeedState = {
  readonly label: string;
  readonly status: FeedStatus;
  readonly detail: string | null;
  readonly meta: RecordingMeta | null;
  readonly board: RecordedBoard | null;
  readonly frames: readonly RecordedFrame[];
};

export const IDLE: FeedState = { label: '', status: 'idle', detail: null, meta: null, board: null, frames: [] };

/**
 * The seam interactive debugging would use: pausing a run and answering an open Ask from the browser
 * (spec → Interactive debugging is a door, not a room). Nothing implements it. Both feeds return null,
 * and building it "makes the viewer a second thin client, which is a larger surface than a renderer".
 */
export type ControlChannel = {
  answer(askId: string, answer: unknown): void;
  resume(): void;
};

export interface Feed {
  readonly kind: 'recording' | 'live';
  readonly control: ControlChannel | null;
  /**
   * Returns the unsubscribe function, and a caller must hold it: `close()` is not a substitute,
   * because a live feed's socket closes asynchronously and its listener can fire after another feed
   * has replaced it. Swapping and tearing down go through `attachFeed` and `detachFeed` below, which
   * own that order; Task 11's test drives them.
   */
  subscribe(listener: (state: FeedState) => void): () => void;
  close(): void;
}

/** One subscriber list, one current state. Both feeds extend it; nothing else needs a store. */
export class Emitter {
  #state: FeedState;
  readonly #listeners = new Set<(state: FeedState) => void>();

  constructor(initial: FeedState) {
    this.#state = initial;
  }

  get state(): FeedState {
    return this.#state;
  }

  subscribe(listener: (state: FeedState) => void): () => void {
    this.#listeners.add(listener);
    listener(this.#state);
    return () => this.#listeners.delete(listener);
  }

  emit(next: Partial<FeedState>): void {
    this.#state = { ...this.#state, ...next };
    for (const listener of this.#listeners) listener(this.#state);
  }
}

/** Let a feed go: its listener first, then the feed itself. Both halves, always in this order. */
export function detachFeed(previous: Feed | null, unsubscribe: (() => void) | null): void {
  unsubscribe?.();
  previous?.close();
}

/**
 * Swap one feed for another and hand back the new unsubscribe function, which the caller must hold.
 * The order is the whole point and lives here rather than in a component: a live feed's socket closes
 * asynchronously, so a listener left attached across the swap can fire afterwards and overwrite the
 * new feed's state with the old feed's frames.
 */
export function attachFeed(
  previous: Feed | null,
  unsubscribe: (() => void) | null,
  next: Feed,
  listener: (state: FeedState) => void,
): () => void {
  detachFeed(previous, unsubscribe);
  return next.subscribe(listener);
}
```

- [ ] **Step 3: Write `recordingFeed.ts`**

```ts
// viewer/src/lib/feed/recordingFeed.ts
import type { RecordingSource } from '../recording/source';
import { Emitter, IDLE, type ControlChannel, type Feed, type FeedState } from './feed';

export { IDLE } from './feed';
export type { FeedState } from './feed';

/** A recording arrives complete, so this feed emits once and never again. */
export function recordingFeed(source: RecordingSource): Feed {
  const emitter = new Emitter({
    ...IDLE,
    label: source.label,
    status: 'open',
    meta: source.recording.meta,
    board: source.recording.board,
    frames: source.recording.frames,
  });

  return {
    kind: 'recording',
    control: null as ControlChannel | null,
    subscribe: (listener: (state: FeedState) => void) => emitter.subscribe(listener),
    close: () => emitter.emit({ status: 'closed' }),
  };
}
```

- [ ] **Step 4: Write `SourcePicker.svelte` and `Scoreboard.svelte`**

```svelte
<!-- viewer/src/lib/components/SourcePicker.svelte -->
<script lang="ts">
  import { fromFile, fromUrl } from '../recording/source';
  import type { Feed } from '../feed/feed';
  import { recordingFeed } from '../feed/recordingFeed';

  let { onFeed, onError }: { onFeed: (feed: Feed) => void; onError: (message: string) => void } = $props();
  let url = $state('');

  async function load(work: () => Promise<Feed>) {
    try { onFeed(await work()); } catch (error) { onError((error as Error).message); }
  }
</script>

<div class="picker">
  <label>
    Recording
    <input type="file" accept="application/json,.json" onchange={(e) => {
      const file = e.currentTarget.files?.[0];
      if (file !== undefined) load(async () => recordingFeed(await fromFile(file)));
    }} />
  </label>
  <input type="url" placeholder="or a URL to a recording" bind:value={url} style="width: 18rem" />
  <button disabled={url.length === 0} onclick={() => load(async () => recordingFeed(await fromUrl(url)))}>Load</button>
</div>

<style>
  .picker { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }
</style>
```

```svelte
<!-- viewer/src/lib/components/Scoreboard.svelte -->
<!-- The score and the scheduler's position, read straight off the frame: whole state, no accumulation. -->
<script lang="ts">
  import { teamColour } from '../board/palette';
  import type { RecordedFrame, RecordedTeam } from '../protocol/recording.generated';

  let { teams, frame }: { teams: readonly RecordedTeam[]; frame: RecordedFrame | null } = $props();

  const ordered = $derived([...teams].sort((a, b) => a.ordinal - b.ordinal));
  const passed = $derived(new Set(frame?.passed ?? []));
</script>

{#if frame !== null && ordered.length > 0}
  <div class="scoreboard">
    <span class="muted">round {frame.round}</span>
    {#each ordered as team (team.id)}
      <span class="team" class:active={frame.activeTeamId === team.id} style="--team: {teamColour(team.ordinal)}">
        <span class="swatch"></span>
        {team.name}
        <strong>{frame.scores[team.id] ?? 0}</strong>
        {#if passed.has(team.id)}<span class="tag">passed</span>
        {:else if frame.activeTeamId === team.id}<span class="tag">active</span>{/if}
      </span>
    {/each}
  </div>
{/if}

<style>
  .scoreboard { display: flex; gap: 0.6rem; align-items: center; flex-wrap: wrap; }
  .team { display: inline-flex; gap: 0.3rem; align-items: center;
    padding: 0.1rem 0.4rem; border: 1px solid var(--line); border-radius: 0.3rem; }
  .team.active { border-color: var(--team); }
  .swatch { width: 0.6rem; height: 0.6rem; border-radius: 50%; background: var(--team); }
  .tag { font-size: 0.85em; color: var(--muted); text-transform: uppercase; letter-spacing: 0.04em; }
</style>
```

- [ ] **Step 5: Rewrite `App.svelte` and extend `app.css`**

```svelte
<!-- viewer/src/App.svelte -->
<script lang="ts">
  import Board from './lib/components/Board.svelte';
  import Timeline from './lib/components/Timeline.svelte';
  import LogPanel from './lib/components/LogPanel.svelte';
  import AskPanel from './lib/components/AskPanel.svelte';
  import RollPanel from './lib/components/RollPanel.svelte';
  import SourcePicker from './lib/components/SourcePicker.svelte';
  import Scoreboard from './lib/components/Scoreboard.svelte';
  import { sceneOf } from './lib/board/frame';
  import type { Hit } from './lib/board/pick';
  import { attachFeed, detachFeed, IDLE, type Feed, type FeedState } from './lib/feed/feed';
  import { recordingFeed } from './lib/feed/recordingFeed';
  import { fromUrl, recordingUrlFrom } from './lib/recording/source';
  import { EMPTY, frameOf, reduce, type Timeline as TimelineState, type TimelineAction } from './lib/timeline/timeline';

  let feed: Feed | null = $state(null);
  let unsubscribe: (() => void) | null = null;
  let state: FeedState = $state(IDLE);
  let timeline: TimelineState = $state(EMPTY);
  let error: string | null = $state(null);
  let selected: string | null = $state(null);
  let tab: 'log' | 'ask' | 'dice' | 'meta' = $state('log');
  let showGrid = $state(true), showZones = $state(true), showLabels = $state(true);

  function attach(next: Feed) {
    error = null;
    selected = null;
    // Set before subscribing: a feed emits its current state to a new listener synchronously.
    timeline = next.kind === 'live' ? { ...EMPTY, follow: true } : EMPTY;
    // attachFeed owns the order — old listener, then old feed, then the new subscription — and hands
    // back the unsubscribe function, which this component holds and passes back on the next swap.
    unsubscribe = attachFeed(feed, unsubscribe, next, (s) => {
      state = s;
      timeline = reduce(timeline, { type: 'frames', count: s.frames.length });
    });
    feed = next;
  }

  const dispatch = (action: TimelineAction) => { timeline = reduce(timeline, action); };
  const frame = $derived(frameOf(state.frames, timeline));
  const scene = $derived(state.board !== null && frame !== null ? sceneOf(state.board, frame) : null);
  const teamLabels = $derived(new Map((state.meta?.teams ?? []).map((t) => [t.id, t.name])));
  const seqs = $derived(state.frames.map((f) => f.seq));
  const picked = $derived(scene?.models.find((m) => m.id === selected) ?? null);

  $effect(() => {
    const url = recordingUrlFrom(window.location.search);
    if (url !== null) fromUrl(url).then((s) => attach(recordingFeed(s))).catch((e: Error) => (error = e.message));
  });

  // Tearing the page down is the same discipline as swapping, and the same module owns it.
  $effect(() => () => {
    detachFeed(feed, unsubscribe);
    unsubscribe = null;
    feed = null;
  });

  function pick(hit: Hit | null) {
    selected = hit?.kind === 'model' ? hit.id : null;
  }
</script>

<header>
  <strong>Tabletop viewer</strong>
  <SourcePicker onFeed={attach} onError={(m) => (error = m)} />
  <span class="muted">
    {state.status === 'idle' ? 'no recording loaded' : `${state.label} · ${state.status} · ${state.frames.length} frames`}
  </span>
  {#if error !== null}<span class="error">{error}</span>{/if}
  <Scoreboard teams={state.meta?.teams ?? []} {frame} />
</header>

<div class="panes">
  <div class="left">
    {#if scene === null}
      <p class="muted pad">Load a recording to draw it.</p>
    {:else}
      <Board {scene} {selected} onPick={pick} {showGrid} {showZones} {showLabels} />
    {/if}
  </div>
  <aside class="right">
    <nav>
      {#each ['log', 'ask', 'dice', 'meta'] as const as name (name)}
        <button class:active={tab === name} onclick={() => (tab = name)}>{name}</button>
      {/each}
    </nav>
    <div class="tab">
      {#if tab === 'log'}
        <LogPanel frames={state.frames} index={timeline.index} onJump={(seq) => dispatch({ type: 'jump', seq, seqs })} />
      {:else if tab === 'ask'}
        <AskPanel asks={frame?.openAsks ?? []} {teamLabels} />
      {:else if tab === 'dice'}
        <RollPanel rolls={frame?.rolls ?? []} />
      {:else}
        <div class="pad">
          {#if state.meta !== null}
            <p>{state.meta.scenario} · seed {state.meta.seed} · {state.meta.driver}</p>
            <p class="muted">{state.meta.gameRules} · {state.meta.schedulerShape} · {state.meta.enforcement}</p>
            <p>outcome: <strong>{state.meta.outcome}</strong>{state.meta.outcomeDetail === undefined ? '' : ` — ${state.meta.outcomeDetail}`}</p>
            <p class="muted">{state.meta.rounds} round(s), last seq {state.meta.lastSeq}, {Math.round(state.meta.durationMs)} ms</p>
            {#each state.meta.teams as team (team.id)}<p>Team {team.ordinal}: {team.name}</p>{/each}
          {/if}
          {#if scene !== null}
            <p class="muted">{scene.offTable.length} model(s) off the table</p>
            {#each scene.offTable as m (m.id)}
              <p class="muted">{m.label} — {m.where}{m.containerLabel === undefined ? '' : ` ${m.containerLabel}`}</p>
            {/each}
            {#each scene.zones as z (z.id)}<p class="muted">zone {z.shape} → team {z.teamOrdinal}</p>{/each}
          {/if}
          <label><input type="checkbox" bind:checked={showGrid} /> grid</label>
          <label><input type="checkbox" bind:checked={showZones} /> zones</label>
          <label><input type="checkbox" bind:checked={showLabels} /> labels</label>
        </div>
      {/if}
    </div>
    {#if picked !== null}
      <div class="pad selected">
        <strong>{picked.label}</strong> <span class="muted">{picked.unitName}</span>
        <p class="muted">team {picked.teamOrdinal} · {picked.centre.x.toFixed(1)}, {picked.centre.y.toFixed(1)}
          · {picked.wounds} wound(s){picked.spellTokens > 0 ? ` · ${picked.spellTokens} token(s)` : ''}</p>
        {#if picked.conditions.length > 0}<p class="muted">{picked.conditions.join(', ')}</p>{/if}
      </div>
    {/if}
  </aside>
</div>

<Timeline {timeline} {dispatch} seq={frame?.seq ?? null} line={frame?.line ?? ''} />
```

Add to `viewer/src/app.css`:

```css
body { display: grid; grid-template-rows: auto 1fr auto; height: 100vh; }
header { display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap;
  padding: 0.4rem 0.6rem; border-bottom: 1px solid var(--line); }
.panes { display: grid; grid-template-columns: 1fr 24rem; min-height: 0; }
.left { min-width: 0; }
.right { display: grid; grid-template-rows: auto 1fr auto; border-left: 1px solid var(--line); min-height: 0; }
.right nav { display: flex; gap: 0.25rem; padding: 0.3rem; border-bottom: 1px solid var(--line); }
.right nav button.active { color: #fff; border-color: var(--muted); }
.right .tab { overflow: auto; min-height: 0; }
.pad { padding: 0.4rem 0.5rem; }
.selected { border-top: 1px solid var(--line); }
.error { color: #ff6b5c; }
p { margin: 0.15rem 0; }
```

- [ ] **Step 6: Run everything, then look at it**

```bash
cd /home/cstupi/src/tabletop
dotnet run --project sim -- terrain --seeds 1..1        # writes ./recordings/terrain-1.json
cd viewer && npm test && npm run check && npm run build
npm run dev
```
Then open `http://localhost:5174/?recording=/dev/null` to see the error path, and load `recordings/terrain-1.json` with the file input. Expected: 4 more tests passed; the board draws the table, its deployment zones as dashed team-coloured outlines, the terrain outlines with their declarations, the models coloured by team with facing ticks; the header scoreboard shows the round and each team's score, and marks the active team and any team that has passed; play, step and scrub move the frame; clicking a log line jumps to it; the Ask tab shows the open Ask at frames that have one.

- [ ] **Step 7: Commit**

```bash
cd /home/cstupi/src/tabletop
git add viewer/src viewer/tests/feed.test.ts
git commit -m "Play a recording end to end behind a feed abstraction with an empty control seam"
```
