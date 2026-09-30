# Task 4: Login, the reset-password page, and the signed-in shell

**Files:**
- Create: `web/src/routes/login/+page.svelte`, `web/src/routes/reset-password/+page.svelte`
- Modify: `web/src/routes/+layout.svelte`
- Test: `web/tests/login-flow.test.ts`

**Interfaces:**
- Consumes: `api.auth.*`, `session`, `ApiError`.
- Produces: `/login` (two methods: password sign in / sign up, and email code); `/reset-password?token=…`; the layout: restores the session on load, starts the refresh loop, redirects to `/login` when there is no token (except on `/login` and `/reset-password`), renders a protected page only once the session is ready and holds a token (`isPublic || (session.ready && session.token)`), so a signed-out visitor never sees a protected page before the redirect, and shows the nav (Armies, Library, Account, Log out); `loginErrorMessage(e: unknown): string` in `src/lib/session/messages.ts` mapping `401 → "Wrong email, password or code."`, `409 → "An account with that email already exists — sign in instead."`, `422 → detail` (Stytch's weak-password wording), else the title.

Spec: web app → Login is the web app's (email + password, or email + one-time code, through management); management plan Task 5 (`/otp` → `methodId`; `/otp/verify`; `/password`; `/password/signup` → 201; `/password/reset` → 202 and Stytch mails a link to `{WebUrl}/reset-password`; `/password/reset/complete`); accounts → An account is two fields (the name is the email's local part at first; renamed on the account page).

- [ ] **Step 1: Write the failing test for the error mapping**

```ts
// web/tests/login-flow.test.ts
import { describe, expect, it } from 'vitest';
import { ApiError } from '../src/lib/api/http';
import { loginErrorMessage } from '../src/lib/session/messages';

describe('loginErrorMessage', () => {
  it('maps the statuses management documents', () => {
    expect(loginErrorMessage(new ApiError(401, 'Unauthorized', 'unauthorized_credentials'))).toBe('Wrong email, password or code.');
    expect(loginErrorMessage(new ApiError(409, 'Conflict', 'exists'))).toMatch(/already exists/);
    expect(loginErrorMessage(new ApiError(422, 'Unprocessable', 'weak_password'))).toBe('weak_password');
    expect(loginErrorMessage(new ApiError(502, 'Upstream failure'))).toBe('Upstream failure');
    expect(loginErrorMessage(new Error('boom'))).toBe('Something went wrong. Try again.');
  });
});
```

```ts
// web/src/lib/session/messages.ts
import { ApiError } from '$lib/api/http';
export function loginErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return 'Something went wrong. Try again.';
  switch (e.status) {
    case 401: return 'Wrong email, password or code.';
    case 409: return 'An account with that email already exists — sign in instead.';
    case 422: return e.detail ?? e.title;
    default: return e.title;
  }
}
```

- [ ] **Step 2: Run; expect 1 failure then pass after writing `messages.ts`. Write `login/+page.svelte`:**

```svelte
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
```

- [ ] **Step 3: Write `reset-password/+page.svelte`** — the page Stytch's reset email lands on (`{TABLETOP_WEB_URL}/reset-password?token=…`):

```svelte
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
```

- [ ] **Step 4: Replace `+layout.svelte` with the shell**

```svelte
<script lang="ts">
  import '../app.css';
  import { onMount } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { session } from '$lib/session/session.svelte';

  let { children } = $props();
  const publicRoutes = ['/login', '/reset-password'];
  const isPublic = $derived(publicRoutes.some((p) => page.url.pathname.startsWith(p)));

  onMount(() => { void session.restore(); return session.startRefreshLoop(); });

  $effect(() => {
    if (!session.ready) return;
    if (!session.token && !isPublic) void goto(`/login`);
  });

  async function logout() { await session.logout(); await goto('/login'); }
</script>

{#if session.token}
  <header>
    <nav>
      <a href="/armies" aria-current={page.url.pathname.startsWith('/armies') ? 'page' : undefined}>Armies</a>
      <a href="/library" aria-current={page.url.pathname.startsWith('/library') ? 'page' : undefined}>Library</a>
      <a href="/account">{session.account?.name ?? 'Account'}</a>
      <button onclick={logout}>Log out</button>
    </nav>
  </header>
{/if}
<main>
  {#if isPublic || (session.ready && session.token)}{@render children()}{:else}<p>Loading…</p>{/if}
</main>
```

- [ ] **Step 5: Run `npm test && npm run check`; expect 13 passed, 0 errors. Then run it for real: `docker compose up -d`, `./scripts/management`, `npm run dev`; open `http://localhost:5173/login` from the Windows browser, create an account by password, reload (still signed in), log out, sign in by email code, request a reset and follow the emailed link to `/reset-password?token=…`. The Stytch dashboard must list `http://localhost:5173/reset-password` as a reset redirect URL (management plan → Interfaces needed → Stytch dashboard).**

- [ ] **Step 6: Commit: `Add login by password or email code, the reset-password page and the signed-in shell`.**
