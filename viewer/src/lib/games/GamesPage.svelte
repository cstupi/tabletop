<!-- viewer/src/lib/games/GamesPage.svelte -->
<!-- HomeScreen (UX redesign part 1 → Home, homeView.ts): my games, those ended in the last seven days among them, as cards banded in their
     status colour with the state line, the primary action and a ▸ menu; my older ended games folded until Ended too; the open games as rows
     with Join; New game, the name filter and refresh. -->
<script lang="ts">
  import CreateGameDialog from './CreateGameDialog.svelte';
  import Loading from '../components/Loading.svelte';
  import { apiOf } from '../api/client';
  import { listGames, type GameSummary } from '../live/games';
  import type { Auth } from '../shell/auth';
  import { hrefOf } from '../shell/route';
  import { ACTION_LABEL, homeListsOf, type HomeAction, type HomeCard } from './homeView';
  import { STATUS_LABEL, ago } from './present';
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
  let search = $state('');
  let endedToo = $state(false);
  let confirming: { title: string; text: string; ok: string; run: () => Promise<void> } | null = $state(null);
  let confirmDialog: HTMLDialogElement | undefined = $state();

  const lists = $derived(homeListsOf(games, me, search, endedToo));
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
    const onFocus = () => { if (loaded && busy === null) void reload(); };
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
  });

  $effect(() => {
    if (confirming !== null) confirmDialog?.showModal(); else confirmDialog?.close();
  });

  /** Runs one action on a game, then reloads the lists. */
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

  function run(card: HomeCard, action: HomeAction) {
    const g = card.game;
    switch (action) {
      case 'replay': window.location.hash = hrefOf({ page: 'replay', gameId: g.id }); break;
      case 'leave': ask('Leave this game?', `Leave ${g.name}? Your seat and the army you brought go.`, 'Leave', () => api.leaveGame(g.id)); break;
      case 'end': ask('End this game?', `${g.name} ends for every player.`, 'End game', () => api.endGame(g.id)); break;
      case 'delete': ask('Delete this game?', `${g.name} and its whole log are deleted for every player. This cannot be undone.`, 'Delete', () => api.deleteGame(g.id)); break;
      default: void open(g);
    }
  }

  function picked(card: HomeCard, event: Event) {
    const select = event.currentTarget as HTMLSelectElement;
    const action = select.value as HomeAction | '';
    select.value = '';
    if (action !== '') run(card, action);
  }

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

