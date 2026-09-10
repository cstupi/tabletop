# Management API

`server/management/` is the project's HTTP surface: one ASP.NET Core minimal-API process,
`Tabletop.Management`, over the `management` Postgres schema. It owns identity (Stytch login,
accounts), the asset library and its folders, armies imported from Army Forge, layouts, game
records and the public game browser, and the pinned `Properties` sets a game may choose. It holds
no live state and can restart at any time.

It is not the sole writer of the rows it defines. While a game is in `lobby` or `setup` the game
server writes that game's own rows: `game.status`, `team`, `participant.team_id`,
`participant.army_id`, `participant.army_snapshot`, and the `table_state` tree keyed by `game_id`.
Every game endpoint here is written around that split rather than around a lock.

Asset bytes never pass through the process, and the service issues no token of its own.

---

## Scope

### In

| Subject | Surface |
| --- | --- |
| Identity | `POST /v1/auth/otp`, `/otp/verify`, `/password`, `/password/signup`, `/password/reset`, `/password/reset/complete`, `/session/refresh`, `DELETE /v1/auth/session` |
| Account | `GET`, `PATCH`, `DELETE /v1/account`; deletion is total |
| Asset library | `/v1/assets` (keyset), create/confirm/download, `PATCH`, `DELETE`, `/volumes` |
| Folders | `/v1/folders`, nested, many-to-many with assets |
| Armies | `POST /v1/armies/import` from Army Forge, list (keyset), get, patch bindings and bases, delete |
| Layouts | `/v1/layouts`, a `TableState` owned by an account |
| Games | `/v1/properties`, `/v1/games` (public, keyset), create, get, join the game, leave, end, delete. Taking a team is the game server's, not here |
| Development mint | `POST /v1/dev/sessions`, a real Stytch session JWT for a named account, mapped only under a development environment |
| Manifest | `GET /v1/games/{id}/assets`, the one cross-owner read in the service |
| Plumbing | the `Tabletop-Client-Version` check, keyset paging, RFC 7807 problems, CORS, DbUp migrations |

### Not in

- **No rules.** No legality, no dice, no turn order, no interpretation of a `Properties` set. The
  service stores `properties` rows and serves `(content_hash, name, game_rules)`. The game server
  owns the catalog types (`architecture.md` → *The load-bearing seam*; contracts plan → *`Properties`
  types are not in `libs/`*).
- **No WebSocket, no live state.** Commands, events, Asks, presence and setup edits are the game
  server's, over `Tabletop.Protocol`.
- **No team, army-pick or draft writes.** Lobby and setup edits are socket messages, not endpoints
  (whole-design review ruling 4). `POST /v1/games/{id}/join` joins the game and assigns no team.
- **No mesh processing.** Conversion, decimation and the bounding-box volume run in the browser
  (mesh conversion → *Conversion runs in the browser*). The service reads a header.
- **No multipart upload.** `IBlobStore` has no multipart member. The management API spec's open
  item stays open.
- **No content import.** How authored `Properties` rows reach the eight tables is undesigned
  (`architecture.md` → *Rules as data*), so `GET /v1/properties` can return an empty list and
  `POST /v1/games` then answers `422`.
- **No hosting.** Local only: Postgres and MinIO from `docker-compose.yml`, configuration from
  `TABLETOP_*` and `dotnet user-secrets` (hosting ruling 2026-09-04).

### Where the code lands

```
server/management/Tabletop.Management.csproj      net10.0, LangVersion 12, RootNamespace Tabletop.Management
  Program.cs                                      composition root; `public partial class Program` for the test host
  Configuration/                                  ManagementOptions, DotEnv
  Http/                                           ClientVersionMiddleware, Keyset, Page<T>, Problems, CurrentAccount
  Persistence/                                    Database, Migrator, Migrations/001_schema.sql, Migrations/002_blob_uploaded.sql
  Identity/                                       StytchJwks, StytchAuthentication, StytchClient, Sessions,
                                                  AccountRepository, AuthEndpoints, AccountEndpoints, AccountDeleter
  Content/VolumeRows.cs                           VolumeRow, shared by asset_volume and terrain_volume
  Content/Assets/                                 IBlobStore, S3BlobStore, GlbHeader, AssetRepository/Endpoints/Dtos,
                                                  FolderRepository/Endpoints
  Content/Armies/                                 ArmyForgeList, ArmyForgeClient, ArmyForgeImport, CatalogKinds,
                                                  ArmyRepository, ArmyEndpoints
  Content/Layouts/                                LayoutRepository, LayoutEndpoints
  Games/                                          GameDtos, GameRepository, GameEndpoints, PropertiesEndpoint, ManifestEndpoint
tests/Tabletop.Management.Tests/                  TestDatabase, TestKeys, FakeStytch, FakeArmyForge, TestGlb, ManagementFactory
scripts/management                                run the API from WSL
```

Split by module and then by responsibility: a repository is SQL, an endpoints class is HTTP, a
client class is an external system. `server/management/` is the project root, so the SDK's
`**/*.cs` glob covers every folder with no `.csproj` edits.

### Position in the build order, and prerequisites

Step 3 of `architecture.md`'s build order, in parallel with the web app. It consumes exactly one
earlier plan, **contracts and local dev** (`docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md`):

- `libs/Tabletop.Content`: `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`,
  `ArmySnapshot`, `TableState` and its children, `VisualRef`, `ContentJson`.
- `libs/Tabletop.Protocol`: `ProtocolVersion`, `GameStatus`, `SchedulerShape`, `Enforcement`,
  `Team`, `Participant`, `GameInfo`.
- `libs/Tabletop.Geometry`: `Base`, `Orientation`, `Volume`, `Footprint`, `TableSize`, `Point3`, `Yaw`.
- `scripts/postgres/init/001_schema.sql`, adopted verbatim as DbUp migration `001`.
- `docker-compose.yml` (Postgres 5432, MinIO 9000/9001, bucket `tabletop-blobs`) and the
  `TABLETOP_*` names in `.env.example`. Port `5100` is reserved for this service.

Nothing else needs to exist. The geometry plan's bodies are not called here. Only its value types
are used. The game server, web app and client shell all consume this service, and none of them is
a prerequisite.

### What already exists

Only `client/` at scaffold stage. `server/management/`, `libs/` and `tests/` are created by the
contracts plan and this one.

---

## Context

```mermaid
flowchart TD
    web["Web app (Svelte SPA)"]
    client["Game client (Godot)"]
    mgmt["server/management (this module)"]
    game["server/game (WebSocket)"]
    pg[("Postgres: management + game schemas")]
    s3[("S3 / MinIO: tabletop-blobs")]
    stytch["Stytch Consumer API"]
    forge["Army Forge api/tts"]

    web -->|"HTTP: login, library, armies, account"| mgmt
    client -->|"HTTP: login, games, properties, manifest"| mgmt
    client -->|"WebSocket: JWT + game id"| game
    mgmt -->|"Basic auth: fourteen calls, four of them development only"| stytch
    mgmt -->|"GET api/tts?id="| forge
    mgmt -->|"read and write management schema"| pg
    mgmt -->|"delete game.game_event with a game"| pg
    game -->|"read all, and write a game's rows in lobby or setup"| pg
    mgmt -->|"presign PUT and GET, plus a ranged GET of the header"| s3
    web -->|"PUT bytes to the presigned URL"| s3
    client -->|"GET bytes from the manifest URL"| s3
    client -.->|"verify the same JWT against the same JWKS"| stytch
    game -.->|"JWKS, no credentials"| stytch
```

*The two servers never call each other.*

**Auth seam.** Management is the only process holding the Stytch project secret, and the only one
that calls Stytch's Basic-auth endpoints. The game server never touches Stytch's API surface. It
fetches the same public JWKS and applies the same validation parameters. There is no contract type
to agree on and no M2M call. The contract is four constants (`iss`, `aud`, `RS256`, the claim name
`account_id`) plus a URL, recorded under *Interfaces needed from other modules* for the
game-server plan to copy. Auth is not in `architecture.md` → *Contracts to freeze*, and the join
token that was once on that list is gone.

**Database seam.** Both servers open the same database. Management owns the schema and runs DbUp
at its own startup. The game server never migrates. A management migration can break the game
server either way, and both specs record that and leave it (management API → *Still open*;
`schema.md` → *Still open*). The write split is not enforced by the database: `game.status`,
`team`, `participant.team_id/army_id/army_snapshot` and a `game_id`-owned `table_state` are
writable by both processes, kept apart by convention inside each. The split buys a game server
that hydrates a game with plain `SELECT`s and no service call. It costs a rule that only code
review enforces.

**Blob seam.** No `.glb` is read or written by this process except 20 bytes plus a JSON chunk.
That removes a job queue, a worker and an async upload state machine (`architecture.md` → *GLB
only; conversion is not in the product*). It costs a two-step create-then-confirm protocol and a
`blob.uploaded` flag that can be false forever.

---

## Design

### 1. Composition and the request pipeline

One process, one composition root. Every collaborator is a singleton. The repositories hold only a
`Database`, which holds an `NpgsqlDataSource`, which is the pool. There is no scoped state and no
unit of work. A repository method opens a connection, does its work, and returns.

```mermaid
graph TD
    subgraph Http
        cv["ClientVersionMiddleware"]
        ks["Keyset and Page of T"]
        pr["Problems"]
        ca["CurrentAccount"]
    end
    subgraph Identity
        jwks["StytchJwks + StytchAuthentication"]
        sc["StytchClient"]
        se["Sessions"]
        ar["AccountRepository"]
        ad["AccountDeleter"]
        dv["DevSessions, development only"]
    end
    subgraph Content
        asr["AssetRepository"]
        bs["S3BlobStore : IBlobStore"]
        glb["GlbHeader"]
        fr["FolderRepository"]
        afc["ArmyForgeClient + ArmyForgeImport"]
        ck["CatalogKinds"]
        arr["ArmyRepository"]
        lr["LayoutRepository"]
        vr["VolumeRow"]
    end
    subgraph Games
        gr["GameRepository"]
        man["ManifestEndpoint"]
    end
    db["Persistence.Database + Migrator"]

    prog["Program.cs"] --> Http
    prog --> Identity
    prog --> Content
    prog --> Games
    Identity --> db
    Content --> db
    Games --> db
    asr --> vr
    lr --> vr
    afc --> ck
    man --> gr
    man --> bs
    asr --> bs
    asr --> glb
    ad --> gr
    ad --> bs
    ad --> sc
    se --> sc
    se --> ar
    dv --> sc
    dv --> ar
    dv --> se
```

*Four modules over one `Database`.*

`AccountDeleter` reaches into `GameRepository` and `IBlobStore` because deletion is total.
`ManifestEndpoint` reaches into `IBlobStore` because it presigns. `DevSessions` is the one
collaborator whose registration and route are conditional, on `IHostEnvironment.IsDevelopment()`. `VolumeRow` is shared because
`asset_volume` and `terrain_volume` are the same seventeen columns.

Middleware order is load-bearing:
`UseExceptionHandler` → `UseCors` → `ClientVersionMiddleware` → `UseAuthentication` →
`UseAuthorization` → the endpoint, which calls `CurrentAccount.Id(user)` and passes it to a
repository that puts `owner_id` in the `WHERE` clause. **CORS runs before the version check**
because a preflight carries no version header and must be answered whatever the caller's version.
**The version check runs before authentication** because a client months out of date must be told
to update rather than told its token is bad. The exception handler maps
`ArgumentOutOfRangeException` (a bad `limit`) to `400` and everything else to `500`.

`ClientVersion.Decide(string? header, int minimum)` returns `Accept | UpgradeRequired | Malformed`
and is a pure function, unit-tested apart from the middleware. **A missing header is accepted**:
the web app ships with the API and does not send one, and the mechanism exists for the downloaded
desktop client (management API → *`/v1` in the path*; client shell → *Home*).

### 2. Configuration

`ManagementOptions` is one `sealed record` with eleven members and a `static From(IConfiguration)`
that throws on anything missing. Keys are the `.env` names with `TABLETOP_` stripped
(`AddEnvironmentVariables("TABLETOP_")`), so `TABLETOP_STYTCH_SECRET` and
`dotnet user-secrets set STYTCH_SECRET` are the same key and the secret is never in a committed
file. `DotEnv.Load()` walks up from `AppContext.BaseDirectory` and reads the repo-root `.env`, the
same file `docker compose` reads. Existing environment variables win. One file configures the
compose stack, the API and the tests.

Two settings are easy to confuse:

| Setting | Value | What it is |
| --- | --- | --- |
| `SessionMinutes` | 43200 (30 days) | Stytch's `session_duration_minutes`. The life of the **session**, so a player is not logged out mid-campaign |
| the JWT's own lifetime | 5 minutes, fixed by Stytch | why `POST /v1/auth/session/refresh` exists, and why deleting an account locks it out within five minutes |

Revocation in the management API spec depends on the second number, not the first. Deleting an
account revokes the Stytch user, refresh then fails, and the last JWT lapses within five minutes.
No session table is needed for revocation.

### 3. Authentication

No token of our own is issued. The spec designed two opaque token tables, a `session` table and a
join token. Both are gone (management API → *We issue no tokens of our own*). The credential is a
Stytch session JWT that both servers verify locally.

