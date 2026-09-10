# Closing the open questions

Design session, 2026-09-09. The twenty-five questions the ten plans left open, decided before
execution starts.

Every plan proceeds under a stated assumption, so none of these blocked execution. They are closed
now because six of them change an interface, a catalog entry or a stored constraint, and each of
those is cheaper to write before the module exists than after.

Ten were not open. A document already settled them and the plan that asked never recorded the
answer; they are closed here on that document's authority. Twelve went to the project owner. Four
are closed as assumed, with the reason stated rather than the assumption repeated.

**Three more things were found by propagating rather than by asking**, and they are the largest
work in this document: a 2026-09-06 ruling that had reached no code, a default base that was wrong
for every model it applied to, and a base shape that could not express a rectangle. Each has its
own section.

## Precedence

This spec is newest and beats every spec before it on the questions it names, which are only those
questions. It does not touch the ruleset: `2026-09-06-ruleset-3-5-1-design.md` still governs there,
and the two rulings below that read the rulebook — the elevated coherency allowance and `Thrust` —
are written against 3.5.1 and close that spec's two open questions.

## Closed by a document, not by a ruling

Neither the owner nor this session decided these. Each plan asked a question its own sources had
already answered, and the answer is recorded here so the question does not come back.

| Question | The document that settles it |
| --- | --- |
| Geometry 4 — is straight-line reach enough for the movement region? | `architecture.md` → the region is a cosmetic prediction and the server judges the landed move. Star-shaped about the start is therefore sufficient; geodesic reach is a visibility graph over footprint polygons, which nothing asks for |
| Geometry 7 — ovals as 64-gons? | The error is under 0.2% of the radius, inside the tolerance `architecture.md` allows. Exact ellipse–ellipse distance has no closed form and would need an iterative solver on both client and server |
| Interpreter 10 — may a caster inside a container cast? | Ruled 2026-09-06. Only the model-grain branch of `SpellTargets` is residue, and the plan records it |
| Interpreter 11 — is the disembark radius measured edge to edge? | `2026-08-27-table-domain-model-design.md` → *"Distance is edge to edge between base shapes, never centre to centre"*. The assumption already conforms, and `Containment.BaseFullyWithin` is the only place the number is applied |
| Web app 1 — where does the browser keep the session JWT? | `localStorage`. The accounts spec's thirty-day Stytch session means nothing if closing a tab ends it, and `sessionStorage` would end it |
| Simulator 3 — is `sim/` in `Tabletop.sln`? | `CLAUDE.md` → the solution is *"at the repo root, spanning every project"* |
| Viewer 1 — a headless rendering test? | `2026-09-06-simulation-design.md` → *"The rendering is checked by looking at it"*, and headless Chrome cannot launch in this WSL image for want of `libnspr4` |
| Viewer 3 — fetch a recording over HTTP? | A recording is a `sim/` artifact on the developer's filesystem and neither server has the concept. `?recording=<url>` already covers a static file server |
| Viewer 4 — refuse an unknown ruleset? | The viewer holds no ruleset and nothing it draws depends on one. `schemaVersion` is the only gate the file needs |

Interpreter 11's other half, where the 6 comes from, is p.15's `Transport` figure and applies to
transports. A garrisonable building has no automated exit at all — see *Buildings stay manual*.

## Closed as assumed

Four are the owner's to decide in principle and were not worth the asking, each being one
identifier or one constant with a stated fallback.

- **Presence sample rate: 20 Hz.** `architecture.md`'s figure and the game server plan's relay rate
  already agree, and the whole-design review's objection was that the figure was a first-pass
  assertion rather than that it was wrong. One `Timer.WaitTime` if it changes. This closes client
  question 1 and game server question 4 together; they are one question.
- **The disembark region is drawn for the lead model's base**, `ModelIds[0]`, exactly as
  `MoveRuler.Region` already is. A unit with mixed base sizes sees the lead's boundary and the
  server refuses an outlier individually through `GfMoves.ExitPlacement`. Per-base boundaries are
  `MoveRegionNode.Show` taking a list of polygons and nothing else.
