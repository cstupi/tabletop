<!-- viewer/src/lib/games/GamesPage.svelte -->
<!-- HomeScreen's list: the account's games (open, replay, leave, end, delete), the open games to join, and Create game. -->
<script lang="ts">
  import CreateGameDialog from './CreateGameDialog.svelte';
  import { apiOf } from '../api/client';
  import { listGames, type GameSummary } from '../live/games';
  import type { Auth } from '../shell/auth';
  import { hrefOf } from '../shell/route';
  import { listsOf } from './listModel';
  import { rowActionsOf } from './rowActions';
  import { STATUS_LABEL, ago, mainActionOf, playersLine } from './present';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';

  let { auth }: { auth: Auth } = $props();

  const api = apiOf(() => auth.jwt());
  let games: GameSummary[] = $state([]);
  let loaded = $state(false);
  let me: string | null = $state(null);
  let error: string | null = $state(null);
  let busy: string | null = $state(null);
  let creating = $state(false);
  let showAll = $state(false);
  let confirming: { title: string; text: string; ok: string; run: () => Promise<void> } | null = $state(null);
  let confirmDialog: HTMLDialogElement | undefined = $state();

  const lists = $derived(listsOf(games));
  const admin = $derived(auth.access === 'admin');

  async function reload() {
    try {
      games = await listGames(await auth.jwt());
      me = (await auth.account()).id;
      error = null;
    } catch (e) { error = (e as Error).message; }
    loaded = true;
  }
  $effect(() => { void reload(); });

  $effect(() => {
    if (confirming !== null) confirmDialog?.showModal(); else confirmDialog?.close();
  });

  /** Runs one action on a row, then reloads the lists. */
  async function act(game: GameSummary, work: () => Promise<void>) {
    busy = game.id; error = null;
    try { await work(); } catch (e) { error = (e as Error).message; }
    busy = null;
    await reload();
  }

  const open = (game: GameSummary) => act(game, async () => {
    if (!game.joined) await api.joinGame(game.id);
    window.location.hash = hrefOf({ page: 'game', gameId: game.id });
  });

  function ask(title: string, text: string, ok: string, work: () => Promise<void>) {
    confirming = { title, text, ok, run: work };
  }
  const leave = (g: GameSummary) => ask('Leave this game?', `Leave ${g.name}? Your seat and the army you brought go.`, 'Leave', () => api.leaveGame(g.id));
  const end = (g: GameSummary) => ask('End this game?', `${g.name} ends for every player.`, 'End game', () => api.endGame(g.id));
  const remove = (g: GameSummary) => ask('Delete this game?',
    `${g.name} and its whole log are deleted for every player. This cannot be undone.`, 'Delete', () => api.deleteGame(g.id));

  async function confirmed() {
    const pending = confirming;
    confirming = null;
    if (pending !== null) await pending.run().catch((e: Error) => { error = e.message; });
    await reload();
  }

  function created(gameId: string, armyId: string | null) {
    creating = false;
    window.location.hash = hrefOf({ page: 'game', gameId, ...(armyId === null ? {} : { army: armyId }) });
  }
</script>

