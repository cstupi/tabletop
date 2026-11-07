# Client releases

How the Godot client is built, published and downloaded. Owner decisions, 2026-10-17: players download the client from
a signed-in web page; Windows, Linux and macOS builds, unsigned; stored in the blob store under `releases/`, handed out
through short-lived presigned links from management; a release is cut by pushing a version tag `vX.Y.Z`.

## Versions

- A release version is the tag without its `v`: `X.Y.Z`, three integers. The tag names the commit every artifact is
  built from.
- The client carries it from the build: `TabletopVersion` (MSBuild property or environment variable) becomes the
  assembly's `Version` and the `TabletopVersion` assembly metadata. `BuildInfo.Current.Version` reads it; a build
  without it is `dev`. The client shows it in the window title, under the login form and on the update-required screen.
  A development build (no `TabletopWebUrl`) titles its window `Tabletop (dev)` and shows `Development build` under the
  login form in warning colour; a release build shows `Tabletop <version>` and `Version <version>`.
- The release build also carries `TabletopWebUrl`, the web app's public origin (`TABLETOP_WEB_URL`). The client's
  default settings derive from it: management at that origin, the game socket at `wss://<host>/ws` (the reverse proxy
  fronts `/v1` and `/ws` on the web origin), the Download page at `<origin>/download`. A release build always uses these
  addresses: it ignores server addresses in `settings.json` and never writes them. A build without `TabletopWebUrl` keeps
  the development defaults (`127.0.0.1`), which `settings.json` may override.
- A release build keeps its files in Godot's user dir (`app_userdata/Tabletop`), a development build in the sibling
  `app_userdata/Tabletop-dev` (`UserData`), so neither reads the other's settings or sends the other's session JWT. A release
  build that finds server addresses in `settings.json` (written by a development build or an earlier release) clears
  `session.bin` and rewrites the settings without them.
- The release version and `ProtocolVersion.Current` are independent. `ProtocolVersion.Current` is the wire contract's
  integer; many releases share one. Each manifest records the `protocolVersion` its build sends.
- Raising `TABLETOP_MIN_CLIENT_VERSION` is an operator step taken after a release whose `protocolVersion` meets the new
  minimum is published. A client below it gets the update-required screen, which names the Download page.

## Key layout

All under the one bucket the servers use (`TABLETOP_S3_BUCKET`):