- **`Effects/` may name `Range` and `Attacks`.** `StatKind.Distance` and `StatKind.Count` need a
  *which*, and OPR is the only ruleset. A second ruleset that disagrees adds `StatRef RangeStat`
  and `StatRef AttacksStat` to `IGameRules` and passes them to `StatReader`.
- **Two viewer fixtures, capped at two rounds.** A few hundred kilobytes of checked-in JSON against
  roughly 12 MB for four uncapped scenarios.

## Rulings

Twelve went to the owner on 2026-09-09. Six confirmed an assumption and change nothing:

| Ruling | |
| --- | --- |
| Weapon counts are dealt onto models by an advancing cursor | Management question 4. 9× Rifle and 1× Flamer puts the flamer on the last model. Import stays deterministic and expands a unit on its own; asking the player instead would need a per-slot weapon editor and a `PATCH` shape, neither designed. The player corrects a slot afterwards |
| An offered container is a click target, not a button | Client question 10. The container is already highlighted, because the legal set from the server becomes the panel's choices and the table's highlights, and its name goes in the panel's note. One control for one command, which is what the same plan ruled for the round boundary |
| The viewer's side panel is tabbed | Viewer question 5. Log, Ask, dice and meta tabbed on the right; the source and the timeline stay in the header, outside the tabs, because they are wanted continuously |
| A new game's draft starts as the server default, 72×48 with the built-in `grass` texture | Game server question 2. 72×48 is the size every deployment style assumes and the creator has a legal table to edit without a round trip |
| A newer socket replaces the older one | Game server question 3. Presence stays one value per participant and `AttachCoreAsync`/`DetachAsync` replace cleanly. A reconnection after a blip works; a second window kills the first |
| A `stuck` simulator run fails the console's exit code | Simulator question 2. `RunReport.Passed` is false for `stuck` and `failed`, so `--seeds 1..500` breaks the build on a hang, which is the point of running it in CI |

The other six change something. Those, and the three found while propagating them, are the rest of
this document.

## The elevated coherency allowance is applied without testing its precondition

3.5.1 added to p.11: *"If not all models in a unit can fit on elevation, then a coherency of 3" is
allowed to models at different elevation."* The sentence states an allowance and gives no test for
its own precondition, and *can fit* has no definition anywhere in the book. This closes the second
of `2026-09-06-ruleset-3-5-1-design.md`'s two open questions.

**The allowance applies to every cross-elevation pair, and the precondition is never tested.**
Justified by `architecture.md` → *Rules enforcement*, which flags broken coherency rather than
preventing it: the precondition can only ever widen the allowance, so skipping it can only ever
suppress an advisory flag, never produce an illegal state the server would have caught. The
alternative was a `bool` the ruleset sets after judging whether the unit fits, which needs a fit
test nothing else asks for, against a phrase the book does not define.

`Coherency.Check` takes a second chain distance:

```csharp
CoherencyResult Check(
    IReadOnlyList<PlacedModel> models,
    double chainDistance,
    double elevatedChainDistance,
    double maxSpread);
```

A pair is at different elevation when `Distance.Vertical(a, b) > Tolerance.Distance` — its models'
settled bottoms differ — and that pair is chained at `elevatedChainDistance` instead
of `chainDistance`. Geometry needs no ruleset judgement to know this: `Placement` carries a
`Point3` and settling has already resolved Z against the terrain. `maxSpread` is untouched, because
p.7's 9" spread has no elevation clause and 3.5.1 did not give it one.

The ruleset supplies both numbers, 1" and 3", as it already supplies the 1".

## `Thrust` grants `AP`, as `Rending` does

3.5.1 writes `Thrust` as *"When charging, gets +1 to hit rolls and AP(+1) in melee"* and `Rending`
as *"on unmodified results of 6 to hit, those hits get AP(+4)"*. Both are additive in the book and
the repo modelled them differently: `Rending` as `grant_property`, `Thrust` as `modify_parameter`,
which `Settle` applies to *every matching instance* and which therefore does nothing at all to a
melee weapon carrying no `AP`.

**`Thrust`'s second effect becomes `grant_property`**, `target_entry = AP`, `amount = 1`, keeping
both its conditions (action taken is `Charge`, attack kind is melee) and its ordinal. It is not
per-die, so it settles in pass one exactly where `modify_parameter` did.