{#snippet row(game: GameSummary, joinable: boolean)}
  {@const can = rowActionsOf(game, me)}
  {@const main = mainActionOf(game)}
  <li class="row">
    <div class="what">
      <span class="name">{game.name}</span>
      <span class="meta">
        <span class="chip {game.status}">{STATUS_LABEL[game.status]}</span>
        <span>{playersLine(game.participantNames)}</span>
        <span class="dim">{ago(game.createdAt)}</span>
      </span>
    </div>
    <div class="acts">
      {#if joinable}
        <button class="small" disabled={busy !== null} onclick={() => open(game)}>Join</button>
      {:else if main.replay}
        <a class="button primary small" href={hrefOf({ page: 'replay', gameId: game.id })}><Icon name="replay" />Replay</a>
      {:else}
        <button class="primary small" disabled={busy !== null} onclick={() => open(game)}>{main.label}</button>
        {#if can.canReplay}<a class="button small" href={hrefOf({ page: 'replay', gameId: game.id })} title="Replay from the start">Replay</a>{/if}
      {/if}
      {#if !joinable}
        {#if can.canEnd}<button class="small" disabled={busy !== null} onclick={() => end(game)}>End</button>{/if}
        {#if can.canDelete}<button class="danger small" disabled={busy !== null} onclick={() => remove(game)}>Delete</button>
        {:else}<button class="quiet" disabled={busy !== null} onclick={() => leave(game)}>Leave</button>{/if}
      {/if}
    </div>
  </li>
{/snippet}

<div class="doc"><div class="inner">
  <div class="page-head">
    <h1>Play</h1>
    <div class="actions">
      <button class="primary" onclick={() => (creating = true)}><Icon name="plus" />Create game</button>
      <button onclick={reload}>Refresh</button>
    </div>
  </div>
  {#if error !== null}<p role="alert">{error}</p>{/if}

  {#if !loaded}
    <p class="loading" role="status">Loading games…</p>
  {:else}
    <section class="panel">
      <h2>Your games</h2>
      {#if lists.mine.length === 0}
        <div class="empty"><Icon name="objective" size="2rem" /><p>No games yet. Create one, or join an open game below.</p></div>
      {:else}
        <ul class="rows">{#each lists.mine as game (game.id)}{@render row(game, false)}{/each}</ul>
      {/if}
    </section>

    <section class="panel">
      <h2>Open games</h2>
      {#if lists.open.length === 0}
        <p class="dim">No games are waiting for players.</p>
      {:else}
        <ul class="rows">{#each lists.open as game (game.id)}{@render row(game, true)}{/each}</ul>
      {/if}
    </section>

    {#if admin}
      <section class="panel">
        <div class="head">
          <h2>All games</h2>
          <button class="small" aria-pressed={showAll} onclick={() => (showAll = !showAll)}>{showAll ? 'Hide' : 'Show'} ({games.length})</button>
        </div>
        {#if showAll}
          <ul class="rows">
            {#each games as game (game.id)}
              <li class="row">
                <div class="what">
                  <span class="name">{game.name}</span>
                  <span class="meta"><span class="chip {game.status}">{STATUS_LABEL[game.status]}</span><span>{playersLine(game.participantNames)}</span><span class="dim">{ago(game.createdAt)}</span></span>
                </div>
                <div class="acts"><a class="button small" href={hrefOf({ page: 'replay', gameId: game.id })}>Watch</a></div>
              </li>
            {/each}
          </ul>
        {/if}
      </section>
    {/if}
  {/if}
</div></div>

{#if creating}
  <CreateGameDialog {api} onCreated={created} onClose={() => (creating = false)} />
{/if}

<dialog bind:this={confirmDialog} onclose={() => (confirming = null)} aria-labelledby="confirm-title">
  {#if confirming !== null}
    <h2 id="confirm-title">{confirming.title}</h2>
    <p>{confirming.text}</p>
    <div class="actions">
      <button onclick={() => (confirming = null)}>Cancel</button>
      <button class={confirming.ok === 'Delete' ? 'danger' : 'primary'} onclick={confirmed}>{confirming.ok}</button>
    </div>
  {/if}
</dialog>

<style>
  .page-head h1 { margin-bottom: var(--s4); }
  .page-head .actions { margin: 0 0 var(--s4); }
  .head { display: flex; justify-content: space-between; align-items: center; gap: var(--s3); }
  .head h2 { margin: 0; }
  .rows { list-style: none; margin: 0; padding: 0; }
  .row { display: flex; align-items: center; justify-content: space-between; gap: var(--s4); padding: var(--s3) 0; border-top: 1px solid var(--rule); }
  .row:first-child { border-top: 0; }
  .what { display: flex; flex-direction: column; gap: var(--s1); min-width: 0; }
  .name { font-size: 1.1rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .meta { display: flex; flex-wrap: wrap; align-items: center; gap: var(--s1) var(--s3); color: var(--text); font-size: .95rem; }
  .acts { display: flex; flex-wrap: wrap; justify-content: flex-end; align-items: center; gap: var(--s2); flex: none; }
  dialog { max-width: 28rem; }
  dialog .actions { justify-content: flex-end; margin-bottom: 0; }
</style>
