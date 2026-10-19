# Base shape at import

Army Forge publishes two bases per unit entry, `bases.round` and `bases.square`: a tank `120x92` or `100x60`, bikers
`60x35` or `50x25`, infantry `32` or `30`. Nothing in the export says which units are vehicles. Owner ruling: import
takes `bases.round`; only a unit with no round value uses its square one, so hull-shaped vehicles keep a rectangle.
Model height is scaled from the base the import writes.

## Request

`ImportRequest(Source)`. `POST /v1/armies/import { source }` → `201 Army`. Unknown JSON fields are ignored, so an old
client that still sends `bases` imports as everyone does. Every slot base stays editable through `SlotPatch.Base`.

## Mapping

`ArmyForgeImport.ToArmy(list, source, catalog)` reads each entry's bases, joined entries included:

| Field | Value | Base |
| --- | --- | --- |
| `bases.round` | `"32"` | `Base.Circle(32 mm)` |
| `bases.round` | `"60x35"` | `Base.Oval(60 mm, 35 mm)` |
| `bases.square` (no round value) | `"30"` | `Base.Rect(30 mm, 30 mm)` |
| `bases.square` (no round value) | `"50x25"` | `Base.Rect(50 mm, 25 mm)` |

`ParseBase(AfBases?)` does the mapping; millimetres in, inches out. `BaseShape.Rect` already exists in
`Tabletop.Geometry` (a square is `Rect(s, s)`, open questions → *`Square` becomes `Rect`*), so no shape is added.

An entry with neither base, or none parseable, gets `DefaultBody`, the 221 × 119 × 113 mm hull.

**Height.** `HeightFromBase` of the base the import writes: `1.5 ×` its larger dimension (owner ruling 2026-10-16:
height follows the base actually imported, so a vehicle's long hull sets it). An entry with neither base gets
`DefaultBody`'s height.

## Web app

The import form on `/armies` has no base choice. `api.armies.import(source)` sends only the source, and Re-import is the
same call followed by the delete.

## Tests

- `ArmyForgeImportTests`: the fixture imports round bases with `HeightFromBase` heights; a round-only, a square-only and
  a neither entry; `ParseBase` prefers round and falls back to square; `HeightFromBase` takes the larger dimension.
- `ArmyTests`: `POST /v1/armies/import` writes round bases and ignores a `bases` field.
- `web/tests/api.test.ts`, `web/tests/armies.test.ts`: `import` sends only `source`; `reimport` takes no base set.
