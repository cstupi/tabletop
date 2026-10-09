# Task 8: Hover and distance

**Wave:** 2 · **Depends on:** 1 · **Model:** sonnet

**Files:**
- Create: `client/src/Core/HoverView.cs`
- Test: `tests/Tabletop.Client.Tests/HoverViewTests.cs` (new)

**Interfaces:**
- Consumes: `Projection` (`Units`, `Models`, `ModelInPlay.Wounds` = wounds taken), `GameInfo` (`Teams`, `Participants`),
  `Names.Unit`, `Tabletop.Geometry`'s `Distance.EdgeToEdge(PlacedModel, PlacedModel)` and
  `Distance.ClosestPoints(PlacedModel, PlacedModel)`; `maxWounds` is Task 12's `Session.MaxWounds` (absent id = 1).
- Produces:
  ```csharp
  public sealed record HoverInfo(Guid UnitId, string Name, int TeamOrdinal, double? Distance, Point3? From, Point3? To, IReadOnlyList<(Guid ModelId, int Left, int Max)> Wounds);
  public static class HoverView { public static HoverInfo? Of(Projection p, GameInfo game, Names n, Guid hoveredUnit, Guid? selectedUnit, IReadOnlyDictionary<Guid, int> maxWounds); }
  ```
  `Of` takes `GameInfo game`: `TeamOrdinal` needs participant → team → ordinal, and `Names` keeps its `GameInfo`
  private. Task 18 calls it as `HoverView.Of(session.Projection, session.Game, names, …)`.

Spec: *Table interaction → Hover a unit*. "Its name floats above it in its team's colour. With one of my units
selected, the distance follows the name (`Red Warriors · 8.4"`), measured between the closest models of the two
units, base edge to base edge (p.5), and a thin line joins those two bases. Each model with more than one wound shows
its wounds left above it (`2/3`)."

Rulebook p.5, *Measuring Distances*: "When measuring the distance between two models you always measure from/to the
closest point of their bases. When measuring the distance between two units you always measure from/to the closest
model in each unit."

Rules of the result:
- `null` when the hovered unit is not in the projection or has no model on the table (nothing to label).
- `selectedUnit` is the caller's selection only when it is one of my units; the caller passes null otherwise (spec:
  "With one of my units selected"). `Distance`, `From`, `To` are null without it, when it is the hovered unit, or when
  none of its models is on the table.
- `From` is on the selected unit's closest base, `To` on the hovered unit's, each at its base's settled height
  (`Distance.ClosestPoints`): Task 18 draws the line between them.
