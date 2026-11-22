<script lang="ts">
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import { ApiError } from '$lib/api/http';
  import type { ClientDownload, ClientRelease } from '$lib/api/types';
  import Icon from '$lib/components/Icon.svelte';
  import { detectPlatform, FIRST_RUN, orderDownloads, PLATFORM_DETAIL, PLATFORM_NAMES } from '$lib/download/platforms';
  import { formatBytes } from '$lib/validation';

  type NavigatorWithHints = Navigator & { userAgentData?: { platform?: string } };
  const nav = (typeof navigator === 'undefined' ? undefined : navigator) as NavigatorWithHints | undefined;
  const detected = detectPlatform(nav?.userAgent ?? '', nav?.userAgentData?.platform);

  let release = $state<ClientRelease | null>(null);
  let loading = $state(true); let none = $state(false); let error = $state<string | null>(null);
  const downloads = $derived(release ? orderDownloads(release.downloads, detected) : []);
  const published = $derived(release ? new Date(release.date).toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' }) : '');

  async function load() {
    try { release = await api.client.latest(); none = false; error = null; }
    catch (e) { if (e instanceof ApiError && e.status === 404) none = true; else error = errorMessage(e); }
    finally { loading = false; }
  }
  $effect(() => { void load(); });

  // The links are presigned for 15 minutes: a page left open fetches fresh ones before following one.
  async function follow(e: MouseEvent, d: ClientDownload) {
    if (!release || Date.parse(release.expiresAt) - Date.now() > 60_000) return;
    e.preventDefault();
    await load();
    const fresh = release?.downloads.find((x) => x.platform === d.platform);
    if (fresh) window.location.assign(fresh.url);
  }
</script>

<svelte:head><title>Download · Tabletop</title></svelte:head>

<h1>Download the client</h1>
{#if loading}
  <p class="loading" role="status">Loading…</p>
{:else if error}
  <p role="alert">{error}</p>
{:else if none || !release}
  <div class="panel empty">
    <Icon name="download" size="2.5em" />
    <p><strong>No client build is published yet.</strong></p>
  </div>
{:else}
  <section class="panel" aria-labelledby="release-heading">
    <h2 id="release-heading">Tabletop {release.version}</h2>
    <p class="dim">Released {published}</p>
    {#if release.notes.trim()}<p class="notes">{release.notes.trim()}</p>{/if}
  </section>

  <ul class="builds">
    {#each downloads as d (d.platform)}
      <li class="panel card" class:mine={d.platform === detected} aria-labelledby="build-{d.platform}">
        <a class="button tile wide" class:primary={d.platform === detected} href={d.url} onclick={(e) => follow(e, d)}>
          <Icon name="download" size="1.75rem" /><span class="what"><strong id="build-{d.platform}">{PLATFORM_NAMES[d.platform]}</strong><small>{PLATFORM_DETAIL[d.platform]} · {formatBytes(d.size)}</small></span>
          {#if d.platform === detected}<span class="chip mine-tag">this computer</span>{/if}</a>
        <dl>
          <dt>File</dt><dd><code>{d.fileName}</code></dd>
          <dt>SHA-256</dt><dd><code>{d.sha256}</code></dd>
        </dl>
        <h3>First run</h3>
        <ol>{#each FIRST_RUN[d.platform] as step}<li class:unsigned={/not signed/.test(step)}>{#if /not signed/.test(step)}<span class="warn"><Icon name="warning" size="1.1em" label="Unsigned build" /></span>{/if}{step}</li>{/each}</ol>
      </li>
    {/each}
  </ul>
{/if}

<style>
  .notes { white-space: pre-line; }
  .builds { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--space-4); grid-template-columns: repeat(auto-fit, minmax(min(100%, 19rem), 1fr)); }
  .builds > li { margin: 0; display: flex; flex-direction: column; }
  .builds > li.mine { order: -1; }
  .tile.wide { gap: var(--space-3); padding: var(--space-3) var(--space-3); min-height: 4rem; }
  .what { display: flex; flex-direction: column; align-items: flex-start; line-height: 1.25; }
  .what small { color: inherit; opacity: .85; }
  .mine-tag { margin-left: auto; color: inherit; }
  .button:not(.primary) { background: none; }
  .warn { color: var(--warning); margin-right: var(--space-1); vertical-align: -.1em; }
  dl { display: grid; grid-template-columns: auto 1fr; gap: var(--space-1) var(--space-3); margin: var(--space-3) 0; }
  dt { color: var(--dim); font-size: .875em; }
  dd { margin: 0; min-width: 0; }
  /* File names and hashes are case-sensitive: a plain monospace, never the theme's small-caps number face. */
  dd code { font-family: ui-monospace, 'Cascadia Mono', Menlo, Consolas, monospace; font-variant: normal; text-transform: none; overflow-wrap: anywhere; }
  h3 { font-size: 1rem; margin: var(--space-3) 0 var(--space-1); }
  ol { margin: 0; padding-left: var(--space-4); }
  ol li { margin: var(--space-1) 0; }
  ol li.unsigned { list-style: decimal; }
</style>
