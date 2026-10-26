# deploy

The production path: the two servers behind Caddy, on Docker (`docker-compose.prod.yml`) or directly on a Linux host
(`systemd/`), or the home server's all-container stack behind its existing Caddy (`docker-compose.home.yml`). The
repo-root `docker-compose.yml` is development only.

| File | What |
| --- | --- |
| `docker-compose.prod.yml` | Postgres, MinIO (profile `minio`), the one-shot `migrate`, management, game |
| `docker-compose.home.yml` | The home server: the above plus MinIO on the NAS, the web image and `pg-backup`, no published ports |
| `systemd/` | `tabletop-migrate`, `tabletop-management`, `tabletop-game` units for a host run |
| `prod.env.example`, `home.env.example` | Every setting and secret for each stack |
| `../server/*/Dockerfile` | The server images (`dotnet publish -c Release` on `aspnet:10.0`, user `app`) |
| `../web/Dockerfile`, `../web/Caddyfile` | The web image: the static build served by Caddy on :8080, SPA fallback, `frame-ancestors`; the viewer's hosted build (browser play and replay) at `/viewer/` |
| `../scripts/postgres/roles.sh` | The database roles |
| `../scripts/postgres/backup.sh` | `pg-backup`'s nightly `pg_dump`, keeping the newest 14 |
| `../scripts/minio/init.sh` | The bucket, its policy and the service user; the release publisher (Put on `releases/*` and `builtin/*`) when `RELEASE_S3_ACCESS_KEY` is set |
| `../scripts/release`, `../scripts/release-client` | Cut a release from a clone: test, publish the client, install the web app, deploy (below) |
| `../.github/workflows/` | `ci.yml` (build and tests), `release.yml` (a `vX.Y.Z` tag publishes the client) |

## What the servers enforce

- `ASPNETCORE_ENVIRONMENT` is `Production` in the images and the units. Outside Development a server refuses to start
  with the test Stytch URL, an http or loopback `TABLETOP_WEB_URL` or `TABLETOP_S3_PUBLIC_ENDPOINT`, a dev-stack
  credential (`tabletop`, `tabletop-secret`, ...), or `TABLETOP_DEV_MODE` set. The message names every setting at fault.
- Development behaviour (`POST /v1/dev/sessions`, `SetDice`, migrations at startup, the developer exception page) needs
  `ASPNETCORE_ENVIRONMENT=Development` and `TABLETOP_DEV_MODE=1` together. A `.env` is read only in Development.
- Management never migrates at startup outside dev mode: it connects as a runtime role, and refuses to start while the
  schema is behind the build. `Tabletop.Management --migrate` applies the migrations as the owner role and exits.
- Both servers answer `GET /healthz`; it is for local health checks, not for the public.

## Database roles

`scripts/postgres/roles.sh` (run once, as a superuser) creates:

| Role | Rights |
| --- | --- |
| `tabletop_owner` | Owns the database and every object; used only by `--migrate` |
| `tabletop_management`, `tabletop_game` | Members of `tabletop_runtime`: CONNECT, USAGE on the owner's schemas, SELECT/INSERT/UPDATE/DELETE on its tables, USAGE/SELECT on its sequences, by default privileges |

Under compose the Postgres image runs it on an empty volume. On a host: `sudo -u postgres env POSTGRES_DB=tabletop
TABLETOP_DB_OWNER_PASSWORD=... TABLETOP_DB_MANAGEMENT_PASSWORD=... TABLETOP_DB_GAME_PASSWORD=... scripts/postgres/roles.sh`
after `createdb tabletop`.

## Object store

S3 or Cloudflare R2 is the preferred production store: set `TABLETOP_S3_ENDPOINT` and `TABLETOP_S3_PUBLIC_ENDPOINT` to
the provider, use a token scoped to the bucket (object Get/Put/Delete, and ListBucket so a missing object answers 404),
set the bucket's CORS to allow `PUT`, `GET` and `HEAD` from `TABLETOP_WEB_URL`, and leave the `minio` profile off.

