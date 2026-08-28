# 03 — Management: identity

**Owns:** `server/management/`, `web/`
**Depends on:** nothing. This workstream can start from a cold repo.
**Branch:** `ws/03-management-identity`

Read `docs/workstreams/README.md`, `docs/architecture.md`, and `CLAUDE.md` first.

## Goal

A user can sign up, log in, log out, and see their account. Nothing else.

This is phase 1 of four for the management service (identity → collections → assets → lobby).
Build only phase 1. There is no army, no game, no asset, and no lobby in this workstream — if
you find yourself modelling one, stop.

## Deliverable

- `web/` — SvelteKit SPA (`adapter-static`), dev server on **port 3000**
- `server/management/` — ASP.NET Core API, `net10.0`, no Godot reference
- Postgres via docker compose, running in WSL
- Working: signup, login, logout, `/me` returning the current account

Plus two **stub pages**, behind auth, reachable from navigation:

- **Armies** — where list import and model upload will live
- **Games** — where available games will be listed

These are placeholders: real layout, real navigation, real empty states, and **no API calls**.
Their endpoints are designed in workstreams 09 and 10. Do not invent an armies or games API,
do not add tables for them, and do not build upload handling. The point is that the shell of
the app exists and is navigable, so later workstreams fill panels rather than inventing the
application around them.

## Stytch

The project already exists. **Use the Stytch MCP** to read config and make changes; do not
hand-edit anything in the dashboard.

| | |
| --- | --- |
| Project | `TabletopWar`, vertical `CONSUMER` |
| Test project id | `project-test-437432c7-798d-49d2-9dc4-eff103937e0f` |
| Test public token | `public-token-test-5a68c418-a0fe-48f3-87a0-314818ce3561` |
| Authorized domains | `http://localhost:3000`, `https://tt.cstupi.com` |

Already enabled in the test environment — **do not change these**:

- **Magic links** — `login_or_create` enabled, **PKCE required**
- **Passwords** — enabled, PKCE required for resets
- **OAuth** — disabled. Do not add providers.
- **Sessions** — max duration 90 minutes
- **Cookies** — `http_only: ENFORCED`

Use **magic link + password**. Magic link covers signup and login in one path; password exists
because magic-link-only is tedious when logging in repeatedly during development.

PKCE is required, so the browser flow must go through Stytch's JS SDK. Do not hand-roll the
magic-link exchange against the REST API.

### Secrets

- **API secret** — fetch via the Stytch MCP, store with `dotnet user-secrets`. This writes to
  `~/.microsoft/usersecrets/`, outside the repo, so it cannot be committed. Do not put it in
  `appsettings.json`, and do not put it in `.env`.
- **Public token** — the value above. Safe in the browser bundle; put it in `web/.env` (already
  gitignored) and commit a `web/.env.example` documenting the key names.

Never commit a secret. `.gitignore` already covers `.env` and `.env.*`.

## The httpOnly cookie constraint

`http_only` is `ENFORCED`, so the session cookie is invisible to JavaScript and will not be sent
cross-origin. **The SPA and the API must be same-site.**

- **Local:** Vite dev server on `:3000` proxies `/api` to the ASP.NET Core port. The browser
  only ever sees origin `localhost:3000`.
- **Deployed:** ALB path routing — `tt.cstupi.com` → SPA, `tt.cstupi.com/api` → management API.

Do not solve this with permissive CORS and `SameSite=None`. If the proxy approach seems not to
work, stop and report rather than loosening cookie policy.

## Identifiers

**No integer ids anywhere in this product.** Not for accounts, not for anything added later.

Primary keys are **UUIDv7**, generated in application code with `Guid.CreateVersion7()`
(available in `net10.0`), stored in a Postgres `uuid` column.

- **v7, not v4** — v7's leading 48 bits are a millisecond timestamp, so inserts append near the
  end of the B-tree instead of scattering across it. v4 destroys index locality and causes page
  splits on every write.
- **Application-generated, not a column default** — the id must exist *before* the Stytch
  `external_id` call, so a `DEFAULT` would force insert-then-read-back-then-call.
- Let Npgsql map `Guid` to `uuid` directly. **Do not** store ids as `bytea` via
  `Guid.ToByteArray()` — .NET's byte layout is mixed-endian for the first fields, which silently
  destroys the time ordering that made v7 worth choosing.

## Data model

One entity. Resist adding more.

```
Account
  id              uuid  primary key, UUIDv7, app-generated
  stytch_user_id  text  unique, the Stytch subject
  display_name    text
  created_at      timestamptz
```

Stytch owns credentials, email verification, and session lifecycle. This table exists only so
the rest of the product has a local foreign key to hang armies and assets off later. **Do not
mirror email, password state, or session data into Postgres.**

## Linking to Stytch

The Stytch user's `external_id` must equal `Account.id`, so either side can be resolved from
the other without a lookup table.

`external_id` is a first-class field on the Consumer User object — not metadata. It is usable
anywhere `user_id` is expected, must be unique within the project, and accepts alphanumerics,
`.`, `_`, and `-` up to 128 characters, so a UUID's hex-and-hyphen form fits directly.

Signup happens through the browser SDK, so the Stytch user exists before our API ever sees it.
The order on first login is therefore:

1. API receives a session for a `stytch_user_id` it has no `Account` for
2. Generate a UUIDv7, insert the `Account`
3. Call Stytch **Update User** to set that user's `external_id` to the new id

Note that step 3 is Update User, not Create User — the SDK's `login_or_create` already made the
user, so `external_id` cannot be supplied at creation time.

**Step 3 can fail after step 2 succeeds.** Make first-login handling idempotent and
self-healing: a retry must not create a second `Account`, and an `Account` whose Stytch user is
missing `external_id` must get it set on the next request rather than staying broken.

## API surface

Keep it this small:

- `POST /api/auth/session` — exchange a Stytch token for a session, upserting the Account
- `DELETE /api/auth/session` — log out
- `GET /api/me` — the current account, or 401

## Decisions left to you

Record each in a `## Decisions` section appended to this file, one line of reasoning each:

- ORM or data access approach (EF Core, Dapper, raw). Migrations must be checked in.
- Minimal API vs controllers.
- Test framework and how you test an endpoint that requires a Stytch session.

## Out of scope — do not build

Armies, Army Forge import, art bindings, assets, uploads, S3, lobby, rooms, invites, teams, the
game server, anything touching `libs/`, AWS infrastructure or deployment. Local docker only.

## Done when

1. `docker compose up` brings up Postgres.
2. The API runs and `GET /api/me` returns 401 when unauthenticated.
3. The SPA runs on `localhost:3000`; a real signup via magic link creates an Account row and
   `/api/me` then returns it.
4. Log out clears the session and `/api/me` returns 401 again.
5. `dotnet build` from the repo root succeeds; `dotnet test` passes.
6. No secret is present anywhere in the working tree — verify with `git diff` before committing.

Paste the commands and their output. Do not claim done without it.

## Redirect URLs

The test environment has two registered:

```
http://localhost:3000/authenticate      is_default: false   <- use this
https://tt.cstupi.com/authenticate.     is_default: true
```

The default is not localhost, so **always pass the redirect URL explicitly** in SDK calls
rather than relying on the default. Do not change redirect URL configuration from this
workstream.

There is no live/production environment set up, and production will not use these domains.
Nothing in this workstream deploys anywhere — local docker only.
