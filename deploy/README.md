# deploy

The production path: the two servers behind Caddy, on Docker (`docker-compose.prod.yml`) or directly on a Linux host
(`systemd/`). The repo-root `docker-compose.yml` is development only.

| File | What |
| --- | --- |
| `docker-compose.prod.yml` | Postgres, MinIO (profile `minio`), the one-shot `migrate`, management, game |
| `systemd/` | `tabletop-migrate`, `tabletop-management`, `tabletop-game` units for a host run |
| `prod.env.example` | Every production setting and secret |
| `../server/*/Dockerfile` | The server images (`dotnet publish -c Release` on `aspnet:10.0`, user `app`) |
| `../scripts/postgres/roles.sh` | The database roles |
| `../scripts/minio/init.sh` | The bucket, its policy and the service user |

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
```

What the reverse proxy fronts, all on 127.0.0.1: the static `web/build` (SPA fallback to `index.html`), `/v1/*` →
management 5100, `/ws` → game 5200, and the blob endpoint (`TABLETOP_S3_PUBLIC_ENDPOINT`) → MinIO 9000 or the provider.
`/v1/dev/*` exists only in dev mode. The web build's CSP is in `web/svelte.config.js` (hash mode; `TABLETOP_BLOB_ORIGIN`
names the blob origin it may connect to). Management reads client addresses from `X-Forwarded-For` only from
`TABLETOP_TRUSTED_PROXIES`.

## Stytch

- `TABLETOP_STYTCH_BASE_URL=https://api.stytch.com` with the live project's id and secret.
- Register `{TABLETOP_WEB_URL}/reset-password` as a `RESET_PASSWORD` redirect URL in the live project.
