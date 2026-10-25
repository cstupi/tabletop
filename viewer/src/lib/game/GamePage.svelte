<!-- viewer/src/lib/game/GamePage.svelte -->
<!-- One game over one GameConnection: the lobby, setup, or (from playing) the board, by the game's status. -->
<script lang="ts">
  import { untrack } from 'svelte';
  import { apiOf } from '../api/client';
  import PlaybackView from '../components/PlaybackView.svelte';
  import { connectGame, type ConnectionState, type GameConnection } from '../connection/gameConnection';
  import type { Feed } from '../feed/feed';
  import LobbyPanel from '../lobby/LobbyPanel.svelte';
  import { isCreator } from '../lobby/seats';
  import SetupPage from '../setup/SetupPage.svelte';
  import type { View } from '../board/transform';
  import type { Auth } from '../shell/auth';
  import { connectionFeed } from './connectionFeed';

  let { gameId, army, auth, socketUrl }: { gameId: string; army?: string | undefined; auth: Auth; socketUrl: string } = $props();

  const api = apiOf(() => auth.jwt());
  let connection: GameConnection | null = $state.raw(null);
  let snap = $state.raw<ConnectionState | null>(null);
  let feed: Feed | null = $state.raw(null);
  let view: View | null = $state.raw(null);

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
        void conn.sendEdit({ kind: 'pick_army', armyId: pick }).catch(() => {});
      }
    });
    return () => { stop(); feed?.close(); feed = null; conn.close(); connection = null; snap = null; };
  });

  const status = $derived(snap?.game?.status ?? null);
  $effect(() => {
    const inPlay = status === 'playing' || status === 'ended';
    untrack(() => {
      if (inPlay && feed === null && connection !== null) feed = connectionFeed(connection);
      if (!inPlay && feed !== null) { feed.close(); feed = null; }
    });
  });

  function leave() {
    void api.leaveGame(gameId).then(() => { window.location.hash = '#/games'; }).catch(() => {});
  }
</script>

<div class="game">
  <header class="bar pad">
    <a href="#/games">Games</a>
    <strong>{snap?.game?.name ?? 'Game'}</strong>
    <span class="muted">{status ?? snap?.phase ?? 'connecting'}</span>
    {#if snap !== null && snap.phase !== 'connected'}<span class="muted">{snap.phase}</span>{/if}
    {#if snap?.closeReason}<span class="error" role="alert">{snap.closeReason}</span>{/if}
  </header>

  {#if snap?.phase === 'closed'}
    <p class="pad">The game connection is closed. <a href="#/games">Back to the games list</a></p>
  {:else if connection !== null && snap !== null && snap.game !== null}
    {#if status === 'lobby'}
      <div class="side"><LobbyPanel {connection} {snap} {api} onLeave={leave} /></div>
    {:else if status === 'setup'}
      <div class="split">
        <details class="side">
          <summary class="pad">Lobby</summary>
          <LobbyPanel {connection} {snap} {api} onLeave={leave} />
        </details>
        <SetupPage {connection} {snap} {api} bind:view creator={isCreator(snap.game, snap.participantId)} />
      </div>
    {:else}
      <PlaybackView {feed} models="circles" />
    {/if}
  {:else}
    <p class="muted pad">Connecting to the game…</p>
  {/if}
</div>

<style>
  .game { display: grid; grid-template-rows: auto minmax(0, 1fr); height: 100%; min-height: 0; }
  .bar { display: flex; gap: 0.75rem; align-items: baseline; border-bottom: 1px solid var(--line); }
  .side { overflow: auto; max-width: 32rem; }
  .split { display: grid; grid-template-columns: auto minmax(0, 1fr); min-height: 0; }
  details.side { max-width: 22rem; border-right: 1px solid var(--line); }
</style>
