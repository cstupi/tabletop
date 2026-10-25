<!-- viewer/src/App.svelte -->
<script lang="ts">
  import PlaybackView from './lib/components/PlaybackView.svelte';
  import ReplayPage from './lib/components/ReplayPage.svelte';
  import SourcePicker from './lib/components/SourcePicker.svelte';
  import GamePage from './lib/game/GamePage.svelte';
  import GamesPage from './lib/games/GamesPage.svelte';
  import TestsPage from './lib/components/TestsPage.svelte';
  import type { Feed } from './lib/feed/feed';
  import { recordingFeed } from './lib/feed/recordingFeed';
  import { fromUrl, recordingUrlFrom } from './lib/recording/source';
  import { configOf, signInHref } from './lib/hosted/hosted';
  import { refreshSession } from './lib/live/management';
  import { devAuth, hostedAuth, type Auth, type DevAuth } from './lib/shell/auth';
  import { hrefOf, parseRoute, tabsOf, type Route } from './lib/shell/route';

  const config = configOf(import.meta.env, window.location);
  // Lazy, so a blocked localStorage throws inside hostedSession's own try/catch.
  const storage = {
    getItem: (key: string) => window.localStorage.getItem(key),
    setItem: (key: string, value: string) => window.localStorage.setItem(key, value),
  };
  const dev: DevAuth | null = config.hosted ? null : devAuth((jwt) => refreshSession(jwt));
  const auth: Auth = dev ?? hostedAuth(storage, (jwt) => refreshSession(jwt));
  // Hosted opens to the games list; the developer build to its recording flow.
  const home: Route = config.hosted ? { page: 'games' } : { page: 'watch' };
  const LABELS = { games: 'Games', watch: 'Watch', tests: 'Tests' } as const;

  let route: Route = $state(parseRoute(window.location.hash, home));
  // Hosted: the website's session decides what the page shows; the developer build is always open.
  let access: Auth['access'] = $state(config.hosted ? 'checking' : 'admin');
  let error: string | null = $state(null);
  // The feed the Watch page plays: a recording, a live game, or a run from the Tests page.
  let watchFeed: Feed | null = $state.raw(null);

  $effect(() => {
    const onHash = () => { route = parseRoute(window.location.hash, home); };
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  });

  $effect(() => {
    if (!config.hosted) return;
    auth.jwt().catch((e: Error) => { if (auth.access === 'checking') error = e.message; }).finally(() => { access = auth.access; });
  });

  $effect(() => {
    const url = recordingUrlFrom(window.location.search);
    if (url !== null) fromUrl(url).then((s) => show(recordingFeed(s))).catch((e: Error) => (error = e.message));
  });

  function show(feed: Feed) {
    error = null;
    watchFeed = feed;
    window.location.hash = hrefOf({ page: 'watch' });
  }
</script>

{#if access === 'checking'}
<main class="gate">
  <h1>Tabletop</h1>
  {#if error === null}<p class="muted">Checking your Tabletop session…</p>{:else}<p class="error">{error}</p>{/if}
</main>
{:else if access === 'signed-out'}
<main class="gate">
  <h1>Signed out</h1>
  <p>Sign in to Tabletop to play.</p>
  <p><a href={signInHref(window.location.pathname)}>Sign in</a></p>
</main>
{:else}
<header>
  <strong>{config.hosted ? 'Tabletop' : 'Tabletop viewer'}</strong>
  {#if config.hosted}<a href="/">Back to Tabletop</a>{/if}
  <nav class="views">
    {#each tabsOf(access, config.runner) as page (page)}
      <a class="tab-link" class:active={route.page === page} href={hrefOf({ page })}>{LABELS[page]}</a>
    {/each}
  </nav>
  {#if error !== null}<span class="error" role="alert">{error}</span>{/if}
</header>

<main class="page">
  {#if route.page === 'games'}
    <GamesPage {auth} />
  {:else if route.page === 'game'}
    {#key route.gameId}<GamePage gameId={route.gameId} army={route.army} {auth} socketUrl={config.socketUrl} />{/key}
  {:else if route.page === 'replay'}
    {#key route.gameId}<ReplayPage gameId={route.gameId} socketUrl={config.socketUrl} {auth} />{/key}
  {/if}
  <!-- Watch and Tests stay mounted while hidden: leaving them must not close the feed or lose a run. -->
  <div class="watch" hidden={route.page !== 'watch'}>
    <SourcePicker onFeed={show} onError={(m) => (error = m)} socketUrl={config.socketUrl} {auth} {dev} />
    <PlaybackView feed={watchFeed} />
  </div>
  {#if config.runner}
    <div hidden={route.page !== 'tests'}>
      <TestsPage onFeed={show} onError={(m) => (error = m)} socketUrl={config.socketUrl} />
    </div>
  {/if}
</main>
{/if}

<style>
  .watch { display: grid; grid-template-rows: auto minmax(0, 1fr); height: 100%; min-height: 0; }
</style>
