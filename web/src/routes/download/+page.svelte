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
    <p class="notice">These builds are not code-signed yet, so Windows and macOS warn the first time you open one. The steps under each download get past the warning.</p>
  </section>

  <ul class="builds">
    {#each downloads as d (d.platform)}
      <li class="panel" class:mine={d.platform === detected} aria-labelledby="build-{d.platform}">
        <div class="head">
          <h2 id="build-{d.platform}">{PLATFORM_NAMES[d.platform]}</h2>
          {#if d.platform === detected}<span class="mine-tag">This computer</span>{/if}
        </div>
        <p class="dim">{PLATFORM_DETAIL[d.platform]}</p>
        <p><a class="button" class:primary={d.platform === detected} href={d.url} onclick={(e) => follow(e, d)}>
          <Icon name="download" />Download for {PLATFORM_NAMES[d.platform]}<span class="size">{formatBytes(d.size)}</span></a></p>
        <dl>
          <dt>File</dt><dd><code>{d.fileName}</code></dd>
          <dt>SHA-256</dt><dd><code>{d.sha256}</code></dd>
        </dl>
        <h3>First run</h3>
        <ol>{#each FIRST_RUN[d.platform] as step}<li>{step}</li>{/each}</ol>
      </li>
    {/each}
  </ul>
{/if}

<style>
  .notes { white-space: pre-line; }
  .builds { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--s5); grid-template-columns: repeat(auto-fit, minmax(min(100%, 19rem), 1fr)); }
  .builds > li { margin: 0; display: flex; flex-direction: column; }
  .head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: var(--s2); }
  .head h2 { margin: 0; }
  .mine-tag { font-size: .8rem; color: var(--accent); border: 1px solid currentColor; padding: 0 var(--s2); }
  .button { display: inline-flex; align-items: center; gap: var(--s2); text-decoration: none; }
  .size { opacity: .8; font-size: .875em; }
  dl { display: grid; grid-template-columns: auto 1fr; gap: var(--s1) var(--s3); margin: var(--s3) 0; }
  dt { color: var(--dim); font-size: .875em; }
  dd { margin: 0; min-width: 0; }
  /* File names and hashes are case-sensitive: a plain monospace, never the theme's small-caps number face. */
  dd code { font-family: ui-monospace, 'Cascadia Mono', Menlo, Consolas, monospace; font-variant: normal; text-transform: none; overflow-wrap: anywhere; }
  h3 { font-size: 1rem; margin: var(--s3) 0 var(--s1); }
  ol { margin: 0; padding-left: 1.25rem; }
  ol li { margin: var(--s1) 0; }
</style>
