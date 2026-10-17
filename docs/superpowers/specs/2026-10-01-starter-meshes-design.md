# Starter meshes

Every account's asset library holds a set of miniature meshes from its first login, so a fresh account
can give its army a mesh in the army editor without uploading anything first. The meshes are
generated primitives, not art. The simulator loads no meshes (simulation spec: "no mesh is ever
loaded") and the client draws a model with no asset as its hitbox, so nothing here is required to
play.

## The set

Six miniatures, sized to what Army Forge import writes (`ArmyForgeImport`): a round base of
diameter *d* gets height `HeightScale × d`, and an entry with no published base gets
`DefaultBody`, the 221 × 119 × 113 mm hull.

| Name | Modelled base | Height |
|---|---|---|
| Figure, 25mm round | `Circle(25 / 25.4)` | 1.5 × base |
| Figure, 32mm round | `Circle(32 / 25.4)` | 1.5 × base |
| Figure, 40mm round | `Circle(40 / 25.4)` | 1.5 × base |
| Figure, 50mm round | `Circle(50 / 25.4)` | 1.5 × base |
| Figure, 60mm round | `Circle(60 / 25.4)` | 1.5 × base |
| Hull, 221 × 119mm | `DefaultBody.Base` | `DefaultBody.Height` |

The heights and the hull come from `ArmyForgeImport.HeightFromBase` and `ArmyForgeImport.DefaultBody`
in code, not as copied numbers, so the meshes keep matching import's slot bodies.

**Shape.** Authored in inches, Z up, front toward +Y (`Tabletop.Geometry`'s yaw 0), so every asset
row is `scale = 1`, orientation `(0, 0, 0)`. A figure is a base disc 0.125" thick, a tapered
16-sided body, a head, and a wedge on the +Y side of the body that shows the facing. The hull is a box
on the rect footprint with its +Y end bevelled. Each mesh fills its base and reaches its height, so
it matches the hitbox the client draws under it.

**Bytes.** Management builds the six GLBs in memory, without committed binaries or a generator
script: positions, normals and indices in one buffer, one material each, glTF 2.0 with `POSITION`
`min`/`max`. The build is deterministic, so a mesh's bytes and its sha256 are stable until its
geometry code changes. `GlbHeader` reads them like any upload.

## How an account gets them

`account.starter_seeded_at timestamptz null`, added by migration `003_account_starter.sql`.
`Sessions.Complete` seeds when it is null, after the account row exists and before the session is
stamped:

1. Find or create a top-level folder named `Starter` for the owner.
2. For each mesh: write the bytes to `IBlobStore.Key(owner, hash)` with a new `IBlobStore.Put`, insert
   `blob` with `uploaded = true` (on conflict do nothing), insert the `asset` row
   (`kind = miniature`, the modelled base above), and add it to the folder.
3. Set `starter_seeded_at = now()`.

Nothing else reads this column. An existing account gets the set on its next login. A user who
deletes a starter asset doesn't get it back, because the column is already set. If seeding throws
(MinIO or Postgres unavailable), the login still succeeds, and the blob writes are cancelled after a 5 s budget so an
unreachable MinIO does not hold the login: the error is logged and the column stays
null, so the next login tries again. A retry after a partial seed can add a second copy of a mesh
whose row was already written. The user can delete it, and nothing depends on there being one.

Dev sessions (`POST /v1/dev/sessions`) go through `Sessions.Complete` and get the set as well. The
simulator never reads it.

## Not included

- Ovals and squares. Import writes only rounds and the default hull.
- Starter terrain or textures. Built-in terrain and textures already ship with the client.
- Re-seeding when the geometry changes. Accounts keep the copies they were given.
