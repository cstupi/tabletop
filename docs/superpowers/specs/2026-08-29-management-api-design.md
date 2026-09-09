# The management API

Design session, 2026-08-29. The HTTP surface over the schema.

Fourteenth module. It follows [`docs/schema.md`](../../schema.md) and covers what the two
deliverables actually call — the web app for preparation, the client for games — plus the
two mechanics earlier sessions deferred: how asset bytes arrive, and how a list is imported.

Not the game server's WebSocket protocol. That is `Tabletop.Protocol` and a contract rather
than an API.

## Shape

**One API, two consumers.** The web app and the game client call the same endpoints; nothing
is exposed to one and not the other except by what the caller owns.

**Resource-shaped, with action sub-resources where the operation is a verb** — `join`,
`leave`, `end`, `import`, `confirm`. Pretending those are `PATCH`es on a state field would be
worse than admitting they are actions.

**`/v1` in the path.** Not a strategy: two versions will never be served side by side, and
every client has to match anyway. It is a label plus a place to stand, and the mechanism that
matters is a version check that returns an explicit *update required* rather than a
mysterious rejection — because the Godot client is a downloaded desktop application and a
user can be months behind.

## Authentication

**Management is the only thing that talks to Stytch**, and the only thing that issues tokens.
Settled by [accounts](2026-08-29-accounts-design.md), revised by
[the web app](2026-08-29-web-app-design.md), and revised again 09-04 by
[the client shell](2026-09-04-client-shell-design.md): **both the web app and the client log in
here, with their own forms**, and there is no social login.

```
POST   /v1/auth/otp                      send a code to an email address → method id
POST   /v1/auth/otp/verify               method id + code → session
POST   /v1/auth/password                 email and password → session
POST   /v1/auth/password/signup          email and password → new user, session
POST   /v1/auth/password/reset           email → Stytch sends the reset link to the web app
POST   /v1/auth/password/reset/complete  token + password → session
POST   /v1/auth/session/refresh          an expiring or expired session JWT → a fresh one
DELETE /v1/auth/session                  log out
```

~~`POST /v1/auth/magic-link`, `GET /v1/auth/oauth/{provider}/start`,
`GET /v1/auth/oauth/callback`~~ — struck 09-04 with the methods they served.

Every session-returning call ends the same way: the account row is created or found by
`external_id`. The JWT carries our id because the project's Stytch custom claim template maps
`{{ user.external_id }}` to the claim `account_id`, and Stytch evaluates the template on every
mint; *We issue no tokens of our own* states what the claim is and what Stytch's own JWT holds
instead. Management sends `session_custom_claims` nowhere. On the one login where `external_id`
is first written the session was minted a moment too early, so that login re-mints the JWT once.
No other login and no refresh does. Refresh exists because the JWT lives five minutes and only
management holds the project secret.

The web app is served on a different origin in development (`TABLETOP_WEB_URL`, port 5173) and
uses a Vite proxy for `/v1`. When it is served from anywhere but management's own origin,
management must answer CORS preflights for that origin, with `Authorization` and `Content-Type`
allowed.

OTP and password are Basic-auth Stytch endpoints requiring the project secret, so they run
server-side regardless.

**The version check** this document called "the mechanism that matters" is a request header:
the client sends its version on every call, and management answers `426 Upgrade Required` when
it is below the minimum the server accepts. No endpoint, one status code, and the client shell
shows it as a screen. Placed 09-04. A request with no version header is accepted: the web app is
deployed with the API and does not send one, and the Godot client always does.

### We issue no tokens of our own

**Stytch's session JWT is the only credential, and it carries our account UUID.** Stytch's
`external_id` already holds `Account.id` — [accounts](2026-08-29-accounts-design.md) made that
the entire link between the two systems — so the claim a service needs is in the token it is
handed.

Both services verify it **locally, against Stytch's public JWKS**. No call to Stytch on the
request path, no shared signing key, and no session row to look up.

**This session originally designed two opaque token tables, and both are gone.** The join token
went on 08-30; `session` went with it, and the argument that had justified it did not survive
examination:

