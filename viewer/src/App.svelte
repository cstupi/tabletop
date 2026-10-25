<!-- viewer/src/App.svelte -->
<script lang="ts">
  import PlaybackView from './lib/components/PlaybackView.svelte';
  import ReplayPage from './lib/replay/ReplayPage.svelte';
  import SourcePicker from './lib/components/SourcePicker.svelte';
  import GamePage from './lib/game/GamePage.svelte';
  import GamesPage from './lib/games/GamesPage.svelte';
  import TestsPage from './lib/components/TestsPage.svelte';
  import type { Feed } from './lib/feed/feed';
  import { recordingFeed } from './lib/feed/recordingFeed';
  import { fromUrl, recordingUrlFrom } from './lib/recording/source';
  import { configOf, signInHref } from './lib/hosted/hosted';
  import { refreshSession } from './lib/live/management';
  import { hostedAuth, type Auth } from './lib/shell/auth';
  import SiteHeader from './lib/shell/SiteHeader.svelte';
  import { hrefOf, parseRoute, tabsOf, type Route } from './lib/shell/route';

  const config = configOf(import.meta.env, window.location);
  // Lazy, so a blocked localStorage throws inside hostedSession's own try/catch.
  const storage = {
    getItem: (key: string) => window.localStorage.getItem(key),
    setItem: (key: string, value: string) => window.localStorage.setItem(key, value),
  };
  const auth: Auth = hostedAuth(storage, (jwt) => refreshSession(jwt));
  const home: Route = { page: 'games' };
  const LABELS = { games: 'Games', watch: 'Watch', tests: 'Tests' } as const;

  let route: Route = $state(parseRoute(window.location.hash, home));
  // The website's session decides what the page shows.
  let access: Auth['access'] = $state('checking');
  let error: string | null = $state(null);
  // The feed the Watch page plays: a recording, a live game, or a run from the Tests page.
  let watchFeed: Feed | null = $state.raw(null);
  let accountName: string | null = $state(null);
  // The source picker lists the games (a refresh and GET /v1/games), so it mounts the first time Watch is shown, then stays.
  let watchShown = $state(false);
  $effect(() => { if (route.page === 'watch') watchShown = true; });

  $effect(() => {
    const onHash = () => { route = parseRoute(window.location.hash, home); };
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  });

  $effect(() => {
    auth.jwt().catch((e: Error) => { if (auth.access === 'checking') error = e.message; }).finally(() => { access = auth.access; });
    auth.account().then((a) => { accountName = a.name; }).catch(() => {});
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

<SiteHeader account={accountName} />
{#if access === 'checking'}
<main class="viewer"><div class="doc"><div class="inner">
  {#if error === null}<p class="loading" role="status">Checking your Tabletop session…</p>{:else}<p role="alert">{error}</p>{/if}
</div></div></main>
{:else if access === 'signed-out'}
<main class="viewer"><div class="doc"><div class="inner">
  <div class="panel empty">
    <p>Sign in to Tabletop to play.</p>
    <a class="button primary" href={signInHref(window.location.pathname)}>Sign in</a>
  </div>
</div></div></main>
{:else}
{#if tabsOf(access, config.runner).length > 1 && (route.page === 'games' || route.page === 'watch' || route.page === 'tests')}
  <nav class="subnav" aria-label="Play">
    {#each tabsOf(access, config.runner) as page (page)}
      <a href={hrefOf({ page })} aria-current={route.page === page ? 'page' : undefined}>{LABELS[page]}</a>
    {/each}
  </nav>
{/if}
{#if error !== null}<p role="alert" class="shell-error">{error} <button class="quiet" onclick={() => (error = null)}>Dismiss</button></p>{/if}
<main class="viewer">
  {#if route.page === 'games'}
    <GamesPage {auth} />
  {:else if route.page === 'game'}
    {#key route.gameId}<GamePage gameId={route.gameId} army={route.army} {auth} socketUrl={config.socketUrl} />{/key}
  {:else if route.page === 'replay'}
    {#key route.gameId}<ReplayPage gameId={route.gameId} socketUrl={config.socketUrl} {auth} />{/key}
  {/if}
  <!-- Watch and Tests stay mounted while hidden: leaving them must not close the feed or lose a run. -->
  <div class="watch" hidden={route.page !== 'watch'}>
    {#if watchShown && tabsOf(access, config.runner).includes('watch')}
      <SourcePicker onFeed={show} onError={(m) => (error = m)} socketUrl={config.socketUrl} {auth} />
    {/if}
    <PlaybackView feed={watchFeed} />
  </div>
  {#if config.runner}
    <div class="tests" hidden={route.page !== 'tests'}>
      <TestsPage onFeed={show} onError={(m) => (error = m)} socketUrl={config.socketUrl} />
    </div>
  {/if}
</main>
{/if}

<style>
  .watch { display: grid; grid-template-rows: auto minmax(0, 1fr); min-height: 0; }
  .tests { overflow: auto; }
  .shell-error { margin: 0; flex: none; }
</style>
