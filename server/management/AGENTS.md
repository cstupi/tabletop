# server/management

`Tabletop.Management` (`net10.0`, ASP.NET Core minimal APIs): the `/v1` HTTP API for accounts, the content library, and game creation. Consumed by `web/`, the Godot client (`ManagementClient`), `sim/` and `viewer/`. Game play is the game server's job.

## Layout

- `Program.cs`: DI, middleware order, endpoint mapping, `/healthz`.
- `Configuration/`: `ManagementOptions` (every setting), `DotEnv` (loads the repo-root `.env`).
- `Identity/`
  - `StytchClient`: the only code that calls Stytch (basic auth, snake_case JSON).
  - `StytchAuthentication`: JWT bearer setup, `AccountClaim = "account_id"`, `RolesClaim = "roles"`. `StytchJwks`: key cache.
  - `AuthEndpoints` (`/v1/auth/*`), `AccountEndpoints` (`/v1/account`), `IAccountRepository` / `AccountRepository`, `AccountDeleter`.
  - `Sessions`: the post-authenticate step. `DevSessions`: the Development-only token mint.
- `Content/Assets/`: `AssetEndpoints`, `IAssetRepository` / `AssetRepository`, `FolderEndpoints`, `IFolderRepository` / `FolderRepository`, `IBlobStore` / `S3BlobStore`, `GlbHeader`, `StarterAssets`, `StarterMeshes`.
- `Content/Armies/`: `ArmyForgeClient` (fetch), `ArmyForgeImport` (list to army), `ArmyEndpoints`, `IArmyRepository` / `ArmyRepository`, `ICatalogKinds` / `CatalogKinds`.
- `Content/Layouts/`: `LayoutEndpoints`, `ILayoutRepository` / `LayoutRepository`. `Content/VolumeRows.cs`: asset volume rows.
- `Games/`: `GameEndpoints`, `IGameRepository` / `GameRepository`, `ManifestEndpoint` (`GET /v1/games/{id}/assets`), `PropertiesEndpoint` (`GET /v1/properties`; `GET /v1/properties/{hash}/rules`, a pinned set's catalog entries with `rule_text`, 404 for an unknown hash).
- `Http/`: `ClientVersionMiddleware`, `Keyset` / `Page<T>`, `CurrentAccount`, `Problems`.
- `Persistence/`: `Database` (one `NpgsqlDataSource`), `Migrator` (DbUp), `IMigrator` / `PostgresMigrator` (the startup step), `Migrations/NNN_*.sql` (embedded resources).

## How it works

- Login: the client calls `/v1/auth/{otp,otp/verify,password,password/signup,password/reset,password/reset/complete,session/refresh}`. Each Stytch authenticate result goes through `Sessions.Complete`.
- `Sessions.Complete` ensures the account row exists, links the Stytch user's `external_id` to the account id, seeds the starter miniatures, and stamps `account_id` and `roles` (the Stytch user's RBAC role ids, the authenticate response's `user.roles`) into the session's custom claims through `StytchClient.StampAccount`. A refreshed session keeps its stamp unless the user's Stytch roles differ from the ones it carries, when it is stamped again, so a role change reaches the next refresh.
- A new account id is a v7 UUID. The link to Stytch is written before the row insert, so a retry leaves no orphan and the next login inserts a missing row.
- Authenticated requests carry the Stytch session JWT. Verification: RS256, issuer `stytch.com/{projectId}`, audience the project id, keys from `StytchJwks`. `CurrentAccount.Id` reads the `account_id` claim and throws `UnauthorizedAccessException` (401) without it.
- Assets: `POST /v1/assets` returns a presigned PUT URL, the client uploads to MinIO, `POST /v1/assets/{id}/confirm` checks the object size and the GLB header (`GlbHeader`) and marks the blob uploaded. `GET /v1/assets/{id}/download` returns a presigned GET.
- Blobs are keyed by owner and content hash (`IBlobStore.Key`). Presigned PUT URLs cannot enforce size; the confirm size check is the only guard.
- Starter miniatures: `StarterAssets.SeedIfNeeded` runs at login while `account.starter_seeded_at` is null. It puts `StarterMeshes.All` (deterministic in-memory GLBs) into the blob store, then `IAssetRepository.AddStarter` files them in a `Starter` folder. The blob writes have a 5 s budget (`StarterAssets.Budget`), so an unreachable MinIO holds a login for at most that. A failure is logged and never blocks login; the next login tries again.
- Armies: `POST /v1/armies/import` takes an Army Forge share link or list id, fetches `https://army-forge.onepagerules.com/api/tts?id=...`, and maps it with `ArmyForgeImport`. `bases` (`round` by default, or `square`) picks which of Army Forge's two published bases every slot gets; height is `HeightFromBase` (1.5 × the larger dimension) of that base. Only Grimdark Future and Age of Fantasy lists import. Fetch failures return 502. `ArmyBookAdditions` gives a model carrying Takedown Shot or Takedown Strike the rule's attack as a weapon named after it (army-book rules spec → Readings, batch 3).
- Layouts: `/v1/layouts` stores a `TableState` per owner. Folders: `/v1/folders` nest and hold assets via `PUT /v1/folders/{id}/assets/{assetId}`.
- `DELETE /v1/account` (`AccountDeleter`): deletes the games the account hosted (`game.created_by`) with their logs, leaves the games it joined through `IGameRepository.Leave`, then the account row (content cascades), its blobs and the Stytch user.
- Games: create, list, get, join, leave, end, delete under `/v1/games`. The list is public and each `GameSummary` carries `ParticipantNames`. `GameRepository.Create` generates the game's `rng_seed` once. `GET /v1/games/{id}/assets` returns presigned downloads (60 min) for participants only.
- Lists page by keyset: `limit` (default 50, max 200) and `after`; the cursor is the last item's id because ids are v7. `Page<T>.Next` is null on a short page.
- `ClientVersionMiddleware` reads `Tabletop-Client-Version` (`ProtocolVersion.Header`) on every request. Below `MinimumClientVersion` gives `ProtocolVersion.UpgradeRequiredStatusCode`; a non-integer gives 400; absent is accepted.
- Rows by writer: management writes a game's rows at creation and at end/leave/delete. The game server writes them only while the game is `lobby` or `setup`, plus `game.status` `playing → ended` when its log ends the game.

## Configuration

- `ManagementOptions.From` reads `TABLETOP_`-prefixed environment variables (`AddEnvironmentVariables("TABLETOP_")`), names below without the prefix. `DotEnv.Load` fills the environment from the nearest `.env` above the binary; an existing variable wins.
- Required: `DB_CONNECTION`, `S3_ENDPOINT`, `S3_BUCKET`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `STYTCH_PROJECT_ID`, `STYTCH_SECRET`. A missing one fails startup.
- Optional: `STYTCH_BASE_URL` (default `https://test.stytch.com`), `WEB_URL` (default `http://localhost:5173`; the only CORS origin, and the base of the password-reset redirect), `MANAGEMENT_URL` (default `http://0.0.0.0:5100`), `MIN_CLIENT_VERSION` (default `ProtocolVersion.Current`), `S3_PUBLIC_ENDPOINT`.
- `S3_PUBLIC_ENDPOINT` is the address presigned URLs name, for a client that reaches MinIO at a different address than the server. SigV4 signs the host, so `S3BlobStore` presigns with a second client on `S3SigningEndpoint` (public endpoint, else `S3_ENDPOINT`). Compose sets it to `http://127.0.0.1:9000` beside `S3_ENDPOINT=http://minio:9000`.
- `STYTCH_SECRET` is never in a file: `dotnet user-secrets set STYTCH_SECRET <secret>` in `server/management` (`UserSecretsId` `tabletop-management`), or `TABLETOP_STYTCH_SECRET` in `.env` for the compose container.
- `ManagementOptions` is registered as a factory so `WebApplicationFactory` overrides apply. It is resolved right after `Build()`, then `IMigrator.Run`.

## Identity and the Stytch dashboard

- Password reset sends `{WEB_URL}/reset-password` as the redirect. It must be registered as a `RESET_PASSWORD` redirect URL in the Stytch project dashboard (`http://localhost:5173/reset-password` locally), or resets fail at Stytch.
- The `account_id` claim needs no dashboard setting. The `roles` claim carries whatever roles the Stytch user has: an admin (who watches any game on the game server) is a user assigned the `admin` role of the project's RBAC policy in the dashboard.
- `Sessions.Problem` maps Stytch errors: 401/404 to 401, `duplicate_email` to 409, 429 to 429, 5xx to 502, other to 422.
- `POST /v1/dev/sessions` (`{ "name": "...", "roles": ["admin"] }`, `roles` optional and stamped in place of the Stytch user's) exists only when the host environment is Development (`DevSessions` and `MapDevSession` are both gated). It derives a name-based v8 account id, creates the Stytch user at `<name>@example.com` if absent, mints a real Stytch session through an embeddable magic-link token, and runs `Sessions.Complete`. The simulator's socket driver uses it.
- `./scripts/management` sets `ASPNETCORE_ENVIRONMENT=Development`. Compose publishes port 5100 on loopback only because of this route.

## Persistence

- Endpoints and services depend on the repository interfaces; the SQL classes implement them (Npgsql plus Dapper, SQL inline) and are registered in `Program.cs`. A new query is a member on the interface, the SQL class and the in-memory fake. `Database` forces `Search Path=management,game,public`, so SQL names tables bare.
- `Migrator.Run` runs at startup through `IMigrator`: DbUp over the embedded `Persistence/Migrations/*.sql`, one transaction per script, journal `public.schema_version`. It strips the connection's `Search Path` for DbUp, so every script names its schema.
- `Migrator` baselines a database created by compose's init SQL (the `management.account` table exists, no journal) by recording `001_schema.sql` as applied.
- `001_schema.sql` is a copy of `scripts/postgres/init/001_schema.sql`. Neither is edited. A schema change is a new `NNN_*.sql` here, and `docs/schema.md` follows.

## Commands

```bash
docker compose up -d                  # Postgres and MinIO (full stack: management and game as dotnet-watch containers)
./scripts/management                  # host run on :5100, Development; needs .env and the Stytch secret
dotnet build server/management
dotnet test tests/Tabletop.Management.Tests                                   # unit: nothing running
dotnet test tests/Tabletop.Management.Tests --settings tests/e2e.runsettings  # E2E: docker compose up -d
```

- Under compose, the `management` service runs `dotnet watch` with its own build slot (`TabletopBuildSlot=-management`), `TABLETOP_DB_CONNECTION` host `postgres`, and `TABLETOP_S3_ENDPOINT=http://minio:9000`. The game server container waits on its health check.
- Host `.env` addresses are `127.0.0.1`, never `localhost`.
- `/healthz` returns `{ "status": "ok" }`.

## Tests

- `tests/Tabletop.Management.Tests`: xUnit over `ManagementFactory` (`WebApplicationFactory<Program>`), which registers the in-memory fakes in `Infrastructure/`: an `InMemory*` per repository interface over one `InMemoryRows` (keys, foreign keys and cascades of the migrations), `InMemoryBlobStore` (its `Client()` serves the presigned URLs), and a no-op `IMigrator`. Database and S3 settings name a closed port. Environment is `Testing`; `DevSessionTests` sets `HostEnvironment` to Development.
- Tests seed what no repository writes (Properties sets, catalog entries, lobby picks, frozen armies, a game's table, the event log) through `InMemoryRows`.
- `Contracts/RepositoryContract` runs against the fakes (unit) and the SQL classes on a `TestDatabase` (E2E); `BlobStoreContract` against `InMemoryBlobStore` and `S3BlobStore`. A fake's behaviour changes with its SQL class, and the contract test with both. `MigrationTests` is E2E.
- `FakeStytch`, `FakeArmyForge` and `TestKeys` replace the network; tests sign their own JWTs. `Fixtures/` and `TestGlb` supply files.
- Config overrides in tests are in-memory keys without the `TABLETOP_` prefix.

## Pitfalls

- Add endpoints in the repositories' owner-scoped style: take the account from `CurrentAccount.Id`, never from the request.
- The web app never calls `/v1/games*`, `/v1/properties` or `/v1/layouts`.
- `GameRepository` row types use `DateTime` for `timestamptz`, because Dapper matches constructors on the reader's field type.
- Dapper maps records by constructor parameter names, so SQL aliases are PascalCase (`as Id`).

## Docs

- `docs/design/management-api/design.md`
- `docs/superpowers/specs/2026-08-29-management-api-design.md`, `2026-08-29-accounts-design.md`, `2026-08-29-identity-and-enumerations-design.md`, `2026-08-28-asset-library-design.md`, `2026-08-28-army-design.md`, `2026-08-29-setup-content-design.md`, `2026-10-01-starter-meshes-design.md`, `2026-09-06-simulation-design.md`, `2026-10-09-admin-watch-design.md`
- `docs/schema.md`, `docs/data-model.md`
