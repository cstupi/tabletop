# Built-in content from the server

Owner decision, 2026-10-17: the built-in content (the terrain catalog, its meshes and the table textures) is published
with each server release and cached on the client, so new or improved built-in terrain reaches players without a client
update. A release busts the client's cache by carrying a new catalog version.

## Source of truth

- `client/content/builtin/catalog.json` is the catalog, kept by hand beside the files it names:
  `client/content/builtin/terrain/<mesh>.glb` (from `scripts/TerrainMeshes`) and `client/content/builtin/textures/<name>.png`.

```json
{
  "meshes": ["hill", "hill-rect", "woods", "..."],
  "textures": ["grass", "desert", "urban", "snow", "ash", "wasteland", "alien"],
  "terrain": [
    { "id": "barricade", "label": "Barricade", "mesh": "wall", "kind": "solid",
      "footprint": { "kind": "rect", "a": 3, "b": 1 }, "top": 1, "declarations": ["difficult", "cover"] },
    { "id": "ruins", "label": "Ruins or Debris", "mesh": "ruins", "kind": "area",
      "footprint": { "kind": "rect", "a": 6, "b": 6 }, "top": 4, "declarations": ["cover", "dangerous_on_rush_charge"] }
  ]
}
```

- An entry is one volume: `kind` `solid` or `area`, `footprint` `circle` (`a` the radius) or `rect` (`a` width, `b`
  length), base at 0, `top` its height, `declarations` from the seven `TerrainDeclarations`, optional `notes`. `mesh` names
  one of `meshes`; `MeshLoader` fits that mesh to the entry's volume.
- The client embeds the file (`BuiltinContent.Packed`), and the export packs the meshes and textures under
  `res://content/builtin/`. That is the catalog a client uses before its first fetch, offline, and against a server with
  nothing published.
- Nothing in the rules engine reads the catalog. Placing a built-in copies its volume into the placement
  (`BuiltinContent.Place`, `TerrainPlacement.Volumes`); the draft, layouts and the game's table carry those copies, so a
  catalog change never alters a placed piece or a game in progress. A placed piece's mesh is looked up by its `mesh` id
  in the current catalog.

## Publishing

`scripts/release-client builtin <version> <out>` (run by `scripts/release` in every deploy mode, `--skip-builtin` to
skip):

1. Hash each named file (sha256, lower-case hex) and write `<out>/builtin/catalog.json`: the repo file plus `version`
   (the release version) and `files`, keyed by path under `client/content/builtin/`:

   ```json
   "version": "1.3.0",
   "files": { "terrain/hill.glb": { "sha256": "…", "size": 51234 }, "textures/grass.png": { "sha256": "…", "size": 80211 } }
   ```

2. Upload each file to `builtin/<sha256>`, then the catalog to `builtin/catalog.json` (`no-cache`), so the catalog never
   names a missing object. Content addressing makes a re-upload of an unchanged file a no-op in effect, and leaves the
   objects an older catalog names in place.

The upload uses the release publisher (`RELEASE_S3_*`), the same path and container as the client zips; on docker1 it
runs on `tabletop-home_backend` against `http://tabletop-s3:9000`, so the key stays on the host. `scripts/minio/init.sh`
grants the publisher `s3:PutObject` on `builtin/*` beside `releases/*`. `builtin/` collides with neither
`{owner}/{hash}` (an owner is a UUID), `staging/` nor `releases/`. `BUILTIN_SOURCE` points the step at another directory
laid out like `client/content/builtin/` (a test publish).

## Serving

- `GET /v1/builtin` (signed in) reads `builtin/catalog.json` through `BuiltinCatalogs`, cached 60 s, and answers it with
  each file's `url` (a presigned GET of `builtin/<sha256>`, 15 min) and `expiresAt`. A file whose `sha256` is not 64
  lower-case hex is dropped. No catalog is a 404, an unreadable one (or one without a `version`) a 502. Management passes
  `meshes`, `textures` and `terrain` through without reading them.
- Every `/v1` response carries `Tabletop-Builtin-Version: <version>` once a catalog has been read
  (`ProtocolVersion.BuiltinHeader`). The header reads the cached version and never waits on the blob store: a stale cache
  starts one refresh in the background.
- No wire shape changes; `ProtocolVersion.Current` stays. An older client ignores the header and keeps its packed catalog.

## Client

- `BuiltinCatalog.Parse` reads the repo file, the published file and the response alike, and refuses a catalog that
  names an unknown mesh, a duplicate id, an unknown kind or declaration, a non-positive size, a label over 64 or notes
  over 500 characters, or (with `files`) a mesh or texture without a file entry.
- `BuiltinContent.Catalog` is the catalog in use, the packed one at start. `BuiltinContent.File(path)` is the cached
  blob when the catalog lists the path and `AssetCache` holds its hash, else `res://content/builtin/<path>`.
- `BuiltinSync` keeps `<user dir>/builtin.json` (the catalog without URLs):
  - At start it loads that file and uses it when every file it names is in the cache.
  - `ManagementClient` raises `BuiltinVersionSeen` for each response's header. While signed in, a version different from
    the one in use starts one sync: fetch `/v1/builtin`, download the files whose hash is not in `AssetCache` (hash-checked,
    mesh 64 MB and texture 16 MB caps), then use the new catalog on the main thread; entries it no longer lists are gone
    from setup. The file is written only when every download succeeded, so a failed one is retried at the next notice of
    that version (at most once a minute). A failure keeps the catalog in use.
- `BuiltinContent.Changed` redraws: `TableView` reloads every built-in terrain mesh and the slab texture, `SetupToolbar`
  refills its texture and terrain lists. `TerrainGenerators` skips a piece the catalog in use does not list.
- A cached mesh is a downloaded blob: `MeshLoader` runs `GlbPreflight` on it like any asset. A cached texture passes
  `ImageHeader` and loads with `Image.LoadPngFromBuffer`.

## Web app

The web app lists no built-ins (layouts are edited in the client), so it does not read `/v1/builtin`.