{#snippet status(g: GameSummary, edition: string)}
  <span class="status {g.status}"><span class="mark" aria-hidden="true"></span>{STATUS_LABEL[g.status].toUpperCase()}{#if edition !== ''}<span class="sep">·</span>{edition}{/if}</span>
{/snippet}

{#snippet players(c: HomeCard)}
  <span class="players" title={(c.game.participantNames ?? []).join(', ')}>
    <Icon name="account" size="1em" /><span class="names">{c.players.length === 0 ? 'No players' : c.players.join('   ')}</span>
    {#if c.morePlayers > 0}<span class="dim">+{c.morePlayers}</span>{/if}
  </span>
{/snippet}

{#snippet menu(c: HomeCard)}
  {#if c.menu.length > 0}
    <select class="menu" aria-label="More" title="More" disabled={busy !== null} onchange={(e) => picked(c, e)}>
      <option value="">▸</option>
      {#each c.menu as a (a)}<option value={a}>{ACTION_LABEL[a]}</option>{/each}
    </select>
  {/if}
{/snippet}

{#snippet primary(c: HomeCard, filled: boolean)}
  <button class="small {filled ? 'primary' : ''}" disabled={busy !== null} onclick={() => run(c, c.primary)}>
    {#if c.primary === 'replay'}<Icon name="replay" />{:else if c.primary === 'join'}<Icon name="plus" />{/if}{ACTION_LABEL[c.primary]}
  </button>
{/snippet}

{#snippet row(c: HomeCard)}
  <li class="row band-{c.game.status}">
    {@render status(c.game, c.edition)}
    <span class="name" title={c.game.name}>{c.game.name}</span>
    {@render players(c)}
    <span class="when dim">{c.game.status === 'ended' ? c.state : ago(c.game.createdAt)}</span>
    {@render primary(c, false)}
    {@render menu(c)}
  </li>
{/snippet}

<div class="doc"><div class="inner">
  <div class="toolbar">
    <button class="primary" onclick={() => (creating = true)}><Icon name="plus" />New game</button>
    <span class="grow"></span>
    <input type="search" placeholder="name" aria-label="Filter by name" bind:value={search} />
    <button class="quiet" title="Refresh the list" aria-label="Refresh" onclick={reload}><Icon name="replay" /></button>
    <label class="check"><input type="checkbox" bind:checked={endedToo} />Ended too</label>
  </div>
  {#if error !== null}<p role="alert">{error}</p>{/if}

  {#if !loaded}
    <Loading text="Loading games…" inline />
  {:else}
    <h2>Your games</h2>
    {#if lists.yours.length === 0}
      <p class="dim">{search !== '' ? 'No game of yours matches' : lists.hasAny ? 'None in play' : 'No games yet'}</p>
    {:else}
      <ul class="cards">
        {#each lists.yours as c (c.game.id)}
          <li class="card band-{c.game.status}">
            {@render status(c.game, c.edition)}
            <span class="title" title={c.game.name}>{c.game.name}</span>
            {@render players(c)}
            <span class="state" class:warn={c.stateWarns}>{c.state}</span>
            <div class="foot">{@render primary(c, true)}<span class="grow"></span>{@render menu(c)}</div>
          </li>
        {/each}
      </ul>
    {/if}

    {#if endedToo}
      <h2>Ended</h2>
      {#if lists.ended.length === 0}
        <p class="dim">None</p>
      {:else}
        <ul class="rows">{#each lists.ended as c (c.game.id)}{@render row(c)}{/each}</ul>
      {/if}
    {/if}

    <h2>Open games</h2>
    {#if lists.open.length === 0}
      <p class="dim">None</p>
    {:else}
      <ul class="rows">{#each lists.open as c (c.game.id)}{@render row(c)}{/each}</ul>
    {/if}

    {#if admin}
      <section class="panel all">
        <div class="head">
          <h2>All games</h2>
          <button class="small" aria-pressed={showAll} onclick={() => (showAll = !showAll)}>{showAll ? 'Hide' : 'Show'} ({games.length})</button>
        </div>
        {#if showAll}
          <ul class="rows">
            {#each games as game (game.id)}
              <li class="row band-{game.status}">
                {@render status(game, '')}
                <span class="name">{game.name}</span>
                <span class="when dim">{ago(game.createdAt)}</span>
                <a class="button small" href={hrefOf({ page: 'replay', gameId: game.id })}>Watch</a>
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
  .inner { --lobby: var(--dim); --setup: var(--warning); --playing: var(--accent); --ended: var(--dim); }
  .toolbar { display: flex; align-items: center; gap: var(--s3); margin-bottom: var(--s5); flex-wrap: wrap; }
  .toolbar input[type='search'] { width: 14rem; }
  .grow { flex: 1; }
  .check { display: inline-flex; align-items: center; gap: var(--s2); white-space: nowrap; }
  h2 { margin-top: var(--s5); letter-spacing: .06em; text-transform: uppercase; font-size: .9rem; }

  .band-lobby { --band: var(--lobby); } .band-setup { --band: var(--setup); } .band-playing { --band: var(--playing); } .band-ended { --band: var(--ended); }
  .status { display: inline-flex; align-items: center; gap: var(--s2); color: var(--band); font-size: .85rem; letter-spacing: .04em; white-space: nowrap; }
  .status .sep { opacity: .7; }
  .mark { width: .8rem; height: .8rem; border-radius: 50%; border: 2px solid var(--band); box-sizing: border-box; flex: none; }
  .status.setup .mark { background: linear-gradient(90deg, transparent 50%, var(--band) 50%); }
  .status.playing .mark { background: var(--band); }
  .status.ended .mark { border: 0; border-radius: 0; width: .8rem; height: .45rem; border-left: 2px solid var(--band); border-bottom: 2px solid var(--band); transform: rotate(-45deg) translate(1px, -1px); }

  .cards { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr)); gap: var(--s4); }
  .card { display: flex; flex-direction: column; gap: var(--s2); min-width: 0; padding: var(--s4) var(--s4) var(--s4) calc(var(--s4) + 6px);
    background: var(--panel-solid); border: 1px solid color-mix(in srgb, var(--band) 55%, transparent); border-left: 6px solid var(--band); }
  .title { font-family: var(--font-display); font-weight: var(--display-weight); letter-spacing: var(--display-tracking); font-size: 1.05rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .players { display: flex; align-items: center; gap: var(--s2); min-width: 0; }
  .players .names { overflow: hidden; text-overflow: ellipsis; white-space: pre; min-width: 0; }
  .state { color: var(--dim); }
  .state.warn { color: var(--warning); }
  .foot { display: flex; align-items: center; gap: var(--s2); margin-top: var(--s1); }

  .rows { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--s2); }
  .row { display: grid; grid-template-columns: 9.5rem minmax(8rem, 2fr) minmax(6rem, 2fr) 7.5rem auto 2.75rem; align-items: center; gap: var(--s3);
    padding: var(--s2) var(--s3); background: var(--panel-solid); border-left: 4px solid var(--band); }
  .row .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .row .when { text-align: right; white-space: nowrap; }
  .menu { width: 2.75rem; min-height: 2rem; padding: 0 var(--s2); }
  .all { margin-top: var(--s6); }
  .all .row { grid-template-columns: 9.5rem 1fr 7.5rem auto; }
  .head { display: flex; justify-content: space-between; align-items: center; gap: var(--s3); }
  .head h2 { margin: 0; }
  dialog { max-width: 28rem; }
  dialog .actions { justify-content: flex-end; margin-bottom: 0; }
  @media (max-width: 40rem) {
    .row { grid-template-columns: 1fr auto; }
    .row .status, .row .players, .row .when { grid-column: 1 / -1; }
  }
</style>
