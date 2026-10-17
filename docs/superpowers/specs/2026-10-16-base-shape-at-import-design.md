# Base shape at import

Army Forge publishes two bases per unit entry, `bases.round` and `bases.square`: a tank `120x92` or `100x60`, bikers
`60x35` or `50x25`, infantry `32` or `30`. Nothing in the export says which units are vehicles. Owner ruling: the player
picks the base set when the army is imported, round or square, for the whole army. Model height stays as imported.

## Request

`ImportRequest(Source, Bases = BaseSet.Round)`; `BaseSet { Round, Square }`, on the wire `"round"` / `"square"`
(`ContentJson`'s snake-case enum converter). An omitted `bases` is `round`, so existing callers import as before.

`POST /v1/armies/import { source, bases? }` → `201 Army`. The set is not stored on the army; it only decides the slot
bases the import writes, and every slot base stays editable through `SlotPatch.Base`.

## Mapping

`ArmyForgeImport.ToArmy(list, source, catalog, set = BaseSet.Round)` reads the chosen field of each entry, joined entries
included:

| Field | Value | Base |
| --- | --- | --- |
| `bases.round` | `"32"` | `Base.Circle(32 mm)` |
| `bases.round` | `"60x35"` | `Base.Oval(60 mm, 35 mm)` |
| `bases.square` | `"30"` | `Base.Rect(30 mm, 30 mm)` |
| `bases.square` | `"50x25"` | `Base.Rect(50 mm, 25 mm)` |

`ParseBase(text, set)` does the mapping; millimetres in, inches out. `BaseShape.Rect` already exists in
`Tabletop.Geometry` (a square is `Rect(s, s)`, open questions → *`Square` becomes `Rect`*), so no shape is added.

An entry whose chosen field is absent or unparseable gets `DefaultBody`, the 221 × 119 × 113 mm hull, as a missing
`bases.round` does today. The other set is not consulted.

**Height.** Height stays what a round import writes: `HeightFromBase` of the entry's round base when Army Forge
publishes one, so choosing square changes bases and nothing else. An entry with a square base and no round base gets
`HeightFromBase` of its square base; an entry with neither gets `DefaultBody`'s height.

## Web app

The import form on `/armies` has a Round / Square choice beside the source field, default Round. `api.armies.import(source,
bases)` sends it. Re-import uses the form's current choice, since the army does not record the set it was imported with;
the confirm dialog names the set.

## Tests

- `ArmyForgeImportTests`: the fixture imported square gives `Rect(50, 25)` Beast Riders and Blood Hounds and `Rect(30, 30)` Blood Warriors, with the
  same heights as the round import; a square-only and a neither entry; `ParseBase` per set.
- `ArmyTests`: `POST /v1/armies/import` with `bases: "square"` writes rect bases; without `bases` writes round ones.
- `web/tests/armies.test.ts`: `api.armies.import` sends `bases`; `reimport` passes the chosen set.
