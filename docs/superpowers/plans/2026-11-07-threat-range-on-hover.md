# Threat range on hover

The owner's item: "Maximum threat range on hover. Hovering a model of the other team shows its maximum threat range: drawn like
the existing range indicators, and it also includes its charge range."

Hovering an enemy unit draws, in that unit's team colour, the union outlines (`RangeOutline.Of`) of how far its next activation
reaches: one **Charge** outline at its charge move, and one **Shoot** outline per distinct shooting reach, the move it may shoot
after plus a model's longest weapon range. Every distance comes from the server (`ModelFacts.Threats`); the client adds the move to
the range and draws.

## The rules

Both editions print the same actions (v3.5.1 p.7, 3.6.0 p.7):

- "Hold - Doesn’t move, can shoot."
- "Advance - Moves 6” and can shoot after moving."
- "Rush - Moves 12” but can’t shoot."
- "Charge - Moves 12” into melee."

Charging (p.7, 3.6.0 p.7): "all models in the unit may move by up to 12”, trying to reach base contact with one enemy unit". p.9
(3.6.0 p.9): "If at least one model in the unit is within charge distance of one model from the target unit, and has a clear path to
reach it, then that enemy is a valid target (no line of sight is needed)." So the charge reach is the charge move from the base edge:
an enemy base inside the outline is within charge distance.

Shooting (p.8, 3.6.0 p.8, Who can shoot): "All models in a unit with line of sight to the target, and that have a weapon that is
within range of it, may fire at it." Distances are base to base (p.5: "from/to the closest point of their bases"). So the shooting
reach of a model is the move it may make before shooting plus its weapon's range, from its base edge.

What changes them:

| Rule | Text | Threat |
| --- | --- | --- |
| Fast (p.13; 3.6.0 p.14) | "Models with this special rule move +2” when using Advance and +4” when using Rush/Charge." | read through `StatReader.ActionDistance` (the rule's `modify_stat` rows) |
| Slow (p.14; 3.6.0 p.15) | "Models with this special rule move -2” when using Advance, and -4” when using Rush/Charge." | the same |
| Aircraft (p.13; 3.6.0 p.13) | "May only use Advance actions, moving in a straight line, and adding 30” to its total move (even if Shaken)." | no charge; shoot move Advance + 30 (`GfNames.AircraftMove`) |
| Flying (p.13; 3.6.0 p.14) | "may move through units and terrain, and ignore terrain effects whilst moving" | no change to a distance: the outline ignores terrain for every unit |
| Immobile (p.14; 3.6.0 p.14) | "Models with this special rule may only use Hold actions." | no charge; shoot move 0 |
| Artillery (p.13, 3.5.1 only) | "May only use Hold actions." | no charge; shoot move 0 (`GfEdition.ArtilleryHoldOnly`); 3.6.0 p.13 Artillery moves |
| Cumbersome (3.6.0 p.14) | "May only use Hold or Advance actions, but can’t shoot when moving." | no charge; shoot move 0 |
| Shaken (p.10; 3.6.0 p.10) | "When activated, Shaken units must spend their activation being idle (can’t take any actions …)" | no threat at all; an Aircraft's mandatory move shoots nothing either (`ShootOrNotStep` ends a Shaken unit's activation) |
| Quick Shot (army book) | "This model may shoot after using Rush actions." | innate on the unit: shoot move is the longer of Advance and Rush |
| Army-book distance rules (Agile, Rapid Charge, Rapid Advance, …) | `modify_stat` rows on an action | read through `ActionDistance` |
| Range rules (Increased Shooting Range, …) | `modify_stat` rows on a weapon's Range | read through `StatReader.PeekWeaponRange` on a shooting interaction with no target: a row whose conditions need a target reads the printed range |

Not read: difficult terrain's 6" cap (p.11) and Melee Shrouding (the target's rule) depend on the path and the target, which a
hover has neither of. An outline is the reach on open ground. A unit that has activated this round shows its next round's reach.

## Wire (`libs/Tabletop.Protocol/Wire.cs`)

```csharp
public sealed record UnitThreat(double? Charge, double? ShootMove, IReadOnlyDictionary<Guid, double> Ranges);
public sealed record ModelFacts(..., IReadOnlyDictionary<Guid, UnitThreat>? Threats = null) : SocketMessage;
```

Keyed by unit id, every unit with a model alive on the table. `Charge`: the charge move, null when it may not charge. `ShootMove`:
the move it may shoot after (0 when it may only shoot standing), null when it may not shoot in its next activation. `Ranges`: per
living model with a ranged weapon, its longest range as its rules leave it. Additive and nullable; `PROTOCOL_VERSION` stays.
`wire.generated.ts` is regenerated.

## Server (`server/game`)

- `IGameRules.Threats(GameState, PropertySet)`, default empty; forwarded by `FakeRules`, `StubRules`, `WithSequences`.
- `GrimdarkFutureRules.Threats` in `GfRules.Facts.cs` (`GfThreats` helper if it outgrows it): per unit with alive models on the
  table, a lone live set (`Interaction` with the whole unit, no defender, `AttackKind.Shooting`, the action, the terrain under it),
  `ActionDistance` for Advance, Rush, Charge, the restrictions above by `Lookup.UnitHas` and `GfEdition`, and `PeekWeaponRange` per
  weapon with a Range.
