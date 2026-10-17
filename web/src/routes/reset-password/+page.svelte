<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { api } from '$lib/api/client';
  import { session } from '$lib/session/session.svelte';
  import { loginErrorMessage } from '$lib/session/messages';
  import { HOME } from '$lib/session/redirect';

  const token = $derived(page.url.searchParams.get('token'));
  let password = $state(''); let again = $state(''); let error = $state<string | null>(null); let busy = $state(false);
  const mismatch = $derived(again.length > 0 && password !== again);

  async function submit(e: SubmitEvent) {
    e.preventDefault();
    if (!token || !password || password !== again) return;
    busy = true; error = null;
    try { session.accept(await api.auth.completeReset(token, password)); await goto(HOME, { replaceState: true }); }
    catch (err) { error = loginErrorMessage(err); } finally { busy = false; }
  }
</script>

<svelte:head><title>New password · Tabletop</title></svelte:head>

<div class="auth">
<section class="panel" aria-labelledby="reset-heading">
<h1 id="reset-heading">Choose a new password</h1>
{#if !token}
  <p role="alert">This link is missing its token. Ask for a new reset email from the <a href="/login">sign-in page</a>.</p>
{:else}
  <form class="stack" onsubmit={submit}>
    <label>New password <input type="password" bind:value={password} autocomplete="new-password" required /></label>
    <label>Type it again <input type="password" bind:value={again} autocomplete="new-password" required aria-invalid={mismatch} aria-describedby={mismatch ? 'mismatch' : undefined} /></label>
    {#if mismatch}<p id="mismatch"><small>The two passwords do not match.</small></p>{/if}
    <div class="actions"><button type="submit" class="primary" disabled={busy || !password || password !== again}>{busy ? 'Saving…' : 'Set password and sign in'}</button></div>
    <p><small>Reset links expire. If this one has, ask for a new one from the <a href="/login">sign-in page</a>.</small></p>
  </form>
  {#if error}<p role="alert">{error}</p>{/if}
{/if}
</section>
</div>
