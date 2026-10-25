<!-- viewer/src/lib/components/SourcePicker.svelte -->
<script lang="ts">
  import { untrack } from 'svelte';
  import { fromFile, fromUrl } from '../recording/source';
  import type { Feed } from '../feed/feed';
  import { recordingFeed } from '../feed/recordingFeed';
  import { liveFeed } from '../feed/liveFeed';
  import { listGames, rowOf, type GameRow } from '../live/games';
  import type { Auth } from '../shell/auth';
  import { watchUrl } from '../shell/route';

  let { onFeed, onError, socketUrl, auth }: {
    onFeed: (feed: Feed) => void;
    onError: (message: string) => void;
    socketUrl: string;
    auth: Auth;
  } = $props();
  let url = $state('');

  async function load(work: () => Promise<Feed>) {
    try { onFeed(await work()); } catch (error) { onError((error as Error).message); }
  }

  let games: GameRow[] = $state([]);
  let open = $state(false);
  let loaded = false;

  async function run(work: () => Promise<void>) {
    try { await work(); } catch (error) { onError((error as Error).message); }
  }

  const reload = () => run(async () => { games = (await listGames(await auth.jwt())).map(rowOf); });

  // The website's session is already there, so the panel opens with the list loaded.
  $effect(() => {
    if (!loaded) { loaded = true; open = true; untrack(reload); }
  });

  // A watcher's socket: the same URL with watch=1, so it never replaces the account's play socket.
  const watch = (game: GameRow) => run(async () => {
    onFeed(liveFeed({ url: watchUrl(socketUrl), gameId: game.id, sessionJwt: await auth.jwt(), label: `${game.name} · ${game.status}` }));
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
  <details class="live" bind:open>
    <summary>Games</summary>
      <button onclick={reload}>Refresh</button>
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
  </details>
</div>

<style>
  .picker { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; padding: 0.3rem 0.6rem; }
  .game { cursor: pointer; }
  .game:hover { text-decoration: underline; }
  td, th { padding: 0 0.5rem; text-align: left; }
</style>