| Key | What |
| --- | --- |
| `releases/X.Y.Z/tabletop-X.Y.Z-windows-x86_64.zip` | `Tabletop.exe`, `Tabletop.pck`, `data_Tabletop_windows_x86_64/` |
| `releases/X.Y.Z/tabletop-X.Y.Z-linux-x86_64.zip` | `Tabletop.x86_64`, `Tabletop.pck`, `data_Tabletop_linuxbsd_x86_64/` |
| `releases/X.Y.Z/tabletop-X.Y.Z-macos-universal.zip` | `Tabletop.app` (Godot's macOS zip, ad-hoc signed) |
| `releases/X.Y.Z/manifest.json` | That version's manifest |
| `releases/latest.json` | A copy of the newest version's manifest |

Asset keys are `{owner uuid}/{sha256}` and staged uploads `staging/…`; `releases/` matches neither, and no user URL
writes under it. `UploadSweep` lists only `staging/`.

## Manifest

```json
{
  "version": "1.2.0",
  "date": "2026-10-17T12:00:00Z",
  "notes": "the tag annotation, else the CHANGELOG.md section for the version, else empty",
  "protocolVersion": 4,
  "source": "the client's source fingerprint, 64 hex",
  "platforms": {
    "windows": { "key": "releases/1.2.0/tabletop-1.2.0-windows-x86_64.zip", "size": 120000000, "sha256": "…" },
    "linux":   { "key": "releases/1.2.0/tabletop-1.2.0-linux-x86_64.zip",   "size": 80000000,  "sha256": "…" },
    "macos":   { "key": "releases/1.2.0/tabletop-1.2.0-macos-universal.zip", "size": 140000000, "sha256": "…" }
  }
}
```

`sha256` is lower-case hex of the zip. `source` is the client's source fingerprint at the tag (Publishing). Upload order: the zips, then `X.Y.Z/manifest.json`, then `latest.json`, so
`latest.json` never names a missing object.

## Publishing

- `scripts/release-client` holds every client step: `export` (Godot import, the three presets, a check that each
  export holds its .NET assemblies, since Godot exits 0 when the C# step fails), `package` (zips, sizes, hashes,
  manifest; notes from `notes`: the tag annotation, else the `CHANGELOG.md` section) and `upload` (`aws s3 cp --endpoint-url`, from `RELEASE_S3_ENDPOINT`, `RELEASE_S3_BUCKET`,
  `RELEASE_S3_ACCESS_KEY`, `RELEASE_S3_SECRET_KEY`).
- A client is published only when the client changed. Its source fingerprint (`scripts/release-client source`) is one
  SHA-256 over `git ls-tree -r <commit>` of `client/` (without `client/content/builtin/`), `libs/Tabletop.Geometry/`,
  `libs/Tabletop.Content/`, `libs/Tabletop.Protocol/`, `Directory.Build.props`, `global.json`, `scripts/release-client` and
  `scripts/client-build/`, none of their `*.md`. `build` and `all` first read the published `releases/latest.json` (on the
  host with `aws`, else in the upload container, with the publisher's key); when its `source` equals the commit's they print
  `client unchanged since <version>: not published`, write `<out>/unchanged.txt` and skip export, package and upload, and
  `scripts/release` skips the home upload. `latest.json` and the Download page keep the earlier version. `--force-client`
  (`scripts/release`; `RELEASE_FORCE_CLIENT=1` for `release-client`) publishes anyway; a `latest.json` without `source`,
  or one that cannot be read, publishes. The built-in content publishes on every release and is not part of the
  fingerprint; a server-only release never raises `TABLETOP_MIN_CLIENT_VERSION`.
- `.github/workflows/release.yml` runs on a pushed `v*.*.*` tag: build and tests, the sim's in-process rules suite,
  the web checks, then `scripts/release-client` with Godot 4.7.2 .NET and its export templates (cached).
- `scripts/release` runs the same from a clone on the server host, then builds and installs the web app and deploys the
  servers (`docker`, `systemd` or `home`).
- Without Godot on the host (`--in-container`, automatic when `godot` is not on PATH, always for `--deploy home`),
  `scripts/release-client` runs export and package in the builder image `tabletop-client-build:<Godot version>`
  (`scripts/client-build/Dockerfile`: Godot .NET headless, the three release templates, the .NET SDK, zip, jq, the AWS
  CLI; pinned, hash-checked downloads) as the clone's owner, the clone read-only and the output writable, and the upload
  in a second container given `RELEASE_S3_*` at run time and joined to `RELEASE_S3_NETWORK`. The home server uploads on
  its internal backend network straight to its MinIO (`http://tabletop-s3:9000`), so the publisher's key never leaves
  that host. `--deploy home` uploads after the deploy, since the deploy starts MinIO and creates the publisher.
- Re-running a release for the same version overwrites its objects (with `--force-client` once that version is the published
  one); nothing else changes.

## Serving

- `GET /v1/client/latest` (signed in) reads `releases/latest.json`, cached for 60 s, and answers `version`, `date`,
  `notes`, `protocolVersion` and one `downloads` entry per platform present: `platform`, `fileName`, `size`, `sha256`,
  `url` (a presigned GET, 15 min), with `expiresAt`. No `latest.json` is a 404; an unreadable one is a 502. A
  platform entry whose key is outside `releases/` is dropped.
- The web app's Download page (`/download`, signed in) shows the newest release, offers the detected OS first and all
  three, with size, SHA-256 and first-run steps for an unsigned build: Windows SmartScreen (More info → Run anyway),
  macOS (right-click → Open, or System Settings → Privacy & Security → Open Anyway), Linux (`chmod +x`). With no
  release it says none is published yet.

## Retention

Every version stays under `releases/X.Y.Z/` until an operator deletes it (`aws s3 rm --recursive`); only
`latest.json` moves. Nothing in management lists or prunes `releases/`.

## Access

- Management's service user reads `releases/*` through its existing object policy (Get on the bucket's objects).
- The publisher's credentials need Put on `releases/*` (and `builtin/*`, which it also publishes: spec
  `2026-10-18-builtin-content-from-server-design.md`) and Get on `releases/latest.json`. Self-hosted MinIO: `scripts/minio/init.sh` creates a
  `<bucket>-releases` policy and a user for it when `RELEASE_S3_ACCESS_KEY` is set in `prod.env`. S3 or R2: a token scoped to the
  bucket with object write.
