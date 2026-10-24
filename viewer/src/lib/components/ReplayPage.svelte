<!-- viewer/src/lib/components/ReplayPage.svelte -->
<!-- A game's log replayed from the start, over a watch socket (`?watch=1`): read-only, and it never replaces the account's play socket. -->
<script lang="ts">
  import PlaybackView from './PlaybackView.svelte';
  import { liveFeed } from '../feed/liveFeed';
  import type { Feed } from '../feed/feed';
  import type { Auth } from '../shell/auth';
  import { watchUrl } from '../shell/route';

  let { gameId, socketUrl, auth }: { gameId: string; socketUrl: string; auth: Auth } = $props();

  let feed: Feed | null = $state.raw(null);
  let error: string | null = $state(null);

  $effect(() => {
    const id = gameId;
    let cancelled = false;
    error = null;
    auth.jwt().then((sessionJwt) => {
      if (!cancelled) feed = liveFeed({ url: watchUrl(socketUrl), gameId: id, sessionJwt, label: `replay · ${id}` });
    }).catch((e: Error) => { if (!cancelled) error = e.message; });
    return () => { cancelled = true; };
  });
</script>

<div class="replay">
  <p class="bar"><a href="#/games">Back to games</a>{#if error !== null} <span class="error" role="alert">{error}</span>{/if}</p>
  <PlaybackView {feed} />
</div>

<style>
  .replay { display: grid; grid-template-rows: auto minmax(0, 1fr); height: 100%; min-height: 0; }
  .bar { margin: 0; padding: 0.3rem 0.6rem; }
</style>
