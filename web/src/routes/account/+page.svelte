<script lang="ts">
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import { ApiError } from '$lib/api/http';
  import { session } from '$lib/session/session.svelte';

  let name = $state(session.account?.name ?? '');
  let confirm = $state(''); let error = $state<string | null>(null); let saved = $state(false);

  async function rename() {
    error = null; saved = false;
    try { const row = await api.account.rename(name.trim()); session.accept({ sessionJwt: session.token!, account: row }); saved = true; }
    catch (e) { error = e instanceof ApiError ? (e.detail ?? e.title) : 'Could not save.'; }
  }
  async function destroy() {
    error = null;
    try { await api.account.delete(); session.clear(); await goto('/login'); }
    catch (e) { error = e instanceof ApiError ? (e.detail ?? e.title) : 'Could not delete.'; }
  }
</script>

<h1>Account</h1>
<section>
  <label>Display name <input bind:value={name} /></label>
  <button onclick={rename} disabled={!name.trim() || name.trim() === session.account?.name}>Save</button>
  {#if saved}<span>Saved.</span>{/if}
</section>

<section>
  <h2>Delete this account</h2>
  <p>This removes your armies, assets, folders and layouts, and deletes <strong>every game you took part in — for everyone in it</strong>. There is no undo.</p>
  <label>Type your display name to confirm <input bind:value={confirm} /></label>
  <button onclick={destroy} disabled={confirm !== session.account?.name}>Delete everything</button>
</section>
{#if error}<p role="alert">{error}</p>{/if}