Stytch's JWT names the user by Stytch's own `sub`. It does not carry `external_id`. The account id
therefore rides in the custom claim `account_id`, rendered by the Stytch project's `SESSION` custom
claim template, `{"account_id": "{{ user.external_id }}"}`, which Stytch evaluates every time it
mints a JWT (owner ruling 2026-09-06). Management passes `session_custom_claims` nowhere. Its one
job is keeping every Stytch user's `external_id` equal to its `Account.id`, which
`Sessions.Complete(StytchSession)` does, and every login method passes through it:

```mermaid
sequenceDiagram
    participant C as Client or web app
    participant A as AuthEndpoints
    participant S as StytchClient
    participant X as Sessions
    participant R as AccountRepository
    participant K as Stytch

    C->>A: POST /v1/auth/otp {email}
    A->>S: SendEmailOtp
    S->>K: POST /v1/otps/email/login_or_create
    K-->>S: user_id, email_id, user_created
    A-->>C: OtpSentResponse{methodId = email_id}
    C->>A: POST /v1/auth/otp/verify {methodId, code}
    A->>S: AuthenticateOtp
    S->>K: POST /v1/otps/authenticate
    K-->>S: session_jwt, user{external_id?}
    A->>X: Complete(session)
    alt external_id absent
        X->>R: Insert(Guid.CreateVersion7(), email local part)
        X->>S: SetExternalId(user_id, account id)
        S->>K: PUT /v1/users/{user_id}
        X->>S: RefreshSession(jwt)
        S->>K: POST /v1/sessions/authenticate
        K-->>X: a re-minted JWT, the template now rendering account_id
    else external_id present
        X->>R: Insert(linked id, name) on conflict do nothing
        Note over X,K: no further Stytch call: the minted JWT already carries account_id
    end
    X-->>A: SessionResponse{sessionJwt, account}
    A-->>C: 200
```

*Every session-returning endpoint ends in this same tail.*

A returning login costs no Stytch call beyond the authenticate itself, and a refresh exactly one.
One re-mint survives, in one place. On a brand-new account Stytch mints the session by the same
call that spends the single-use one-time code, before `external_id` exists, so that first JWT
carries no claim. `Sessions.Complete` therefore creates the account row, sets `external_id`, and
re-mints once through `POST /v1/sessions/authenticate`. Linking earlier was rejected: the code is
single-use, so the session must be minted by the call that spends it, and linking at
`POST /v1/auth/otp` would create an account row for any email anyone types.

`external_id` is the entire link between the two systems, and the mechanism rather than an
incidental: the template reads it, so a user whose `external_id` is unset gets JWTs with no claim.
**No Stytch identifier is stored in our schema** (accounts → *Auth is a module, not a vendor*).
`account` is two columns.

The template is project configuration rather than code, and its absence is silent: every JWT mints
without the claim, `CurrentAccount.Id` throws on every management request, the game server rejects
every connection, and Stytch reports no error. It is recorded as environment setup beside the
reset-password redirect URL (*Obligations this module places on others → Stytch dashboard*). It is
also readable and writable through Stytch's Workspace Management API at `GET` and
`PUT https://management.stytch.com/pwa/v3/projects/{project_slug}/environments/{environment_slug}/jwt_templates/SESSION`,
authenticated with a workspace management key rather than the project secret. That `PUT` body
requires both `template_content` and `custom_audience`, and a non-empty `custom_audience` replaces
`aud`, which both verifiers require to equal the project id.

Verification is `Microsoft.AspNetCore.Authentication.JwtBearer` with
`MapInboundClaims = false`, `ValidIssuer = stytch.com/{project_id}`, `ValidAudience = {project_id}`,
`ValidAlgorithms = [RS256]`, `ClockSkew = 30s`, and an `IssuerSigningKeyResolver` that delegates to
the `StytchJwks` singleton. `StytchJwks.Resolve(kid)` caches the key set. On a `kid` it does not
hold, it refetches **once, at most every 30 seconds**, behind a `SemaphoreSlim`. That is the key
rotation path, and the only reason the cache is not permanent.

The JWKS endpoint `GET {StytchBaseUrl}/v1/sessions/jwks/{project_id}` **takes no credentials**.
Stytch's reference page lists a `basicAuth` scheme and passes `-u project:secret` in its example.
An unauthenticated `curl` answers `200` with the key set (verified 2026-09-05 and 2026-09-06). That
is what lets the game server verify a JWT without holding the project secret.

`CurrentAccount.Id(ClaimsPrincipal)` parses the claim and throws `UnauthorizedAccessException` when
it is missing. Every authorized endpoint's first act is to call it, and every repository method
takes `ownerId` as a parameter rather than reading ambient state.

#### The development mint

`POST /v1/dev/sessions` takes a name and returns a real Stytch session JWT for the account of that
name. Owner ruling 4 of 2026-09-06. The simulator's socket driver needs one session token per team,
hand-pasting them was rejected, and management is the only process holding the Stytch project
secret, so the mint belongs here.

`DevSessions.Mint(name)`:

1. `AccountIdFor(name)` is a name-based UUIDv8, RFC 9562 §5.8: SHA-256 over a fixed namespace GUID
   and the UTF-8 name, the first sixteen bytes, version and variant nibbles stamped. The account id
   is a pure function of the name, so nothing has to be looked up and a caller can predict it.
2. `AccountRepository.Insert(accountId, name)`, which is `on conflict do nothing`.
3. `GET /v1/users/{accountId}`, the external-id substitution Stytch's get-user documents. A
   `404` is followed by `POST /v1/users {email, external_id = accountId}`.
4. `POST /v1/magic_links` for an embeddable token, then `POST /v1/magic_links/authenticate` with
   `session_duration_minutes`. The second is the same endpoint an emailed magic link would reach.
5. `Sessions.Complete(session)`, the path every login takes.

Because `external_id` is written when the user is created, the claim template renders `account_id`
on the first mint and `Complete` takes its one-call branch. The re-mint above never fires here.

The derived email is the name lowercased, non-alphanumeric characters replaced by `-`, at
`example.com`, reserved by RFC 2606 and undeliverable. No call on this path sends mail: creating a
user does not, and an embeddable magic link is the endpoint that returns the token instead of
mailing it. Two names that slug to one address collide as `duplicate_email` → `409`.

The gate is `IHostEnvironment.IsDevelopment()`, in two places: `builder.Environment` around the
service registration and `app.Environment` around `MapDevSession`. `EnvironmentName` defaults to
`Production` when neither `ASPNETCORE_ENVIRONMENT` nor `DOTNET_ENVIRONMENT` is set, so a published
build has no such route and answers `404`. No configuration key controls it, so no deployment turns
it on by mistake. The test host runs as `Testing`, which is also not development.

An account that does not exist is created rather than refused, which is what lets the simulator's
`SocketRowSeeder` make its own accounts. A repeated name is idempotent down to the Stytch user:
same name, same account id, same row, same user found by `external_id`. Only the JWT differs. A
first mint costs four Stytch calls and every later one three.

Nothing about the verification path changes. There is no second signing key, no local trust anchor,
and no development branch in either verifier. The token is Stytch-signed and checked against
Stytch's JWKS exactly as a player's is, which is the reason for minting a real one rather than
forging a cheap one.

The cost is network access. A `--driver socket` simulator run reaches the Stytch test project for
every token; the in-process driver reaches nothing. Automated tests are unaffected, since they mint
with `TestKeys` against a fake JWKS.

### 4. Authorization: owner-only, and 404 rather than 403

There is one rule and two exceptions.

| Route | Rule |
| --- | --- |
| everything under `/v1/assets`, `/v1/folders`, `/v1/armies`, `/v1/layouts`, `/v1/account` | owner only |
| `GET /v1/games`, `GET /v1/games/{id}`, `GET /v1/properties` | any logged-in caller; games are public |
| `POST /v1/games/{id}/leave`, `GET /v1/games/{id}/assets` | participant |
| `POST /v1/games/{id}/end`, `DELETE /v1/games/{id}` | creator |

Owner-only is expressed **in SQL, not in a check before the SQL**: `where id = @id and owner_id =
@ownerId`. A row another account owns and a row that does not exist produce the same zero-row
result and the same `404`. Existence is not disclosed (management API → *The rest of the surface*).
There is no `403` anywhere in the service. A permission error is indistinguishable from a mistyped
id. That is accepted for a single-owner model with no sharing.

`/end` and `DELETE /v1/games/{id}` are the creator's alone, ruled 2026-09-06 against this design's
earlier reading of the spec's "unowned" as any participant. `GameRepository.IsCreator` gates both
routes and everyone else gets the same `404` as any other row that is not the caller's. The right
survives leaving, because `game.created_by` has no `on delete` action and outlives the participant
row. A creator's account deletion takes the game with it, so nothing is left undeletable. The game
server reads `created_by` for the two status transitions (setup phase → *The lifecycle*).

### 5. Persistence

Hand-written SQL over Npgsql, with Dapper for row mapping. No ORM, no `DbContext`, no entities.
`libs/` stays persistence-free by rule (`architecture.md` → *Persistence*). The schema is already
normalised and hand-written, with every closed set as a check constraint. Rows are
`sealed record`s mapped by constructor, so every query aliases columns to parameter names
(`owner_id as OwnerId`) and every `real` column is cast (`dim_a::float8 as DimA`) because the
records use `double`.

`Database` builds one `NpgsqlDataSource` with `SearchPath = "management,game,public"`, so SQL names
tables bare and the cross-schema delete qualifies (`delete from game.game_event`). `game` is both
a schema and a table name. With this search path `from game` resolves to `management.game`, the
table.

`Migrator.Run(connectionString)` is DbUp over `Persistence/Migrations/*.sql` embedded in the
assembly, journalled in `public.schema_version`, one transaction per script. It runs at process
startup and in `TestDatabase`, and is idempotent. `001_schema.sql` is a byte-for-byte copy of
`scripts/postgres/init/001_schema.sql` and is never edited. Later changes are new scripts. The
**baseline** case: a database created by `docker compose`'s init already holds `001` and has no
journal, so `Migrator` detects `management.account` without a journal, creates the journal, and
records `001` as applied rather than re-running it.

| `management.account` exists | journal exists | What happens |
| --- | --- | --- |
| no | no | DbUp applies 001 then 002 |
| yes | no | journal created, 001 recorded, DbUp applies 002 |
| yes | yes | DbUp applies whatever is unapplied |
| no | yes | the early return fires and DbUp applies only what the journal lacks. If the journal already records `001`, `002` runs against a table that is not there and fails. Not reachable through either supported path, and not tested |

Migration `002_blob_uploaded.sql` adds `blob.uploaded boolean not null default false` and
`blob.content_type text not null check (content_type in ('model/gltf-binary','image/png','image/jpeg','image/webp'))`.
Both are needed by the presigned protocol. Neither is in `schema.md`. Spec correction 1 proposes
them.

### 6. The asset library and the presigned protocol

Two records, per the asset library spec. `blob` is the bytes, keyed `(owner_id, hash)`, so
**per-user dedup is a property of the schema**. `asset` is the library item carrying `name`,
`kind`, `scale`, the three orientation angles and, for a miniature only, the modelled base. `kind`
profiles are per kind and one is empty: a texture carries nothing (identity and enumerations →
*`ASSET` profiles are per kind*).

Upload is three calls and no server-side bytes:

```mermaid
sequenceDiagram
    participant B as Browser (web app)
    participant M as Management API
    participant DB as Postgres
    participant S3 as MinIO

    B->>B: convert STL to GLB, SHA-256 the bytes
    B->>M: POST /v1/assets {name, kind, hash, byteSize, contentType, scale, orientation, modelledBase}
    M->>M: validate: 64-hex hash, content type per kind, modelledBase iff miniature
    M->>DB: insert blob on conflict do nothing, then insert asset
    alt blob.uploaded already true (same bytes twice)
        M-->>B: 201 CreateAssetResponse{asset, uploadUrl = null}
    else new bytes
        M->>S3: presign PUT {owner}/{hash}, 15 min
        M-->>B: 201 CreateAssetResponse{asset, uploadUrl}
        B->>S3: PUT the bytes with the declared Content-Type
    end
    B->>M: POST /v1/assets/{id}/confirm
    M->>S3: HEAD: Size(key)
    M->>S3: GET bytes 0-19
    M->>S3: GET the JSON chunk
    M->>M: CountTriangles: sum over primitives
    M->>DB: update blob set uploaded = true
    M-->>B: ConfirmResponse{asset, triangles, warning?}
    B->>M: PUT /v1/assets/{id}/volumes (terrain only)
```

*The three-call upload, including the dedup short-circuit.*

The hash is computed client-side so the service can answer "already there" before any bytes move
(management API, "Asset bytes never pass through the API"). Nothing recovers a `blob` row whose
confirm never arrives. The presigned `PUT` carries no size constraint, so the declared `byteSize`
is checked only after the bytes are in the bucket.

`confirm` is where every check lives:

| Condition | Answer |
| --- | --- |
| `Size(key)` is null | `409`, the bytes are not there yet |
| size ≠ `asset.ByteSize` | `422` |
| content type is `model/gltf-binary` and the magic is not `glTF`/version 2 | the object is **deleted** and `422`. An STL renamed `.glb` dies here |
| triangles > `GlbHeader.WarnAbove` (100,000) | `200` with `Warning` set. **Never refused** |
| otherwise | `200`, `blob.uploaded = true` |