- *"Opaque tokens keep the vendor out of the client."* They do not need to. Our UUID is the
  identifier on both sides, so swapping auth vendors changes what issues the token, not what the
  token means.
- *"The game server cannot call Stytch to validate."* It does not. Signature verification is
  local, against published public keys.
- *"Instant revocation is required by account deletion."* Short expiry covers it — a JWT good
  for five minutes, refreshed every minute, locks a deleted account out within five. That is
  acceptable, and it was never worth a table.
- *"No signing key shared between two services."* There is none. JWKS is asymmetric: Stytch
  signs, both services verify with a public key.

**A join token was designed here too, and dropped for its own reasons.** It carried a game id,
an account id and an expiry, all to state a fact `participant` already records — joining a game
*is* the row. Its expiry was also a live failure: a four-player game runs hours, so a token
issued at the lobby could lapse before a player who dropped at hour three tried to reconnect.

**Corrected 2026-09-06.** Stytch's session JWT puts its own `user_id` in `sub` and does not carry
`external_id`. Our account UUID arrives as the top-level custom claim `account_id`, rendered by
the project's Stytch custom claim template from `{{ user.external_id }}` on every mint. Stytch
reserves `sub` and the `https://stytch.com/*` names, and custom claims are merged at the top
level. The game server reads `account_id` and nothing else.

## Asset bytes never pass through the API

**Presigned S3 URLs in both directions.** The client asks management for permission and gets
a signed URL S3 will accept; the bytes go direct. No AWS credentials reach the client, and no
`.glb` is streamed through a Fargate task.

This matters more for download than upload: **every participant pulls every mesh in a game**,
including other people's.

```
POST   /v1/assets                 create metadata → presigned PUT
POST   /v1/assets/{id}/confirm    upload finished; validate
GET    /v1/assets/{id}/download   → presigned GET
```

The client computes the content hash first, so management issues the URL for a known key —
which also lets a duplicate upload be short-circuited before any bytes move.

### The triangle count is a ranged read of the GLB header

`architecture.md` made a triangle-count check the only guardrail once conversion left the
product, and never said where it would run. Presigned upload appears to make it impossible,
since management never sees the file.

It does not. **A GLB's first chunk is its JSON**, carrying the mesh accessors and their
counts, so the triangle total is a sum over the index accessors and needs no part of the
binary. On `confirm`, management does a **ranged `GET` of the first chunk** and gets an exact
count from a small read.

No worker, no job queue, no streaming — the subsystem deliberately removed when conversion
left the product stays removed.

**The check warns; it does not block.** The server says this mesh is 800,000 triangles and
will hurt everyone in the game, and the upload proceeds. The threshold is 100,000
([mesh conversion](2026-09-04-mesh-conversion-design.md)). This is an upload check, unrelated to
the game's enforcement setting, which gained a refusing position on 09-04; nothing about uploads
refuses.

### GLB only

`architecture.md` accepts "glTF/GLB". **Narrowed to GLB.**

A `.gltf` with external `.bin` buffers is several files with relative references between them,
which breaks single-object presigned upload, makes the header read pointless, and can fail
when the same asset is served to another player's client mid-game.

Nothing is lost. GLB is the same format in one file — same scene graph, same materials — and
it is Blender's default glTF export. Users converting an STL already pass through
[mesh conversion](2026-09-04-mesh-conversion-design.md) in the browser, which emits GLB so they
never encounter the question. The only real cost
is that a texture shared across many models is embedded once per file, and dedup is per user
anyway.

## The game manifest

The library is **private for discovery, not access**. Every other asset endpoint is
owner-only; this is the single exception.

```
GET /v1/games/{id}/assets
```

Authorised by being a participant. Returns presigned `GET` URLs for every mesh and texture in
play, whoever owns them. It is also the answer to *what does this client need before the game
starts*, which is a real loading concern for a game with forty models on the table.

A presigned URL outlives the participation check that produced it. **This does not matter** —
it is a game, and nobody is harmed by another player retaining a mesh they were already
shown.

## Army import

```
POST /v1/armies/import        { source }
```

**Management fetches the list**, rather than the browser fetching and posting JSON. The
parser lives in one place, there is no CORS problem, and a change to Army Forge's format is a
server deploy rather than a client release.