- `TeamOrdinal` is 0 for a participant without a team; `Palette.Team` maps it to the first colour.
- `Wounds`: the hovered unit's models on the table whose max wounds exceed 1, by slot ordinal, `Left = max(0, Max − Wounds)`.
- Formatting (`8.4"`, `2/3`) is the overlay's (Task 18).

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.Client.Tests/HoverViewTests.cs`:

```csharp
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class HoverViewTests
{
    static readonly Guid TeamA = Guid.CreateVersion7(), TeamB = Guid.CreateVersion7(), PA = Guid.CreateVersion7(), PB = Guid.CreateVersion7(), PWatch = Guid.CreateVersion7();
    static ModelSlot Slot(int ordinal) => new(Guid.CreateVersion7(), ordinal, "Trooper", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, []);
    static Unit UnitOf(string name, int models) => new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string>(), [], Enumerable.Range(1, models).Select(Slot).ToList());
    static readonly Unit Blue = UnitOf("Blue Warriors", 3), Red = UnitOf("Red Warriors", 3), Loose = UnitOf("Loose Warriors", 1);
    static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, PA, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(TeamA, 1), new Team(TeamB, 2)],
        [new Participant(PA, Guid.CreateVersion7(), "Clay", TeamA, null), new Participant(PB, Guid.CreateVersion7(), "Sam", TeamB, null),
         new Participant(PWatch, Guid.CreateVersion7(), "Watcher", null, null)]);
    static Army ArmyOf(Unit unit) => new(Guid.CreateVersion7(), unit.Name, "grimdark-future", "test", [unit]);
    static readonly Board Board = new(TableStateTests.Sample(),
        [new ArmyInPlay(PA, ArmySnapshot.Of(ArmyOf(Blue), Guid.CreateVersion7())), new ArmyInPlay(PB, ArmySnapshot.Of(ArmyOf(Red), Guid.CreateVersion7())),
         new ArmyInPlay(PWatch, ArmySnapshot.Of(ArmyOf(Loose), Guid.CreateVersion7()))]);

    static ModelRelocation Put(ModelSlot slot, double x, double y) => new(slot.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));
    /// <summary>Blue in a row at y = 10, Red at y = 20 with its first model straight across from Blue's first: 10" centre to centre, 9" edge to edge.</summary>
    static Projection Table(params EventPayload[] more)
    {
        var a = new EventApplier(Board, Game);
        EventPayload[] history = [new ModelsMoved([Put(Blue.Models[0], 10, 10), Put(Blue.Models[1], 11.5, 10), Put(Blue.Models[2], 13, 10),
            Put(Red.Models[0], 10, 20), Put(Red.Models[1], 20, 20), Put(Red.Models[2], 21.5, 20)]), .. more];
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, PA, p, null)));
        return a.Projection;
    }
    static readonly IReadOnlyDictionary<Guid, int> NoFacts = new Dictionary<Guid, int>();

    [Fact]
    public void Hover_names_the_unit_in_its_teams_ordinal()
    {
        var p = Table();
        var info = HoverView.Of(p, Game, new Names(Game, p), Red.Id, null, NoFacts)!;
        Assert.Equal((Red.Id, "Red Warriors", 2), (info.UnitId, info.Name, info.TeamOrdinal));
        Assert.Null(info.Distance); Assert.Null(info.From); Assert.Null(info.To);
        Assert.Empty(info.Wounds);
    }

    // p.5: "When measuring the distance between two units you always measure from/to the closest model in each unit", and between two
    // models "from/to the closest point of their bases".
    [Fact]
    public void With_a_unit_selected_the_distance_is_closest_models_base_edge_to_base_edge()
    {
        var p = Table();
        var info = HoverView.Of(p, Game, new Names(Game, p), Red.Id, Blue.Id, NoFacts)!;
        Assert.Equal(9.0, info.Distance!.Value, 6);
        Assert.Equal(new Point3(10, 10.5, 0), info.From);                 // on the selected unit's closest base
        Assert.Equal(new Point3(10, 19.5, 0), info.To);                   // on the hovered unit's
    }

    [Fact]
    public void No_distance_to_itself_or_from_a_unit_off_the_table()
    {
        var p = Table();
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Red.Id, Red.Id, NoFacts)!.Distance);
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Red.Id, Loose.Id, NoFacts)!.Distance);   // Loose is on the shelf
    }

    [Fact]
    public void A_unit_with_no_model_on_the_table_or_unknown_gives_nothing()
    {
        var p = Table();
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Loose.Id, Blue.Id, NoFacts));
        Assert.Null(HoverView.Of(p, Game, new Names(Game, p), Guid.CreateVersion7(), Blue.Id, NoFacts));
    }

    [Fact]
    public void Wounds_show_for_models_that_take_more_than_one_by_what_is_left()
    {
        var p = Table(new WoundsSet(Red.Models[0].Id, 1), new WoundsSet(Red.Models[1].Id, 1));
        var facts = new Dictionary<Guid, int> { [Red.Models[0].Id] = 3, [Red.Models[1].Id] = 1 };   // Red.Models[2] absent: 1
        var info = HoverView.Of(p, Game, new Names(Game, p), Red.Id, null, facts)!;
        Assert.Equal([(Red.Models[0].Id, 2, 3)], info.Wounds);
    }

    [Fact]
    public void A_unit_whose_participant_has_no_team_is_ordinal_zero()
    {
        var p = Table(new ModelsMoved([Put(Loose.Models[0], 40, 40)]));
        Assert.Equal(0, HoverView.Of(p, Game, new Names(Game, p), Loose.Id, null, NoFacts)!.TeamOrdinal);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter HoverViewTests`
Expected: compile error, `The name 'HoverView' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `client/src/Core/HoverView.cs`:

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// What hovering a unit shows (spec: in-game UX → Hover a unit): its name in its team's colour; with a unit selected, the distance between
/// the two units and the two base-edge points it runs between; the wounds left of each model that takes more than one.
/// </summary>
public sealed record HoverInfo(Guid UnitId, string Name, int TeamOrdinal, double? Distance, Point3? From, Point3? To, IReadOnlyList<(Guid ModelId, int Left, int Max)> Wounds);

public static class HoverView
{
    /// <summary>
    /// Null when the hovered unit is unknown or has no model on the table. <paramref name="selectedUnit"/> is the caller's selection when it is
    /// one of my units, else null. Distance is null without one, when it is the hovered unit, or when none of its models is on the table.
    /// Team ordinal 0: the unit's participant has no team. An id absent from <paramref name="maxWounds"/> takes one wound.
    /// </summary>
    public static HoverInfo? Of(Projection p, GameInfo game, Names n, Guid hoveredUnit, Guid? selectedUnit, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        if (!p.Units.TryGetValue(hoveredUnit, out var unit)) return null;
        var hovered = OnTable(p, hoveredUnit);
        if (hovered.Count == 0) return null;
        var teamId = game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId;
        var ordinal = game.Teams.FirstOrDefault(t => t.Id == teamId)?.Ordinal ?? 0;

        double? distance = null; Point3? from = null, to = null;
        if (selectedUnit is { } sel && sel != hoveredUnit && OnTable(p, sel) is { Count: > 0 } selected)
        {
            // p.5: between two units, from/to the closest model in each; between two models, from/to the closest point of their bases.
            var (a, b) = selected.SelectMany(s => hovered.Select(h => (s.Placed, h.Placed))).MinBy(pair => Distance.EdgeToEdge(pair.Item1, pair.Item2));
            distance = Distance.EdgeToEdge(a, b);
            (from, to) = Distance.ClosestPoints(a, b);
        }

        var wounds = hovered.Select(m => (m.Model.Id, Max: maxWounds.GetValueOrDefault(m.Model.Id, 1), m.Model.Wounds))
            .Where(m => m.Max > 1).Select(m => (m.Id, Math.Max(0, m.Max - m.Wounds), m.Max)).ToList();
        return new HoverInfo(hoveredUnit, n.Unit(hoveredUnit), ordinal, distance, from, to, wounds);
    }

    /// <summary>The unit's models on the table, by slot ordinal, each with its placed body.</summary>
    static IReadOnlyList<(ModelInPlay Model, PlacedModel Placed)> OnTable(Projection p, Guid unitId) =>
        p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).OrderBy(m => m.Slot.Ordinal)
            .Select(m => (m, new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement))).ToList();
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter HoverViewTests && ./scripts/check-client`
Expected: 6 passed; `check-client` prints nothing.

- [ ] **Step 5: Commit**

```bash
git add client/src/Core/HoverView.cs tests/Tabletop.Client.Tests/HoverViewTests.cs
git commit -m "Hover a unit: name, team, closest-model distance base edge to base edge, wounds left" -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
