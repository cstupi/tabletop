# Passengers

How a player puts a unit inside a Transport for deployment, and boards one in play. Owner ruling, 2026-10-16.

## The rule

Rulebook p.15, Transport(X):

> May transport up to X models or Heroes with up to Tough(6), and non-Heroes with up to Tough(3) which occupy 3 spaces
> each. Transports may deploy with units inside, and units may enter/exit by using any move action, but must stay fully
> within 6" of it when exiting. […]
> Note that units inside Transports are deployed at the same time as the Transport, and units can't both
> embark/disembark as part of the same activation. […] they ignore the 1" move restriction when embarking (only one
> model needs to reach the Transport for everyone to embark).
> Example: Regular models and Heroes with Tough(3) or Tough(6) occupy 1 space, Tough(3) models occupy 3 spaces, and
> models with Tough(6) or higher can't be transported.

## Deployment

**The mark is the location.** A passenger waiting for its Transport has every model `Inside(transport model)` while
the Transport is still on the shelf. Nothing new on the wire: putting it in and taking it out are hand moves
(`MoveModels`) that the server judges.

- **Put in transport.** `OnShelf → Inside(c)` is legal at any time when `IGameRules.MayDeployInside(state, unit, c,
  deploying)` holds. Grimdark Future (`GfRules.Facts.cs`):
  - `c` is a Transport model of another unit of the same team;
  - the Transport's unit is the one the team is placing now (`deploying`), or has never deployed: every model on the
    shelf, none ever placed on the table or taken off it;
  - the passenger has never deployed (`NeverDeployed`): every model on the shelf, or already inside that Transport's
    unit; no model ever placed on the table, none ever moved to the shelf from the table or from inside a Transport
    that had been on the table. Taking a passenger back out of a Transport still on the shelf is not deploying;
  - the passenger is set aside exactly when the Transport is (an Ambusher never rides into the ordinary deployment, an
    ordinary unit never into a Scout's or an Ambusher's);
  - there is room: the Transport's occupants of other units plus the passenger fit in X spaces (a model without Tough,
    or a Hero up to Tough(6): 1 space; a non-Hero up to Tough(3): 3 spaces; anything else cannot be transported).
- **Take out of transport.** `Inside(c) → OnShelf` is legal when `c` is on the shelf and `MayDeployInside(state,
  unit, c, ∅)` holds for the unit as it stands.
- Neither spends a deployment turn: no Ask is answered, and the alternating order is untouched.
- A passenger is not offered as a deploy pick: the deploy Asks list only units with every model on the shelf
  (`TeamUnits.AllShelved`). A deploy answer naming a unit with no model on the shelf (an Ask opened before it boarded)
  is the `deploy_inside` breach (`GrimdarkFutureRules.AnswerBreach`).
- When the Transport is placed, its passengers are already inside: they deploy with it and count as deployed, and
  their set-aside clears with the Transport's (`GfHooks.ArrivedFromSetAside`). Placing a Transport whose occupants
  `MayDeployInside` no longer allows (set aside since they boarded) is a `hand_move` breach.
- A unit inside a Transport that is not in play is not in play either: not activated (`TeamUnits.AnyOnTable`), not
  counted alive (`Lookup.AliveModels`).
- The old path stays legal: while the Transport is the unit being placed, a unit may board it on the shelf or on
  the table.

**What the client is told.** `ModelFacts` gains `DeployInside`: for each unit that may be put in a Transport now, the
Transport models it may go in (`IGameRules.DeployInside(state)`). The session sends it with `MaxWounds` and
broadcasts it whenever either changes.

```csharp
public sealed record ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? DeployInside = null) : SocketMessage;
```

**The roster menu** (`MenuModel.ForRosterUnit`), a unit of my team while playing:

- every model on the shelf, unwounded: Set aside / Return to deployment (Ambush or Scout, as before), then one
  **Put in transport: {Transport unit}** per `DeployInside` entry, each one `MoveModels` of every model to
  `Inside(that model)`;
- inside a Transport model that is on the shelf: **Take out of transport**, one `MoveModels` of every model back to the
  shelf.

The roster row shows `in {Transport unit}` for a unit inside a Transport, and dims it while that Transport is off the
table. The deploy pick lists only units with a model on the shelf.

## In play

Embarking is a move (p.15 "units may enter/exit by using any move action"). The move step's `MoveRegion` already carries
`Containment.Enterable`, the friendly Transports one model can reach (`GfMoves.ContainersInReach`), and the decision box
answers a click on one. The table's right-click menu on a model of the moving unit adds **Board transport: {Transport
unit}** per `Enterable` model while my move Ask for that unit is open; it answers the Ask with every model
`Inside(container)`, the same `Destinations` the click sends.

## Sim

`ScenarioArmy.Passengers` (builder `passengers:`) maps a unit ordinal to the Transport unit ordinal it deploys inside.
`RunLoop` sends one `MoveModels` per passenger after the set-aside conditions, as the roster menu does. The suite game
`core-transport-deploy` plays it under strict.