**GLB only**, narrowed from `architecture.md`'s "glTF/GLB": a `.gltf` with external `.bin` buffers
is several files with relative references. That breaks single-object presigned upload and makes the
header read pointless. `GlbHeader` is pure and headless:

- `IsGlb(first12)`: magic `0x46546C67` and version 2.
- `JsonChunkLength(first20)`: the chunk length at offset 12, with chunk type `JSON` at 16.
- `CountTriangles(jsonChunk)`: for every primitive of every mesh, take
  `accessors[indices ?? attributes.POSITION].count` and convert by `mode`: 4 → `count/3`, 5 and 6
  (strip, fan) → `count - 2`, points and lines → 0. Meshes are counted **once each, not per node**,
  because that is what a client loads into memory.

The threshold warns and does not block (mesh conversion → *The threshold*). It is unrelated to
`game.enforcement`, which gained a refusing position on 09-04. Nothing about uploads refuses.

Download is `GET /v1/assets/{id}/download` → a presigned `GET` valid 60 minutes, `409` until
confirmed. Delete removes the asset row. When that was the owner's **last** reference to the hash
it removes the `blob` row too, and returns the S3 key so the endpoint can delete the object after
the transaction commits. Per-user dedup makes that check a single `not exists` over the owner's own
rows rather than a cross-account count.

`Content/VolumeRows.cs` holds `VolumeRow`, one record over the seventeen columns
`asset_volume` and `terrain_volume` share, with `ToVolume()`/`From(id, Volume)` and three
`const string`s (`Columns`, `InsertColumns`, `InsertValues`) so the SQL is written once. The six
terrain declarations are `bool` columns mapped to the `TerrainDeclarations` flags enum. `PUT
/v1/assets/{id}/volumes` is delete-then-insert with fresh ids and answers `409` unless the kind is
terrain.

### 7. Folders

Nested, many-to-many with assets, returned whole. Folders are small enough that they are not paged
(management API → *Pagination is keyset*). `GET /v1/folders` is two queries: the `folder` rows plus
one join over `asset_folder`, assembled into a flat `FolderDto(Id, ParentId, Name, AssetIds)` list.
The client makes the tree. Deleting a folder cascades its children in the schema and touches no
asset. An asset in no folder is valid.

The only logic is cycle prevention on `PATCH`: a new parent must be the owner's own folder, must
not be the folder itself, and must not be a descendant, checked by a `with recursive` query. That
answers `422`. A folder that is not the caller's answers `404`.

### 8. Armies and the Army Forge import

Management fetches the list. The parser lives in one place, there is no CORS problem, and a change
to Army Forge's format is a server deploy rather than a client release (management API → *Army
import*).

`ArmyForgeClient.ListId(source)` accepts a share link's `id=` parameter or a bare id and returns
null otherwise (`422`). `Fetch(listId)` calls `GET https://army-forge.onepagerules.com/api/tts?id={id}`
and throws `ArmyForgeException(status)` on non-200, which the endpoint maps to `502`.

`ArmyForgeList.cs` is the **subset of the export this project reads**, verified against a live
fetch on 2026-09-06 (`c2-6YsWRH3py`, 26 KB, saved as a fixture). Every optional member is nullable
and nothing is `required`, because a required-field model throws on the real payload. What the
fetch established:

| Observation | Consequence for import |
| --- | --- |
| `gameSystem` is `"gf"` on this list and is the list's own field | the only signal import uses. `gf` and `aof` are the two accepted values and both map to `grimdark-future`; a list carrying another book's content still reports its own system, and nothing here resolves the book |
| `selectionId` is unique; `id` repeats across duplicated units | combining and joining key on `selectionId` |
| `quality` and `defense` are **plain integers** (`4`, not `"4+"`, never objects) | `TargetNumber` accepts a number or a string and emits `"4+"` |
| `loadout` is the resolved post-upgrade list; `weapons` is the pre-upgrade profile | import reads `loadout` and ignores `weapons`. Unit 0's `weapons[0]` is a "Blood Sword" that its `loadout[0]` replaced |
| a rule's `rating` is **absent**, not null, when it takes none | `AfRule.Rating` is `JsonElement?` and `Rating()` returns `int?` |
| `range` is `0` on some melee weapons and `null` on others in one payload | the melee test is `is { } range and > 0`, and a melee weapon gets **no** `Range` stat at all |
| `attacksMultiplier` is `1` on every entry observed | not read; a list carrying anything else makes the `Attacks` stat wrong, and the fix is one term |
| `bases` is `{round, square}`, both strings of millimetres | `"32"` → `Base.Circle(32/25.4)`, `"60x35"` → `Base.Oval(60/25.4, 35/25.4)` |
| there is no `customName` | `name` is the only name field |
| `combined: true` on two entries whose `joinToUnit` cross-reference | one unit of double size; sizes and loadout counts add |
| `joinToUnit` on an entry that is not a `combined` partner | a hero joining that unit; its models become models of that unit (ruled 2026-09-06). This list carries no such entry, so the join path is tested against hand-written lists |

`ArmyForgeImport.ToArmy(list, source, catalog)` is a pure function: no database, no HTTP. The
mapping is the most heavily unit-tested piece in the module.

```mermaid
flowchart TD
    l["AfList"] --> gs{"gameSystem in GameRules?"}
    gs -->|no| ex["UnsupportedGameSystemException, 422"]
    gs -->|"gf or aof"| mg["Merge: combined pairs into one entry"]
    mg --> jn{"joinToUnit resolves to an entry that joins nothing?"}
    jn -->|yes| jsec["a section of that entry"]
    jn -->|no| host["a unit of its own: Quality/Defense as unit stats"]
    jsec --> sec["per section, the host entry first, then each joining entry in export order"]
    host --> sec
    sec --> jst{"joining entry?"}
    jst -->|yes| ss["its Quality/Defense as slot stats on its own slots"]
    jst -->|no| ns["no slot stats"]
    ss --> rules["rules: Place(count = size)"]
    ns --> rules
    rules --> lo["loadout, in order"]
    lo --> w{"type"}
    w -->|ArmyBookWeapon| wc{"count >= size?"}
    wc -->|yes| all["a Weapon on every slot of the section"]
    wc -->|no| cur["count slots from the section's cursor"]
    w -->|ArmyBookItem| ic["each content rule: Place(count)"]
    ic --> lvl{"count >= size?"}
    lvl -->|yes| jlv{"joining entry?"}
    jlv -->|yes| every["a PropertyInstance on every slot of the section"]
    jlv -->|no| cat{"catalog says model?"}
    cat -->|yes| every
    cat -->|no| unit["a unit property"]
    lvl -->|no| slots["count slots from the cursor"]
    all --> out["Unit: the host's ModelSlot rows, then each joining entry's"]
    cur --> out
    every --> out
    unit --> out
    slots --> out
```

*Expansion to one slot per model happens here and nowhere else, because nothing carries a count
(`data-model.md`).*

Two branch points are state-dependent. The catalog lookup depends on database state at import
time. The cursor is shared, and both weapons and part-count rules advance it.

The cursor answers `9x Rifle, 1x Flamer`: the flamer lands on slot 10, not slot 1. Because it is
shared between weapons and partial-count item rules, a shield on two models and a flamer on one do
not both start at slot 1. The choice is arbitrary and deterministic (question 4). There is one
cursor per section, so a joining entry's loadout can never reach a slot of the unit it joined.

The level of a rule comes from `CatalogKinds.For(gameRules)`, a `distinct` query over
`catalog_entry` joined to `properties` on `game_rules`: a name the catalog calls `model` goes onto
every slot, anything else is a unit property. **The dictionary is empty until content is loaded**,
and no content import exists yet, so today every rule imports at unit level.

#### A joined hero as a model of its host

An entry that carries `joinToUnit` and is not a `combined` partner contributes its models to the
unit it names, ruled 2026-09-06. `Merge` returns a host entry plus the entries joining it, and
`Section` appends one entry's slots to the unit under construction. The host's slots come first,
then each joining entry's in export order. `ModelSlot.Ordinal` counts over the merged unit, while
the slot name is `"{entry name} {index within that entry}"`. The two coincide for a unit nothing
joined.

Nothing had to be added to hold the hero. `ModelSlot` already carries `Stats` and `Properties`,
`slot_stat` and `slot_property` are already among the ten tables the repository writes in one
transaction and reads back, and the content values spec declares stats at unit, model and weapon
level. A joining entry's own quality and defence become slot stats on the slots it contributes. Its
base comes from its own `bases.round`, and its height is scaled from that base.

Every rule on a joining entry becomes a slot property on that entry's slots, whatever level
`CatalogKinds` gives the name. The unit's stats and properties stay the host's. A stat read for the unit reads the
host's value; a stat read for a model reads that model's slot and falls back to the host's, which
is how a joined hero's own `Quality` reaches its hit roll (rulebook p.4, owner ruling of
2026-09-06). A `unit`-kind rule on a joined slot is the whole unit's. Two obligations on the
interpreter follow, in *Obligations this module places on others*.

Join resolution is one pass over what the combined pass left. An absorbed `combined` partner's
`selectionId` resolves to the entry that absorbed it, so a hero naming either half of a combined
pair joins the merged unit of ten. Combined pairing requires `combined: true` on both entries, so a
hero is never absorbed as a partner and the two mechanisms cannot collide. A `joinToUnit` that no
surviving entry answers leaves the entry as its own unit, the behaviour this module had before the
ruling. A `joinToUnit` naming an entry that itself joins does the same, so joins are never chained
and a cycle needs no walk.

Defaults where nothing supplies one: `DefaultBody`, a 221 × 119 × 113 mm vehicle hull, when
`bases.round` is absent. Ruled 2026-09-09, over the `Base.Circle(32/25.4)` that stood before: Army
Forge publishes `bases.round` reliably for infantry, so a missing base means a vehicle or monster in
practice, and a 32 mm circle put a tank's whole 6" disembark region — and its movement, cover and
line of sight — inside its own hull. The default carries its own height rather than going through
`HeightFromBase`, which would scale 221 mm into a 13" tall tank. The army spec says bases default to
"whatever the ruleset says" and no ruleset value exists anywhere in the model (question 3; spec
correction 5). The player corrects it per model through `SlotPatch.Base`, which the web app's base
editor already writes.

`HeightFromBase(base)` is `HeightScale * base.A`, `1.5 ×` the base's long axis, ruled 2026-09-06.
A 25.4 mm circle gets 1.5", a 32 mm circle 1.89", a 60x35 mm oval 3.54" and a 120x92 mm oval 7.09".
Neither ruleset publishes a height and neither does Army Forge, so the base is the only physical
dimension available to scale from and the factor is a guess. The player corrects it per slot with
`SlotPatch.Height`.

Storage is ten tables and a single transaction: `army`, `unit`, `unit_stat`, `unit_property`,
`model_slot`, `slot_stat`, `slot_property`, `weapon`, `weapon_stat`, `weapon_property`.
`ArmyRepository.Get` reads the whole tree in **nine queries plus one head query** and assembles it
with `ToLookup`, rather than one join that would multiply rows across six child tables.
`model_slot.asset_id` is written as given with **no existence check**. The reference is soft, the
army holds the hitbox, and deleting a mesh must not be blocked (the army → *Assets are a soft
reference*).

`PATCH /v1/armies/{id}` is **a patch, not a document**: `PatchArmyRequest(Name?, Units?, Slots?)`
where `UnitPatch(UnitId, AssetId?)` sets the unit default across every slot and
`SlotPatch(SlotId, AssetId?, ClearAsset, Base?, Height?)` overrides one. That is the army spec's
shape: the pack or the import supplies a default, and the user overrides per slot. Spec correction
3 settles the spec's open question about document versus patch. There is no `Name?` on either
patch. Names are the list's. Appearance and physical presence are the user's.

A changed list means a re-import, with no reconciliation and no preserved bindings. The army spec
accepts that cost. `list_entry` and `upgrades` therefore do not exist as columns.

### 9. Layouts

A layout and a game's setup result are **one shape with two owners**: `table_state` carries a
nullable `owner_id` and a nullable `game_id` with a check constraint that exactly one is set (setup
content → *The setup phase is snapshotted*; `schema.md` → *`table_state` has two nullable owners*).
Management writes the `owner_id` half and never the `game_id` half.

