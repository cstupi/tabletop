# Built-in content from the server

Owner decision, 2026-10-17: the built-in content (the terrain catalog, its meshes and the table textures) is published
with each server release and cached on the client, so new or improved built-in terrain reaches players without a client
update. A release busts the client's cache by carrying a new catalog version. The client export carries the catalog but not
the files, which a client fetches by hash when a table needs them (spec `2026-11-07-assets-on-demand-and-client-releases-design.md`).

## Source of truth

- `client/content/builtin/catalog.json` is the catalog, kept by hand beside the files it names:
  `client/content/builtin/terrain/<mesh>.glb` (from `scripts/TerrainMeshes`) and `client/content/builtin/textures/<name>.png`.
- `client/content/builtin/tables/<planet>.json` are the predefined tables: whole `TableState`s at 72×48 written by the
  management tests' `BuiltinTablesWriter` (`TABLETOP_WRITE_GENERATED=1`) from `RandomTables`. Management embeds these and the
  catalog (`BuiltinLayouts`), the random table's terrain source and `GET /v1/layouts/builtin`; the Dockerfile copies them.

```json
{
  "meshes": ["hill", "hill-rect", "woods", "..."],
  "textures": ["grass", "desert", "urban", "snow", "ash", "wasteland", "alien"],
  "terrain": [
    { "id": "barricade", "label": "Barricade", "mesh": "wall", "kind": "solid",
      "footprint": { "kind": "rect", "a": 3, "b": 1 }, "top": 1, "declarations": ["difficult", "cover"] },
    { "id": "ruins", "label": "Ruins or Debris", "mesh": "ruins", "kind": "area",
      "footprint": { "kind": "rect", "a": 6, "b": 6 }, "top": 4, "declarations": ["blocks_line_of_sight", "see_in_and_out", "cover", "dangerous_on_rush_charge"] }
  ]
}
```

- An entry is one volume: `kind` `solid` or `area`, `footprint` `circle` (`a` the radius) or `rect` (`a` width, `b`
  length), base at 0, `top` its height, `declarations` from the seven `TerrainDeclarations`, optional `notes`. `mesh` names
  one of `meshes`; `MeshLoader` fits that mesh to the entry's volume.
- The client embeds the file (`BuiltinContent.Packed`): ids, kinds and volumes, no hashes. That is the catalog a client
  uses before its first fetch and against a server with nothing published. The export excludes `content/builtin/terrain/`
  and `content/builtin/textures/` (`export_presets.cfg`, `exclude_filter`); a development run reads them from
  `res://content/builtin/`.
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

2. Upload each file to `builtin/<sha256>` (`Cache-Control: public, max-age=31536000, immutable`), then the catalog to
   `builtin/catalog.json` (`no-cache`), so the catalog never names a missing object. Content addressing makes a re-upload of an unchanged file a no-op in effect, and leaves the
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
- `BuiltinContent.Catalog` is the catalog in use, the packed one at start. `BuiltinContent.File(path)` is the cached blob
  when the catalog in use lists the path and `AssetCache` holds its hash; else, in a build that packs it (development; Godot
  finds `res://content/builtin/<path>`), that res:// path; else null, which means fetch it.
- `BuiltinSync` keeps `<user dir>/builtin.json` (the catalog without URLs):
  - At start the client uses that file when it parses (missing files are fetched on demand), else the packed catalog.
  - `ManagementClient` raises `BuiltinVersionSeen` for each response's header. While signed in, a version different from
    the one in use starts one sync: fetch `/v1/builtin`, write `builtin.json`, use the new catalog on the main thread;
    entries it no longer lists are gone from setup. It downloads no file. A failure keeps the catalog in use and the version
    is retried at its next notice, at most once a minute.
  - `BuiltinSync.FileAsync(path)` fetches a file not on hand: the hash and URL from the last `/v1/builtin` answer, fetched
    again (one `CoalescedRefresh`, at most once per 5 s) when there is none, it lacks the path or its URLs have lapsed;
    `AssetCache.FetchAsync` hash-checks it under the mesh 64 MB or texture 16 MB cap. Requests for one path share one download.
- A table's built-in files count in its `AssetProgress` with its user meshes (the loading curtain and strip). Until a mesh
  lands the piece draws its volumes; the slab draws untextured until its print lands, and the curtain waits for it
  (`TableView.SlabReady`). A piece placed in setup fetches its mesh then; the setup lists need no files.
- The shader warm-up draws only built-in files on hand, and dresses a stand-in in each surface grain so the terrain
  materials compile without them; its board uses the table's wood when no print is on hand.
- `AssetCache` is outside the install folder, so a client update keeps it. A use (`AssetCache.Resolve` for a load, a
  finished or cached `FetchAsync`) sets the blob's last-write time; at start, over 1 GB, the least recently used blobs go
  until under 768 MB, never one the catalog in use names, and `.part` files older than a day go too.
- `BuiltinContent.Changed` redraws: `TableView` reloads every built-in terrain mesh and the slab texture, `SetupToolbar`
  refills its texture and terrain lists. The random table lists as unplaced no piece the catalog lacks: management's embedded catalog is the repo's, and a piece it lacks comes back in `unlisted`.
- A cached mesh is a downloaded blob: `MeshLoader` runs `GlbPreflight` on it like any asset. A cached texture passes
  `ImageHeader` and loads with `Image.LoadPngFromBuffer`.

## Web app

The web app lists no built-ins (layouts are edited in the client), so it does not read `/v1/builtin`.
