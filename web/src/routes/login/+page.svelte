<script lang="ts">
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { tick } from 'svelte';
  import { api } from '$lib/api/client';
  import { session } from '$lib/session/session.svelte';
  import { loginErrorMessage } from '$lib/session/messages';
  import { safeNext } from '$lib/session/redirect';

  const next = $derived(safeNext(page.url.searchParams.get('next')));
  const expired = $derived(page.url.searchParams.has('expired'));

  let method = $state<'password' | 'code'>('password');
  let email = $state(''); let password = $state(''); let code = $state('');
  let methodId = $state<string | null>(null);   // set once the code has been sent
  let error = $state<string | null>(null); let notice = $state<string | null>(null); let busy = $state<string | null>(null);
  let codeInput = $state<HTMLInputElement>();

  // Already signed in (another tab, or a reload): carry on to where the reader was going.
  $effect(() => { if (session.ready && session.token) void goto(next, { replaceState: true }); });

  async function run(label: string, fn: () => Promise<void>) {
    busy = label; error = null; notice = null;
    try { await fn(); } catch (e) { error = loginErrorMessage(e); } finally { busy = null; }
  }
  const finish = async () => { await goto(next, { replaceState: true }); };
  const signIn = () => run('Signing in…', async () => { session.accept(await api.auth.password(email.trim(), password)); await finish(); });
  const signUp = () => run('Creating your account…', async () => { session.accept(await api.auth.signup(email.trim(), password)); await finish(); });
  const sendCode = () => run('Sending the code…', async () => {
    methodId = (await api.auth.sendOtp(email.trim())).methodId; code = '';
    notice = `We sent a 6-digit code to ${email.trim()}.`;
    await tick(); codeInput?.focus();
  });
  const verifyCode = () => run('Checking the code…', async () => { session.accept(await api.auth.verifyOtp(methodId!, code.trim())); await finish(); });
  const forgot = () => run('Sending the reset link…', async () => { await api.auth.startReset(email.trim()); notice = `If ${email.trim()} has a password, a reset link is on its way.`; });
  function choose(m: 'password' | 'code') { method = m; error = null; notice = null; methodId = null; }
  const submit = (fn: () => void) => (e: SubmitEvent) => { e.preventDefault(); fn(); };
  const emailOk = $derived(/^\S+@\S+\.\S+$/.test(email.trim()));
</script>

<svelte:head><title>Sign in · Tabletop</title></svelte:head>

<div class="auth">
<section class="panel" aria-labelledby="sign-in-heading">
<h1 id="sign-in-heading">Sign in</h1>
{#if expired}<p class="notice" role="status">Your session has ended. Sign in again.</p>{/if}

<div class="seg method" role="group" aria-label="Sign-in method">
  <button type="button" aria-pressed={method === 'password'} onclick={() => choose('password')}>Password</button>
  <button type="button" aria-pressed={method === 'code'} onclick={() => choose('code')}>Email me a code</button>
</div>

{#if method === 'password'}
  <form class="stack" onsubmit={submit(signIn)}>
    <label>Email <input type="email" bind:value={email} autocomplete="email" required /></label>
    <label>Password <input type="password" bind:value={password} autocomplete="current-password" required /></label>
    <div class="actions">
      <button type="submit" class="primary" disabled={!!busy || !emailOk || !password}>Sign in</button>
      <button type="button" onclick={signUp} disabled={!!busy || !emailOk || !password}>Create account</button>
      <button type="button" class="quiet" onclick={forgot} disabled={!!busy || !emailOk}>Forgot password</button>
    </div>
  </form>
{:else if methodId === null}
  <form class="stack" onsubmit={submit(sendCode)}>
    <label>Email <input type="email" bind:value={email} autocomplete="email" required /></label>
    <div class="actions"><button type="submit" class="primary" disabled={!!busy || !emailOk}>Send code</button></div>
  </form>
{:else}
  <form class="stack" onsubmit={submit(verifyCode)}>
    <label>6-digit code <input bind:this={codeInput} bind:value={code} inputmode="numeric" autocomplete="one-time-code" maxlength="6" pattern="[0-9]{'{'}6{'}'}" required /></label>
    <div class="actions">
      <button type="submit" class="primary" disabled={!!busy || !/^\d{6}$/.test(code.trim())}>Verify</button>
      <button type="button" onclick={sendCode} disabled={!!busy}>Send again</button>
      <button type="button" class="quiet" onclick={() => (methodId = null)} disabled={!!busy}>Use another email</button>
    </div>
  </form>
{/if}

<div aria-live="polite">
  {#if busy}<p class="loading">{busy}</p>{/if}
  {#if notice}<p class="notice">{notice}</p>{/if}
</div>
{#if error}<p role="alert">{error}</p>{/if}
</section>
</div>

<style>
  .method { margin: 0 0 var(--s3); }
  .method > button { flex: 1 1 auto; }
</style>
