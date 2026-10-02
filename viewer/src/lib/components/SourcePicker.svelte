<!-- viewer/src/lib/components/SourcePicker.svelte -->
<script lang="ts">
  import { fromFile, fromUrl } from '../recording/source';
  import type { Feed } from '../feed/feed';
  import { recordingFeed } from '../feed/recordingFeed';
  import { liveFeed } from '../feed/liveFeed';
  import { logIn } from '../live/management';

  let { onFeed, onError }: { onFeed: (feed: Feed) => void; onError: (message: string) => void } = $props();
  let url = $state('');

  async function load(work: () => Promise<Feed>) {
    try { onFeed(await work()); } catch (error) { onError((error as Error).message); }
  }

  let gameId = $state('');
  let email = $state('');
  let password = $state('');
  let jwt = $state('');
  const socketUrl = import.meta.env.VITE_GAME_SOCKET ?? 'ws://localhost:5200/ws';

  async function connect() {
    const token = jwt.length > 0 ? jwt : (await logIn(email, password)).sessionJwt;
    onFeed(liveFeed({ url: socketUrl, gameId, sessionJwt: token }));
  }

  async function tryConnect() {
    try { await connect(); } catch (error) { onError((error as Error).message); }
  }
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
    <summary>Watch a live game</summary>
    <p class="muted">
      A spectator socket to {socketUrl}. The account must already be a participant with no team on that
      game; the viewer only watches — it sends no command and answers no Ask.
    </p>
    <input type="text" placeholder="game id" bind:value={gameId} style="width: 22rem" />
    <input type="text" placeholder="session JWT (or log in below)" bind:value={jwt} style="width: 22rem" />
    <input type="email" placeholder="email" bind:value={email} />
    <input type="password" placeholder="password" bind:value={password} />
    <button disabled={gameId.length === 0 || (jwt.length === 0 && email.length === 0)}
      onclick={tryConnect}>Connect</button>
  </details>
</div>

<style>
  .picker { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }
</style>