Three cases, and the grant is right in all three where the modifier was right in one:

| Weapon | `modify_parameter` | `grant_property` |
| --- | --- | --- |
| `AP(1)`, `Thrust`, charging | 2 | 2 |
| No `AP`, `Thrust`, charging | nothing | 1 |
| `AP(1)` plus `Rending`'s granted `AP(4)` on a 6, `Thrust`, charging | 7 — it adds to both instances | 6 |

`LiveSet.Parameter` sums the instances of an entry, so the first row is unchanged and the existing
assertion on it stands.

`ModifyParameter` keeps its place in the grammar with `Counter`'s −1 to `Impact` rolls as its
example, which is a genuine modification of an existing instance's parameter rather than an
addition of one.

## The game list is newest-first

`GET /v1/games` orders `id < @after order by id desc`. `Page<T>.Next` still holds the last id, so
the keyset cursor is unchanged; only the comparison and the direction flip. Management question 7.

Ascending by id was what the cursor gave for free, and it puts every finished game ahead of the one
started this evening. The list already filters by status, name and created-by-me, which is a filter
set for finding something recent.

## The texture kind accepts WebP

`AssetRules.ContentTypes[AssetKind.Texture]` becomes `["image/png", "image/jpeg", "image/webp"]`
and the `002` migration's check constraint gains the value. Management question 8.

Godot 4 loads WebP natively, and a table texture is downloaded by every client in the game, where
the saving is real. The web app's intake follows: the `ContentType` union, `classifyFiles`'
extension list and its test.

Miniature and terrain are unaffected; GLB remains the only mesh format uploaded.

## The simulator ships six scenarios, and can declare its own spells

`casting` joins `skirmish`, `terrain`, `player-turn`, `free-for-all` and `stalemate`. Simulator
question 4.

**Casting is not a mode.** A spell is cast inside an ordinary activation, so the preset differs
from `skirmish` only in fielding a `Caster(2)` and declaring a spell. It exists because none of the
other five fields a caster, and so none of them opens casting's two player decisions — the token
spend map, and the caster spending its own leftover tokens on its own roll — both of which were
changed by rulings of 2026-09-06, the first overturning a server-chosen payer and the second
overturning a flat 4+ from inside a transport. The rules modules' full-game tests run through the
simulator, so an Ask shape no scenario opens is an Ask shape nothing exercises.

**The spell is the scenario's own.** The shipped content document is every special rule on pages
13–15 and holds no spells, because a spell list belongs to an army book this project has not
designed — which is why the interpreter's own tests fabricate `Fireball` and `Ward` through
`TestProperties.WithSpells`. `ScenarioBuilder.Spells` does the same for a run:
`ScenarioCompiler.PropertiesFor` folds the entries into the core document and rebuilds the set, so
the hash differs for that scenario alone and every other scenario stays on the production hash.
Nothing invented for a test run reaches the game's content.

The viewer's fixtures stay at two. A recording of the casting scenario is generated on demand, not
committed.

## The default base for a model that publishes none

Found the same way, from the owner's note that vehicles may have no base. Every model in the data
model has one: Army Forge publishes `bases.round` and `ParseBase` reads it, and an entry with none
fell back to `Base.Circle(32/25.4)` — a 32 mm infantry base. Army Forge publishes that field
reliably for infantry, so a missing base means a vehicle or monster in practice, and the fallback
was wrong for exactly the models it applied to. A tank measured from a 1.26" circle at its hull
centre puts its whole 6" disembark region inside itself, and gets its movement, cover and line of
sight from the same wrong shape.

**The default is a vehicle hull: 221 × 119 × 113 mm**, the owner's sample, which is 8.70" long,
4.69" wide and 4.45" tall. It applies to **every** entry with no published base rather than to
entries that look like vehicles, because detecting a vehicle would mean a hand-written rule list
and 3.5.1 renamed five rules such a list would have named. The player corrects it per model through
`SlotPatch.Base`, which the web app's base editor already writes.

**The default carries its own height.** `HeightFromBase` is `1.5 × base.A`, which on 221 mm gives a
13" tall tank. `ArmyForgeImport.DefaultBase` therefore becomes `DefaultBody`, a `ModelBody`, and
`HeightFromBase` applies only to a base that *was* published.

