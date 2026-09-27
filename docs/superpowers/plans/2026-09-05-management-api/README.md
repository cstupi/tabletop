# Management API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `server/management/` — the ASP.NET Core HTTP API over `docs/schema.md`: Stytch-backed login and accounts, the asset library with presigned MinIO upload and download, armies imported from Army Forge, layouts, games and the public game browser — with its migrations, its integration tests against the Docker stack, and unit tests for everything with logic.

**Architecture:** One minimal-API project, `Tabletop.Management`, in four modules — Identity, Content (assets, folders, armies, layouts), Games, and the HTTP plumbing (version check, Stytch JWT verification, keyset paging, ProblemDetails). Persistence is hand-written SQL over Npgsql with Dapper for row mapping; migrations are DbUp scripts embedded in the assembly, the first of which is the contracts plan's `001_schema.sql`. `Tabletop.Content` and `Tabletop.Protocol` types are the response bodies wherever a contract type already exists (`Army`, `TableState`, `GameInfo`, `ProtocolVersion`); everything else is a DTO of this project. Blob bytes never pass through the API: the S3 client mints presigned URLs against MinIO and the only server-side read is a ranged GET of the GLB header.

**Tech Stack:** .NET 10 (`net10.0`, C# 12) minimal APIs; Npgsql 10.0.3 + Dapper 2.1.79; dbup-postgresql 6.1.5; AWSSDK.S3 4.0.102.5 (MinIO, path-style); Microsoft.AspNetCore.Authentication.JwtBearer 10.0.11; xUnit 2.9.3 + Microsoft.AspNetCore.Mvc.Testing 10.0.11; Stytch Consumer API (test project `project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43`, base `https://test.stytch.com`); Army Forge `GET https://army-forge.onepagerules.com/api/tts?id={id}`.

**Spec:** `docs/superpowers/specs/2026-08-29-management-api-design.md` (primary); `2026-08-29-accounts-design.md`; `2026-08-28-army-design.md`; `2026-08-28-asset-library-design.md`; `2026-08-29-setup-content-design.md`; `2026-08-29-web-app-design.md`; `2026-09-04-mesh-conversion-design.md`; `2026-09-04-client-shell-design.md`; `2026-09-04-setup-phase-design.md`; `2026-08-29-identity-and-enumerations-design.md`; `2026-08-29-content-values-design.md`; `2026-09-04-whole-design-review.md` (newest, wins); `docs/schema.md`; `docs/architecture.md`. Earlier plan obeyed: `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md` (its *Interfaces → Produces* names are used verbatim).

**Requires landed:** the contracts plan (`libs/`, `docker-compose.yml`, `.env.example`, `scripts/postgres/init/001_schema.sql`). May run alongside the geometry and game-server plans.

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`; root namespace `Tabletop.Management` (matches `client/Tabletop.csproj` conventions); `Directory.Build.props` puts `obj/` under `obj/$(OS)/`; `global.json` pins SDK `10.0.400`.
- Identifiers are `Guid.CreateVersion7()` everywhere; no integer ids; ids are generated in application code before any external system hears of them (`architecture.md` → Game management data model).
- `libs/` stays persistence-free: no ORM entities, attributes or `DbContext`. All row mapping lives in this project (`architecture.md` → Persistence).
- No rules engine code, no legality, no `Properties` interpretation here. Management stores `properties` rows and serves `(content_hash, name, game_rules)`; the game server owns the catalog types (contracts plan → *`Properties` types are not in `libs/`*).
- Management is the only thing that talks to Stytch; no Stytch identifier is stored in our schema; `external_id` = `account.id` (accounts → Auth is a module, not a vendor). We issue no token of our own (management API → We issue no tokens of our own).
- Auth on every non-`/v1/auth/*` request is a Stytch session JWT verified locally against `GET {StytchBaseUrl}/v1/sessions/jwks/{project_id}` (RS256, `iss = stytch.com/{project_id}`, `aud = {project_id}`); the account id rides in the custom claim `account_id`. No call to Stytch on the request path.
- The `account_id` claim is put there by the project's **Stytch custom claim template**, `{"account_id": "{{ user.external_id }}"}`, evaluated by Stytch every time it mints a JWT — not by our code (ruling 1). Management passes `session_custom_claims` nowhere; its only job is to keep `external_id` set to the account id.
- Version check: header `Tabletop-Client-Version` (`ProtocolVersion.Header`); below the minimum → `426` (`ProtocolVersion.UpgradeRequiredStatusCode`), no body work done (management API → The version check).
- Authorisation is owner-only throughout, except `GET /v1/games` (public) and `GET /v1/games/{id}/assets` (participants). A row another account owns is `404`, never `403` (management API → The rest of the surface).
- Asset bytes never pass through the API: presigned PUT and GET against `TABLETOP_S3_ENDPOINT`, path-style, key `{owner_id}/{hash}`; GLB accepted, STL rejected; the triangle check warns above **100,000** and never blocks (management API → Asset bytes never pass through the API; mesh conversion → The threshold).
- Dedup is per user: `blob` is keyed `(owner_id, hash)`; the client computes the SHA-256 first (`architecture.md` → Assets are a library).
- Pagination is keyset: `limit` + `after` (a UUIDv7); on assets, armies, the game browser only (management API → Pagination is keyset).
- Local only: Postgres and MinIO from `docker-compose.yml`; configuration from `TABLETOP_*` environment variables and `dotnet user-secrets`; Stytch secret never committed (`architecture.md` → Hosting is deferred).
- The game server writes a game's rows while `status` is `lobby` or `setup` — `game.status` (the two transitions), `team`, `participant.team_id`, `participant.army_id`, `participant.army_snapshot`, and the `table_state` tree where `game_id` is set. Management never writes those columns except: `game.status = 'ended'` on `/end` and on last leave; participant insert on create/join and delete on leave; whole-game delete (whole-design review → Five rulings, 4; setup phase → The lifecycle). Everything else in the management schema is management's alone.
- Test-driven throughout: unit tests for logic (GLB header, import mapping, version check, JWT verification, dedup, authorisation), integration tests through `WebApplicationFactory` against the Docker Postgres and MinIO. Every task ends green and committed (imperative one-line message, `Co-Authored-By` trailer naming the model).

---

## File Structure

```
Tabletop.sln                                      modified: + Tabletop.Management, + Tabletop.Management.Tests
.env.example                                      modified: + TABLETOP_STYTCH_*, TABLETOP_MANAGEMENT_URL, TABLETOP_WEB_URL
CLAUDE.md                                         modified: commands, layout, environment
server/management/
  Tabletop.Management.csproj                      UserSecretsId, embedded Migrations/*.sql
  Program.cs                                      composition root; `public partial class Program` for tests
  appsettings.json                                non-secret defaults (MinimumClientVersion, StytchBaseUrl, SessionMinutes)
  Configuration/ManagementOptions.cs              typed options bound from TABLETOP_* keys
  Configuration/DotEnv.cs                         loads the repo-root .env into the process, unconditionally
  Http/ClientVersionMiddleware.cs                 426 below the minimum
  Http/Keyset.cs                                  limit/after parsing and the Page<T> envelope
  Http/CurrentAccount.cs                          account id from the JWT claim
  Http/Problems.cs                                ProblemDetails helpers (401/404/409/422/429/502)
  Persistence/Database.cs                         NpgsqlDataSource + Dapper settings
  Persistence/Migrator.cs                         DbUp over embedded scripts
  Persistence/Migrations/001_schema.sql           copy of scripts/postgres/init/001_schema.sql
  Persistence/Migrations/002_blob_uploaded.sql    blob.uploaded, blob.content_type
  Identity/StytchJwks.cs                          fetch + cache the JWKS; key resolver
  Identity/StytchAuthentication.cs                JwtBearer wiring
  Identity/StytchClient.cs                        the fourteen Stytch calls (typed HTTP)
  Identity/AccountRepository.cs                   account rows
  Identity/Sessions.cs                            ensure-account, link external_id, re-mint on the first login
  Identity/AuthEndpoints.cs                       /v1/auth/*
  Identity/DevSessions.cs                         POST /v1/dev/sessions; mapped only in a development environment
  Identity/AccountEndpoints.cs                    /v1/account
  Identity/AccountDeleter.cs                      total deletion
  Content/Assets/IBlobStore.cs, S3BlobStore.cs    presign, ranged read, delete
  Content/Assets/GlbHeader.cs                     triangle count from the JSON chunk
  Content/Assets/AssetRepository.cs, AssetEndpoints.cs, AssetDtos.cs
  Content/Assets/FolderRepository.cs, FolderEndpoints.cs
  Content/Armies/ArmyForgeClient.cs               GET api/tts?id=
  Content/Armies/ArmyForgeList.cs                 the export's JSON shape
  Content/Armies/ArmyForgeImport.cs               list → Tabletop.Content.Army
  Content/Armies/ArmyRepository.cs, ArmyEndpoints.cs
  Content/Layouts/LayoutRepository.cs, LayoutEndpoints.cs
  Games/GameRepository.cs, GameEndpoints.cs, GameDtos.cs, ManifestEndpoint.cs, PropertiesEndpoint.cs
tests/Tabletop.Management.Tests/
  Tabletop.Management.Tests.csproj
  TestDatabase.cs                                 one database per test run, migrated, dropped
  TestKeys.cs                                     RSA key + JWT minting + fake JWKS
  FakeStytch.cs                                   HttpMessageHandler stub for Stytch
  ManagementFactory.cs                            WebApplicationFactory<Program> with the fakes
  Fixtures/army-forge-c2-6YsWRH3py.json           a real export
  *Tests.cs                                       per task
```

Files are split by module and by responsibility (repository = SQL, endpoints = HTTP, client = an external system). `server/management/` is the project root so the SDK's `**/*.cs` glob covers every module folder without `.csproj` edits.

## Tasks

One file per task, in order; each ends green and committed.

- [Task 1: Project, solution, configuration and the test host](task-01-project-and-configuration.md)
- [Task 2: Migrations, the data source and the test database](task-02-migrations-and-test-database.md)
- [Task 3: HTTP plumbing — the version check, keyset paging, problems, JSON](task-03-http-plumbing.md)
- [Task 4: Stytch JWT verification, the current account, `GET /v1/account`](task-04-jwt-verification-and-account.md)
- [Task 5: The Stytch client, session completion, `/v1/auth/*` and the development-only mint](task-05-stytch-client-and-auth.md)
- [Task 6: The blob store and the GLB header read](task-06-blob-store-and-glb-header.md)
- [Task 7: Assets — create, confirm, download, library CRUD, volumes](task-07-assets.md)
- [Task 8: Folders](task-08-folders.md)
- [Task 9: Army Forge — the export's shape and the import mapping](task-09-army-forge-import.md)
- [Task 10: Armies — import, list, get, patch, delete](task-10-armies.md)
- [Task 11: Layouts](task-11-layouts.md)
- [Task 12: Properties, games, the public browser and the manifest](task-12-games-and-manifest.md)
- [Task 13: The account — rename, and total deletion](task-13-account-deletion.md)
- [Task 14: Run script, CLAUDE.md, and the whole-tree check](task-14-run-script-and-whole-tree-check.md)

## Interfaces → Produces, whole project

Namespace `Tabletop.Management` unless stated. Later plans (game server, web app, client shell) copy these verbatim.

### HTTP surface

| Method and path | Body → response | Auth |
| --- | --- | --- |
| `GET /healthz` | → `{status}` | none |
| `POST /v1/auth/otp` | `EmailRequest` → `OtpSentResponse(MethodId)` | none |
| `POST /v1/auth/otp/verify` | `OtpVerifyRequest(MethodId, Code)` → `SessionResponse(SessionJwt, Account)` | none |
| `POST /v1/auth/password` | `PasswordRequest(Email, Password)` → `SessionResponse` | none |
| `POST /v1/auth/password/signup` | `PasswordRequest` → `201 SessionResponse` | none |
| `POST /v1/auth/password/reset` | `EmailRequest` → `202` | none |
| `POST /v1/auth/password/reset/complete` | `ResetCompleteRequest(Token, Password)` → `SessionResponse` | none |
| `POST /v1/auth/session/refresh` | `RefreshRequest(SessionJwt)` → `SessionResponse` | none (the JWT may be expired) |
| `DELETE /v1/auth/session` | → `204` | bearer |
| `GET /v1/account`, `PATCH /v1/account` (`RenameRequest`), `DELETE /v1/account` | `AccountRow(Id, Name)` / `204` | bearer |
| `GET /v1/assets?limit&after&kind` | → `Page<AssetDto>` | owner |
| `POST /v1/assets` | `CreateAssetRequest` → `201 CreateAssetResponse(Asset, UploadUrl?)` | owner |
| `GET/PATCH/DELETE /v1/assets/{id}` | `PatchAssetRequest` → `AssetDto` / `204` | owner |
| `POST /v1/assets/{id}/confirm` | → `ConfirmResponse(Asset, Triangles?, Warning?)` | owner |
| `GET /v1/assets/{id}/download` | → `DownloadResponse(Url, ExpiresAt)` | owner |
| `GET/PUT /v1/assets/{id}/volumes` | `Volume[]` → `TerrainVolume[]` | owner |
| `GET /v1/folders`, `POST` | `FolderRequest(Name, ParentId)` → `FolderDto(Id, ParentId, Name, AssetIds)` (`GET` the whole tree as a list; `POST` → `201`) | owner |
| `PATCH /v1/folders/{id}`, `DELETE /{id}`, `PUT/DELETE /{id}/assets/{assetId}` | `FolderRequest` for `PATCH`; all four → `204`, no body | owner |
| `POST /v1/armies/import` | `ImportRequest(Source)` → `201 Tabletop.Content.Army` | owner |
| `GET /v1/armies?limit&after` | → `Page<ArmySummary>` | owner |
| `GET/PATCH/DELETE /v1/armies/{id}` | `PatchArmyRequest(Name?, Units?, Slots?)` → `Army` / `204` | owner |
| `GET /v1/layouts`, `POST` (`TableState` with `Name`), `GET/PATCH/DELETE /{id}` | `PatchLayoutRequest(Name?, State?)` → `LayoutSummary[]` / `TableState` / `204` | owner |
| `GET /v1/properties` | → `PropertiesSummary(ContentHash, Name, GameRules)[]` | bearer |
| `GET /v1/games?limit&after` | → `Page<GameSummary>` | bearer (public) |
| `POST /v1/games` | `CreateGameRequest(Name, PropertiesHash, SchedulerShape, Enforcement, WaitsBetweenSteps)` → `201 Tabletop.Protocol.GameInfo` (`RngSeed` is generated server-side, not requested) | bearer |
| `GET /v1/games/{id}` | → `GameInfo` | bearer (public) |
| `POST /v1/games/{id}/join` | → `GameInfo` | bearer |
| `POST /v1/games/{id}/leave` | → `204` | participant |
| `POST /v1/games/{id}/end`, `DELETE /v1/games/{id}` | → `204` | creator |
| `GET /v1/games/{id}/assets` | → `Manifest(Assets: ManifestEntry(AssetId, Kind, Hash, Url, Scale, Orientation)[], ExpiresAt)` | participant |
| **`POST /v1/dev/sessions`** — **development only, not a product endpoint** | `DevSessionRequest(Name)` → `SessionResponse` | none, and the route exists only when `IHostEnvironment.IsDevelopment()` |

The last row is the simulator's token source and nothing else calls it. It is mapped inside `if (app.Environment.IsDevelopment())`, so a build whose environment is unset — the default, `Production` — has no such route and answers `404`. No client, no web app and no product feature may use it.

Conventions: JSON per `ContentJson.Configure` (camelCase, snake_case enums, nulls omitted); errors are `application/problem+json`; `Page<T>(Items, Next)` with `?limit=` (default 50, max 200) and `?after=<uuid>`; `Tabletop-Client-Version` below `MinimumClientVersion` → `426` (a missing header is accepted); another owner's row → `404`; CORS for the origin `TABLETOP_WEB_URL` with `Authorization`, `Content-Type` and `Tabletop-Client-Version` allowed and the five methods above (Task 3).

### Types and services

| Type | Members |
| --- | --- |
| `Configuration.ManagementOptions` | `DbConnection, S3Endpoint, S3Bucket, S3AccessKey, S3SecretKey, StytchProjectId, StytchSecret, StytchBaseUrl, WebUrl, MinimumClientVersion, SessionMinutes`; `static From(IConfiguration)` |
| `Configuration.DotEnv` | `static Load()` |
| `Http.ClientVersion` / `ClientVersionMiddleware` | `Decide(string?, int) : ClientVersionVerdict` |
| `Http.Keyset`, `Http.Page<T>`, `Http.Problems`, `Http.CurrentAccount` | `Keyset.Parse(int?, Guid?)`; `Page<T>.Of(items, limit, id)`; `NotFound/Unauthorized/Conflict/Unprocessable/BadGateway/TooManyRequests`; `CurrentAccount.Id(ClaimsPrincipal)` |
| `Persistence.Database`, `Persistence.Migrator` | `Open()`, `Source`; `Migrator.Run(connectionString)` |
| `Identity.StytchJwks`, `Identity.StytchAuthentication` | `Resolve(kid)`; `AddStytchJwt(this IServiceCollection)`, `AccountClaim = "account_id"`, `SessionClaim` (declared; nothing reads it), `Issuer(projectId)`, `JwksClient` |
| `Identity.StytchClient` | the fourteen calls listed in Task 5, four of them reached only by the development mint; `HttpClientName = "stytch"`; `StytchException(Status, ErrorType)`, where a transport failure or a non-JSON body is a 5xx with the synthetic types `transport` and `unparseable_response` |
| `Identity.Sessions` | `Complete(StytchSession) : SessionResponse`; `static Problem(StytchException)` |
| `Identity.DevSessions`, `Identity.DevSessionEndpoint` | `Mint(name) : SessionResponse`, `static AccountIdFor(name) : Guid`, `static EmailFor(name) : string`; `MapDevSession` — registered only under a development environment |
| `Identity.AccountRepository`, `Identity.AccountDeleter` | `Get/Insert/Rename`; `Delete(accountId)` |
| `Content.VolumeRow` | `ToVolume()`, `From(id, Volume)`, `Columns/InsertColumns/InsertValues` |
| `Content.Assets.IBlobStore`, `S3BlobStore` | `Key(owner, hash)`, `PresignUpload(key, contentType, ttl)` (a query-string presigned PUT carries no size condition; confirm's size check is the only enforcement), `PresignDownload`, `Size`, `ReadRange`, `Delete` |
| `Content.Assets.GlbHeader` | `IsGlb`, `JsonChunkLength`, `CountTriangles`, `HeaderBytes = 20`, `WarnAbove = 100_000` |
| `Content.Assets.AssetRepository`, `FolderRepository` | per Tasks 7 and 8 |
| `Content.Armies.ArmyForgeClient`, `ArmyForgeImport`, `CatalogKinds`, `ArmyRepository` | `ListId`, `Parse`, `Fetch`; `ToArmy(list, source, catalog)`, `ParseBase`, `GameRules`, `DefaultBody`, `HeightScale`, `HeightFromBase`; `For(gameRules)`; `Insert/Get/List/Patch/Delete` |
| `Content.Layouts.LayoutRepository` | `Save/Get/List/Patch/Delete`, `static ReadTree` |
| `Games.GameRepository` | `ListProperties/Create/List/Get/IsParticipant/IsCreator/Join/Leave/End/Delete/ManifestAssets` |

### Migrations

`001_schema.sql` (the contracts plan's schema, baselined when the compose init already applied it) and `002_blob_uploaded.sql` (`blob.uploaded boolean`, `blob.content_type text`). Journal: `public.schema_version`.

### Rows by writer

| Table / column | Management | Game server (`lobby`/`setup` only) |
| --- | --- | --- |
| `account`, `blob`, `asset`, `asset_volume`, `folder`, `asset_folder`, the ten army tables, `table_state` with `owner_id` and its children, `properties` and the seven tables under it (read here; loaded by the content import) | writes | reads |
| `game` insert; `game.name/game_rules/properties_hash/scheduler_shape/enforcement/waits_between_steps/created_by/created_at` | writes on create | rewrites configuration on `set_configuration` |
| `game.rng_seed` | writes once on create (`Random.Shared.NextInt64()`); no endpoint updates it | never — reads it at hydration into `GameInfo.RngSeed` |
| `game.status` | `'lobby'` on create; `'ended'` on `/end` and on the last leave | `lobby → setup → playing` |
| `team` | never | inserts (`create_team`), while `lobby` or `setup` only |
| `participant` insert / delete — **joining the game**, which is a row with no team | create, join / leave | never |
| `participant.team_id` — **taking a team**, which is not joining | never | writes (`pick_team`), while `lobby` or `setup` only |
| `participant.army_id`, `participant.army_snapshot` | never (null on insert) | writes |
| `table_state` with `game_id` and its `terrain_placement`, `terrain_volume`, `marker`, `deployment_zone` | never | writes (the draft), then frozen |
| `game.game_event` | deletes with the game | appends |

---

## Interfaces needed from other modules

- **Game-server plan** — must match, not consume: verify the session JWT exactly as Task 4 does (JWKS `GET {StytchBaseUrl}/v1/sessions/jwks/{project_id}`, no auth; `iss = stytch.com/{project_id}`; `aud = project_id`; RS256; 30 s clock skew; the account id is the top-level claim `account_id`, a GUID string — `StytchAuthentication.AccountClaim`), reading `TABLETOP_STYTCH_PROJECT_ID` and `TABLETOP_STYTCH_BASE_URL` from the same `.env`; it verifies at connect only — a socket outlives the five-minute JWT and is never re-verified, and a reconnect presents whatever JWT the client then holds; write only the rows in *Rows by writer* above and set `participant.army_id` on `pick_army` (the manifest reads it before the snapshot exists); read `game.rng_seed` into `GameInfo.RngSeed` at hydration and never write it — this project generates it once in `GameRepository.Create` and the column has no default, so it is a value the game server can rely on being there; on `replace_draft` from a loaded layout, regenerate every child id, because `terrain_placement`, `terrain_volume`, `marker` and `deployment_zone` ids are global primary keys and the layout's rows keep theirs; write `army_snapshot` as `ContentJson.Serialize(ArmySnapshot)` (the manifest deserialises it with `ContentJson`); seed the OPR `stat_definition` names `Quality` and `Defense` (unit, and at model level too — the interpreter bullet below says why) and `Range` and `Attacks` (weapon), which import writes as stat keys; load at least one `properties` row, without which `POST /v1/games` answers `422` and no game can be created.
- **Interpreter plan** — **no Age of Fantasy content set exists.** `aof` lists import (ruling 5), and a game pins a content set by `properties.content_hash`, so a player can import an Age of Fantasy army and then have nothing to play it with: the only authored document is `grimdark-future-core.properties.json`, whose catalog is Grimdark Future's army books and spells, and that plan owns both the documents and `PropertiesSeeder`. Age of Fantasy's units, rules and spells differ even though its mechanics do not, so the set is content to author, not code to write, and nothing in this plan can stand in for it. This project does not detect the mismatch either: `army.game_system` and `properties.game_rules` are both `grimdark-future` for an Age of Fantasy army, which is exactly what makes one rules implementation serve both, so `pick_army` sees no conflict. Recorded, not solved.
- **Interpreter plan** — two consequences of ruling 2 (a joined hero is a model of the unit it joined) for the OPR properties document (`grimdark-future-core.properties.json`, its Task 3) and the readers over it. First, `Quality` and `Defense` are declared there at `unit` level only, and a joined hero's slots carry both as slot stats, so the document should declare the same two names at `model` level as well: `stat_definition` is where a stat's kind at a level is declared, and `slot_stat` has no foreign key to it, so nothing catches the gap. A slot's `Quality` is read: a joined hero attacks at its own Quality and blocks on the unit's Defense until every other model is dead (GF p.4 tests against *"the model's Quality value"*, and the Hero rule overrides `Defense` alone: *"must use the unit's Defense until all other models have been killed"*); the interpreter plan's `GfRolls.HitQuality`, `GfAttack.Attacks` and `GfRolls.DefensePlan` implement it. The declaration is what keeps a stored stat a declared one either way. Second, every rule a joined hero carries lands on its slot whatever its catalog kind, so a `unit`-kind entry sitting on a slot must mean the unit has the rule, in `LiveProperties.Gather` and in `Lookup.UnitHas` alike (the interpreter plan's Task 6 reads it off the unit's definition rather than its living models). `Aircraft`, `Ambush` and `Scout` are the only `unit`-kind entries in the OPR core set, so this is what puts a joined hero's `Ambush` into the scheduler's deployment set. Import produces that shape for a rule with `count < size` too; a hero merge makes it ordinary.
- **Web-app plan** — consumes the HTTP surface above; hosts `/reset-password` at `TABLETOP_WEB_URL` and reads `?token=` from Stytch's redirect, then calls `POST /v1/auth/password/reset/complete`; computes the SHA-256 before `POST /v1/assets`; PUTs the bytes to `UploadUrl` with the same `Content-Type` it declared; calls `/confirm` and shows `Warning`; writes the bounding-box volume with `PUT /v1/assets/{id}/volumes`. Gets CORS for `TABLETOP_WEB_URL` from Task 3 (its request). Does not get a way to rename units or model slots: the army spec gives the user "everything about appearance and physical presence" and the list "composition and loadout"; names are the list's, so `UnitPatch` and `SlotPatch` stay without `Name?` and the editor shows them read-only (a one-column change in `ArmyRepository.Patch` if that is ever wanted).
- **Client-shell plan** — consumes `/v1/auth/*`, `/v1/account`, `/v1/games*` including the manifest `GET /v1/games/{id}/assets` (whose `ManifestEntry.Scale/Orientation` it applies to another participant's mesh), `/v1/properties`, `/v1/armies` (list and get only), `/v1/layouts` (list, get, `POST` to save), and three owner-only asset routes for the setup toolbar: `GET /v1/assets?kind=terrain|texture&limit=200`, `GET /v1/assets/{id}/volumes`, `GET /v1/assets/{id}/download`; sends `Tabletop-Client-Version: {ProtocolVersion.Current}` on every request; refreshes with `POST /v1/auth/session/refresh` before the five-minute JWT lapses (once a minute, per the management API's working figures). Its `ManagementDtos.cs` mirrors `AccountRow`, `SessionResponse`, `OtpSentResponse`, `Page<T>`, `GameSummary`, `CreateGameRequest`, `PropertiesSummary`, `ArmySummary`, `LayoutSummary`, `AssetKind`, `AssetDto`, `DownloadResponse`, `ManifestEntry`, `Manifest` member-for-member; a change to any of these here is a change there.
- **Simulation plan** — consumes `POST /v1/dev/sessions` for `--driver socket`. `SocketCredentials` calls it once per team plus once for the recorder, and `SocketRowSeeder` inserts `participant` rows for the account ids it comes back with. It therefore needs the management API running under `ASPNETCORE_ENVIRONMENT=Development` with the Stytch secret set, which `./scripts/management` does, and it needs network access to the Stytch test project, which the in-process driver does not.
- **Stytch dashboard** (the test project named in `.env.example`) — two pieces of project state this code depends on and does not create, plus a note on managing the second:
  - `TABLETOP_WEB_URL/reset-password` must be a registered redirect URL for password resets; the redirect URLs registered for OAuth and magic links can be removed.
  - The **session custom claim template** must be set to exactly `{"account_id": "{{ user.external_id }}"}` (dashboard → Custom Claim Templates, the `SESSION` template, for the test environment and again for live). `{{ user.external_id }}` is one of the supported variables, and the rendered object "will be used as the initial set of custom claims for all of that user's sessions", so Stytch stamps the claim on every mint with no call from us. The JSON key is ours to choose and must stay `account_id`, because both `StytchAuthentication.AccountClaim` and the game server's `StytchJwtVerifier.AccountClaim` read that name. **If it is missing or misspelled nothing errors:** every JWT mints without `account_id`, `CurrentAccount.Id` throws `UnauthorizedAccessException` on every management request, and the game server rejects every WebSocket connection — a total, silent outage with no Stytch error anywhere. Task 5's `AuthTests` catch a code regression but not a missing template, so the manual check in Task 5 Step 7 (read the JWT at jwt.io) is what proves the template exists. Limits, from the same page: total claims under 4 kB, no registered claim names (`iss`, `sub`, `aud`, `exp`, `nbf`, `iat`, `jti`, or anything under `https://stytch.com/*` — `account_id` is none of these), and template changes reach existing sessions only the next time a JWT is minted.
  - The template is also readable and writable over the Programmatic Workspace Actions API, per Stytch's OpenAPI: `GET` and `PUT https://management.stytch.com/pwa/v3/projects/{project_slug}/environments/{environment_slug}/jwt_templates/SESSION`, Basic auth with a *workspace management* key (not the project secret), `PUT` body `{"template_content": …, "custom_audience": …}` with **both fields required** — send `custom_audience` back exactly as `GET` returned it, because a non-empty value replaces `aud` and both verifiers require `aud = project_id`. Nothing in this plan calls it; it is worth knowing only as a way to read the template back in CI, or to set it on a live project without the dashboard.

## Rulings

A request with no `Tabletop-Client-Version` header is accepted — `ClientVersion.Decide` in Task 3. The version check exists *"because the Godot client is a downloaded desktop application and a user can be months behind"* (management API → `/v1` in the path), and the client shell is the document that says *"the client sends its version on every request"*; the web app is deployed with the API and is not the case the mechanism was built for (management API → The version check).

1. **The account id enters the JWT by the project's Stytch custom claim template**, `{"account_id": "{{ user.external_id }}"}`, not by a per-login `session_custom_claims`. A per-login claim costs an extra Stytch call on every login *and* on every refresh, and refresh runs once a minute per client; Stytch's custom-claim-templates page lists `{{ user.external_id }}` among the supported variables and says the rendered object "will be used as the initial set of custom claims for all of that user's sessions", so the template costs nothing at run time. Consequences, all in Tasks 4 and 5: `StytchClient.RefreshSession` takes no `customClaims` parameter and `Sessions.Complete` makes no second call on a returning login or a refresh; `external_id = account.id` is the mechanism; the template is recorded under *Interfaces needed from other modules → Stytch dashboard*, with what its absence breaks; and the one case the template cannot cover on its own — the first login, where the session is minted before `external_id` is written — is handled by re-minting the JWT once on that login only, which `AuthTests` asserts happens exactly once there and not at all afterwards.
2. **A joined hero (`joinToUnit` on a non-combined entry) is a model of the unit it joined.** No army table records the join and nothing needs adding to hold the hero: `ModelSlot` carries `Stats` and `Properties`, and `slot_stat` and `slot_property` are written and read by `ArmyRepository`, so the hero's quality and defence go on its own slot and its rules go there with them. In Task 9: `Merge` returns a host plus the entries joining it, one `Section` per entry appends that entry's slots with its own base, its own cursor and its own stats, the unit's stats and properties stay the host's, and a `joinToUnit` that resolves to nothing or to another joining entry imports as a unit of its own. Two facts follow that no code enforces: `Quality` and `Defense` are declared at unit level in the OPR properties document and are stored at model level too, and a joined hero's `unit`-kind rules sit on its slots, where the interpreter must read them as the unit's — both under *Interfaces needed from other modules*.
3. **Base and height when Army Forge gives none.** An entry with no `bases.round` gets `DefaultBody`, a 221 × 119 × 113 mm vehicle hull carrying its own height: Army Forge publishes `bases.round` reliably for infantry, so a missing base is a vehicle or monster in practice, and a 32 mm circle would measure a tank from a 1.26" circle at its hull centre (open questions → The default base for a model that publishes none). A published base gets height `HeightFromBase(base)`, `1.5 ×` the base's long axis, so a 32 mm circle starts at 1.89", a 60 mm cavalry oval at 3.54" and a 120 mm oval at 7.09". It is a guess and is meant to read as one: nothing in either ruleset publishes a height, Army Forge publishes none, and the base is the only physical dimension the export gives, so the scale is arbitrary and the player corrects it per slot through `SlotPatch.Height`. A per-`GameRules` table of heights would be the alternative and no such table exists.
4. **Partial weapon counts are dealt onto models by an advancing cursor** (open questions → Rulings): 9× Rifle, 1× Flamer puts the flamer on the last model. Import stays deterministic and expands a unit on its own; asking the player instead would need a per-slot weapon editor and a `PATCH` shape for weapons, neither designed, and the player corrects a slot afterwards.
5. **Grimdark Future and Age of Fantasy both import**: the mechanics are the same and only the content differs. `GameRules` maps `gf` and `aof` to the one `grimdark-future` rules implementation, which is what the project already records: the scheduler and interpreter plans build one OPR engine, and `architecture.md` → *Rules as data* and the geometry values spec both say Age of Fantasy's terrain rules are word for word identical to Grimdark Future's, which is why it is not a second ruleset. The consequence recorded against the interpreter plan under *Interfaces needed from other modules*: an Age of Fantasy list can be imported before any content set exists to play it with. The skirmish and regiment variants (`gff`, `aofs`, `aofr`) answer `422`; they are different mechanics, not different content.
6. **`/end` and `DELETE` on a game are the creator's alone.** The creator starts and ends setup, so ending and destroying the game belong to the same account. `GameRepository.IsCreator` guards `POST /v1/games/{id}/end` and `DELETE /v1/games/{id}`, and anyone else gets `404`, the same answer this project gives for any other row that is not the caller's (Task 3's owner-only rule). The right stays with the creator after they leave, because `game.created_by` has no `on delete` action and outlives the participant row. A game does not become undeletable when its creator's account goes: Task 13 deletes every game the account created as well as every game it played in, which is why the game set there is a union.
7. **`GET /v1/games` is newest-first.** The list filters by status, name and created-by-me, a filter set for finding something recent, and ascending by id would put every finished game ahead of the one started this evening. The query is `id < @after order by id desc` and `Page<T>.Next` still holds the last id, so the keyset cursor is unchanged.
8. **The texture kind accepts `image/webp`.** Godot 4 loads WebP natively and the table texture is downloaded by every client in the game, where the saving is real. `AssetRules.ContentTypes[AssetKind.Texture]` and the `002` check constraint carry it, and the web app's intake follows — the `ContentType` union, `classifyFiles`' extension list and its test. Miniature and terrain are unaffected; GLB is the only mesh format uploaded.