- `GameSession.Facts` adds it; `Same` compares it, so a broadcast follows a change (Shaken set or cleared, a grant, a model killed).

## Client (`client/src/Core`, `client/src/Table`)

- `ThreatRings.Of(Projection, unitId, UnitThreat)` → `IReadOnlyList<RangeRing>`: the charge ring (`Charge 12"`) over every model on
  the table, and per distinct `ShootMove + range` a ring over the models with that reach (`Shoot 30"`), ascending. `ThreatRings.Key`
  and `ThreatRingCache`: recomputed only when the unit, its placements or its `UnitThreat` change.
- `Session.Threats` from the latest `ModelFacts`.
- `RangeRingNode` draws a second set: the hovered unit's threat rings, when it is a unit of another team, in its team colour, from
  `TableInput.HoveredUnitChanged`. Not gated by V: the owner's item is hover. My own selected unit's range rings under V stay.

## Viewer (`viewer/src/lib/play`)

- `threatRings.ts` ports `ThreatRings` and `ThreatRingCache`; `gameConnection.ts` keeps `threats` from `model_facts`; `PlayPage`
  adds the overlay for the hovered enemy unit, through `ringsOverlay`.
- Tests: `threatRings.test.ts` (rings, labels, cache identity).

## Performance

- Server: one lone settle per unit with models on the table per batch, plus one per Facts compare; the facts are built once per batch
  already (`BroadcastFactsIfChangedAsync`).
- Client: the outline is computed once per unit, placements and threat (`ThreatRingCache`), as `RangeRingCache` does; a hover that
  returns to the same unit with nothing moved redraws from the cache without a mesh rebuild when the rings are the same object.

## Validation

- Server unit tests (`ThreatTests`), both editions where they differ: plain unit (Charge 12, ShootMove 6, ranges), Fast (8/16),
  Slow (4/8), Aircraft (no charge, 36), Flying (unchanged), Immobile (no charge, 0), Artillery (3.5.1 Hold only; 3.6.0 moves),
  Cumbersome (3.6.0: no charge, 0), Shaken (nothing), Quick Shot (Rush 12), a range rule (Increased Shooting Range +6 only when its
  condition holds without a target), melee-only unit (no Ranges), a session test that Threats goes out and re-broadcasts on change.
- Client Core tests (`ThreatRingsTests`): rings and labels, a model with no ranged weapon, no ShootMove, the cache.
- Viewer tests: `threatRings.test.ts`.
- `dotnet test` for Protocol, GameServer, Client, Viewer tests; `./scripts/check-client` empty; `cd viewer && npm run check && npm test`;
  `dotnet run --project sim -- suite` PASSED.
- On-screen: a game server from this worktree on a free port, a dev client pointed at it, an enemy unit hovered, screenshot.

## Results

- Server: `ThreatTests` (22: plain, Fast, Slow, Aircraft, Flying, Immobile, Artillery and Shaken under both editions; Cumbersome
  3.6.0; a Shaken Aircraft; Quick Shot; Increased Shooting Range; melee only; off the table, stub and wrapped rulesets) and
  `SightAndFactsSessionTests.Facts_carry_each_units_threat_and_are_broadcast_when_it_changes`. A property set without an action
  (a test's bare set) gives no distance for it rather than throwing: `WaitingForPlayersTests` found it.
  `dotnet test tests/Tabletop.GameServer.Tests`: 1855 passed.
- Protocol: `A_unit_threat_carries_its_charge_its_shooting_move_and_each_models_range` and a round-trip sample; 66 passed.
- Client Core: `ThreatRingsTests` (4) and `SessionFactsTests.Model_facts_carry_each_units_threat`; 961 passed.
  `./scripts/check-client` empty; `dotnet build client/Tabletop.csproj` clean.
- Viewer: `threatRings.test.ts` (4), `gameConnection.test.ts` threats; `npm run check` 0 errors, `npm test` 1694 passed, 6 skipped.
  `wire.generated.ts` regenerated; `Tabletop.Viewer.Tests` 16 passed.
- `dotnet test` (every project, unit suite): all passed.
- `dotnet run --project sim -- suite`: PASSED, 685 of 685 games, 1122 of 1122 entries covered.
- On-screen: this worktree's game server on `127.0.0.1:5290` (dev mode, the shared Postgres), a `skirmish` game played one round
  over it by `sim skirmish --driver socket`, the worktree's Linux client signed in as `sim-player-0` (team 1) with
  `GameServerUrl` `ws://127.0.0.1:5290/ws`. Hovering Blue unit 1 (team 2) draws two outlines in team 2's colour about its
  models: `Charge 12"` and `Shoot 30"` (Advance 6 + its 24" weapons), labelled at each outline's far edge. Synthetic X
  `MotionNotify` events drove the hover (`xdotool mousemove` did not reach the window under WSLg with other clients open).
