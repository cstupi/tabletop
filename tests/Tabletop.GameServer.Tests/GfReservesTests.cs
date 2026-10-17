using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.AppliedKindsTests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Spawn, Split and Reinforcement place a reserve unit the import added (army-book rules spec → Readings, batch 7).</summary>
public class GfReservesTests
{
    static Unit Reserve(string name, int models, string rule, Guid of) => Unit(name, "5+", "6+", [], models, []) with { Reserve = new UnitReserve(rule, of) };

    /// <summary>Puts each listed model at a point on the table (the frame's origin is the table's corner).</summary>
    static void Stand(Sim sim, params (Guid Model, double X, double Y)[] at) =>
        sim.Append([new ModelsMoved(at.Select(a => new ModelRelocation(a.Model, sim.Model(a.Model).Location, new OnTable(new Placement(new Point3(a.X, a.Y, 0), new Yaw(0))))).ToList())]);

    static Destinations Ring(Unit unit, double x, double y, double r) =>
        new(unit.Models.Select((m, i) => new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(x + r * Math.Cos(i * 2 * Math.PI / unit.Models.Count), y + r * Math.Sin(i * 2 * Math.PI / unit.Models.Count), 0), new Yaw(0))))).ToList());

    static IReadOnlyList<AskOption> Offered(Sim sim) => sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices : [];

    [Fact]
    public void Spawn_is_offered_as_the_model_activates_and_places_its_unit_within_6_inches_once()
    {
        var lord = Unit("Lord", "4+", "4+", [], 1, [P("Spawn")]);
        var swarm = Reserve("Swarm", 3, "Spawn", lord.Models[0].Id);
        var sim = Sim.Gf(State([lord, swarm], [Unit("E", "4+", "4+", [], 1, [])]), 4);
        Stand(sim, (lord.Models[0].Id, 30, 24), (sim.State.Board.Armies[1].Snapshot.Units[0].Models[0].Id, 60, 24));
        Assert.All(swarm.Models, m => Assert.IsType<OnShelf>(sim.Model(m.Id).Location));
        sim.Append([new ActionRecorded(lord.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(lord.Id, [], [], GfNames.Hold));
        var use = Assert.Single(Offered(sim), o => o.Id.StartsWith("Spawn|", StringComparison.Ordinal));
        sim.Answer(Samples.Team1, new Chosen(use.Id));
        var ask = sim.AskFor(Samples.Team1);
        var region = Assert.IsType<MoveRegion>(ask.Legal);
        Assert.Equal((lord.Models[0].Id, 6.0), (region.Containment!.ExitingId!.Value, region.Containment.ExitWithin!.Value));
        Assert.NotNull(GrimdarkFutureRules.Instance.AnswerBreach(sim.State, ask, Ring(swarm, 30, 24, 9)));            // beyond 6"
        Assert.Null(GrimdarkFutureRules.Instance.AnswerBreach(sim.State, ask, Ring(swarm, 30, 24, 3.5)));
        sim.Answer(Samples.Team1, Ring(swarm, 30, 24, 3.5));
        Assert.All(swarm.Models, m => Assert.IsType<OnTable>(sim.Model(m.Id).Location));
        Assert.Contains("Spawn", Applied(sim, "place"));
        Assert.Empty(GfReserves.SpawnOffers(sim.State, lord.Id));                                                     // "Once per game"
    }

    [Fact]
    public void Split_places_its_unit_within_6_inches_of_the_last_model_before_removing_it()
    {
        var rifle = Unit("R", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var horror = Unit("Horror", "4+", "6+", [P("Split")], 1, []);
        var lesser = Reserve("Lesser", 2, "Split", horror.Id);
        var sim = Sim.Gf(State([rifle], [horror, lesser]), 5, 1);
        Stand(sim, (rifle.Models[0].Id, 20, 24), (horror.Models[0].Id, 30, 24));
        sim.Start(GfNames.AttackSequence, new SequenceContext(rifle.Id, [horror.Id], [rifle.Models[0].Weapons[0].Id], GfNames.Hold));
        var ask = sim.AskFor(Samples.Team2);
        Assert.Equal(horror.Models[0].Id, Assert.IsType<MoveRegion>(ask.Legal).Containment!.ExitingId);
        Assert.IsType<OnTable>(sim.Model(horror.Models[0].Id).Location);                                             // "before removing the last model"
        sim.Answer(Samples.Team2, Ring(lesser, 30, 24, 3.5));
        Assert.All(lesser.Models, m => Assert.IsType<OnTable>(sim.Model(m.Id).Location));
        Assert.IsType<OnShelf>(sim.Model(horror.Models[0].Id).Location);
        Assert.Contains("Split", Applied(sim, "place"));
    }

    [Fact]
    public void A_unit_whose_split_reserve_was_placed_loses_its_last_model_at_once()
    {
        var rifle = Unit("R", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var plain = Unit("Plain", "4+", "6+", [P("Split")], 1, []);
        var sim = Sim.Gf(State([rifle], [plain]), 5, 1);
        sim.Start(GfNames.AttackSequence, new SequenceContext(rifle.Id, [plain.Id], [rifle.Models[0].Weapons[0].Id], GfNames.Hold));
        Assert.IsType<OnShelf>(sim.Model(plain.Models[0].Id).Location);                                              // no reserve (an army imported before reserves): nothing waits
        Assert.Null(sim.AskForOrNull(Samples.Team2));
    }

    [Fact]
    public void Reinforcement_offers_the_copy_of_a_shaken_or_destroyed_unit_and_removes_a_shaken_original()
    {
        var rats = Unit("Rats", "4+", "4+", [P("Reinforcement")], 2, []);
        var copy = Unit("Rats", "4+", "4+", [], 2, []) with { Reserve = new UnitReserve("Reinforcement", rats.Id) };
        var state = State([rats, copy], [Unit("E", "4+", "4+", [], 1, [])]);
        Assert.Empty(GfReserves.Reinforcements(state, Samples.Team1));
        var sim = Sim.Gf(state);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, rats.Id), GfNames.Shaken, true)]);
        Assert.Equal([copy.Id], GfReserves.Reinforcements(sim.State, Samples.Team1));
        var removal = Assert.IsType<ModelsMoved>(Assert.Single(GfReserves.RemoveOriginal(sim.State, copy.Id)));
        Assert.Equal(rats.Models.Select(m => m.Id), removal.Moves.Select(m => m.ModelId));
        sim.Append([removal]);
        Assert.Equal([copy.Id], GfReserves.Reinforcements(sim.State, Samples.Team1));                               // destroyed now
    }

    [Fact]
    public void A_reinforcement_copy_is_placed_fully_within_12_inches_of_a_table_edge()
    {
        var size = new TableSize(72, 48);
        PlacedModel At(double x, double y) => new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(x, y, 0), new Yaw(0)));
        Assert.True(GfReserves.NearEdge(size, At(11, 30)));
        Assert.True(GfReserves.NearEdge(size, At(61, 30)));
        Assert.True(GfReserves.NearEdge(size, At(36, 47)));
        Assert.False(GfReserves.NearEdge(size, At(11.9, 30)));                                                      // the base reaches past 12"
        Assert.False(GfReserves.NearEdge(size, At(36, 24)));
    }
}
