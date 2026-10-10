<!-- viewer/src/lib/components/TestsPage.svelte -->
<script lang="ts">
  import type { Feed } from '../feed/feed';
  import { liveFeed } from '../feed/liveFeed';
  import { recordingFeed } from '../feed/recordingFeed';
  import { fromUrl } from '../recording/source';
  import { fetchSuite, recordingUrl, runEvents, spectate, startRun, type SuiteGameResult } from '../suite/client';
  import {
    applyEvent, canRun, counts, coverageCell, failed, groupsOf, initial, requestOf, setDriver, setKeep, started, toggleGame, toggleGroup,
    type Page,
  } from '../suite/model';

  let { onFeed, onError }: { onFeed: (feed: Feed) => void; onError: (message: string) => void } = $props();

  let page: Page = $state.raw(initial([]));
  let loaded = $state(false);
  let loadError: string | null = $state(null);
  const socketUrl = import.meta.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws';

  const groups = $derived(groupsOf(page.games));
  const tally = $derived(counts(page));

  $effect(() => {
    fetchSuite().then((games) => { page = initial(games); loaded = true; })
      .catch((e: Error) => { loadError = e.message; loaded = true; });
  });

  async function run() {
    try {
      const id = await startRun(requestOf(page));
      page = started(page, id);
      for await (const event of runEvents(id)) page = applyEvent(page, event);
      if (page.status === 'running') page = failed(page, 'the event stream ended before the run finished');
    } catch (e) {
      page = failed(page, (e as Error).message);
    }
  }

  async function watch(r: SuiteGameResult) {
    try { onFeed(recordingFeed(await fromUrl(recordingUrl(page.runId!, r.name)))); }
    catch (e) { onError((e as Error).message); }
  }

  async function openLive(r: SuiteGameResult) {
    try {
      const { gameId, jwt } = await spectate(page.runId!, r.name);
      onFeed(liveFeed({ url: socketUrl, gameId, sessionJwt: jwt }));
    } catch (e) { onError((e as Error).message); }
  }
</script>

<div class="tests pad">
  {#if !loaded}
    <p class="muted">Loading the suite…</p>
  {:else if loadError !== null}
    <p class="error">{loadError}</p>
  {:else if page.games.length === 0}
    <p class="muted">The suite has no games yet.</p>
  {:else}
    <div class="bar">
      <button disabled={!canRun(page)} onclick={run}>{page.status === 'running' ? 'Running…' : 'Run'}</button>
      <span class="muted">{page.selected.size} of {page.games.length} selected</span>
      <label>driver
        <select value={page.driver} onchange={(e) => (page = setDriver(page, e.currentTarget.value as 'in-process' | 'socket'))}>
          <option value="in-process">in-process</option>
          <option value="socket">socket</option>
        </select>
      </label>
      <label><input type="checkbox" checked={page.keep} onchange={(e) => (page = setKeep(page, e.currentTarget.checked))} /> keep recordings</label>
      {#if page.status !== 'idle'}
        <span>{tally.passed} passed, {tally.failed} failed{page.status === 'done' ? (page.passed ? ' — PASS' : ' — FAIL') : ''}</span>
      {/if}
      {#if page.message !== null}<span class="error">{page.message}</span>{/if}
    </div>

    {#each groups as group (group.name)}
      <section>
        <h3>
          <label>
            <input type="checkbox" checked={group.games.every((g) => page.selected.has(g.name))}
              onchange={() => (page = toggleGroup(page, group.name))} />
            {group.name} <span class="muted">({group.games.length})</span>
          </label>
        </h3>
        {#each group.games as game (game.name)}
          {@const r = page.results.get(game.name)}
          <div class="game">
            <label>
              <input type="checkbox" checked={page.selected.has(game.name)} onchange={() => (page = toggleGame(page, game.name))} />
              {game.name}
            </label>
            {#if r !== undefined}
              <strong class={r.passed ? 'ok' : 'error'}>{r.passed ? 'passed' : 'failed'}</strong>
              {#if r.recordingPath !== null}<button onclick={() => watch(r)}>Watch</button>{/if}
              {#if r.gameId !== null && r.spectator !== null}<button onclick={() => openLive(r)}>Open live</button>{/if}
            {:else if page.status === 'running' && page.selected.has(game.name)}
              <span class="muted">waiting</span>
            {/if}
            {#if game.covers.length > 0}<div class="muted covers">covers {game.covers.join(', ')}</div>{/if}
            {#if r !== undefined && !r.passed}
              {#each r.failures as f (f)}<div class="error covers">{f}</div>{/each}
              <div class="muted covers">replay: {r.replay}</div>
            {/if}
          </div>
        {/each}
      </section>
    {/each}

    {#if page.coverage.length > 0}
      <h3>Coverage</h3>
      <table>
        <thead><tr><th>entry</th><th>kind</th><th>fired</th><th>games</th><th>exact checks</th></tr></thead>
        <tbody>
          {#each page.coverage as c (c.kind + c.name)}
            {@const cell = coverageCell(c)}
            <tr>
              <td>{c.name}</td><td class="muted">{c.kind}</td>
              <td class={cell.tone}>{cell.text}</td>
              <td class="muted">{c.games.join(', ')}</td>
              <td>{#each c.exact as x (x.game + x.check)}<div class={x.passed ? 'ok' : 'error'}>{x.game}: {x.check}</div>{/each}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  {/if}
</div>

<style>
  .tests { overflow: auto; height: 100%; }
  .bar { display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap; margin-bottom: 0.5rem; }
  h3 { margin: 0.8rem 0 0.2rem; }
  .game { padding: 0.2rem 0 0.2rem 1.2rem; border-bottom: 1px solid var(--line); }
  .covers { padding-left: 1.4rem; }
  .ok { color: #6fcf7f; }
  table { border-collapse: collapse; }
  th, td { text-align: left; padding: 0.15rem 0.8rem 0.15rem 0; vertical-align: top; border-bottom: 1px solid var(--line); }
</style>