Self-hosted MinIO (profile `minio`): `minio-init` creates the bucket, the policy `<bucket>-objects` (Get/Put/Delete on
its objects, ListBucket on the bucket) and the service user `TABLETOP_S3_ACCESS_KEY` the servers sign with; the root
user is used only by `minio-init`. `MINIO_API_CORS_ALLOW_ORIGIN` is `TABLETOP_WEB_URL`. MinIO publishes no images of
its own any more; the image is `pgsty/minio`, the community build of the same server, pinned by digest.

## Docker

```bash
sudo install -d -m 0700 /etc/tabletop
sudo install -m 0600 -o root deploy/prod.env.example /etc/tabletop/prod.env    # then fill it in: openssl rand -hex 32
docker compose --env-file /etc/tabletop/prod.env -f deploy/docker-compose.prod.yml --profile minio up -d --build
```

- `migrate` runs first on every `up`; `management` starts only after it exits 0, `game` after management is healthy.
- Containers: `read_only`, `tmpfs /tmp`, `cap_drop: [ALL]`, `no-new-privileges`, `restart: unless-stopped`; Postgres runs
  as its image user (70). Postgres and MinIO sit on an internal network with no route out.
- Published on 127.0.0.1 only: management 5100, game 5200, MinIO 9000 (profile `minio`, the blob endpoint presigned URLs name).

## Host (systemd)

```bash
dotnet publish server/management -c Release -o /opt/tabletop/management     # needs the ASP.NET Core 10 runtime on the host
dotnet publish server/game -c Release -o /opt/tabletop/game
sudo install -m 0644 deploy/systemd/*.service /etc/systemd/system/
sudo install -d -m 0700 /etc/tabletop
# /etc/tabletop/migrate.env      TABLETOP_DB_CONNECTION as tabletop_owner
# /etc/tabletop/management.env   runtime TABLETOP_DB_CONNECTION, S3, Stytch, TABLETOP_WEB_URL (prod.env.example's names)
# /etc/tabletop/game.env         runtime TABLETOP_DB_CONNECTION, TABLETOP_STYTCH_BASE_URL, TABLETOP_STYTCH_PROJECT_ID
sudo chmod 0600 /etc/tabletop/*.env
sudo systemctl daemon-reload && sudo systemctl enable --now tabletop-management tabletop-game
```

- The units run with `DynamicUser=yes`, `ProtectSystem=strict`, `NoNewPrivileges`, `PrivateTmp`, an empty capability set
  and a `@system-service` syscall filter, and listen on 127.0.0.1:5100 and 127.0.0.1:5200.
- `tabletop-management` requires `tabletop-migrate`, so each start of management applies pending migrations first.
- Connection strings: `Host=127.0.0.1;Port=5432;Database=tabletop;Username=...;Password=...;GSS Encryption Mode=Disable`,
  plus `Search Path=management,game,public` for the two runtime roles.

## Web app and upstreams

```bash
cd web && npm ci && TABLETOP_BLOB_ORIGIN=https://blobs.tabletop.example.org npm run build   # static site in web/build
./scripts/geometry-wasm && cd viewer && npm ci && npm run build:hosted                      # the play app in viewer/dist, for /viewer/ (geometry-wasm: the WebAssembly runtime in public/geometry/)
```

The viewer's hosted build is the browser play and replay app, open to every signed-in player: same origin as the web app, so it reuses the website's session
`scripts/geometry-wasm` publishes `Tabletop.Geometry` as WebAssembly into `viewer/public/geometry/`, which the build copies into `dist/`: compiled ahead of time when the SDK has the `wasm-tools` workload, else on the runtime's interpreter (the same answers, 3-4x slower per call). `web/Dockerfile` does it in its own stage, which installs the workload and the `python3` its Emscripten needs. `/viewer/`'s CSP carries `script-src 'self' 'wasm-unsafe-eval'` so the runtime can compile, and the file server serves the precompressed `.br`/`.gz` files.