**The source is a share link** (`https://army-forge.onepagerules.com/share?id=…`) or a bare list
id, and management fetches `GET /api/tts?id={id}`. Grimdark Future (`gameSystem: "gf"`) and Age
of Fantasy (`"aof"`) both import, and both carry `game_system = "grimdark-future"`, because one
rules implementation serves both and only the content differs. The skirmish and regiment variants
are refused. Two `combined` entries are one unit. An entry whose `joinToUnit` names another
becomes models of that unit, carrying its own quality, defence and rules on its own slots; a
`joinToUnit` that names nothing, or names an entry which itself joins, imports as its own unit.
A rule's level comes from the `catalog_entry` rows for the game rules, so a model-level rule goes
onto every slot, and a name no catalog knows is a unit rule. Weapons with a count below the
unit's size are assumed to be dealt onto successive models from an advancing cursor, which is
management question 4 and still open.

## The rest of the surface

```
GET    /v1/account
PATCH  /v1/account                            name
DELETE /v1/account                            the full cascade

GET    /v1/assets                             keyset
GET    /v1/assets/{id}
PATCH  /v1/assets/{id}
DELETE /v1/assets/{id}
GET    /v1/assets/{id}/volumes
PUT    /v1/assets/{id}/volumes

GET    /v1/folders
POST   /v1/folders
PATCH  /v1/folders/{id}
DELETE /v1/folders/{id}
PUT    /v1/folders/{id}/assets/{assetId}
DELETE /v1/folders/{id}/assets/{assetId}

GET    /v1/armies                             keyset
GET    /v1/armies/{id}
PATCH  /v1/armies/{id}                        bindings, bases
DELETE /v1/armies/{id}

GET    /v1/layouts
POST   /v1/layouts                            the client saves a setup draft here
GET    /v1/layouts/{id}                       and loads one from here (setup phase, 09-04)
PATCH  /v1/layouts/{id}
DELETE /v1/layouts/{id}

GET    /v1/properties                         the pinned sets a game may choose: name, hash, game_rules (09-04)

GET    /v1/games                              the public browser, keyset
POST   /v1/games                              name, properties_hash, scheduler_shape, enforcement, waits_between_steps → game id
GET    /v1/games/{id}
POST   /v1/games/{id}/join                    → creates the participant row, on no team yet
POST   /v1/games/{id}/leave
POST   /v1/games/{id}/end
DELETE /v1/games/{id}
```

**Authorisation is owner-only throughout**, with two exceptions: the game browser lists games
regardless of owner, because games are public; and the manifest, which is authorised by
participation.

## Pagination is keyset, not offset

`limit` plus an `after` cursor. **UUIDv7 is time-ordered by construction, so the id is the
cursor** and no sort column is needed.

Offset pagination skips and repeats rows when the underlying list changes, and a library
being added to while someone scrolls it is the normal case rather than the edge one.

Needed on assets, armies and the game browser. Folders, teams and a game's participants are
small enough to return whole.

## Deliberately not decided

- ~~Error shapes and status code conventions.~~ Settled 2026-09-05 by the management API plan:
  RFC 7807 `application/problem+json`, and another owner's row is `404`.
- Rate limiting.
- ~~Whether `PATCH` on an army takes a whole document or a patch.~~ Settled 2026-09-05 by the
  management API plan: a patch, carrying a name, unit-default bindings (`UnitPatch`), and
  per-slot binding, base and height (`SlotPatch`).
- The JWT lifetime and refresh interval. Five minutes and one minute are the working figures.
- Whether the manifest is re-issued on reconnect or the client re-requests it.

## Still open

- **The two services now share a database.** The game server reads management's schema, and
  since 09-04 **writes** a game's own rows while its status is `lobby` or `setup` — status,
  teams, participants' team and army snapshot, the `table_state` draft — and is read-only from
  `playing` on ([the setup phase](2026-09-04-setup-phase-design.md)). Teams, team picks and
  configuration changes are lobby commands over the socket, not endpoints here. A management
  migration can break the game server either way.
- Whether asset uploads need multipart for large files, which presigned `PUT` does not cover
  on its own.
