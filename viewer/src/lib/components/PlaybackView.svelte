<!-- viewer/src/lib/components/PlaybackView.svelte -->
<!-- A Feed played back: the board, the log tabs and the timeline. Playback and play are siblings (spec → Where). -->
<script lang="ts">
  import { untrack } from 'svelte';
  import Board from './Board.svelte';
  import Timeline from './Timeline.svelte';
  import LogPanel from './LogPanel.svelte';
  import AskPanel from './AskPanel.svelte';
  import RollPanel from './RollPanel.svelte';
  import Scoreboard from './Scoreboard.svelte';
  import { sceneOf } from '../board/frame';
  import type { Hit } from '../board/pick';
  import { attachFeed, detachFeed, IDLE, type Feed, type FeedState } from '../feed/feed';
  import { EMPTY, frameOf, reduce, type Timeline as TimelineState, type TimelineAction } from '../timeline/timeline';

  let { feed, models = 'bases' }: { feed: Feed | null; models?: 'bases' | 'circles' } = $props();

  let current: Feed | null = null;
  let unsubscribe: (() => void) | null = null;
  let feedState: FeedState = $state.raw(IDLE);
  let timeline: TimelineState = $state(EMPTY);
  let selected: string | null = $state(null);
  let tab: 'log' | 'ask' | 'dice' | 'meta' = $state('log');
  let showGrid = $state(true), showZones = $state(true), showLabels = $state(true);

  // A new feed replaces the old one through attachFeed, which owns the order: old listener, old feed, new subscription.
  $effect(() => {
    const next = feed;
    untrack(() => swap(next));
  });

  function swap(next: Feed | null) {
    if (next === current) return;
    selected = null;
    // Set before subscribing: a feed emits its current state to a new listener synchronously.
    timeline = next?.kind === 'live' ? { ...EMPTY, follow: true } : EMPTY;
    if (next === null) {
      detachFeed(current, unsubscribe);
      unsubscribe = null;
      feedState = IDLE;
    } else {
      unsubscribe = attachFeed(current, unsubscribe, next, (s) => {
        feedState = s;
        timeline = reduce(timeline, { type: 'frames', count: s.frames.length });
      });
    }
    current = next;
  }

  // Leaving the page is the same discipline as swapping.
  $effect(() => () => {
    detachFeed(current, unsubscribe);
    unsubscribe = null;
    current = null;
  });

  const dispatch = (action: TimelineAction) => { timeline = reduce(timeline, action); };
  const frame = $derived(frameOf(feedState.frames, timeline));
  const scene = $derived(feedState.board !== null && frame !== null ? sceneOf(feedState.board, frame) : null);
  const teamLabels = $derived(new Map((feedState.meta?.teams ?? []).map((t) => [t.id, t.name])));
  const seqs = $derived(feedState.frames.map((f) => f.seq));
  const picked = $derived(scene?.models.find((m) => m.id === selected) ?? null);

  function pick(hit: Hit | null) {
    selected = hit?.kind === 'model' ? hit.id : null;
  }
</script>

<section class="playback">
  <div class="status">
    <span class="muted">
      {feedState.status === 'idle' ? 'no recording loaded' : `${feedState.label} · ${feedState.status} · ${feedState.frames.length} frames`}
    </span>
    {#if feedState.detail !== null}<span class="error">{feedState.detail}</span>{/if}
    <Scoreboard teams={feedState.meta?.teams ?? []} {frame} />
  </div>

  <div class="panes">
    <div class="left">
      {#if scene === null}
        <p class="muted pad">Load a recording to draw it.</p>
      {:else}
        <Board {scene} selected={new Set(selected === null ? [] : [selected])} interaction={{ onSelect: (hit) => pick(hit) }}
          {models} {showGrid} {showZones} {showLabels} />
      {/if}
    </div>
    <aside class="right">
      <nav class="seg" aria-label="Panel">
        {#each ['log', 'ask', 'dice', 'meta'] as const as name (name)}
          <button class="small" aria-pressed={tab === name} onclick={() => (tab = name)}>{name}</button>
        {/each}
      </nav>
      <div class="tab">
        {#if tab === 'log'}
          <LogPanel frames={feedState.frames} index={timeline.index} onJump={(seq) => dispatch({ type: 'jump', seq, seqs })} />
        {:else if tab === 'ask'}
          <AskPanel asks={frame?.openAsks ?? []} {teamLabels} />
        {:else if tab === 'dice'}
          <RollPanel rolls={frame?.rolls ?? []} />
        {:else}
          <div class="pad">
            {#if feedState.meta !== null}
              <p>{feedState.meta.scenario} · seed {feedState.meta.seed} · {feedState.meta.driver}</p>
              <p class="muted">{feedState.meta.gameRules} · {feedState.meta.schedulerShape} · {feedState.meta.enforcement}</p>
              <p>outcome: <strong>{feedState.meta.outcome}</strong>{feedState.meta.outcomeDetail === undefined ? '' : ` — ${feedState.meta.outcomeDetail}`}</p>
              <p class="muted">{feedState.meta.rounds} round(s), last seq {feedState.meta.lastSeq}, {Math.round(feedState.meta.durationMs)} ms</p>
              {#each feedState.meta.teams as team (team.id)}<p>Team {team.ordinal}: {team.name}</p>{/each}
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
</section>

<style>
  .playback { display: grid; grid-template-rows: auto minmax(0, 1fr) auto; height: 100%; min-height: 0; }
  .status {
    display: flex; gap: var(--s3); align-items: center; flex-wrap: wrap; padding: var(--s2) var(--s4);
    border-bottom: 1px solid var(--rule); background: var(--panel-solid);
  }
  .panes { display: grid; grid-template-columns: minmax(0, 1fr) 24rem; min-height: 0; }
  .left { position: relative; min-width: 0; min-height: 0; }
  .right {
    display: flex; flex-direction: column; gap: var(--s2); min-height: 0; padding: var(--s3);
    border-left: 1px solid var(--rule); background: var(--panel-solid);
  }
  .tab { flex: 1; min-height: 0; overflow: auto; }
  .pad { padding: var(--s3); margin: 0; }
  .pad p { margin: 0 0 var(--s1); }
  .selected { border-top: 1px solid var(--rule); flex: none; }
</style>
