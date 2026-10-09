using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>A move answer's From is the projection's location and its Inside must be a container the Ask listed as enterable, under every enforcement level.</summary>
public class GfForgedMoveTests
{
    static IEnumerable<ModelRelocation> Relocations(Sim sim) => sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Changes).OfType<ModelsMoved>().SelectMany(m => m.Moves);

    static (Sim Sim, Tabletop.Content.Unit Squad, Tabletop.Content.Unit Bus) Advancing(Enforcement enforcement)
    {
        var squad = Unit("Squad", "4+", "4+", [], 1, []); var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var sim = Sim.Gf(State([squad], [bus], x1: -6, x2: 6, enforcement: enforcement), 6);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(squad.Id, [], [], GfNames.Advance));
        return (sim, squad, bus);
    }

    [Theory]
    [InlineData(Enforcement.Strict)] [InlineData(Enforcement.Warn)]
    public void A_forged_From_does_not_skip_the_distance_measure(Enforcement enforcement)
    {
        var (sim, squad, _) = Advancing(enforcement);
        var mine = squad.Models[0].Id;
        var start = ((OnTable)sim.Model(mine).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(mine, new Inside(Guid.CreateVersion7()),
            new OnTable(new Placement(start.Position with { X = start.Position.X + 4 }, new Yaw(0))))]));
        var moved = Assert.Single(Relocations(sim), r => r.ModelId == mine);
        Assert.Equal(new OnTable(start), moved.From);
        Assert.Equal(40, sim.Steps("move").First().Values!["distance_used_tenths"]);
    }

    [Theory]
    [InlineData(Enforcement.Strict)] [InlineData(Enforcement.Warn)]
    public void A_move_Inside_a_model_the_Ask_did_not_offer_moves_nobody(Enforcement enforcement)
    {
        var (sim, squad, bus) = Advancing(enforcement);
        var mine = squad.Models[0].Id;
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.DoesNotContain(bus.Models[0].Id, ask.Containment?.Enterable ?? []);
        var before = sim.Model(mine).Location;
        var forged = new Destinations([new ModelRelocation(mine, before, new Inside(bus.Models[0].Id))]);
        if (enforcement == Enforcement.Strict)
        {
            // Not a silent zero move: strict refuses the relocation the step would not read (move_not_offered).
            var refused = Assert.IsType<Tabletop.GameServer.Dispatch.Refused>(sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, forged), Samples.P1, Samples.Team1));
            Assert.Equal(RefusalReason.Illegal, refused.Reason);
            return;
        }
        sim.Answer(Samples.Team1, forged);
        Assert.Equal(before, sim.Model(mine).Location);
        Assert.DoesNotContain(Relocations(sim), r => r.To is Inside);
    }
}
