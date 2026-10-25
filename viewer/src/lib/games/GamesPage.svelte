<!-- viewer/src/lib/games/GamesPage.svelte -->
<!-- HomeScreen's list: the account's games (open, replay, leave, end, delete), the open games to join, and Create game. -->
<script lang="ts">
  import CreateGameDialog from './CreateGameDialog.svelte';
  import { apiOf } from '../api/client';
  import { listGames, rowOf, type GameSummary } from '../live/games';
  import type { Auth } from '../shell/auth';
  import { hrefOf } from '../shell/route';
  import { listsOf } from './listModel';
  import { rowActionsOf } from './rowActions';

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

<div class="games pad">
  <div class="bar">
    <button onclick={() => (creating = true)}>Create game</button>
    <button onclick={reload}>Refresh</button>
    {#if admin}<label><input type="checkbox" bind:checked={showAll} /> All games</label>{/if}
  </div>
  {#if error !== null}<p class="error" role="alert">{error}</p>{/if}

  <h2>My games</h2>
  {#if !loaded}
    <p class="muted">Loading games…</p>
  {:else}
    <table>
      <thead><tr><th>Name</th><th>Status</th><th>Players</th><th>Created (UTC)</th><th></th></tr></thead>
      <tbody>
        {#each lists.mine as game (game.id)}
          {@const row = rowOf(game)}
          {@const can = rowActionsOf(game, me)}
          <tr>
            <td>{row.name}</td><td>{row.status}</td><td>{row.players}</td><td>{row.created}</td>
            <td class="acts">
              <button disabled={busy !== null} onclick={() => open(game)}>Open</button>
              {#if can.canReplay}
                <a class="button" href={hrefOf({ page: 'replay', gameId: game.id })}
                  title="Replay the selected game from its start; nothing you do there changes it">Replay</a>
              {:else}
                <button disabled title="Replay a game you are in once it is playing or ended">Replay</button>
              {/if}
              <button disabled={busy !== null} onclick={() => leave(game)} title="Leave the game, giving up your seat">Leave</button>
              <button disabled={busy !== null || !can.canEnd} onclick={() => end(game)}
                title={can.canEnd ? 'End the selected game for everyone' : "Only the game's creator ends a game that has not ended"}>End</button>
              <button disabled={busy !== null || !can.canDelete} onclick={() => remove(game)}
                title={can.canDelete ? 'Delete the selected game and its log' : "Only the game's creator deletes it"}>Delete</button>
            </td>
          </tr>
        {:else}
          <tr><td colspan="5" class="muted">No games yet. Create one with Create game.</td></tr>
        {/each}
      </tbody>
    </table>

    <h2>Open games</h2>
    <table>
      <thead><tr><th>Name</th><th>Status</th><th>Players</th><th>Created (UTC)</th><th></th></tr></thead>
      <tbody>
        {#each lists.open as game (game.id)}
          {@const row = rowOf(game)}
          <tr>
            <td>{row.name}</td><td>{row.status}</td><td>{row.players}</td><td>{row.created}</td>
            <td class="acts"><button disabled={busy !== null} onclick={() => open(game)} title="Join the game">Join</button></td>
          </tr>
        {:else}
          <tr><td colspan="5" class="muted">No open games to join.</td></tr>
        {/each}
      </tbody>
    </table>

    {#if admin && showAll}
      <h2>All games</h2>
      <table>
        <thead><tr><th>Name</th><th>Status</th><th>Players</th><th>Created (UTC)</th><th></th></tr></thead>
        <tbody>
          {#each games as game (game.id)}
            {@const row = rowOf(game)}
            <tr>
              <td>{row.name}</td><td>{row.status}</td><td>{row.players}</td><td>{row.created}</td>
              <td class="acts"><a class="button" href={hrefOf({ page: 'replay', gameId: game.id })}>Replay</a></td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  {/if}
</div>

{#if creating}
  <CreateGameDialog {api} onCreated={created} onClose={() => (creating = false)} />
{/if}

<dialog bind:this={confirmDialog} onclose={() => (confirming = null)} aria-labelledby="confirm-title">
  {#if confirming !== null}
    <h2 id="confirm-title">{confirming.title}</h2>
    <p>{confirming.text}</p>
    <div class="actions">
      <button onclick={confirmed}>{confirming.ok}</button>
      <button onclick={() => (confirming = null)}>Cancel</button>
    </div>
  {/if}
</dialog>

<style>
  h2 { font-size: 1rem; margin: 1rem 0 0.3rem; }
  .bar { display: flex; gap: 0.5rem; align-items: center; }
  table { border-collapse: collapse; width: 100%; }
  td, th { padding: 0.2rem 0.5rem; text-align: left; border-bottom: 1px solid var(--line); }
  .acts { display: flex; gap: 0.3rem; justify-content: flex-end; }
  a.button { font: inherit; padding: 0.2rem 0.45rem; border: 1px solid var(--line); border-radius: 3px; background: var(--panel); text-decoration: none; }
  dialog { background: var(--panel); color: var(--ink); border: 1px solid var(--line); max-width: 26rem; }
  dialog::backdrop { background: rgba(0, 0, 0, 0.5); }
  dialog h2 { margin-top: 0; }
  .actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
</style>