`LayoutRepository.Save(ownerId, TableState)` regenerates **every** id: the layout row, each
`terrain_placement`, each `terrain_volume`, each `marker`, each `deployment_zone`. These are global
primary keys and saving is a copy. Loading happens elsewhere. The client reads
`GET /v1/layouts/{id}` and sends a `ReplaceDraft` setup edit, so the game server regenerates ids on
the way in. That keeps the rule *the game server never writes an owner-side row* true
(whole-design review ruling 4: **layouts stay management's**). It costs a round trip through the
client, which a service-to-service call would have avoided. There are no service-to-service calls.

`PATCH /v1/layouts/{id}` takes `PatchLayoutRequest(Name?, State?)`. A name-only patch is an
`update`. A state patch deletes the three child collections and rewrites them. `POST` requires a
name and answers `422` without one. A game's draft has none. A saved layout must have one.

`LayoutRepository.ReadTree(connection, id, name, size, texture)` is `static`. It is the one reader
of the placement/volume/marker/zone tree, and the game-server plan reads the same rows for a game's
draft.

### 10. Games, the browser and the row-ownership split

A game is a lobby: one row with a status (accounts → *A game is a lobby*). Management creates it
at `lobby`, inserts a `participant` row for the creator with **no team and no army**, and from then
on the row is co-owned.

```mermaid
stateDiagram-v2
    [*] --> lobby : POST v1 games, management
    lobby --> setup : StartSetup socket edit, game server, creator only
    setup --> playing : EndSetup socket edit, game server, creator only
    lobby --> ended : end or last leave, management
    setup --> ended : end or last leave, management
    playing --> ended : end or last leave, management
    ended --> playing : reversible, hydrate from the log and serve
    lobby --> [*] : DELETE a game, management
    setup --> [*] : DELETE
    playing --> [*] : DELETE
    ended --> [*] : DELETE
```

*`ended` is a flag, not a terminal state. Restarting an ended game is the same
hydrate-from-the-log operation as recovering a crashed task.*

The full split:

| Table or column | Management writes | Game server writes (only while `lobby` or `setup`) |
| --- | --- | --- |
| `account`, `blob`, `asset`, `asset_volume`, `folder`, `asset_folder`, the ten army tables, `table_state` with `owner_id` and its children | always | never, reads only |
| `properties` and the seven tables under it | never (loaded by the undesigned content import) | never, reads only |
| `game` insert, `name`, `game_rules`, `properties_hash`, `scheduler_shape`, `enforcement`, `waits_between_steps`, `created_by`, `created_at` | on create | rewrites the configuration on `SetConfiguration` |
| `game.status` | `'lobby'` on create; `'ended'` on `/end` and on the last leave | `lobby → setup → playing` |
| `team` | never | inserts on `CreateTeam`, while `lobby` or `setup` |
| `participant` insert and delete, which is **joining the game** | create, join, leave | never |
| `participant.team_id`, which is **taking a team** and is not joining | never, null on insert | writes on `PickTeam`, while `lobby` or `setup` |
| `participant.army_id`, `participant.army_snapshot` | never, null on insert | writes on `PickArmy`, `EndSetup` |
| `table_state` with `game_id`, and its `terrain_placement`, `terrain_volume`, `marker`, `deployment_zone` | never | writes the live draft, then frozen by the status flip |
| `game.game_event` | deletes with the game | appends |

These are two disjoint write sets over the same handful of tables, separated by convention. Nothing
in the database enforces it: no trigger, no per-column grant, no status guard. The service does not
read `game.status` before writing `'ended'`. The game server does not check whether management has
deleted the game under it.

`participant` needs two columns `schema.md` does not yet have, both proposed by the contracts plan:
`game_id uuid not null references game(id) on delete cascade` with `unique (game_id, account_id)`
(its spec correction 4: ruling 5 made `team_id` nullable, which left a team-less participant
attached to nothing), and `army_id uuid references army(id) on delete set null` (its spec
correction 2: the lobby's pick, which the manifest reads before a snapshot exists). This module's
SQL depends on both.

Endpoint behaviour:

- `POST /v1/games` validates the `properties_hash` against the `properties` table and copies
  `game_rules` from the chosen row, so a game can never name a ruleset its pinned set disagrees
  with. An unknown hash is `422` with a pointer to `GET /v1/properties`.
- `GET /v1/games` lists **every status**, keyset over `id`, **descending**. Filtering is the user's, not
  the server's. "Open games" was the wrong phrase. Nothing is hidden by status (accounts → *Still
  open*). Descending by id means newest first, ruled 2026-09-09 over the ascending order the cursor
  gave for free: the list filters by status, name and created-by-me, which is a filter set for
  finding something recent, and ascending puts every finished game ahead of tonight's (question 7).
  `id < @after order by id desc`; `Page<T>.Next` still holds the last id.
- `POST /v1/games/{id}/join` is `insert ... on conflict (game_id, account_id) do nothing` guarded by
  `where exists (select 1 from game ...)`, so joining twice is the same row and joining a
  nonexistent game is `404`. Joining is allowed at every status, including `ended`. The arriving
  client hydrates from the log like any reconnect (setup phase → *Joining is allowed at any status*).
  This endpoint joins the game. It does not take a team, and the row it inserts has `team_id null`.
  Taking a team is a lobby edit on the game server's socket, allowed only while the game is `lobby`
  or `setup`, and no route here reaches it. An account that joins a `playing` game therefore has a
  row and no team, which is what a spectator is. A player who already holds a slot rejoins it by
  reconnecting, not by joining again. The canonical statement of the team model is game flow →
  *Teams are frozen at the flip, and a team is a slot*.
- `POST /v1/games/{id}/leave` deletes the row and then, in the same transaction, sets `status =
  'ended'` when no participant remains. Disconnecting is not leaving. The client's Home button
  closes the socket and keeps the row (client shell → *Screens*).
- `DELETE /v1/games/{id}` deletes `game.game_event` first and then `game`, in one transaction,
  because no foreign key crosses the schema boundary.

### 11. The manifest, the one cross-owner read

The library is private for discovery, not for access. Every other asset route is owner-only. This
one is authorized by participation and returns presigned URLs for every asset in play, whoever owns
it (management API → *The game manifest*).

`GameRepository.ManifestAssets(gameId)` collects one `HashSet<Guid>` of asset ids from five
sources, then joins `asset` to `blob` and keeps only what is uploaded:

| Source | Query | When |
| --- | --- | --- |
| a participant's picked army | `participant.army_id` → `unit` → `model_slot.asset_id` | only where `army_snapshot is null`, so `lobby` and `setup` |
| a participant's frozen army | `army_snapshot::text` → `ContentJson.Deserialize<ArmySnapshot>` → slots' `AssetId` | only where `army_snapshot is not null`, so `playing` and after |
| the board texture | `table_state.texture_asset_id where game_id = @id` | always |
| terrain meshes | `terrain_placement.asset_id` under that `table_state` | always |
| marker visuals | `marker.visual_asset_id` under that `table_state` | always |

The `army_snapshot is null` test is what makes one endpoint serve both before and after the freeze.
`blob.uploaded` is what keeps a half-uploaded asset out. Then one presigned `GET` per asset, 60
minutes, and the result is `Manifest(ManifestEntry[], ExpiresAt)`.

`ManifestEntry(AssetId, Kind, Hash, Url, Scale, Orientation)` carries the owner's confirmed scale
and orientation because `GET /v1/assets/{id}` is owner-only. A participant drawing someone else's
mesh has no other way to learn how it should stand. The alternative is loosening the owner-only
rule. It carries `Hash` so the client's content-addressed on-disk cache can key on it and fetch a
blob once per machine (client shell → *The table view*).

Nothing is copied and no row is created. It is a download, not a share, and per-user dedup is
untouched. A presigned URL outlives the participation check that produced it. The spec accepts
that: another player retaining a mesh they were already shown harms nobody.

### 12. Paging, problems, and JSON

**Keyset, not offset.** `Keyset.Parse(limit, after)` defaults to 50, caps at 200, and throws
`ArgumentOutOfRangeException` (→ `400`) on a non-positive limit. The cursor is the id, because
UUIDv7 is time-ordered by construction, so no sort column is needed and a library being added to
while someone scrolls it does not skip or repeat rows. `Page<T>(Items, Next)` sets `Next` to the
last item's id when the page came back full. Applied to assets, armies and the game browser only.
Folders, teams and a game's participants come back whole.

Every error is `application/problem+json` via `Problems.NotFound/Conflict/Unprocessable/BadGateway`
plus `AddProblemDetails()`. The status vocabulary across the whole surface:

| Status | When |
| --- | --- |
| `400` | a malformed version header, a non-positive `limit` |
| `401` | no token, wrong issuer, expired token, Stytch `401`/`404` on a login |
| `404` | a missing row, another owner's row, a game the caller is not a participant of (for the participant-only routes) |
| `409` | confirm before the bytes are there, download before confirm, volumes on a non-terrain asset, Stytch `duplicate_email` |
| `415` | a content type the kind does not accept. STL is refused here, at create time |
| `422` | validation, an unknown `properties_hash`, an unimportable game system, a folder cycle, Stytch `weak_password`/`breached_password` |
| `426` | `Tabletop-Client-Version` below `MinimumClientVersion` |
| `502` | Army Forge unreachable or non-200, Stytch `5xx` |

Response bodies serialise through `ContentJson.Configure`: camelCase, snake_case enums, nulls
omitted. A `TableState` or a `GameInfo` on HTTP is therefore byte-identical to the same value on
the socket. That is why those types live in `libs/Tabletop.Content` and `libs/Tabletop.Protocol`
instead of being redefined here. Everything with no contract type is a DTO of this project.

### 13. Total account deletion

Deleting an account destroys its armies, assets, blobs, folders and layouts, then **every game it
participated in**, both the game record and its event log, and then the account row. No tombstone,
no scrubbed name, no deactivated state. The collateral is accepted: a game other people played in
disappears because one participant left (accounts → *Deletion is total*).

Postgres cascades cannot express this. Content hangs *below* the account and cascades normally.
Games hang *above* it, participant to team to game, and then back down into other people's
participants and across into the `game` schema's log. Deletion is therefore a multi-step operation
rather than a constraint or a trigger. A trigger would hide a destructive cross-schema action
behind an ordinary `DELETE` (`schema.md` → *Deleting an account is application logic*).

```mermaid
sequenceDiagram
    participant E as AccountEndpoints
    participant D as AccountDeleter
    participant DB as Postgres
    participant G as GameRepository
    participant S3 as MinIO
    participant K as Stytch

    E->>D: Delete(accountId)
    D->>DB: exists(account)?
    D->>DB: select s3_key from blob where owner_id = me
    D->>DB: select game_id from participant where account_id = me<br/>UNION select id from game where created_by = me
    loop per game id
        D->>G: Delete(gameId)
        G->>DB: delete game.game_event, then game (cascades team, participant, table_state)
    end
    D->>DB: delete from account (cascades army, asset, blob, folder, table_state)
    D->>S3: Delete(keys)
    D->>K: DELETE /v1/users/{external_id}
    K-->>D: 200, or 404 which is logged and ignored
    D-->>E: 204
```

*Database first, then S3, then Stytch, so a Stytch failure leaves no orphaned content.*

A Stytch failure other than `404` answers `502` after the content is already gone. Logging in again
then recreates an empty account with the same id, because `Sessions.Complete` inserts on conflict.

The union carries the case that cascades miss. `game.created_by` is
`not null references account(id)` with **no** `on delete` action, and `/leave` deletes the
participant row while the game survives. An account that created a game and then left is still
named by `created_by`, so a plain `delete from account` would fail on that foreign key. The union
over `participant.game_id` and `game.created_by` is what makes `schema.md`'s assertion true, that
*"Account deletion already destroys every game the account played in, so a deleted creator leaves
no orphan"*. The plan's test exercises that case: a game created, then left, then still deleted.

---

## Key decisions

### Contested and re-decided

Each of these was decided, reversed, and in several cases reversed again.

| Decision | Reason | Alternative rejected | Source |
| --- | --- | --- | --- |
| **No token of our own.** Stytch's session JWT is the only credential. Both servers verify it locally against Stytch's JWKS | our UUID is the identifier on both sides, so swapping vendors changes what issues the token, not what it means. Verification is local, so nothing calls Stytch on the request path. Short expiry covers revocation | two opaque token tables, a `session` table and a join token, both designed in this session and both dropped 08-30 | management API → *We issue no tokens of our own*; accounts → *Auth is a module* (revised 08-30) |
| **The join token is gone.** A game has an id, a session says who you are, `participant` records who is allowed in | the token restated three facts and added an expiry that could lapse mid-game. A four-player game runs hours, so a token issued at the lobby could be dead when a player who dropped at hour three reconnects | a fourth frozen contract between two servers that never call each other | accounts → *A game is a lobby*; `architecture.md` → *Contracts to freeze* |
| **The client has its own login screen. Both it and the web app call the same two methods** | reversed twice: accounts put login in the client, the web app moved it to the browser because one existed, and the client shell removed social login and magic links so nothing needs a page to redirect to. Accounts' original routing stands | loopback and device code, both designed to carry a browser session into the client; an embedded webview to catch an auth redirect | client shell → *No social login*; web app → *Login is the web app's* (corrected 09-04) |
| **The account id rides in a custom claim the Stytch claim template renders, not in `external_id`** | Stytch's JWT names the user by its own `sub`. The spec's 08-30 confirmation allowed a custom claim if `external_id` could not ride, and it cannot. The `SESSION` template renders `{{ user.external_id }}` on every mint, so the claim costs nothing at run time | reading `external_id` from the token; stamping the claim per login with `session_custom_claims`, which cost an extra Stytch call on every login and every refresh (question 1, ruled 09-06 in favour of the template) | management API → *Confirmed 2026-08-30*; owner ruling 2026-09-06; plan Task 5 |
| **GLB only**, narrowed from "glTF/GLB" | a `.gltf` with external buffers is several files with relative references: single-object presigned upload breaks, the header read becomes pointless, and serving it to another player's client can fail | accepting `.gltf` with sidecar buffers | management API → *GLB only*; `architecture.md` → *GLB only* |
| **The triangle check warns and never blocks**, above 100,000 | the guardrail exists because nothing stops an undecimated Blender export landing on every client. Refusing an upload is not this product's shape | a hard limit; server-side decimation, which would mean a job queue, a worker and an async upload state | mesh conversion → *The threshold*; asset library → *Still open* (settled 09-04) |
| **`enforcement` is `silent \| warn \| strict`**, not a boolean | identity and enumerations ruled a boolean and argued that a blocking mode contradicts the product. Action sequences and the scheduler then both wrote a refusing mode. The owner kept `strict` | `warns boolean`. This module only stores and validates the column | whole-design review ruling 1 |
| **A participant has no team until they pick one, and picking is not joining** | nothing said what team `join` assigned. `POST /v1/games/{id}/join` inserts the row and sets no team, at any status; taking a team is a game-server lobby edit before play. A team-less participant is never addressed by an Ask, which is what a spectator is | presence's "joins a third team", superseded; and the reading that made `join` assign a team, which conflated the two operations | whole-design review ruling 5; owner ruling 1 of 2026-09-06; game flow → *Teams are frozen at the flip, and a team is a slot* |
| **A development-only route mints a real Stytch session for a named account.** `POST /v1/dev/sessions`, gated on `IHostEnvironment.IsDevelopment()` | the simulator's socket driver has to hold a session token per team, and hand-pasting four of them was rejected. Management holds the project secret and already creates users and authenticates sessions, so the mint costs four Stytch calls and no new machinery. Minting a real token keeps the socket driver on the production verification path, which is the only reason that driver exists | a hand-pasted `TABLETOP_SIM_JWT`; a second signing key or a local trust anchor for either verifier, which would put a bypass in the auth path; a feature flag, which a deployment could set by accident | owner ruling 4 of 2026-09-06; simulation design → *How a socket run gets its session JWTs* |
| **The game server writes a game's own rows before play** | three documents said it was read-only and wrote only events. The setup phase had it flipping status, writing the draft, and taking team and army picks over the socket | keeping management the sole writer, which would have meant endpoints for every lobby and setup edit | whole-design review ruling 4 |
| **Layouts stay management's** even though the game server owns the draft | the client saves by `POST /v1/layouts` from its own copy of the draft and loads by `GET` plus a replace-draft edit, so the game server never writes an owner-side row | letting the game server write the layout directly, which would have put an owner's row in the other service's write set | whole-design review ruling 4; setup phase → *Layouts* |
| **Grimdark Future and Age of Fantasy both import**, both carrying `game_system = "grimdark-future"` | the mechanics are the same and only the content differs, which is what `architecture.md` → *Rules as data* already records when it rules Age of Fantasy out as a second ruleset: its terrain rules are word for word identical. One rules implementation therefore serves both, and the list's `gameSystem` selects nothing but whether the list is accepted | `gf` alone, on the reading that Age of Fantasy is not a second ruleset and so not a second import; a second `GameRules` identifier, which would need a second engine to mean anything. The skirmish and regiment variants stay refused, being different mechanics rather than different content | owner ruling 2026-09-06; question 5; plan Task 9 |
| **A slot's starting height is scaled from its base**, `1.5 ×` the long axis | a flat 1.5" gave a 120 mm monster the height of an infantryman, and the base is the only physical dimension Army Forge publishes. The scale is arbitrary and reads as arbitrary, which suits a value the player is expected to correct per slot through `SlotPatch.Height` | the flat `DefaultHeight = 1.5`; per-`GameRules` heights from `Properties`, which needs a table that does not exist | owner ruling 2026-09-06; question 3; plan Task 9 |
| **Only the creator may `/end` or `DELETE` a game**, and still may after leaving it | the creator already starts and ends setup, so ending and destroying the game belong to the same account. `game.created_by` has no `on delete` action and outlives the participant row, so leaving does not surrender the right | any participant, on the reading that the setup phase leaves a game "unowned" (question 6). A game whose creator's account is deleted is not stranded: account deletion destroys every game the account created as well as every game it played in | owner ruling 2026-09-06; question 6; plan Task 12 |
| **A joined hero is a model of the unit it joined**, with its own quality, defence and rules on its own slot | Army Forge publishes the attachment on the joining entry, and dropping it imports a squad of five plus a hero of one where the list describes one unit of six. `ModelSlot` already carries `Stats` and `Properties`, `slot_stat` and `slot_property` are already written and read back, and content values declares stats at model level, so the merge cost a restructuring of `ArmyForgeImport` and no schema change | importing the hero as its own unit and losing the join, on the reading that no column records it; a host column on `unit`, which the army tables do not have and which only import would ever write | owner ruling 2026-09-06; question 2; plan Task 9 |
| **A changed list means a re-import**, losing per-slot bindings | the army session originally copied an army and applied the new list to the copy so bindings survived. Armies are cheap to rebuild and list changes are rare | reconciliation, copy-on-write, and the `Binding` table and `list_entry` column that served them, all dropped | the army → *A changed list means a re-import*; asset library → *Binding is not a third record*; content values → *`list_entry` is dropped* |
| **Dedup is per user**, `blob` keyed `(owner_id, hash)` | `architecture.md` deduped globally. Nobody shares storage, so cross-user reference counting never has to exist, and deleting an asset checks only the owner's own rows | global content addressing with reference counts | asset library → *Two records*; `schema.md` → *Identifiers* |
| **Normalised, not `jsonb`** | with `jsonb` the database checks nothing, at the layer where checking is cheapest. An earlier draft had the army tree, the table state and the asset profile as documents | document columns per aggregate. Two `jsonb` columns survive for opposite reasons: `game_event.payload` is heterogeneous, and `participant.army_snapshot` **must not** be reasoned about | `schema.md` → *Normalised, not documents*, *Where `jsonb` survives* |

### Taken as designed

| Decision | Reason | Alternative rejected | Source |
| --- | --- | --- | --- |
| Asset bytes never pass through the service. Presigned `PUT` and `GET` | every participant pulls every mesh in a game, including other people's. Streaming that through the API is the wrong shape, and no AWS credential reaches a client | proxying bytes; a signed-download endpoint that reads and re-serves | management API → *Asset bytes never pass through the API* |
| The triangle count is a ranged `GET` of the GLB header | a GLB's first chunk is JSON carrying the mesh accessors, so the total needs no part of the binary. One small read, no worker, no queue | a background job on upload; trusting a client-declared count | management API → *The triangle count is a ranged read* |
| Owner-only authorization answering `404` | existence is not disclosed, and there is no sharing model for a `403` to mean anything in | `403` for another owner's row | management API → *The rest of the surface* |
| Keyset paging with the id as the cursor | UUIDv7 is time-ordered, so no sort column is needed. Offset skips and repeats rows when the list changes, and a library being added to while it is scrolled is the normal case | offset/limit | management API → *Pagination is keyset* |
| Dapper over Npgsql with hand-written SQL | the schema is normalised and hand-written already, the army tree and table state load whole in a handful of queries, and every constraint lives in the database rather than in mapping attributes. `libs/` must stay persistence-free | EF Core | plan Task 2; `architecture.md` → *Persistence* |
| DbUp with embedded scripts, `001` baselined | the compose init SQL must keep working for a bare `docker compose up`, so the migrator recognises a database that already holds `001` and records it instead of re-running it | EF migrations; running the init SQL only from compose and never versioning it | plan Task 2; contracts plan → *Local development* |
| `game.game_event` deleted explicitly with a game | no foreign key crosses the schema boundary, so the cascade cannot reach it | a cross-schema FK; a trigger | plan Task 12; `schema.md` |
| Management fetches the Army Forge list | the parser lives in one place, there is no CORS problem, and a format change is a server deploy rather than a client release | the browser fetching and posting the JSON | management API → *Army import* |
| Single-object presigned `PUT`, no multipart | a converter-produced GLB is a few megabytes; adding multipart later is a second presign method plus a complete call and changes none of the three endpoints | pre-empting the spec's open item with a multipart API nothing needs yet | management API → *Still open*; plan Task 6 |
| The manifest carries `Scale`, `Orientation` and `Hash` | `GET /v1/assets/{id}` is owner-only, so a participant has no other way to learn how another player's mesh should stand. `Hash` keys the client's per-machine blob cache | relaxing owner-only on the asset row; a second cross-owner endpoint | plan Task 12; client shell → *The table view* |

### Taken because a spec was silent

| Question the specs do not answer | What the plan does | What it costs if wrong |
| --- | --- | --- |
| Is a request with no version header accepted? | yes, the web app ships with the API and does not send one (spec correction 7) | a stale cached SPA would not be told to reload. The mechanism was built for the desktop client |
| Does `PATCH /v1/armies/{id}` take a document or a patch? | a patch: `Name?`, `UnitPatch` (unit default), `SlotPatch` (per-slot asset, base, height), spec correction 3 | a whole-document `PUT` would need the client to hold the full tree and would race with itself |
| What are the error shapes? | RFC 7807 `application/problem+json` throughout. Another owner's row is `404`, spec correction 3 | nothing. The spec listed this under *Deliberately not decided* |
| How are partial weapon counts dealt onto models? | an advancing cursor. `9x Rifle, 1x Flamer` puts the flamer last (question 4) | if the user must be asked, import cannot expand deterministically and the web app needs a weapon editor that is not designed |
| Is the game browser newest-first? | **yes**, ruled 2026-09-09 (question 7) | `id < @after order by id desc`; `Page<T>.Next` still holds the last id, so the cursor mechanism is unchanged |
| Does the texture kind accept WebP? | **yes**, ruled 2026-09-09 (question 8): Godot 4 loads it natively and every client in a game downloads the table texture | one value in `AssetRules.ContentTypes`, one in the `002` check constraint, and the web app's intake |
| Is the manifest re-issued on reconnect? | not decided here. The client re-requests it. `ExpiresAt` is 60 minutes and a game runs hours | management API → *Deliberately not decided*; the client must handle expiry |

---

## Interfaces

### Produced: the HTTP surface

| Method and path | Body → response | Auth |
| --- | --- | --- |
| `GET /healthz` | → `{status}` | none |
| `POST /v1/auth/otp` | `EmailRequest(Email)` → `OtpSentResponse(MethodId)` | none |
| `POST /v1/auth/otp/verify` | `OtpVerifyRequest(MethodId, Code)` → `SessionResponse(SessionJwt, Account)` | none |
| `POST /v1/auth/password` | `PasswordRequest(Email, Password)` → `SessionResponse` | none |
| `POST /v1/auth/password/signup` | `PasswordRequest` → `201 SessionResponse` | none |
| `POST /v1/auth/password/reset` | `EmailRequest` → `202` | none |
| `POST /v1/auth/password/reset/complete` | `ResetCompleteRequest(Token, Password)` → `SessionResponse` | none |
| `POST /v1/auth/session/refresh` | `RefreshRequest(SessionJwt)` → `SessionResponse` | none; the JWT may be expired |
| `DELETE /v1/auth/session` | → `204` | bearer |
| `GET /v1/account` | → `AccountRow(Id, Name)` | bearer |
| `PATCH /v1/account` | `RenameRequest(Name)` → `AccountRow` | bearer |
| `DELETE /v1/account` | → `204` | bearer |
| `GET /v1/assets?limit&after&kind` | → `Page<AssetDto>` | owner |
| `POST /v1/assets` | `CreateAssetRequest` → `201 CreateAssetResponse(Asset, UploadUrl?)` | owner |
| `GET`/`PATCH`/`DELETE /v1/assets/{id}` | `PatchAssetRequest(Name?, Scale?, Orientation?, ModelledBase?)` → `AssetDto` / `204` | owner |
| `POST /v1/assets/{id}/confirm` | → `ConfirmResponse(Asset, Triangles?, Warning?)` | owner |
| `GET /v1/assets/{id}/download` | → `DownloadResponse(Url, ExpiresAt)` | owner |
| `GET`/`PUT /v1/assets/{id}/volumes` | `Volume[]` → `TerrainVolume[]` | owner |
| `GET /v1/folders`, `POST /v1/folders` | `FolderRequest(Name, ParentId)` → `FolderDto(Id, ParentId, Name, AssetIds)` list / `201` | owner |
| `PATCH /v1/folders/{id}`, `DELETE /v1/folders/{id}`, `PUT`/`DELETE /v1/folders/{id}/assets/{assetId}` | `FolderRequest` for `PATCH`; all four → `204` | owner |
| `POST /v1/armies/import` | `ImportRequest(Source)` → `201 Tabletop.Content.Army` | owner |
| `GET /v1/armies?limit&after` | → `Page<ArmySummary>` | owner |
| `GET`/`PATCH`/`DELETE /v1/armies/{id}` | `PatchArmyRequest(Name?, Units?, Slots?)` → `Army` / `204` | owner |
| `GET /v1/layouts` | → `LayoutSummary(Id, Name, Size, Terrain, Markers)[]` | owner |
| `POST /v1/layouts` | `TableState` with `Name` → `201 TableState` | owner |
| `GET`/`PATCH`/`DELETE /v1/layouts/{id}` | `PatchLayoutRequest(Name?, State?)` → `TableState` / `204` | owner |
| `GET /v1/properties` | → `PropertiesSummary(ContentHash, Name, GameRules)[]` | bearer |
| `GET /v1/games?limit&after` | → `Page<GameSummary>` | bearer, public |
| `POST /v1/games` | `CreateGameRequest(Name, PropertiesHash, SchedulerShape, Enforcement, WaitsBetweenSteps)` → `201 Tabletop.Protocol.GameInfo` | bearer |
| `GET /v1/games/{id}` | → `GameInfo` | bearer, public |
| `POST /v1/games/{id}/join` | → `GameInfo`. Joins the game, no team | bearer |
| `POST /v1/games/{id}/leave` | → `204` | participant |
| `POST /v1/games/{id}/end`, `DELETE /v1/games/{id}` | → `204` | creator |
| `GET /v1/games/{id}/assets` | → `Manifest(Assets, ExpiresAt)` | participant |
| **`POST /v1/dev/sessions`, development only, not a product endpoint** | `DevSessionRequest(Name)` → `SessionResponse` | none, and the route exists only under `IHostEnvironment.IsDevelopment()` |

The last route is the simulator's token source. Nothing else calls it, no client and no web app may,
and a build whose environment is unset serves `404` for it.

### Produced: types and services

| Type | Members |
| --- | --- |
| `Configuration.ManagementOptions` | `DbConnection, S3Endpoint, S3Bucket, S3AccessKey, S3SecretKey, StytchProjectId, StytchSecret, StytchBaseUrl, WebUrl, MinimumClientVersion, SessionMinutes`; `static From(IConfiguration)` |
| `Configuration.DotEnv` | `static Load()` |
| `Http.ClientVersion`, `ClientVersionVerdict`, `ClientVersionMiddleware` | `Decide(string?, int) : ClientVersionVerdict { Accept, UpgradeRequired, Malformed }` |
| `Http.Keyset`, `Http.Page<T>` | `Keyset.Parse(int?, Guid?)`, `Keyset.Default = 50`, `Max = 200`; `Page<T>.Of(items, limit, id)` |
| `Http.Problems`, `Http.CurrentAccount` | `NotFound()`, `Conflict(detail)`, `Unprocessable(detail)`, `BadGateway(detail)`; `CurrentAccount.Id(ClaimsPrincipal)` |
| `Persistence.Database`, `Persistence.Migrator` | `Open(CancellationToken)`, `Source`; `Migrator.Run(connectionString)` |
| `Identity.StytchJwks` | `Resolve(string? kid)`, `Url` |
| `Identity.StytchAuthentication` | `AddStytchJwt(IServiceCollection, ManagementOptions)`, `AccountClaim = "account_id"`, `SessionClaim = "https://stytch.com/session"`, `Issuer(projectId)`, `JwksClient = "stytch-jwks"` |
| `Identity.StytchClient` | `SendEmailOtp`, `AuthenticateOtp`, `AuthenticatePassword`, `CreatePassword`, `StartPasswordReset`, `CompletePasswordReset`, `RefreshSession`, `RevokeSession`, `SetExternalId`, `DeleteUser`, and for the development mint `FindUserId`, `CreateUser`, `CreateEmbeddableToken`, `AuthenticateMagicLink`; `HttpClientName = "stytch"`; `StytchException(Status, ErrorType)`; `StytchSession`, `StytchUser`, `StytchEmail`, `OtpSent` |
| `Identity.Sessions` | `Complete(StytchSession) : SessionResponse`; `static Problem(StytchException) : IResult` |
| `Identity.DevSessions`, `Identity.DevSessionEndpoint` | `Mint(name) : SessionResponse`, `static AccountIdFor(name) : Guid`, `static EmailFor(name) : string`; `DevSessionRequest(Name)`; `MapDevSession`, registered only under a development environment |
| `Identity.AccountRepository`, `AccountRow` | `Get(id)`, `Insert(id, name)`, `Rename(id, name)` |
| `Identity.AccountDeleter` | `Delete(Guid accountId) : Task<bool>` |
| `Content.VolumeRow` | `ToVolume()`, `static From(id, Volume)`, `Columns`, `InsertColumns`, `InsertValues` |
| `Content.Assets.IBlobStore`, `S3BlobStore` | `static Key(owner, hash)`, `PresignUpload`, `PresignDownload`, `Size`, `ReadRange`, `Delete` |
| `Content.Assets.GlbHeader` | `IsGlb`, `JsonChunkLength`, `CountTriangles`, `HeaderBytes = 20`, `WarnAbove = 100_000` |
| `Content.Assets.AssetKind`, `AssetRules` | `{ Miniature, Terrain, Texture }`; `ContentTypes`, `IsSha256`, `Kind`, `Shape` |
| `Content.Assets.AssetRepository` | `Create`, `Get`, `List`, `Patch`, `MarkUploaded`, `Delete`, `GetVolumes`, `PutVolumes` |
| `Content.Assets.FolderRepository` | `List`, `Create`, `Patch`, `Delete`, `AddAsset`, `RemoveAsset` |
| `Content.Armies.ArmyForgeClient` | `static ListId(source)`, `static Parse(json)`, `Fetch(listId)`, `HttpClientName = "army-forge"`, `BaseUrl`; `ArmyForgeException(Status)` |
| `Content.Armies.ArmyForgeImport` | `ToArmy(AfList, source, catalog)`, `ParseBase(round)`, `HeightFromBase(base)`, `GameRules`, `DefaultBody`, `HeightScale`; `PropertyLevel { Unit, Model }`; `UnsupportedGameSystemException` |
| `Content.Armies.CatalogKinds` | `For(string gameRules) : IReadOnlyDictionary<string, PropertyLevel>` |
| `Content.Armies.ArmyRepository` | `Insert`, `Get`, `List`, `Patch`, `Delete`; `ArmySummary`, `ImportRequest`, `UnitPatch`, `SlotPatch`, `PatchArmyRequest` |
| `Content.Layouts.LayoutRepository` | `Save`, `Get`, `List`, `Patch`, `Delete`, `static ReadTree`; `LayoutSummary`, `PatchLayoutRequest` |
| `Games.GameRepository` | `ListProperties`, `Create`, `List`, `Get`, `IsParticipant`, `IsCreator`, `Join`, `Leave`, `End`, `Delete`, `ManifestAssets` |
| `Games` DTOs | `PropertiesSummary`, `CreateGameRequest`, `GameSummary`, `ManifestEntry`, `Manifest`, `ManifestRow` |

### Consumed

| From | Names |
| --- | --- |
| `Tabletop.Content` | `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, `ArmySnapshot`, `TableState`, `TerrainPlacement`, `TerrainVolume`, `Marker`, `MarkerKind`, `DeploymentZone`, `VisualRef`, `ContentJson` |
| `Tabletop.Protocol` | `ProtocolVersion.{Current, Header, UpgradeRequiredStatusCode}`, `GameStatus`, `SchedulerShape`, `Enforcement`, `Team`, `Participant`, `GameInfo` |
| `Tabletop.Geometry` | `Base`, `BaseShape`, `Orientation`, `Volume`, `VolumeKind`, `Footprint`, `FootprintKind`, `TerrainDeclarations`, `TableSize`, `Point2`, `Point3`, `Yaw` |
| contracts plan | `scripts/postgres/init/001_schema.sql`; `TABLETOP_DB_CONNECTION`, `TABLETOP_S3_ENDPOINT`, `TABLETOP_S3_BUCKET`, `TABLETOP_S3_ACCESS_KEY`, `TABLETOP_S3_SECRET_KEY`, `TABLETOP_MANAGEMENT_URL`, `TABLETOP_WEB_URL`, `TABLETOP_STYTCH_*` |
| Stytch Consumer API | `/v1/otps/email/login_or_create`, `/v1/otps/authenticate`, `/v1/passwords`, `/v1/passwords/authenticate`, `/v1/passwords/email/reset/start`, `/v1/passwords/email/reset`, `/v1/sessions/authenticate`, `/v1/sessions/revoke`, `PUT`/`DELETE /v1/users/{id}`, `GET /v1/sessions/jwks/{project_id}`; and for the development mint `GET /v1/users/{id}`, `POST /v1/users`, `POST /v1/magic_links` (embeddable, Consumer API only, returns the token instead of mailing it), `POST /v1/magic_links/authenticate` |
| Army Forge | `GET /api/tts?id={listId}` |

### Obligations this module places on others

| Module | Must honour |
| --- | --- |
| Game server | verify the JWT exactly as here: JWKS at `{StytchBaseUrl}/v1/sessions/jwks/{project_id}` with **no** credentials, `iss = stytch.com/{project_id}`, `aud = project_id`, RS256, 30 s skew, account id in the top-level claim `account_id` as a GUID string; verify **at connect only** (a socket outlives the five-minute JWT and is never re-verified); write only the rows in the split table above; set `participant.army_id` on `PickArmy`, because the manifest reads it before a snapshot exists; regenerate every child id on `ReplaceDraft` from a loaded layout, since placement, volume, marker and zone ids are global primary keys; write `army_snapshot` as `ContentJson.Serialize(ArmySnapshot)`; seed the `stat_definition` names `Quality`, `Defense` (unit) and `Range`, `Attacks` (weapon) that import writes as stat keys; load at least one `properties` row, without which no game can be created |
| Interpreter, through the OPR properties document | author an Age of Fantasy content set. `aof` lists import as of the 2026-09-06 ruling, a game pins a set by `properties.content_hash`, and the only authored document is `grimdark-future-core.properties.json`, whose catalog is Grimdark Future's army books and spells. An Age of Fantasy army can therefore be imported before anything exists to play it with, and nothing detects the mismatch: `army.game_system` and `properties.game_rules` are both `grimdark-future`, which is what lets one rules implementation serve both |
| Interpreter, through the OPR properties document | declare `Quality` and `Defense` at `model` level beside their `unit` declarations, because a joined hero's slots carry both as slot stats and `slot_stat` has no foreign key to `stat_definition` to catch the gap. **Both are discharged.** The owner ruled on 2026-09-06 that a joined hero attacks at its own Quality and blocks on the unit's Defense until every other model is dead, so the slot's `Quality` is read for every hit roll and the slot's `Defense` once the unit holds one model, and a `unit`-kind entry on a slot is the whole unit's rule in both readers. Interpreter question 13 records the mechanism |
| Web app | host `/reset-password` at `TABLETOP_WEB_URL` and read `?token=` from Stytch's redirect; compute the SHA-256 before `POST /v1/assets`; `PUT` with the same `Content-Type` it declared; call `/confirm` and show `Warning`; write the bounding-box volume with `PUT /v1/assets/{id}/volumes`. Unit and slot names are read-only. They are the list's |
| Client shell | send `Tabletop-Client-Version: {ProtocolVersion.Current}` on every request; treat `426` as a screen; refresh with `POST /v1/auth/session/refresh` about once a minute; apply `ManifestEntry.Scale/Orientation` to another participant's mesh; mirror `AccountRow`, `SessionResponse`, `OtpSentResponse`, `Page<T>`, `GameSummary`, `CreateGameRequest`, `PropertiesSummary`, `ArmySummary`, `LayoutSummary`, `AssetKind`, `AssetDto`, `DownloadResponse`, `ManifestEntry`, `Manifest` member-for-member |
| Simulator | call `POST /v1/dev/sessions` once per team plus once for the recorder, and seed `participant` rows for the account ids it returns. It needs this service running under `ASPNETCORE_ENVIRONMENT=Development` with the Stytch secret set, and network access to the Stytch test project |
| Stytch dashboard | `TABLETOP_WEB_URL/reset-password` registered as a redirect URL; the `SESSION` custom claim template set to exactly `{"account_id": "{{ user.external_id }}"}`, in the test environment and again in live, because its absence takes both services down with no Stytch error; the two URLs registered 08-24 for OAuth and magic links can go |

---

## Sequences

Three flows are drawn where they are designed: **login and account linking** (§3), **presigned
upload and confirm** (§6), and **total account deletion** (§13). The two auth flows are stated in
prose rather than drawn.

### Create a game, then connect

```mermaid
sequenceDiagram
    participant C as Client
    participant M as Management API
    participant DB as Postgres
    participant G as Game server

    C->>M: GET /v1/properties
    M->>DB: select from properties
    M-->>C: PropertiesSummary[]
    C->>M: POST /v1/games {name, propertiesHash, shape, enforcement, waits}
    M->>DB: select game_rules from properties where content_hash = hash
    alt unknown hash
        M-->>C: 422 problem+json
    else
        M->>DB: insert game (status 'lobby', game_rules copied, created_by)
        M->>DB: insert participant (team_id null, army_id null, army_snapshot null)
        M-->>C: 201 GameInfo
    end
    C->>G: WebSocket Connect{sessionJwt, gameId, clientVersion}
    G->>G: verify the JWT against the JWKS
    G->>DB: select game, team, participant
    G-->>C: Welcome{participantId, GameInfo}
    Note over M,G: No call between the two servers. The session exists because a client arrived.
```

*Nobody instantiates a game session.*

The client holds a `GameInfo` from management and a `GameInfo` from the socket. After any lobby
edit only the socket's is current. `POST /v1/games` succeeds against a `properties` table the game
server may not have finished seeding.

### Key resolution, and the refresh loop

**Key resolution.** The `JwtBearer` handler calls `StytchJwks.Resolve(kid)` for every request. On
an empty cache it fetches `GET {StytchBaseUrl}/v1/sessions/jwks/{project_id}` with no credentials.
When the token names a `kid` the cached set does not hold, it refetches **once, and only if the
last fetch is more than 30 seconds old**. That floor is a stampede guard. It is also the exposure:
a Stytch key rotation inside 30 seconds of the last fetch answers `401` until the window passes.
The resolver is called synchronously, as `Resolve(kid).GetAwaiter().GetResult()`. The fetch blocks
a request thread behind a `SemaphoreSlim` that every other concurrent caller also waits on.

**Refresh.** The client calls `POST /v1/auth/session/refresh` about once a minute with the JWT it
holds, expired or not. Stytch documents that `/v1/sessions/authenticate` accepts a JWT already past
its `exp`, which the endpoint depends on. The handler calls `RefreshSession(jwt)` and hands the
result to `Sessions.Complete`, which finds the account already linked and returns the JWT Stytch
has just minted. **That is one `POST /v1/sessions/authenticate` call per refresh, per client, per
minute.** The fresh JWT carries `account_id` because the template runs on every mint.

### Ending or deleting a game the game server is holding

```mermaid
sequenceDiagram
    participant P as A participant
    participant M as Management API
    participant DB as Postgres
    participant G as Game server (game in memory)
    participant O as Other participants

    P->>M: DELETE /v1/games/{id}
    M->>DB: IsCreator?
    M->>DB: delete game.game_event where game_id
    M->>DB: delete game (cascades team, participant, table_state)
    M-->>P: 204
    Note over G: Still serving from memory. Nothing told it.
    O->>G: a command
    G->>DB: insert game.game_event (game_id, seq, ...)
    Note over DB: An event row for a game that no longer exists: no FK crosses the schema boundary.
```

*A delete or an `/end` during play is invisible to the process holding the game.*

There is no management-to-game-server channel (`architecture.md`, "Contracts to freeze": no M2M).
`/end` leaves only a stale status, which the server overwrites on its next flip. `DELETE` leaves an
in-memory game with no row, and its appends write orphan log rows into the other schema. Nothing in
either plan detects or cleans that.

---

## State and data

This service holds **no in-memory state** beyond two caches: the `StytchJwks` key set, and the
`NpgsqlDataSource` connection pool. Nothing is rebuilt from the event log here. The event log is
the game server's, and this service only deletes from it. Restarting the process loses nothing.

### The tables this service owns

Three diagrams rather than one: the management schema is twenty-five tables.

```mermaid
erDiagram
    ACCOUNT ||--o{ BLOB : owns
    ACCOUNT ||--o{ ASSET : owns
    ACCOUNT ||--o{ FOLDER : owns
    BLOB ||--o{ ASSET : bytes
    ASSET ||--o{ ASSET_VOLUME : "terrain profile"
    FOLDER ||--o{ FOLDER : nests
    ASSET ||--o{ ASSET_FOLDER : ""
    FOLDER ||--o{ ASSET_FOLDER : ""

    ACCOUNT {
        uuid id PK
        text name
    }
    BLOB {
        uuid owner_id PK
        text hash PK
        text s3_key
        bigint byte_size
        bool uploaded
        text content_type
    }
    ASSET {
        uuid id PK
        uuid owner_id FK
        text blob_hash FK
        text name
        text kind
        real scale
        real orientation_pitch
        real orientation_yaw
        real orientation_roll
        text modelled_base_shape
        real modelled_base_a
        real modelled_base_b
    }
    ASSET_VOLUME {
        uuid id PK
        uuid asset_id FK
        text kind
        text footprint
        real dim_a
        real dim_b
        real top
        bool impassable
        bool blocks_los
        bool cover
        text notes
    }
    FOLDER {
        uuid id PK
        uuid owner_id FK
        uuid parent_id FK
        text name
    }
    ASSET_FOLDER {
        uuid asset_id PK
        uuid folder_id PK
    }
```

*The account and the library. `blob.uploaded` and `blob.content_type` come from this module's
migration `002` and are not yet in `schema.md`. `ASSET_VOLUME` is abbreviated here: it carries all
six declaration booleans and the full footprint, seventeen columns mapped by one `VolumeRow`.*

```mermaid
erDiagram
    ACCOUNT ||--o{ ARMY : owns
    ARMY ||--|{ UNIT : contains
    UNIT ||--o{ UNIT_STAT : ""
    UNIT ||--o{ UNIT_PROPERTY : ""
    UNIT ||--|{ MODEL_SLOT : contains
    MODEL_SLOT ||--o{ SLOT_STAT : ""
    MODEL_SLOT ||--o{ SLOT_PROPERTY : ""
    MODEL_SLOT ||--o{ WEAPON : loadout
    WEAPON ||--o{ WEAPON_STAT : ""
    WEAPON ||--o{ WEAPON_PROPERTY : ""

    ARMY {
        uuid id PK
        uuid owner_id FK
        text name
        text game_system
        text source
    }
    UNIT {
        uuid id PK
        uuid army_id FK
        int ordinal
        text name
    }
    UNIT_STAT {
        uuid unit_id PK
        text stat PK
        text value
    }
    UNIT_PROPERTY {
        uuid unit_id PK
        text name PK
        int parameter
    }
    MODEL_SLOT {
        uuid id PK
        uuid unit_id FK
        int ordinal
        text name
        text base_shape
        real base_a
        real base_b
        real height
        uuid asset_id
    }
    SLOT_STAT {
        uuid slot_id PK
        text stat PK
        text value
    }
    SLOT_PROPERTY {
        uuid slot_id PK
        text name PK
        int parameter
    }
    WEAPON {
        uuid id PK
        uuid slot_id FK
        text name
    }
    WEAPON_STAT {
        uuid weapon_id PK
        text stat PK
        text value
    }
    WEAPON_PROPERTY {
        uuid weapon_id PK
        text name PK
        int parameter
    }
```

*Ten tables written in one transaction by import and read back in nine queries.
`model_slot.asset_id` has no foreign key, so the reference is soft. Stats are rows at three levels
because stats are declared by `Properties` rather than typed into the engine.*

```mermaid
erDiagram
    ACCOUNT ||--o{ GAME : created
    ACCOUNT ||--o{ PARTICIPANT : "plays as"
    ACCOUNT ||--o{ TABLE_STATE : "saved layouts"
    GAME }o--|| PROPERTIES : "pinned by hash"
    GAME ||--o{ TEAM : has
    GAME ||--o{ PARTICIPANT : has
    GAME ||--o| TABLE_STATE : "draft, then snapshot"
    TEAM ||--o{ PARTICIPANT : has
    TABLE_STATE ||--o{ TERRAIN_PLACEMENT : ""
    TABLE_STATE ||--o{ MARKER : ""
    TABLE_STATE ||--o{ DEPLOYMENT_ZONE : ""
    TERRAIN_PLACEMENT ||--o{ TERRAIN_VOLUME : ""

    GAME {
        uuid id PK
        text name
        text status
        text game_rules
        text properties_hash FK
        text scheduler_shape
        text enforcement
        bool waits_between_steps
        uuid created_by FK
        timestamptz created_at
    }
    TEAM {
        uuid id PK
        uuid game_id FK
        int ordinal
    }
    PARTICIPANT {
        uuid id PK
        uuid game_id FK
        uuid team_id FK
        uuid account_id FK
        uuid army_id FK
        jsonb army_snapshot
    }
    PROPERTIES {
        text content_hash PK
        text name
        text game_rules
    }
    TABLE_STATE {
        uuid id PK
        uuid owner_id FK
        uuid game_id FK
        text name
        real width
        real length
        text texture_builtin
        uuid texture_asset_id
    }
    TERRAIN_PLACEMENT {
        uuid id PK
        uuid table_state_id FK
        text builtin
        uuid asset_id
        real pos_x
        real pos_y
        real yaw
        real scale
    }
    TERRAIN_VOLUME {
        uuid id PK
        uuid terrain_placement_id FK
        text kind
        text footprint
        real dim_a
        real dim_b
        real top
        text notes
    }
    MARKER {
        uuid id PK
        uuid table_state_id FK
        text kind
        real pos_x
        real pos_y
        real diameter
        text label
        text visual_builtin
        uuid visual_asset_id
    }
    DEPLOYMENT_ZONE {
        uuid id PK
        uuid table_state_id FK
        text style
        int team_ordinal
    }
```

*Games and the table state. `TABLE_STATE` has two nullable owners with a check constraint that
exactly one is set: management writes the `owner_id` rows, the game server the `game_id` row.
`PARTICIPANT.game_id`, `unique (game_id, account_id)` and `army_id` are the contracts plan's
proposed corrections to `schema.md`, and this module's SQL requires them. `PROPERTIES` and the
seven tables beneath it are read here and written by nothing that exists.*

No column holds a model position, a wound, or whose activation it is. None of that is a row. It is
events, reconstructed into the game server's memory. A "current position" column here would mean
the event spine has been broken.

The one `jsonb` this module reads is `participant.army_snapshot`. It reads it **only** to collect
asset ids for the manifest, via `ContentJson.Deserialize<ArmySnapshot>`. The snapshot stays opaque
on purpose. Normalised, a later migration could alter what a past game was played with.

---

## Failure modes

### Concurrency and ordering

| Hazard | What happens | What the design does |
| --- | --- | --- |
| **Two simultaneous last leaves** | each `Leave` deletes its own `participant` row and then runs `update game set status='ended' where not exists (select 1 from participant where game_id = @id)`. Under read committed each transaction may still see the other's row, so **neither** ends the game and it survives with zero participants | nothing. No advisory lock, no `select for update`, no test. The consequence is an empty game listed as `lobby`, and it is not detected |
| **Concurrent create with the same hash** | `insert blob ... on conflict (owner_id, hash) do nothing` makes the second a no-op, and both assets point at one blob. The **first** row's `byte_size` and `content_type` win | intended for dedup. Unintended when the second caller declared a different `contentType`, because `confirm` branches on `asset.ContentType`, which is read from the blob row |
| **Confirm twice** | idempotent: `Size` still matches, the header is read again, `uploaded` is set again. The dedup path re-reads the header so a second asset over the same bytes still gets its triangle count | by design |
| **Two `PUT /volumes` on one asset** | delete-then-insert inside one transaction. Last writer wins, and volume ids change every time | by design: volumes are a value, not an identity |
| **Migration during a live game** | management runs DbUp at startup against the database the game server is holding games in. A management deploy can add or alter a column under it | named as still open by both the management API spec and `schema.md`, and not addressed. There is no coordination, no read-only window, and no version handshake |
| **Delete or `/end` during play** | the game server keeps serving from memory and appends `game_event` rows for a game row that is gone | no channel exists to tell it |
| **A `PATCH` on an army during setup** | the game server writes `army_snapshot` from the army "as it stands" at `EndSetup`; an edit landing between the read and the write is a lost or surprising update | accepted. The army is loaded once at setup. The army spec says independence rather than immutability, and no versioning exists |

### Lifecycle and leakage

| Hazard | Consequence | Disposition |
| --- | --- | --- |
| `POST /v1/assets` and then no `confirm` | a `blob` row with `uploaded = false` forever, and possibly an S3 object with no confirm | **left in place.** No reaper. The row is invisible to downloads and manifests, and delete removes it with the asset |
| Presigned `PUT` carries no size constraint | `IBlobStore.PresignUpload` takes `byteSize` and does not use it, so a client can `PUT` any number of bytes. The only size check is `confirm`, after the bytes are in the bucket | not addressed. A content-length-range condition would need a POST policy rather than a presigned `PUT` |
| S3 delete after the transaction commits | if `blobs.Delete` throws after the rows are gone, the object leaks with nothing pointing at it | accepted. The alternative is deleting bytes before committing, which loses data on rollback |
| Account deletion: Stytch fails with anything but `404` | content and games are already gone. The caller gets `502`. The Stytch user survives, so logging in again recreates an **empty** account with the same id | ordering is database first, so a Stytch failure leaves no orphaned content |
| A valid JWT with no `account_id` claim | `CurrentAccount.Id` throws `UnauthorizedAccessException`. Task 4 says the Task 3 exception handler maps it to `401`; the handler as written in Task 3 maps only `ArgumentOutOfRangeException`, so this is a `500` | a gap, and untested. Listed under *Open questions* |
| The `SESSION` claim template missing or misspelled | every JWT mints without `account_id`, so every management request is the row above and the game server rejects every connection. Stytch reports no error anywhere | **not defended.** The template is configuration living outside the repo, so no test can see it. The manual pass at the end of Task 5, reading the JWT at jwt.io, is the only evidence it exists |
| JWKS refetch throttle | a Stytch key rotation within 30 seconds of the last fetch yields `401`s until the window passes | accepted stampede guard |
| Blocking key resolution | `IssuerSigningKeyResolver` calls `jwks.Resolve(kid).GetAwaiter().GetResult()`, so the first request after a cold start blocks a thread-pool thread on an outbound HTTP call, behind a semaphore every other concurrent request also waits on | not addressed. A burst at startup is the exposure |
| Presigned download URL outlives participation | a player who left keeps a working URL for up to 60 minutes | **accepted**: another player retaining a mesh they were already shown harms nobody |
| Manifest `ExpiresAt` is 60 minutes, and games run hours | mid-game asset loads fail after an hour unless the client re-requests | the spec leaves re-issue undecided. The client must handle it |
| `GET /v1/games/{id}` exposes participants' display names to any logged-in caller | there is no relationship graph, but a public game reveals who is in it | consistent with games being public; what an account looks like to another account beyond a name is undecided (accounts) |
| Army Forge changes its export format | `POST /v1/armies/import` starts throwing, or maps silently wrong values | only a saved fixture guards it. A format change is a server deploy, which is the reason import lives here |
| `CatalogKinds.For` returns empty | every imported rule lands at unit level. Model-level rules such as `Tough(X)` land wrong until content exists, and re-importing later changes the outcome | not addressed. There is no content import yet, so this is today's behaviour |

### Refusals

Under `strict` enforcement the **game server** refuses commands. Nothing in this service refuses
anything on rules grounds. The triangle check warns. Model placement, coherency and base overlap
are not this service's concern at all. The only things this service refuses are malformed input,
an unknown `properties_hash`, a non-GLB masquerading as one, an unsupported game system, and a row
that is not the caller's.

---

## Testing

Test-driven throughout: a failing test, then the code, every task ending green. `dotnet test
tests/Tabletop.Management.Tests` reaches 58 tests across thirteen tasks.

### Headless unit tests, no Docker

| Subject | Tests |
| --- | --- |
| `ClientVersion.Decide` | five cases: missing, equal, above, below, malformed |
| `Keyset.Parse` / `Page<T>.Of` | default 50, cap 200, non-positive throws, `Next` only on a full page |
| `GlbHeader` | magic and version, the JSON chunk length, triangle count per `mode` (4, 5, 6, lines), summing over primitives, the 100,000 boundary. `TestGlb.Build` synthesises a structurally valid GLB with an empty binary chunk |
| `ArmyForgeImport` | against the **real** saved export (`Fixtures/army-forge-c2-6YsWRH3py.json`): parse, combined merge, stats and bases, catalog-driven property level, the weapon cursor over a hand-written five-model list, range on ranged weapons and absent on melee, `ListId` from a share link and a bare id, an `aof` list importing as `grimdark-future` and `aofs` refused, the height scaled from the base. Two hand-written lists cover the join: a hero merged into a squad (six slots, the host's unit stats, the hero's own slot stats, rules, base and weapon on slot six) and the resolution rules (a hero naming an absorbed `combined` partner joins the merged unit, a name nothing answers and a name whose entry itself joins each stay their own unit) |

### Integration tests, Docker required

`ManagementFactory : WebApplicationFactory<Program>` with three fakes and one real dependency:

- **`TestDatabase`**: a database of its own per factory on the Docker Postgres named in `.env`,
  created, migrated by `Migrator.Run`, dropped `with (force)` on dispose. `MigrationTests` asserts
  33 tables across the `management` and `game` schemas and that a second `Migrator.Run` is a no-op.
- **`TestKeys`**: an RSA key, a JWT minter matching Stytch's claim shape, a fake JWKS handler that
  counts fetches, and `Rotate()`. `AuthenticatedClient(accountId)` inserts the account row, mints a
  JWT and sets the bearer header. Every later test uses that helper.
- **`FakeStytch`**: an `HttpMessageHandler` implementing enough of Stytch to log in: users by
  email, one fixed OTP code, passwords, external ids, sessions minted through `TestKeys`, plus
  revoke and delete lists to assert against. It asserts Basic auth is present on every call. It
  also serves the four calls the development mint uses, so `DevSessionTests` runs without touching
  Stytch. `ManagementFactory.HostEnvironment` defaults to `Testing` and that one test class stands
  a second host up as `Development`.
- **`FakeArmyForge`**: serves the fixture for one id and `404` for anything else.
- **MinIO, real.** `BlobStoreTests` does a presigned `PUT`, a `HEAD`, a ranged read and a presigned
  `GET` against the actual bucket, and the asset tests upload real bytes through the real presigned
  URL. A fake here would have hidden the mechanism being designed.

Notable integration assertions: `426` before anything else runs, and `problem+json` on it; the CORS
preflight answered for `TABLETOP_WEB_URL` and not for another origin; the same bytes twice
short-circuiting the upload and only the second delete removing the blob; an STL renamed `.glb`
refused at confirm; a 100,001-triangle GLB warned and accepted; keyset paging across three assets;
another owner's row answering `404` on `GET`, `PATCH` and `DELETE`; a folder cycle refused;
import storing and reading back a byte-identical tree; unit-default then per-slot binding then
clear; save-a-layout regenerating every id; join at `ended` and idempotent re-join; the last leave
ending the game; a participant who is not the creator answering `404` on `/end` and on `DELETE`,
and the creator still deleting after leaving; a whole-game delete removing `game.game_event`; the manifest returning three
assets across two owners with the owner's corrections and excluding the unconfirmed one; deletion
removing content, blobs, three games including one created-and-left, and the Stytch user, while
leaving another account's untouched game alone; `POST /v1/dev/sessions` answering `404` on the
`Testing` host and no Stytch call being made; the same name minting twice to one account id, one
Stytch user and no re-mint, with the second JWT accepted by `GET /v1/account`.

### What cannot be tested headlessly

- **Real Stytch.** Every automated test runs against `FakeStytch`. Task 5 ends with one manual
  pass: set the secret, `POST /v1/auth/otp` to a real address, verify the emailed code, and check
  the JWT at jwt.io carries `account_id`. Two things stay unproven by the suite: that the project's
  `SESSION` claim template is in place, and that `session_duration_minutes` behaves as documented.
  `FakeStytch` renders the template itself, so a missing real one looks exactly like a passing
  test run. Task 5 also mints once for real through `POST /v1/dev/sessions`, which is the only
  proof that the embeddable-magic-link pair works against the live project. A `--driver socket`
  simulator run depends on that pair and therefore on network access to the Stytch test project,
  which the in-process driver does not need.
- **The live Army Forge format.** A fixture is a snapshot. The suite cannot notice a format
  change.
- **CORS in a browser.** The preflight test exercises the middleware, not a real browser's
  enforcement.
- **Presigned URL reachability from Windows.** URLs are minted against `TABLETOP_S3_ENDPOINT`
  verbatim, so `localhost:9000` works from both WSL and the Windows host. Only the manual
  `Invoke-WebRequest` check in Task 14 exercises that.

### Notable gaps

- **No concurrency test anywhere.** Not the double leave, not two creates racing on one hash, not
  two confirms.
- **The compose-init baseline path is never exercised.** `TestDatabase` creates an empty database,
  so `Migrator.Baseline` always takes its early return. The branch for a database the compose init
  already populated has no test. A partially applied init would be baselined as complete, because
  the probe is `management.account` alone.
- **No test for a valid JWT lacking `account_id`**, the case that reveals the exception-handler
  gap.
- **No test that `blob.content_type` cannot diverge** from a second asset's declared type over the
  same hash.
- Nothing asserts a presigned URL's TTL, or that an expired one is refused.

---

## Open questions and deferred work

### The plan's questions, with the assumption taken

| Question | Assumption | If the answer differs |
| --- | --- | --- |
| ~~1. Custom claim by per-login `session_custom_claims` or by a Stytch dashboard claim template (`{"account_id": "{{ user.external_id }}"}`)?~~ | **ruled 2026-09-06: the template.** The code path's advantage, in the repo and testable, did not survive the extra Stytch call it cost on every login and every refresh | already applied above. `Sessions.Complete` makes no second call on a returning login or a refresh, `external_id` becomes the mechanism, and the template is environment setup whose absence fails silently |
| ~~2. Do joined heroes (`joinToUnit` without `combined`) import as their own unit?~~ | **ruled 2026-09-06: the hero is a model of the unit it joined.** The assumption had been that the join was lost, no table recording it | already applied above. `Merge` returns a host plus the entries joining it, `Section` appends one entry's slots with its own stats, base and cursor, the unit's stats and properties stay the host's, and an unresolvable `joinToUnit` keeps the old behaviour |
| ~~3. Default base and height when Army Forge gives none?~~ | **ruled 2026-09-06: the base default stands, the height is scaled from the base.** `1.5 ×` its long axis, replacing the flat 1.5" | already applied above. `HeightFromBase` is one expression, and a per-`GameRules` table of heights would replace it |
| 4. How are partial weapon counts dealt onto models? | an advancing cursor, and the flamer lands last | if the user must choose, import cannot expand deterministically and the web app needs a per-slot weapon editor and a `PATCH` shape for weapons, neither designed |
| ~~5. Which game systems import?~~ | **ruled 2026-09-06: Grimdark Future and Age of Fantasy, both as `grimdark-future`.** The mechanics are the same and only the content differs | already applied above. No Age of Fantasy content set exists, so such a list imports before anything can play it, recorded against the interpreter |
| ~~6. Who may `/end` and `DELETE` a game?~~ | **ruled 2026-09-06: the creator alone**, still so after leaving | already applied above. `GameRepository.IsCreator` gates both routes and anyone else gets `404` |
| 7. Is `GET /v1/games` newest-first? | **yes**, ruled 2026-09-09 | `id < @after order by id desc`; `Page<T>.Next` still holds the last id |
| 8. Does the texture kind accept `image/webp`? | **yes**, ruled 2026-09-09 | one value in `AssetRules.ContentTypes`, one in the `002` check constraint, and the web app's `ContentType` union and `classifyFiles` |

A ninth question, whether a missing version header is accepted, was struck on 09-06. The specs
answer it: the check exists for the downloaded desktop client, and the client shell says the client
sends its version on every request.

### Deferred, with where it is recorded

| Deferred | Where |
| --- | --- |
| Multipart upload for large files | management API → *Still open*. `IBlobStore` has no multipart member |
| Rate limiting | management API → *Deliberately not decided* |
| Whether the manifest is re-issued on reconnect or re-requested | management API → *Deliberately not decided* |
| The JWT lifetime and refresh interval as anything but working figures | management API → *Deliberately not decided*. Five minutes and one minute are used here |
| How authored `Properties` content reaches the eight tables | `architecture.md` → *Rules as data*; whole-design review → *Left as first-pass assertions*. Until it exists, `GET /v1/properties` is empty and no game can be created |
| Indexes beyond primary and foreign keys | `schema.md` → *Still open*; the game browser's listing query is the first candidate |
| Whether the two schemas become two databases | contracts plan spec correction 5 settles one database for local development; hosting may revisit |
| Hosting, and therefore how a migration is coordinated with a running game server | `architecture.md` → *Deployment* (deferred 09-04) |
| Whether the client ever edits an army | web app → *Still open* |
| Password-protected games, friend-joining, a relationship graph | accounts → *Deliberately not decided* |

### Spec corrections this module proposes

Seven, all listed in the plan's *Spec corrections proposed* and none applied here: `blob.uploaded`
and `blob.content_type` in `schema.md`; the full `/v1/auth/*` list and the claim-template paragraph
in the management API spec; striking two of its *Deliberately not decided* items (army `PATCH`
shape, error conventions); the Army Forge source format and mapping rules; Army Forge's
`bases.round` as the import default in the army spec; the creator as the rule for `/end` and
`DELETE` in accounts; and the missing-version-header rule.

---

## References

**Plan.** `docs/superpowers/plans/2026-09-05-management-api.md` is the source of truth for what
gets built: fourteen tasks, the File Structure, every `Interfaces` block, the *Interfaces →
Produces* tables, *Rows by writer*, the seven spec corrections and the eight owner questions.

**Specs**, in the order the plan names them:

- `docs/superpowers/specs/2026-08-29-management-api-design.md` (primary)
- `docs/superpowers/specs/2026-08-29-accounts-design.md`
- `docs/superpowers/specs/2026-08-28-army-design.md`
- `docs/superpowers/specs/2026-08-28-asset-library-design.md`
- `docs/superpowers/specs/2026-08-29-setup-content-design.md`
- `docs/superpowers/specs/2026-08-29-web-app-design.md`
- `docs/superpowers/specs/2026-09-04-mesh-conversion-design.md`
- `docs/superpowers/specs/2026-09-04-client-shell-design.md`
- `docs/superpowers/specs/2026-09-04-setup-phase-design.md`
- `docs/superpowers/specs/2026-08-29-identity-and-enumerations-design.md`
- `docs/superpowers/specs/2026-08-29-content-values-design.md`
- `docs/superpowers/specs/2026-09-04-whole-design-review.md` (newest, wins)

**Repo documents.** `docs/architecture.md`, `docs/data-model.md`, `docs/schema.md`, `CLAUDE.md`.
`docs/workstreams/03-management-identity.md` and `09-management-collections.md` are stale and not
followed.

**Consumed plan.** `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md` supplies the
three libraries, `001_schema.sql`, `docker-compose.yml`, the `TABLETOP_*` names, and the reserved
ports.
Its spec corrections 2 and 4 (`participant.army_id`, `participant.game_id` and its unique
constraint) are prerequisites for this module's SQL.

**External, verified.** Stytch Consumer API reference, read 2026-09-05 and re-checked page by page
2026-09-06 (test project `project-test-437432c7-798d-49d2-9dc4-eff103937e0f`, base
`https://test.stytch.com`); the JWKS endpoint's lack of required credentials confirmed by an
unauthenticated `curl` on both dates. Army Forge `GET https://army-forge.onepagerules.com/api/tts?id=c2-6YsWRH3py`
fetched live 2026-09-06 (`200`, 26 KB, `access-control-allow-origin: *`) and saved as the fixture.

**Rulebook.** No page of Grimdark Future is read by this module. It stores content and enforces no
rule. The one number from the rules side is the triangle warning threshold, 100,000, and that is a
product decision from mesh conversion rather than a rulebook figure. Pages 6, 16, 17–18, 19 and 49
of Grimdark Future v3.5.1 are read by the setup phase, whose rows this module stores as layouts but
never interprets.

**Sibling design documents** in `docs/design/`: `contracts/`, `geometry/`, `game-server/`,
`scheduler/`, `interpreter/`, `client/`, `web-app/`.
