<script lang="ts">
  import { goto } from '$app/navigation';
  import { api } from '$lib/api/client';
  import { session } from '$lib/session/session.svelte';
  import { loginErrorMessage } from '$lib/session/messages';

  let method = $state<'password' | 'code'>('password');
  let email = $state(''); let password = $state(''); let code = $state('');
  let methodId = $state<string | null>(null);   // set once the code has been sent
  let error = $state<string | null>(null); let notice = $state<string | null>(null); let busy = $state(false);

  async function run(fn: () => Promise<void>) {
    busy = true; error = null; notice = null;
    try { await fn(); } catch (e) { error = loginErrorMessage(e); } finally { busy = false; }
  }
  const signIn = () => run(async () => { session.accept(await api.auth.password(email, password)); await goto('/armies'); });
  const signUp = () => run(async () => { session.accept(await api.auth.signup(email, password)); await goto('/armies'); });
  const sendCode = () => run(async () => { methodId = (await api.auth.sendOtp(email)).methodId; notice = `Code sent to ${email}.`; });
  const verifyCode = () => run(async () => { session.accept(await api.auth.verifyOtp(methodId!, code)); await goto('/armies'); });
  const forgot = () => run(async () => { await api.auth.startReset(email); notice = 'If that address has a password, a reset link is on its way.'; });
</script>

<h1>Sign in</h1>
<nav><button onclick={() => (method = 'password')} disabled={method === 'password'}>Password</button>
     <button onclick={() => (method = 'code')} disabled={method === 'code'}>Email code</button></nav>

<label>Email <input type="email" bind:value={email} autocomplete="email" /></label>

{#if method === 'password'}
  <label>Password <input type="password" bind:value={password} autocomplete="current-password" /></label>
  <button onclick={signIn} disabled={busy || !email || !password}>Sign in</button>
  <button onclick={signUp} disabled={busy || !email || !password}>Create account</button>
  <button onclick={forgot} disabled={busy || !email}>Forgot password</button>
{:else if methodId === null}
  <button onclick={sendCode} disabled={busy || !email}>Send code</button>
{:else}
  <label>Code <input inputmode="numeric" bind:value={code} autocomplete="one-time-code" /></label>
  <button onclick={verifyCode} disabled={busy || code.length < 6}>Verify</button>
  <button onclick={sendCode} disabled={busy}>Send again</button>
{/if}

{#if error}<p role="alert">{error}</p>{/if}
{#if notice}<p>{notice}</p>{/if}