(`localStorage`) and `/v1`, and opens the socket at `wss://<host>/ws`. It goes under `/viewer/` beside `web/build`
(`scripts/release` installs it to `$TABLETOP_WEB_ROOT/viewer/`); `/viewer/` falls back to `/viewer/index.html`, not the
web app's. It has no Tests page and no runner proxy. The game server admits only an admin
or a participant on the socket, and `/v1` requires a session.

What the reverse proxy fronts, all on 127.0.0.1: the static `web/build` (SPA fallback to `index.html`) with the viewer under `/viewer/`, `/v1/*` →
management 5100, `/ws` → game 5200, and the blob endpoint (`TABLETOP_S3_PUBLIC_ENDPOINT`) → MinIO 9000 or the provider.
`/v1/dev/*` exists only in dev mode. The web build's CSP is in `web/svelte.config.js` (hash mode; `TABLETOP_BLOB_ORIGIN`
names the blob origin it may connect to). Management reads client addresses from `X-Forwarded-For` only from
`TABLETOP_TRUSTED_PROXIES`.

## Home server (docker1)

`docker-compose.home.yml` (project `tabletop-home`): everything in containers, so the host needs only Docker and Compose
v2. The owner's Caddy (container `caddy`, network `gateway_default`) terminates TLS for two hosts and reaches the stack by
container name; nothing here publishes a host port.

| Container | Networks | What |
| --- | --- | --- |
| `tabletop-postgres` | backend | Postgres 17, named volume `tabletop-home_postgres-data` on local disk |
| `tabletop-migrate` | backend | one-shot `--migrate` as `tabletop_owner`, on every `up` |
| `tabletop-minio`, `tabletop-minio-init` | backend, gateway | MinIO on `/mnt/tabletop/minio`; init makes the bucket, service user and release publisher |
| `tabletop-management` :5100, `tabletop-game` :5200 | backend, gateway | the servers |
| `tabletop-web` :8080 | gateway | the web app's static build (built in `web/Dockerfile` with the FILES origin in its CSP) and the viewer's hosted build at `/viewer/` |
| `tabletop-pg-backup` | backend | `pg_dump -Fc` daily at 03:30 UTC to `/mnt/tabletop/pg-backups`, newest 14 kept |

`backend` is internal (no route out); `gateway` is the external `gateway_default`, which also gives the servers egress to
Stytch and Army Forge. All containers keep the prod hardening (`read_only`, `cap_drop: [ALL]`, `no-new-privileges`).

**NAS.** `/mnt/tabletop` is the NFS share, mounted before Docker starts (e.g. an `fstab` entry with `_netdev`), with
`minio/` and `pg-backups/` in it. The share squashes every user to 1025:100, so MinIO and `pg-backup` run as `1025:100`
(`TABLETOP_NAS_UID`, `TABLETOP_NAS_GID`) and read back files owned by themselves. `scripts/release` refuses to deploy while
either directory is missing, since Docker would otherwise create it on the local disk.

**First deploy.**

```bash
git clone <repo> /opt/docker/tabletop && cd /opt/docker/tabletop   # as your own user, a member of the docker group
install -m 0600 deploy/home.env.example home.env           # fill in every empty value: openssl rand -hex 32
scripts/release --deploy home --version v1.0.0             # reads ./home.env, else /etc/tabletop/home.env (--env-file)
```

No sudo: every build, test and deploy runs in Docker, as the clone's owner.

With `--tests`, `--deploy home` runs the unit suite, the rules suite and the web and viewer checks in `mcr.microsoft.com/dotnet/sdk:10.0`
and `node:24-alpine` containers over the clone (CI tests every push to `main`, so a release runs none by default). It exports and packages the client in the builder
container (Client releases, below), builds the images tagged with the version, runs `up -d`, waits on the containers' own
states (`migrate` and `minio-init` exited 0; postgres, minio, management, game and web healthy), then uploads the client
and publishes the built-in content (`builtin/`, same publisher and container, `--skip-builtin` skips it).
`--skip-client` skips the client. An update is the same command after `git fetch && git checkout vX.Y.Z`.

