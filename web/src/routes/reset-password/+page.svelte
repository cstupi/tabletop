<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { session } from '$lib/session/session.svelte';
  import { loginErrorMessage } from '$lib/session/messages';

  const token = $derived(page.url.searchParams.get('token'));
  let password = $state(''); let again = $state(''); let error = $state<string | null>(null); let busy = $state(false);

  async function submit() {
    if (!token) return;
    busy = true; error = null;
    try { session.accept(await api.auth.completeReset(token, password)); await goto('/armies'); }
    catch (e) { error = loginErrorMessage(e); } finally { busy = false; }
  }
</script>

<h1>Choose a new password</h1>
{#if !token}
  <p role="alert">This link is missing its token. Ask for a new reset email from the <a href="/login">sign-in page</a>.</p>
{:else}
  <label>New password <input type="password" bind:value={password} autocomplete="new-password" /></label>
  <label>Again <input type="password" bind:value={again} autocomplete="new-password" /></label>
  <button onclick={submit} disabled={busy || !password || password !== again}>Set password and sign in</button>
  {#if error}<p role="alert">{error}</p>{/if}
{/if}
