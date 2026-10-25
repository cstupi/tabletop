<!-- viewer/src/lib/game/GamePage.svelte -->
<!-- One game over one GameConnection: a header strip (back to the games, the name and status, Leave, setup's End setup), then the lobby,
     setup, or (from playing) the board, by the game's status. -->
<script lang="ts">
  import { untrack } from 'svelte';
  import { apiOf } from '../api/client';
  import { connectGame, type ConnectionState, type GameConnection } from '../connection/gameConnection';
  import LobbyPage from '../lobby/LobbyPage.svelte';
  import { armyFromCreate } from '../lobby/lobbyEdits';
  import { isCreator } from '../lobby/seats';
  import PlayPage from '../play/PlayPage.svelte';
  import SetupPage from '../setup/SetupPage.svelte';
  import type { View } from '../board/transform';
  import type { Auth } from '../shell/auth';
  import Confirm, { type Question } from './Confirm.svelte';
  import { headerOf } from './header';
  import { sendEdit } from './send';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';

  let { gameId, army, auth, socketUrl }: { gameId: string; army?: string | undefined; auth: Auth; socketUrl: string } = $props();

  const api = apiOf(() => auth.jwt());
  let connection: GameConnection | null = $state.raw(null);
  let snap = $state.raw<ConnectionState | null>(null);
  let view: View | null = $state.raw(null);
  let asking: Question | null = $state(null);
  let problem: string | null = $state(null);

  $effect(() => {
    const id = gameId;
    const pick = untrack(() => army);
    const conn = connectGame({ url: socketUrl, gameId: id, jwt: () => auth.jwt() });
    connection = conn;
    let armySent = false;
    const stop = conn.subscribe((s) => {
      snap = s;
      // The creator's link carries the army chosen in Create game: it is picked once, after the first welcome.
      if (!armySent && pick !== undefined && s.phase === 'connected' && s.game !== null) {
        armySent = true;
        void conn.sendEdit(armyFromCreate(pick)).catch(() => {});
      }
    });
    return () => { stop(); conn.close(); connection = null; snap = null; };
  });

  const status = $derived(snap?.game?.status ?? null);
  const creator = $derived(snap?.game ? isCreator(snap.game, snap.participantId) : false);
  const strip = $derived(headerOf(snap, creator));

  function leave() {
    const name = snap?.game?.name ?? 'this game';
    asking = {
      title: 'Leave this game?', text: `Leave ${name}? Your seat and the army you brought go.`, ok: 'Leave game', danger: true,
      run: () => api.leaveGame(gameId).then(() => { window.location.hash = '#/games'; }).catch((e: Error) => { problem = e.message; }),
    };
  }

  async function primary() {
    if (connection !== null && strip.primary !== null) problem = await sendEdit(connection, strip.primary.edit);
  }
</script>

<div class="game">
  <header class="strip">
    <a class="back" href="#/games" aria-label="Back to the games"><Icon name="back" />Games</a>
    <span class="sep" aria-hidden="true"></span>
    <h1 class="title" title={strip.title}>{strip.title}</h1>
    {#if strip.status !== null}<span class="chip {strip.status}">{strip.statusLabel}</span>{/if}
    {#if strip.connection !== null}<span class="conn" role="status">{strip.connection}</span>{/if}
    <span class="push"></span>
    {#if strip.waiting !== null}<span class="waiting">{strip.waiting}</span>{/if}
    {#if strip.canLeave}<button class="small" onclick={leave}>Leave game</button>{/if}
    {#if strip.primary !== null}<button class="primary small" onclick={primary}>{strip.primary.label}</button>{/if}
  </header>
  {#if snap?.closeReason && snap.phase !== 'connected'}<p role="alert" class="strip-alert">{snap.closeReason}</p>{/if}
  {#if problem !== null}<p role="alert" class="strip-alert">{problem} <button class="quiet" onclick={() => (problem = null)}>Dismiss</button></p>{/if}

  {#if snap?.phase === 'closed'}
    <div class="center"><div class="panel empty">
      <p>The connection to this game is closed.</p>
      <a class="button primary" href="#/games">Back to the games</a>
    </div></div>
  {:else if connection !== null && snap !== null && snap.game !== null}
    {#if status === 'lobby'}
      <LobbyPage {connection} {snap} {api} />
    {:else if status === 'setup'}
      <SetupPage {connection} {snap} {api} bind:view {creator} />
    {:else}
      <PlayPage {connection} {api} bind:view />
    {/if}
  {:else}
    <div class="center"><p class="loading" role="status">Connecting to the game…</p></div>
  {/if}
</div>

<Confirm bind:ask={asking} />

<style>
  .game { display: flex; flex-direction: column; height: 100%; min-height: 0; }
  .game > :global(:last-child) { flex: 1; min-height: 0; }
  .strip { flex: none; display: flex; align-items: center; gap: var(--s3); padding: var(--s2) var(--s4); min-height: 3.25rem;
    background: var(--panel-solid); border-bottom: 1px solid var(--rule); }
  .back { display: inline-flex; align-items: center; gap: var(--s1); text-decoration: none; white-space: nowrap; }
  .sep { width: 1px; align-self: stretch; margin: var(--s1) 0; background: var(--rule); }
  .title { margin: 0; font-size: 1.05rem; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .title::after { display: none; }
  .conn { color: var(--warning); font-size: .9rem; white-space: nowrap; }
  .push { flex: 1; }
  .waiting { color: var(--dim); font-size: .95rem; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .strip-alert { flex: none; margin: 0; }
  .center { display: grid; place-items: center; padding: var(--s5); }
  .center .panel { max-width: 30rem; width: 100%; }
</style>