## `Square` becomes `Rect`

A square is a rectangle, so the enum carried a special case instead of the general shape and could
not express a hull. **`BaseShape.Square` is replaced by `BaseShape.Rect`**, and `Base.Square(side)`
by `Base.Rect(length, width)`; a square is `Rect(s, s)`. `A` is the length and `B` the width, with
the length along the model's facing — the same convention `Oval` already used, so `BaseOutline`'s
two polygon arms now read alike.

This is a wire-level change and it reaches every module: the `Protocol` enum and `Base` factory;
both check constraints (`model_slot.base_shape` and `asset.modelled_base_shape`, `'square'` →
`'rect'`); `Bodies.BaseOutline` and ten geometry tests; the client's `BoxMesh` arm, which now takes
its width from `B` instead of squaring `A`; the web app's `BaseShape` union, its `square()` preset
helper (now `rect()`), its shape picker and its labels; the simulator's and viewer's
`RecordedModel.BaseShape` strings and the viewer's ported `baseOutline`; and the geometry values,
identity-and-enumerations and asset library specs.

Army Forge's own `bases.square` field is untouched. It is their data, not this project's shape.

## Buildings stay manual

Geometry question 8 asked whether a solid building needs the same 6" exit region a transport gets.
**It does not**, and no overload is added: `Containment.BaseFullyWithin` keeps its `PlacedModel`
container and `MoveRegion.Containment.ExitingId` names a model.

The owner's reasoning: most terrain buildings are open rather than garrisonable, so there is no
region to derive; actual garrisonable buildings are rare enough in play that the players agree how
they work at the table. A building already holds models by hand through the client's right-click
menu, so it releases them by hand too, and the automated path stays `Transport(X)` alone — which is
the only container the interpreter plan implements.

This keeps buildings whole inside the deferred-by-design bucket they already share with buildings
as shootable targets and army terrain, rather than half-automating one of them.

## The disembark-facing ruling had reached no code

Found while propagating, and larger than the stale sentence it looked like.
`2026-09-06-ruleset-3-5-1-design.md` §4 ruled **any facing that fits** over the container's yaw:
p.7 says *"Models may move and turn in any direction regardless of their facing"* and a unit exits
by using an ordinary move action, so a placement is legal if some facing fits, and the drawn region
is the union over the facings that do. The ruling reached that spec and `docs/handoff.md` and
stopped. The client plan still assumed the container's yaw in its question text, in `MoveRuler`'s
`Admits`, and in `AskPanel.YawOf`; `design/client/design.md` stated it as the decision in three
places.

**The server was never wrong.** `GfMoves.ExitPlacement` takes the player's *proposed* `Placement`
and settles it, so it has always accepted whatever facing the client sent. The ruling only ever
bound the client, which is why nothing caught it.

**What is written now.** `MoveRuler.ExitYaws` is 8, and `Admits` reports a position offered if any
of those facings fits; `MoveRuler.ExitYawAt` runs the same search and returns one, which
`AskPanel.YawOf` uses so the answer is a placement the server accepts. A **circular base tries one
facing**, because turning a circle changes nothing — that is every ordinary infantry model, so the
common case costs exactly what it did before. The cost falls only where the rule bites: an oval or
rectangular base riding inside a transport. `Location.Inside` still gains no yaw, because nothing
is preserved.

## What is left open after this

Nothing from the plans' question lists. The residues stay residues, each sitting inside a question
already closed and each recorded where it belongs:

- `Takedown`'s *"resolved as if it was a unit of [1]"* is still not modelled — cover, `Blast`'s cap
  and the defence roll read the whole target unit, and only the wound is redirected.
- `Impact` and melee still read live model counts mid-action, deliberately, because p.9 sequences
  melee by hand.
- The end-of-turn morale test's die is unreachable from the menu: a single-step frame with an actor
  pops before anything can gate it.
- `SpellTargets`' model-grain branch measures per model, which interpreter question 10 records.

Deferred by design, unchanged: hosting; buildings as shootable targets and army terrain; action
boosting; Advanced Casting's optional modules; missions, side-missions and scoring; and everything
waiting on a second ruleset.