**Trusted proxy.** Caddy's requests reach management from its address on `gateway_default`, so
`TABLETOP_TRUSTED_PROXIES` is that network's subnet (`docker network inspect gateway_default -f '{{range
.IPAM.Config}}{{.Subnet}} {{end}}'`). `scripts/release` reads it when `home.env` leaves it unset; a by-hand `docker
compose` needs it in `home.env`. Every container on `gateway_default` can then set `X-Forwarded-For` for management.

**Caddy upstreams** (the owner's config):

| Host | Path | Upstream |
| --- | --- | --- |
| APP `tabletop.cstupi.com` | `/v1/*` | `tabletop-management:5100` |
| APP | `/ws` | `tabletop-game:5200` (WebSocket; Caddy's `reverse_proxy` upgrades it) |
| APP | everything else (the web app, and the play app at `/viewer/`) | `tabletop-web:8080` |
| FILES `files.tabletop.cstupi.com` | everything | `tabletop-minio:9000` |

- Keep the `Host` header (Caddy's default): presigned URLs are signed for the FILES host. No request body limit on FILES
  (mesh uploads and client zips go straight to MinIO).
- `/v1/dev/*` does not exist in Production; nothing to block. `/healthz` on management and game need not be public.
- The web container sends `frame-ancestors 'none'`, `X-Frame-Options`, `nosniff` and `Referrer-Policy`; HSTS is Caddy's.
- Compression is end to end: `/ws` negotiates permessage-deflate between the client and the game server (Caddy's `reverse_proxy` passes
  `Sec-WebSocket-Extensions` through), management compresses `/v1` itself, and the web container its files; Caddy's `encode` leaves an
  encoded answer alone.

**Backups.** A dump now: `docker exec tabletop-pg-backup /scripts/backup.sh once`. Restore into the running stack:
`docker exec -i tabletop-postgres pg_restore -U postgres -d tabletop --clean --if-exists --no-owner --role=tabletop_owner
< /mnt/tabletop/pg-backups/tabletop-<stamp>.dump`. MinIO's data is on the NAS already.

**Client releases.** `--deploy home` runs `scripts/release-client --in-container`, which builds the image
`tabletop-client-build:4.7.2` from `scripts/client-build/Dockerfile` (Godot 4.7.2 .NET headless, its Windows, Linux and
macOS release templates, the .NET 10 SDK, zip, jq, the AWS CLI; downloads pinned and hash-checked; 1.7 GB, about two
minutes cold, cached after). Export and package run in it as the clone's owner, the clone mounted read-only, the output
`build/release/<version>/` writable, NuGet cached in `build/nuget`, resolving through `TABLETOP_DNS_1`/`_2`. The upload
runs in a second container on `tabletop-home_backend` (internal, no route out) against `http://tabletop-s3:9000`, given
`RELEASE_S3_*` by name from `home.env`: the publisher's key stays on the host and off any network with egress. Those are
the defaults when `home.env` leaves `RELEASE_S3_ENDPOINT`, `RELEASE_S3_NETWORK` or `RELEASE_S3_BUCKET` unset (bucket:
`TABLETOP_S3_BUCKET`); another endpoint gets no network default and uploads from Docker's default bridge. GitHub's release
workflow uploads through the FILES host instead: secret `RELEASE_S3_ENDPOINT` is `https://files.tabletop.cstupi.com`,
`RELEASE_S3_ACCESS_KEY`/`SECRET_KEY` the publisher from `home.env`.

**Logs.** Every container logs to the host's journal (`driver: journald`, `tag: "{{.Name}}"`), so a redeploy, which
recreates the containers, keeps their history. Each entry carries `CONTAINER_NAME` and `SYSLOG_IDENTIFIER` set to the
container name:

```bash
journalctl CONTAINER_NAME=tabletop-management --since "1 hour ago"    # or --since "2026-10-17 18:00", --until …
journalctl CONTAINER_NAME=tabletop-game -f                             # follow
journalctl -t tabletop-game -t tabletop-management --since today       # several, by SYSLOG_IDENTIFIER
docker logs --since 1h -f tabletop-management                          # the current container only (Docker's dual-logging cache)
```

The journal's size and age are the host's settings; tabletop's logs share the cap with everything else on the host.
docker1 keeps its journal persistent under `SystemMaxUse=500M` in `/etc/systemd/journald.conf`. On a new host, as root:

```bash
mkdir -p /etc/systemd/journald.conf.d
printf '[Journal]\nStorage=persistent\nSystemMaxUse=500M\n' > /etc/systemd/journald.conf.d/size.conf
systemctl restart systemd-journald
journalctl --disk-usage            # the journal's files and their total size
```

The servers log one line per message (the simple console formatter, `SingleLine`) and leave HTTP-client request
traces at `Warning`, so routine traffic stays small within a shared cap.

A persistent journal lives in `/var/log/journal`. Without that directory the default `Storage=auto` keeps the journal in
`/run/log/journal`, which a reboot clears; `ls /var/log/journal` shows which one the host has, and `Storage=persistent`
creates the directory on the restart.

**DNS.** management, game and the client build container use `TABLETOP_DNS_1`/`TABLETOP_DNS_2` (default 1.1.1.1,
9.9.9.9) as upstream resolvers, whatever the host's resolver config.

## Releases

Spec: `docs/superpowers/specs/2026-10-17-client-releases-design.md`. A release is a `vX.Y.Z` tag. Its client builds
(Windows x86_64, Linux x86_64, macOS universal; unsigned) go to the servers' bucket under `releases/`:
`releases/X.Y.Z/tabletop-X.Y.Z-<platform>.zip`, `releases/X.Y.Z/manifest.json`, and `releases/latest.json`, which
`GET /v1/client/latest` reads and the web app's Download page shows. Asset keys (`{owner}/{hash}`, `staging/`) never
collide with it. Old versions stay until deleted by hand (`aws s3 rm --recursive s3://<bucket>/releases/X.Y.Z/`).

The built-in content (the terrain catalog `client/content/builtin/catalog.json`, its meshes and the table textures; spec
`docs/superpowers/specs/2026-10-18-builtin-content-from-server-design.md`) goes to the same bucket under `builtin/`:
each file at `builtin/<sha256>`, then `builtin/catalog.json` naming the release version. `GET /v1/builtin` serves it,
every `/v1` response names its version (`Tabletop-Builtin-Version`), and a signed-in client fetches a new version within
about two minutes and caches it, so improved or new built-ins reach players without a client update. Objects an older
catalog named stay; delete them by hand if ever needed (nothing references a hash once no catalog names it).

Cut one:

```bash
git tag -a v1.0.0 -m "What changed, as the Download page shows it" && git push origin v1.0.0
```

The tag annotation is the release notes; without one, the `## [v1.0.0]` (or `## 1.0.0`) section of `CHANGELOG.md`.

**GitHub** (`.github/workflows/release.yml`): the pushed tag builds and tests, runs the rules suite and the web and viewer checks,
then `scripts/release-client all`, which exports with Godot 4.7.2 .NET (templates cached), zips, hashes and uploads.
It does not deploy. Repository settings:

| Kind | Name | Value |
| --- | --- | --- |
| secret | `RELEASE_S3_ENDPOINT` | the store's S3 endpoint, reachable from GitHub (R2: `https://<account>.r2.cloudflarestorage.com`) |
| secret | `RELEASE_S3_BUCKET` | `TABLETOP_S3_BUCKET` |
| secret | `RELEASE_S3_ACCESS_KEY`, `RELEASE_S3_SECRET_KEY` | the publisher's credentials, below |
| variable | `TABLETOP_WEB_URL` | the web app's public origin; the client build reaches management and `/ws` through it |
| variable | `RELEASE_S3_REGION` | optional; `auto` for R2 |

`.github/workflows/ci.yml` builds and tests every push and pull request to `main`, without exports.

**On the host** (`scripts/release`), from the clone on `main` (`git checkout main && git pull` first, so the script itself is current):

```bash
sudo -E scripts/release --deploy docker --version v1.0.0   # or systemd; --deploy home needs no sudo (above)
scripts/release --dry-run --deploy docker              # print every step, change nothing
```

It fetches origin's tags, checks the tag out into `build/source` (one reused worktree; its `build/` keeps the NuGet cache) and runs
the tag's own copy of the script there, so the clone's checkout and local edits never reach a release. That copy reads
`/etc/tabletop/prod.env` (`--env-file`; the environment wins; `home.env` in the clone for home), checks the tag names its `HEAD`, with `--tests` builds and runs the unit suite, the rules suite, `check-client` and the web and viewer checks (CI runs them on every push to `main`), publishes
the client like the workflow (`--skip-client`; on the host with `godot` or `GODOT`, `dotnet`, `aws`, `zip`, `jq`, else in
the builder container with `docker` only, as for home), builds the web
app with `TABLETOP_S3_PUBLIC_ENDPOINT`'s origin and the viewer's hosted build, installs them to `TABLETOP_WEB_ROOT` and
`TABLETOP_WEB_ROOT/viewer` (`rsync --delete`), then deploys:

- `docker`: `docker compose --env-file /etc/tabletop/prod.env -f deploy/docker-compose.prod.yml up -d --build`, images
  tagged with the version; `migrate` runs first. Set `COMPOSE_PROFILES=minio` in `prod.env` with self-hosted MinIO.
- `systemd`: `dotnet publish -c Release` of both servers, stops the units, syncs them into `TABLETOP_INSTALL_DIR`
  (`/opt/tabletop`), restarts `tabletop-migrate` (the migrations, as the owner role), starts management and game.
- `home`: the home-server stack, every build and test in containers (Home server, above).

Every mode then publishes the built-in content (`scripts/release-client builtin`; `--skip-builtin` skips it) with the
`RELEASE_S3_*` publisher: on the host when it has `aws` and `jq`, else in the builder container, for `home` always in the
container on `tabletop-home_backend` against `http://tabletop-s3:9000`. Publishing only the built-ins (a terrain
change, no client or server change) is `scripts/release-client [--in-container] builtin X.Y.Z build/release/X.Y.Z` with
the same settings; a new version string busts every client's cache.

`docker` and `systemd` wait for `/healthz` on 127.0.0.1:5100 and :5200; every deploy prints a summary. The env file is
read as compose reads it: one pair of surrounding quotes is dropped (`KEY="x"` is `x`). Export templates for a host run:
`~/.local/share/godot/export_templates/4.7.2.stable.mono/` (Godot's *Manage Export Templates*, or the `.tpz` from the
Godot release page unzipped there).

**Access.** Management's service user already reads `releases/*` and `builtin/*` (Get on the bucket's objects). The
publisher needs Put on `releases/*` and `builtin/*` only:

- MinIO (profile `minio`): set `RELEASE_S3_ACCESS_KEY` and `RELEASE_S3_SECRET_KEY` in `prod.env`; `minio-init` creates
  the policy `<bucket>-releases` (`s3:PutObject` on `arn:aws:s3:::<bucket>/releases/*` and `.../builtin/*`) and attaches it
  to that user. `minio-init` runs on every `up`, so a deploy updates an existing publisher's policy before the publish.
- S3: an IAM user with that statement. R2: an API token with Object Read & Write on the bucket (R2 tokens do not scope
  to a prefix).

**Raising the minimum client version.** Once a release whose `protocolVersion` (in its manifest) meets the new
minimum is published, raise `TABLETOP_MIN_CLIENT_VERSION` for both servers. Older clients get the update screen,
which links the Download page.

## Stytch

- `TABLETOP_STYTCH_BASE_URL=https://api.stytch.com` with the live project's id and secret.
- Register `{TABLETOP_WEB_URL}/reset-password` as a `RESET_PASSWORD` redirect URL in the live project.
- Home server: register `https://tabletop.cstupi.com/reset-password`. The live project's custom auth domain
  `login.tabletop.cstupi.com` is a CNAME configured at Stytch, not served by this stack; the servers still call
  `api.stytch.com`.
