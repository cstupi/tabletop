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
  let feedState: FeedState = $state(IDLE);
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
      feedState = s;
      timeline = reduce(timeline, { type: 'frames', count: s.frames.length });
    });
    feed = next;
  }

  const dispatch = (action: TimelineAction) => { timeline = reduce(timeline, action); };
  const frame = $derived(frameOf(feedState.frames, timeline));
  const scene = $derived(feedState.board !== null && frame !== null ? sceneOf(feedState.board, frame) : null);
  const teamLabels = $derived(new Map((feedState.meta?.teams ?? []).map((t) => [t.id, t.name])));
  const seqs = $derived(feedState.frames.map((f) => f.seq));
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
    {feedState.status === 'idle' ? 'no recording loaded' : `${feedState.label} · ${feedState.status} · ${feedState.frames.length} frames`}
  </span>
  {#if error !== null}<span class="error">{error}</span>{/if}
  {#if feedState.detail !== null}<span class="error">{feedState.detail}</span>{/if}
  <Scoreboard teams={feedState.meta?.teams ?? []} {frame} />
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
