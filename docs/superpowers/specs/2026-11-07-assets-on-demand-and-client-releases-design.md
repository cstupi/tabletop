# Built-in assets on demand, and client releases only on client changes

Owner rulings, 2026-11-07:

- "it would be better not to ship the terrain with the client and instead of the client download and cache things with a
  hash so if it updates it downloads the new model."
- "client updates dont need to redownload all the assets every time."
- "We also only want the client version to change if there was an actual change to the client so users dont keep
  redownloading the client because of server side only changes."

Amends `2026-10-18-builtin-content-from-server-design.md` (Client) and `2026-10-17-client-releases-design.md` (Versions,
Manifest, Publishing).

## What the client packs

- The export no longer carries `client/content/builtin/terrain/` or `client/content/builtin/textures/` (53 MB in v0.1.37).
  `export_presets.cfg` excludes both (`exclude_filter`); `scripts/release-client export` checks that each pack lacks them
  (`scripts/pck-files`) instead of checking that it holds them.
- `client/content/builtin/catalog.json` stays packed (`BuiltinContent.Packed`): ids, kinds and volumes, so setup's lists and
  a table's volumes draw before any download. It carries no hashes: a file wanted before any `/v1/builtin` answer fetches the
  catalog first. `tables/` stays where it is (management embeds it).
- Still packed: `content/table/` (wood, surface grains), `content/shaders/`, `content/sounds/`, `content/themes/`, the shader
  warm-up. A development run from the editor still reads `res://content/builtin/` (the files are in the repo).

## Where files come from

- `BuiltinContent.File(path)`: the cached blob when `AssetCache` holds the catalog's hash for the path; else, in a build that
  packs it, `res://content/builtin/<path>`; else null, which means "fetch". A build packs a path when Godot finds it
  (`FileAccess.FileExists` or `ResourceLoader.Exists`, `BuiltinContent.Packs`), not by `BuildInfo.IsRelease`: a development
  run from the editor has the files, a release export does not.
- A null file is fetched on demand through the same path as a user asset: the hash and URL from the last `GET /v1/builtin`
  answer (the catalog in use once it is used), `AssetCache.FetchAsync` (hash-checked, caps unchanged). A lapsed or missing URL refetches
  `/v1/builtin` through one `CoalescedRefresh`, as manifest refreshes do. Until the file lands the piece draws its volumes, as
  a missing file does today; the slab draws untextured until its print lands.
- A table's built-in files count in `AssetProgress` with its user meshes, so the loading curtain and the strip ("Loading
  models 3 of 12") cover them. The curtain waits for the board print (`TableView.SlabReady`) as now.
- Setup: a piece placed from the terrain list fetches its mesh then; the list itself needs no files.
- The shader warm-up fetches nothing: it draws the built-in files on hand, its board on the table's wood when no print is,
  and a `WarmupGlb` stand-in dressed in each surface grain, so the terrain materials compile on a first start.

## Sync

- `BuiltinSync` on a new `Tabletop-Builtin-Version` fetches `/v1/builtin` and uses the new catalog at once; it downloads no
  files. Files are fetched when a table needs them. `<user dir>/builtin.json` is written on every successful catalog fetch.
- At start the client uses `builtin.json` when present (no cache check: missing files are fetched on demand), else the packed
  catalog.
- `BuiltinContent.Changed` redraws as now; a changed hash fetches the new file on the next draw.

## Cache

- `AssetCache` stays content-addressed (`<user dir>/cache/<sha256>.bin`), outside the install folder: a client update keeps it,
  and only a file whose hash changed downloads again.
- Eviction: a use (`PathFor` resolved for a load, or a finished fetch) sets the file's last-write time. At start, when the
  cache is over 1 GB, it deletes the least recently used blobs until under 768 MB, never one the catalog in use names.
  `.part` files older than a day go too.

## Serving

- `scripts/release-client builtin` uploads each `builtin/<sha256>` with `Cache-Control: public, max-age=31536000, immutable`;
  `builtin/catalog.json` keeps `no-cache`. Nothing else in serving changes.

## Client releases only on client changes

- The client's source fingerprint: the git tree of the client's inputs at the release commit, as one SHA-256 over
  `git ls-tree -r <commit>` of `client/` (without `client/content/builtin/` and `*.md`), `libs/Tabletop.Geometry/`,
  `libs/Tabletop.Content/`, `libs/Tabletop.Protocol/` (without `*.md`), `Directory.Build.props`, `global.json`,
  `scripts/release-client` and `scripts/client-build/`.
- The manifest gains `"source": "<fingerprint>"`.
- `scripts/release` (and `release-client build`/`all`) reads the published `releases/latest.json`; when its `source` equals
  this commit's fingerprint it skips export, package and upload and prints `client unchanged since <version>: not
  published`. `latest.json` and the Download page keep the earlier version; the client a player has stays current.
  `--force-client` publishes anyway; a `latest.json` without `source` (every release before this) publishes.
- The publisher reads `releases/latest.json`: `scripts/minio/init.sh` adds `s3:GetObject` on it to the publisher policy.
- Built-in content publishes on every release as now; it is not part of the fingerprint.
- `TABLETOP_MIN_CLIENT_VERSION` is unchanged: a server-only release never raises it.
