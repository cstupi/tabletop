# Task 14: Run script, CLAUDE.md, and the whole-tree check

**Files:**
- Create: `scripts/management` (shell)
- Modify: `CLAUDE.md`

**Interfaces:**
- Produces: `./scripts/management` runs the API from WSL on `http://0.0.0.0:5100` reading `.env`; `CLAUDE.md` names the project, its tests and the secret handling.

- [ ] **Step 1: Write `scripts/management`** and `chmod +x` it

```bash
#!/usr/bin/env bash
# Run the management API from WSL. Reads .env (docker compose's file) for Postgres, MinIO and Stytch settings;
# the Stytch secret comes from `dotnet user-secrets` (server/management) or TABLETOP_STYTCH_SECRET.
# Development is what maps POST /v1/dev/sessions, which the simulator's socket driver calls for its tokens.
set -euo pipefail
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
cd "$(dirname "$0")/.."
exec dotnet run --project server/management/Tabletop.Management.csproj "$@"
```

- [ ] **Step 2: Edit `CLAUDE.md`**

**Ownership rule for `CLAUDE.md`**: a plan edits only the lines it owns — its own Layout bullet, its own appended Commands lines, its own appended Conventions or Environment text — and never the opening paragraph, the closing note, or another plan's bullet. No *Before* quote below is another plan's text. Match each anchor by its fixed prefix, not by exact wording.

In **Environment**, append a bullet naming the two Stytch dashboard settings the API depends on — they live in the Stytch dashboard, not in this repo, and fail silently when missing: the `SESSION` custom claim template must be exactly `{"account_id": "{{ user.external_id }}"}`, and without it every JWT lacks `account_id`, every authenticated management request answers 401 and the game server rejects every socket; `{TABLETOP_WEB_URL}/reset-password` (`http://localhost:5173/reset-password` locally) must be a registered `RESET_PASSWORD` redirect URL, or password resets fail at Stytch. Say in the same bullet that the secret is set once per machine with `dotnet user-secrets set STYTCH_SECRET <secret>` in `server/management`, and that a real login through `./scripts/management` and a look at the JWT's claims is the only check that the template is in place.

In **Commands**, append at the end of the block:

```bash
./scripts/management         # the management API on http://localhost:5100 (needs docker compose up and the Stytch secret)
cd server/management && dotnet user-secrets set STYTCH_SECRET <secret>   # once per machine; never in a file
```

In **Layout**, replace the `server/management/` bullet (the line beginning "- `server/management/` —", currently the scaffold's stub) with: "`server/management/` — `Tabletop.Management`, ASP.NET Core minimal APIs: `Identity/` (Stytch login, JWT verification, the account), `Content/` (assets over MinIO, folders, armies from Army Forge, layouts), `Games/`, `Http/` (version check, keyset paging), `Persistence/` (Npgsql + Dapper, DbUp migrations in `Persistence/Migrations/`). Tested in `tests/Tabletop.Management.Tests` against the Docker stack."

The `tests/` bullet and the *Tests:* convention already cover this server in the form the contracts plan wrote; edit neither.

In **Conventions**, append: "- Schema changes are new `NNN_*.sql` files under `server/management/Persistence/Migrations/`; `001_schema.sql` there is a copy of `scripts/postgres/init/001_schema.sql` and neither is edited after the fact. The game server writes a game's rows only while it is in `lobby` or `setup`; management writes them at creation and at end/leave/delete (management plan → Rows by writer)."

- [ ] **Step 3: Run the whole check**

```bash
docker compose up -d && dotnet build && dotnet test
./scripts/management &   # then, from Windows PowerShell: Invoke-WebRequest http://localhost:5100/healthz
git status --short
find server tests -path '*/obj/*' -maxdepth 4 -type d | grep -v Unix
```

Expected: build clean; every test project green (Geometry, Content, Protocol, Management — at least 80 in Management); `/healthz` reachable from Windows over forwarded localhost; no `obj/` outside `obj/Unix/`; `git status` shows only the intended files and never `.env`.

- [ ] **Step 4: Commit**

```bash
git add scripts/management CLAUDE.md
git commit -m "Record the management API in CLAUDE.md and add its run script"
```
