<script lang="ts">
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import { session } from '$lib/session/session.svelte';
  import { MAX_NAME, nameProblem } from '$lib/validation';

  let name = $state(session.account?.name ?? '');
  let confirm = $state(''); let error = $state<string | null>(null); let saved = $state(false); let busy = $state(false);
  const current = $derived(session.account?.name ?? '');
  const problem = $derived(nameProblem(name, 'Display name'));
  const unchanged = $derived(name.trim() === current);
  const generated = $derived(/^Player-/.test(current));

  async function rename(e: SubmitEvent) {
    e.preventDefault();
    if (problem || unchanged) return;
    busy = true; error = null; saved = false;
    try { const row = await api.account.rename(name.trim()); session.accept({ sessionJwt: session.token!, account: row }); name = row.name; saved = true; }
    catch (err) { error = errorMessage(err); } finally { busy = false; }
  }
  async function destroy(e: SubmitEvent) {
    e.preventDefault();
    if (confirm !== current) return;
    busy = true; error = null;
    try { await api.account.delete(); session.clear(); await goto('/login', { replaceState: true }); }
    catch (err) { error = errorMessage(err); } finally { busy = false; }
  }
</script>

<svelte:head><title>Account · Tabletop</title></svelte:head>

<h1>Account</h1>
<section class="panel" aria-labelledby="name-heading">
  <h2 id="name-heading">Display name</h2>
  <p>Other players see you as <strong>{current}</strong>.{#if generated} New accounts get a generated name; choose your own below.{/if}</p>
  <form class="stack" onsubmit={rename}>
    <label>Display name
      <input bind:value={name} maxlength={MAX_NAME} autocomplete="nickname" aria-invalid={!!problem} aria-describedby="name-help" oninput={() => (saved = false)} />
    </label>
    <p id="name-help"><small>{problem ?? `Up to ${MAX_NAME} characters.`}</small></p>
    <div class="actions">
      <button type="submit" class="primary" disabled={busy || !!problem || unchanged}>Save name</button>
      <span class="saved" role="status">{#if saved}Saved.{/if}</span>
    </div>
  </form>
</section>

<section class="panel danger-zone" aria-labelledby="delete-heading">
  <h2 id="delete-heading">Delete this account</h2>
  <p>This removes your armies, assets, folders and layouts, and deletes <strong>every game you created — for everyone in it</strong>. Games you joined go on without you. There is no undo.</p>
  <form class="stack" onsubmit={destroy}>
    <label>Type your display name, <strong>{current}</strong>, to confirm <input bind:value={confirm} autocomplete="off" /></label>
    <div class="actions"><button type="submit" class="danger" disabled={busy || confirm !== current}>Delete everything</button></div>
  </form>
</section>
{#if error}<p role="alert">{error}</p>{/if}
<style>.panel { max-width: 48rem } .danger-zone { --accent: var(--failure) } .danger-zone h2 { color: var(--failure) }</style>
