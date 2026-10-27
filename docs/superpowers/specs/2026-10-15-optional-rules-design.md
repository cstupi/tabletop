# Optional rules

A game carries a rules configuration: the enforcement setting (`silent | warn | strict`) and the optional rules the
players opted into. Both are chosen in the lobby, stored on the `game` row, carried to the game server in `GameInfo`,
and frozen when setup ends. The Grimdark Future rules read the optional rules through one typed record,
`GfOptions`, parsed from an opaque string map; nothing outside `server/game/` names an optional rule.

Owner request (2026-10-15): "We will need to setup a rules configuration for all the optional rules (like higher wound
models can shoot over lower wound models)." Owner ruling the same day: by default a unit's own models screen, so a
target unit's models block lines to its other models (`server/game/Interpreter/Sight.cs`, unchanged).

## What the rulebook makes optional

Pages 4–15 are the core game; pages 16–67 are modules that apply only when the game uses them (skill index, step 4).
p.4: *"whenever possible, players should agree on any rules and unusual situations (such as weirdly shaped models)
before the game begins"*. p.16: *"Before the game, players can agree to use one of the following rules packs"*, and
*"you can freely add or remove rules from any of them"*.

### Inside the core pages

| Rule (book's name) | Page | Text |
| --- | --- | --- |
| Force Organisation (Optional) | p.6 | *"Optionally you and your opponent may agree to use the force organisation rules"* |
| Melee separation | p.10 | *"Note that players may agree not to separate units by 1" for simplicity, as long as they remember that units can still freely activate, move, shoot, charge, etc. despite being in contact."* |
| Unusual features | p.5 | *"players may agree to ignore unusual features (like decorative banners or flame effects) for line of sight"* |

The unusual-features clause needs no option: the engine draws sight from base-and-height bodies and never reads a
mesh (`libs/Tabletop.Geometry/LineOfSight.cs`), so banners are already ignored.

### Units as sight blockers: perimeter or per model

p.5: *"Models can't see through solid obstacles, including the perimeter of other units (friendly or enemy), but they
can always see through friendly models from their own unit."* p.11 repeats it for cover: *"since models can't see
through the perimeter of other units (friendly or enemy), they are sight blockers that can provide other units with
cover."* p.56 lists the same sentence among the rules that *"always apply, regardless of which line of sight method is
used"*.

The engine blocks per model. `Sight.From` (`server/game/Interpreter/Sight.cs`) turns every on-table model outside the
observing unit into its own volume through `Bodies.AsBlockingVolume` (`libs/Tabletop.Geometry/Bodies.cs`): a solid
`BlocksLineOfSight` prism of the base footprint (an oval as its bounding rectangle) and the body's height. A line
that passes between two models of a third unit is clear. The target model's own body is ignored for its own line
(`Sight.Ignored`); the target unit's other models block it. For cover, `Battlefield.ModelsBehindSightBlockers` builds
the sight with `alsoIgnoring: targetUnit`, so a model never takes cover behind its own unit.

The book's word is *perimeter*, so the literal reading blocks the gaps between a unit's models too. This is a
configuration option, **Unit sight blocking**: `per_model` (today's engine) or `perimeter`.

- A unit's perimeter is one volume per unit: the convex hull of its on-table models' base outlines, from the lowest
  base to the tallest body's top, keyed by the unit id. The book gives the word and no shape: *perimeter* appears on
  p.5, p.11 and p.56 only, each time as *"the perimeter of other units"*, and no page says what outline a unit has,
  whether it follows the coherency chain, or how high it stands. The convex hull is the smallest outline that holds
  every base and needs no order of models; a unit in a line has a hull no wider than its bases.
- The target's own unit blocks per model under both settings (owner ruling 2026-10-15). Its perimeter would hide every
  model inside it, which p.8's *"If at least one model in the unit has line of sight to an enemy model"* cannot have meant.
- The default is `per_model`. Every game played so far ran under it, and `perimeter` is offered as the literal p.5
  reading for groups that want it.

### Optional modules (pp.16–67)

| Module, as the book names it | Pages | The book's choices |
| --- | --- | --- |
| Advanced Terrain – Terrain Placement | p.17 | Random, Alternating (Free), Alternating (Restricted), Full Table, Two Halves, Six Squares |
| Advanced Terrain – Terrain Generator | pp.18–19 | Generic, Hospitable, Wasteland, Death planets |
| Advanced Terrain – Army Terrain | p.20 | Battle Terrain, Relic Terrain |
| Advanced Terrain – Solid Buildings | p.21 | Solid buildings, building destruction, collateral damage |
| Advanced Deployment Styles | pp.22–24 | Standard, Advanced and Asymmetric styles, six each |
| Advanced Missions – Extra Missions | pp.25–27 | Face-Off, Progressive Scoring, Attack & Defend |
| Advanced Missions – Side-Missions | pp.28–29 | Side-missions by category |
| Advanced Actions – Extra Actions | p.30 | Assault, Last Stand, Hunker Down, Defensive Stance, Focused Fire, Heavy Charge, Stealth Move, Covering Fire, Overwatch |
| Advanced Actions – Action Boosting | p.31 | Tactical, Underdog, Random playstyles |
| Advanced Actions – Hero & Monster Feats | p.32 | Heroic feats, Monstrous feats |
| Advanced Casting | pp.33–36 | Currents of Power, Living Spells, Relic Wargear |
| Battlefield Effects – Special Conditions | p.37 | Geological, Mysterious |
| Battlefield Effects – Terrain & Objective Effects | p.38 | Terrain effects, objective effects |
| Random Events | pp.39–40 | Events 11–66 |
| Fog of War | p.41 | Surprise Engagement, Ebb & Flow, Shifting Focus, Combat Hesitation, Prolonged Battle |
| Unit Psychology | p.42 | Intimidation, Frenzy, Panic, Retreating, Suppression |
| Brutal Damage | p.43 | Unit Attrition, Friendly Fire, Horrible Mishaps, Critical Hits, System Damage, Directional Damage |
| Command Points | pp.44–45 | Fixed, Growing, Random playstyles; stratagems |
| Twists & Gambits | pp.46–47 | Twists, gambits, the underdog |
| Boarding Actions | p.48 | Interior table, hatchways, gear |
| Massive Battles | p.49 | Unit leaders; leader-only movement, shooting and melee |
| Multiplayer Games | p.50 | Free-for-all, King of the Hill, Team Games |
| Kitchen Table Games | p.51 | Small-table deployment and missions |
| Small-Scales & Multi-Basing | p.52 | Halved distances; multi-based units as one Tough(X) |
| Tournament Guidelines – Extras Guide (optional) | pp.55–56 | Mixed Armies, Sideboard Lists, Fortune Points; Top-Down, Hybrid True and Volumetric Line of Sight |
| Solo & Co-Op Rules | pp.57–58 | AI decision trees |
| Campaign Rules | pp.59–65 | Campaign types, progression |
| Mission Cards | pp.66–71 | Fixed and custom decks |

p.16's five packs (Extended Core, Added Complexity, Competitive Play, Narrative Battles, Chaotic Rules) are lists of
these modules.

Some modules are not game options. Campaign Rules and the tournament organiser guide govern a series of games;
Solo & Co-Op replaces a player with decision trees, which belongs to `sim/`'s players if anywhere. Force Organisation
and the tournament army extras judge army lists, and the engine judges no army list (`docs/handoff.md`); they are
recorded here and left out of the configuration.

### Larger models seeing over smaller ones

The owner's example is in the book: **Hybrid True Line of Sight**, p.56, in the Tournament Guidelines' Extras Guide
(p.55: *"All of the following content is optional"*).

> Before the game begins, all models are applied a size class, based on their Tough:
> • Small - No Tough and Tough(3) heroes • Medium - Tough(3) and Tough(6) heroes • Large - Tough(6) to Tough(12)
> • Huge - Tough(15) to Tough(21) • Titanic - Tough(24) or higher
>
> To determine line of sight, simply get down to the eye level of the miniature, and check if it can see the target. If
> the target is visible, then the model has line of sight.
>
> The miniature can't draw line of sight through other units, with these exceptions:
> • Models may draw line of sight through units of smaller size class.
> • Models may draw line of sight through units of equal or lower size class, if the target is of greater size class
> than the unit in the way.
> • Models on elevation may draw line of sight through units that are at a lower elevation than them.

It is one value of the option **Line of sight method**: `core` (p.5, the engine's base-and-height bodies) or
`hybrid_true` (p.56). Under `hybrid_true` the sight test is unchanged except that a unit the exceptions let the
observer see through contributes no blocker to that pair's line. Since p.11's cover reads the same sight blockers, a
unit seen through gives no cover to that pair either.

The second exception's *"equal or lower size class"* is measured against the observer, as the first exception's is (owner,
2026-10-25: *"Sounds like you have it right for the hybrid"*): a Small observer cannot see a Large target through a Medium unit.

The eye-level sentence is p.5's own test, so the engine's sight line (`LineOfSight`, from `Bodies.Eye`) stands.

- A model's Tough is `Lookup.ModelParameter(state, model, GfNames.Tough)`, absent read as none; a hero is a model with
  `GfNames.Hero` among its slot's properties.
- Size class by Tough. The bands are written on the book's Tough scale, which steps by 3 (p.15 Transport: *"Heroes with
  up to Tough(6), and non-Heroes with up to Tough(3)"*; every shipped army book's Tough is 3, 6 or 9). *"Tough(6) to
  Tough(12)"* is a closed range with no hero clause, so a hero with Tough(9) or (12) is Large, and only *"Tough(6)
  heroes"* is carved out of it into Medium. The values off the scale (Tough(1), (2), (4), (5), (13), (14), (22), (23),
  and a hero's (4) and (5)) fall in no band; a model reaches a band at the first value the band names and keeps the
  band below until then:

  | Class | Non-hero | Hero |
  | --- | --- | --- |
  | Small | none, 1–2 | none, 1–5 |
  | Medium | 3–5 | 6 |
  | Large | 6–14 | 7–14 |
  | Huge | 15–23 | 15–23 |
  | Titanic | 24+ | 24+ |

- The book gives each model a class and compares *"units of smaller size class"*; a unit is of a class below the
  observer's only when every one of its models is, so a unit's class is its largest on-table model's. *"The target"*
  is the target model, the other end of the line.
- *"On elevation"* is a model whose base does not rest on the slab (`Bodies.Bottom` above 0: `Collision.RestHeight`
  rests a base on a terrain top or the slab); *"a lower elevation than them"* is every on-table model of the unit in
  the way resting lower than the observer's base.
- The exceptions apply to every unit but the observer's, the target's own included (its models still block per model
  when no exception applies, owner ruling above).

p.56's other two methods, **Top-Down Line of Sight** (heights assigned per unit, lines base to base, blocked by
anything of the same height or higher than both models) and **Volumetric Line of Sight** (a volume per base size,
lines from any point of the model's volume), are catalogued above and are not options until designed: Volumetric is
close to what the engine draws already (`LineOfSight` draws from the body's centre, `Bodies.Eye`, not from any point
of it), and Top-Down needs a height per unit the army data does not carry.

## The configuration

### Where it lives

| Layer | What | Where |
| --- | --- | --- |
| Postgres | `game.optional_rules jsonb not null default '{}'`, beside `enforcement` | migration `server/management/Persistence/Migrations/010_optional_rules.sql`; `docs/schema.md` (`001_schema.sql` is never edited) |
| Protocol | `GameInfo.OptionalRules : IReadOnlyDictionary<string,string>`, an init property defaulting to empty, so every positional construction stays as it is | `libs/Tabletop.Protocol/Game.cs` |
| Lobby edit | `SetConfiguration` gains a last positional `OptionalRules` | `libs/Tabletop.Protocol/Edits.cs` |
| Catalog | `Welcome.Options : RulesOptionCatalog`, an init property defaulting to `RulesOptionCatalog.Empty` | `libs/Tabletop.Protocol/Wire.cs` |
| Management | reads the column; creation leaves the column default `'{}'` | `server/management/Games/GameRepository.cs` (`GameRow`, insert, `GameInfo`) |
| Game server | reads the column; writes it on `SetConfiguration` | `server/game/Persistence/PostgresGameRows.cs`, `PostgresGameRows.Writes.cs` (`SetConfigurationAsync`) |
| Rules | `GfOptions.Of(GameState)`: the typed record | `server/game/Rules/GrimdarkFuture/GfOptions.cs` (new) |

The map is opaque outside `server/game/`: keys and values are snake_case strings the ruleset defines, so the protocol,
management and the client name no rule (root `AGENTS.md`: *"Nothing else names or implements a rule"*). An absent key
reads as that option's default, so `'{}'` is the core game and every existing row stays as it plays today.

Enforcement keeps its typed column and field. It is ruleset-free: `CommandDispatcher`, `RulesEngine.Advance` and
`EnforcementPolicy.Decide` read `state.Info.Enforcement` and know no ruleset, and management already reads the enum
(`docs/architecture.md` → `Tabletop.Protocol`).

### One configuration, two parts

The players see one rules configuration: one lobby section, one `SetConfiguration` edit, one preset that may set both.
Underneath it has two typed parts because two different readers consume them: enforcement is read by the generic
dispatcher and engine, optional rules only by the ruleset. The coupling is one-way and through the existing policy:
an optional rule changes what a ruleset judges (which breaches exist), never how a breach is decided. Every breach an
optional rule adds or removes still goes to `EnforcementPolicy.Decide` under the game's one enforcement setting
([the enforcement engine](2026-10-02-enforcement-engine-design.md)); no option carries its own silent/warn/strict.

### When it can change

Editable in `lobby` and `setup`, frozen from `playing` on, exactly as `SetConfiguration` already is
(`GameSession.Lobby.cs`: an edit outside `Lobby`/`Setup` is refused `WrongStatus`; the game server writes no
configuration row from `playing` on). Reasons:

- The engine holds no state between calls and reads the configuration on every `Advance`. A change mid-play would
  resolve frames already on the stack under a rule they did not start under.
- Undo rebuilds from the log (`Replay.To`) and the engine continues from there; a configuration changed after the
  undone events would continue them under a different rule than the one that produced the events kept. This is the
  same reason `Properties` are pinned per game (`docs/architecture.md` → Pinning).
- p.4 settles rules *"before the game begins"*. Players who change their minds in play use the menu edits, which
  never reach the policy.

### How the rules read it

```csharp
// server/game/Rules/GrimdarkFuture/GfOptions.cs
public enum GfSightMethod { Core, HybridTrue }
public enum GfUnitSightBlocking { PerModel, Perimeter }
public sealed record GfOptions(GfSightMethod LineOfSight, GfUnitSightBlocking UnitSightBlocking, bool MeleeSeparation)
{
    public static readonly GfOptions Core = new(GfSightMethod.Core, GfUnitSightBlocking.PerModel, MeleeSeparation: true);
    public static GfOptions Of(GameState state);                        // parses state.Info.OptionalRules; absent keys read Core's
    public static IReadOnlyList<string> Problems(IReadOnlyDictionary<string, string> map);   // unknown key or value
}
```

Keys: `line_of_sight` (`core` | `hybrid_true`), `unit_sight_blocking` (`per_model` | `perimeter`),
`melee_separation` (`on` | `off`). Each GF rule that an option changes calls `GfOptions.Of(state)` where it decides;
nothing caches it, as the engine holds no state. A module enters the record when its own spec designs it, not before.

`IGameRules` gains three members, default members so `StubRules` and `FakeRules` change nothing:

```csharp
RulesOptionCatalog OptionCatalog => RulesOptionCatalog.Empty;
IReadOnlyList<string> OptionProblems(IReadOnlyDictionary<string, string> optionalRules) => optionalRules.Keys.Select(k => $"unknown option {k}").ToList();
ISightPolicy SightPolicy(GameState state) => ISightPolicy.PerModel;
```

`WithSequences` (`tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs`) forwards all three by hand, with
`MaxWounds`, `Shooters`, `MayDeployInside`, `AnswerBreach`, `PlacementBreach` and `JudgeAdvance`.

### Sight takes a policy

`Sight` is in the ruleset-free interpreter, so the ruleset hands it what blocks:

```csharp
// server/game/Interpreter/Sight.cs
public interface ISightPolicy
{
    /// <summary>True: a unit other than the observer's blocks with one perimeter volume keyed by its id (p.5); false: each model with its body.</summary>
    bool Perimeters { get; }
    /// <summary>True when the line from observer to target passes through blockingUnit as if it were not there (p.56 Hybrid).</summary>
    bool SeesThrough(Guid observerModel, Guid targetModel, Guid blockingUnit);
    static ISightPolicy PerModel { get; }
}
```

`IGameRules.SightPolicy(state)` returns a policy bound to that state (Grimdark Future: `GfSight`, which caches each
model's size class for the life of one `Sight`). The perimeter is geometry, not a rule: `Bodies.Perimeter(id,
models)` (`libs/Tabletop.Geometry/Bodies.cs`) returns a `SightPrism` (a convex outline, bottom and top), and
`SightBlockers` takes prisms beside its volumes. `Sight.From` builds the target-unit-agnostic blockers once per
observer: terrain, every other unit's bodies, and each other unit's perimeter when `Perimeters`. Per pair it ignores
the target's own body, the target unit's perimeter (its models block per model, owner ruling), the bodies of every
other unit when perimeters stand in for them, and every unit `SeesThrough` passes. `SightBlockers.Check` and
`Measure` (`libs/Tabletop.Geometry/SightBlockers.cs`) take an ignore predicate beside the one `Guid?`. Every caller of `Sight.From` follows without change: `GfAttack.AttackInteraction` (targeting and
who can shoot), `GfCasting` (spell targets), `Battlefield.CanSee`, `Battlefield.ModelsBehindSightBlockers` (p.11
cover), `GameSession.Facts` (`SightQuery`) and `GfAttack.Aims` (where a shot's beams go: `determine-attacks`' `aim_<shooter>_<target>`, so a beam never runs to a model hidden behind a perimeter). A `SightLine.BlockedBy` may then hold a unit id; the client's
`client/src/Table/SightLines.cs` highlights a unit id as every model of that unit.

### Wire

```csharp
// libs/Tabletop.Protocol/RulesOptions.cs
public sealed record RulesOptionCatalog(IReadOnlyList<RulesOption> Options, IReadOnlyList<RulesPreset> Presets)
{ public static RulesOptionCatalog Empty { get; } = new([], []); }
public sealed record RulesOption(string Key, string Label, string Page, IReadOnlyList<RulesOptionValue> Values, string Default);
public sealed record RulesOptionValue(string Value, string Label);
public sealed record RulesPreset(string Name, IReadOnlyDictionary<string, string> OptionalRules, Enforcement? Enforcement);
// libs/Tabletop.Protocol/Wire.cs
public sealed record Welcome(Guid ParticipantId, GameInfo Game) : SocketMessage { public RulesOptionCatalog Options { get; init; } = RulesOptionCatalog.Empty; }
```

Labels and pages are the book's (`"Hybrid True Line of Sight"`, `"p.56"`), written once in `GfOptions` and sent as
data. `GameSession.Connect.cs` sends `Info`'s ruleset catalog in `Welcome`. `GameSession.Lobby.cs` refuses a
`SetConfiguration` whose `IGameRules.OptionProblems` is non-empty, `RefusalReason.Unknown` with the problems as
detail, before `SetConfigurationAsync`. `ProtocolVersion` goes up.

### Presets

A preset is a named option map, with an optional enforcement, in the ruleset's catalog. **Core rules** is the empty
map: every option at its default. A p.16 pack becomes a preset once every module it names is built; none is today,
so the catalog has the one preset. The lobby shows the preset whose map equals the game's, else *Custom*.

### Lobby

`client/src/Panels/LobbyPanel.cs`'s configuration section gains a preset `OptionButton` and one `OptionButton` per
catalog option, labelled `Label (Page)`, built from `Welcome.Options`; *Apply configuration* sends the map with the
rest of `SetConfiguration`. Picking a preset sets the option buttons (and enforcement when the preset names one).
The matching and map building are Godot-free in `client/src/Core/RulesOptions.cs`, tested in
`tests/Tabletop.Client.Tests`. `client/src/Screens/HomeScreen.cs` creates the game as today; management cannot
validate a map it cannot read, so options are set in the lobby. `client/src/Core/ManagementDtos.cs` is unchanged.

`sim/Scenarios/Scenario.cs` gains `OptionalRules` (builder `Options`), written by `sim/Drivers/SocketRowSeeder.cs` and
`ScenarioCompiler`, so suite games run under an option. A sight game needs its lines laid out, so `ScenarioArmy` also
gains `Sizes` (a unit's model count) and `Anchors` (where a unit's first model deploys, in place of
`HandPlacement`'s drawn anchor).

## Touch points and tests per option

Rules tests are in `tests/Tabletop.GameServer.Tests` with `ScriptedDice`; each option value that is not the default
also gets a suite game (`sim/Suite/SuiteCatalog.Core.Options.cs`) run under it: `core-perimeter-sight`,
`core-hybrid-sight`, `core-melee-no-separation`.

| Option | Touch points | Tests |
| --- | --- | --- |
| Configuration | `GfOptions.Of`, `GfOptions.Problems`; `GrimdarkFutureRules.OptionCatalog`/`OptionProblems`; `GameSession.Lobby.cs`; `PostgresGameRows` read and `SetConfigurationAsync`; `GameRepository`; migration `010` | `GfOptionsTests`: `{}` reads `GfOptions.Core`; every key and value round-trips; an unknown key or value is a problem. `LobbyTests`: an unknown option is refused `Unknown` and writes no row; a valid one is in the next `GameInfoMessage`; a `SetConfiguration` once `playing` is refused `WrongStatus`. `GameRowsContract` and management's `RepositoryContract.Games`: the map round-trips and is replaced whole, a new game reads `{}` |
| Unit sight blocking `perimeter` (p.5, p.11) | `GfSight` (new, `ISightPolicy`): `Blockers` builds the hull volume; `Bodies.AsBlockingVolume` unchanged; `Sight.From`, `Sight.Ignored`; `SightBlockers.Check`/`Measure` ignore set | `GfSightOptionsTests`: a line through the gap between two models of a third unit is clear under `per_model`, blocked under `perimeter`, `BlockedBy` the unit id; the target's own models block a line to its rear model under both (owner ruling); the observer's own unit never blocks (p.5); a model behind a third unit's gap is behind a sight blocker for p.11 cover only under `perimeter` |
| Line of sight method `hybrid_true` (p.56) | `GfSight.SeesThrough`: `GfSight.SizeClass` from `GfNames.Tough` and `GfNames.Hero`, elevation from `Bodies.Bottom` | `GfSightOptionsTests`: `SizeClass` gives the table above; a Tough(12) model sees a Tough(1) target past a Tough(1) unit; a Tough(1) model does not; a Tough(6) unit blocks a Tough(6) observer's line to a Tough(1) target but not to a Tough(15) target; a model on a 2" ruin sees past a unit on the ground; the same unit gives the target no cover for that observer; `core` blocks every one of these |
| Melee separation `off` (p.10) | `GfMelee.ConsolidateStep`: no `FallBack` move when neither unit is destroyed; `GfMoves.ContactBreach`: no `move_separation` against a unit the mover is in contact with (within p.7's 1") when its move starts; `GfMelee.ChargeTargets` keeps offering a unit already in contact | `GfMeleeSeparationTests`: with both units alive the charger stays in contact and the consolidation step proposes no move; `on` keeps the 1" move back; a unit in contact lands within 1" of that enemy without a `move_separation` breach, and with one against a unit it was not in contact with; a charge at a unit it touches is offered |

The p.10 option's breaches stay under the one enforcement setting: with `melee_separation` `on`, `move_separation` is
judged as today.
