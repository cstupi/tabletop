<!-- viewer/src/lib/components/SourcePicker.svelte -->
<script lang="ts">
  import { fromFile, fromUrl } from '../recording/source';
  import type { Feed } from '../feed/feed';
  import { recordingFeed } from '../feed/recordingFeed';
  import { liveFeed } from '../feed/liveFeed';
  import { logIn, refreshSession } from '../live/management';
  import { listGames, rowOf, type GameRow } from '../live/games';

  let { onFeed, onError }: { onFeed: (feed: Feed) => void; onError: (message: string) => void } = $props();
  let url = $state('');

  async function load(work: () => Promise<Feed>) {
    try { onFeed(await work()); } catch (error) { onError((error as Error).message); }
  }

  let email = $state('');
  let password = $state('');
  let pasted = $state('');
  // The login, kept for the page's session. A JWT lasts five minutes, so each use refreshes it first.
  let session: string | null = $state(null);
  let games: GameRow[] = $state([]);
  const socketUrl = import.meta.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws';

  async function fresh(): Promise<string> {
    if (session === null) throw new Error('not logged in');
    session = (await refreshSession(session)).sessionJwt;
    return session;
  }

  async function run(work: () => Promise<void>) {
    try { await work(); } catch (error) { onError((error as Error).message); }
  }

  const login = () => run(async () => {
    session = pasted.length > 0 ? pasted : (await logIn(email, password)).sessionJwt;
    password = '';
    games = (await listGames(await fresh())).map(rowOf);
  });

  const reload = () => run(async () => { games = (await listGames(await fresh())).map(rowOf); });

  const watch = (game: GameRow) => run(async () => {
    onFeed(liveFeed({ url: socketUrl, gameId: game.id, sessionJwt: await fresh(), label: `${game.name} · ${game.status}` }));
  });
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
  <details class="live">
    <summary>Games</summary>
    {#if session === null}
      <p class="muted">Log in as an admin to watch any game on {socketUrl}; the viewer only watches.</p>
      <input type="email" placeholder="email" bind:value={email} />
      <input type="password" placeholder="password" bind:value={password} />
      <input type="text" placeholder="or a session JWT" bind:value={pasted} style="width: 18rem" />
      <button disabled={pasted.length === 0 && email.length === 0} onclick={login}>Log in</button>
    {:else}
      <button onclick={reload}>Refresh</button>
      <button onclick={() => { session = null; games = []; }}>Log out</button>
      <table>
        <thead><tr><th>Name</th><th>Status</th><th>Players</th><th>Created (UTC)</th></tr></thead>
        <tbody>
          {#each games as game (game.id)}
            <tr class="game" onclick={() => watch(game)}>
              <td>{game.name}</td><td>{game.status}</td><td>{game.players}</td><td>{game.created}</td>
            </tr>
          {:else}
            <tr><td colspan="4" class="muted">no games</td></tr>
          {/each}
        </tbody>
      </table>
    {/if}
  </details>
</div>

<style>
  .picker { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }
  .game { cursor: pointer; }
  .game:hover { text-decoration: underline; }
  td, th { padding: 0 0.5rem; text-align: left; }
</style>
