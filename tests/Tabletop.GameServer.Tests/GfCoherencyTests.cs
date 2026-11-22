using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>p.7 UNIT COHERENCY: "All models in a unit must always stay within 1” of at least one other model, and must stay within 9” of all other models".</summary>
public class GfCoherencyTests
{
    // Bases are 1.26" circles: centres 1.5" apart stand 0.24" apart.
    static Sim Advancing(Enforcement enforcement, out Unit unit, double gap = 1.5)
    {
        unit = Unit("Veterans", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], enforcement: enforcement), 1);
        Place(sim, unit.Models[0].Id, 20, 24); Place(sim, unit.Models[1].Id, 20, 24 + gap);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(unit.Id, [], [], GfNames.Advance));
        return sim;
    }

    static void Place(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    static DispatchOutcome Answer(Sim sim, params ModelRelocation[] moves) =>
        sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);

    [Fact]
    public void A_move_that_keeps_the_unit_in_coherency_is_accepted()
    {
        var sim = Advancing(Enforcement.Strict, out var unit);
        Assert.IsType<Applied>(Answer(sim, At(sim, unit.Models[0].Id, 25, 24), At(sim, unit.Models[1].Id, 25.5, 26)));   // 0.80" apart
    }

    [Fact]
    public void Under_strict_a_move_that_takes_a_model_out_of_coherency_is_refused()
    {
        var sim = Advancing(Enforcement.Strict, out var unit);
        var away = At(sim, unit.Models[1].Id, 21, 27.7);   // 1.74" from its partner
        Assert.Equal(GfNames.MoveCoherencyRule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations([away]))?.Rule);
        var refused = Assert.IsType<Refused>(Answer(sim, away));
        Assert.Equal("Veterans would end out of coherency: all models in a unit must stay within 1\" of at least one other model and within 9\" of all other models", refused.Detail);
    }

    [Fact]
    public void Under_warn_it_is_applied_with_the_breach_named()
    {
        var sim = Advancing(Enforcement.Warn, out var unit);
        var applied = Assert.IsType<Applied>(Answer(sim, At(sim, unit.Models[1].Id, 21, 27.7)));
        Assert.Equal(GfNames.MoveCoherencyRule, applied.Events.OfType<AskAnswered>().Single().Breach);
    }

    /// <summary>The owner, 2026-11-14: "they need to attempt to move towards coherency and cant remain incoherent if its possible for them to
    /// move into coherency at the end of the move". p.7: "If a model is not in coherency with its unit at the beginning of its activation, then
    /// you must use an action so that the model gets back into coherency." A unit that starts out of coherency and can regain it this move
    /// must end in it.</summary>
    [Fact]
    public void A_unit_out_of_coherency_that_can_regain_it_must_end_the_move_in_it()
    {
        var sim = Advancing(Enforcement.Strict, out var unit, gap: 5);                       // 3.74" apart, a 6" Advance
        var still = Assert.IsType<Refused>(Answer(sim, At(sim, unit.Models[1].Id, 22, 29)));
        Assert.Equal("Veterans is out of coherency and this move can bring it back: it must end in coherency", still.Detail);
        Assert.IsType<Refused>(Answer(sim));                                                 // no model moved at all
        Assert.IsType<Applied>(Answer(sim, At(sim, unit.Models[1].Id, 20, 25.8)));           // 0.54" apart
    }

    /// <summary>Out of reach of coherency this move ("or as close as possible", p.7): the models must end closer together, and a move that does is taken.</summary>
    [Fact]
    public void A_unit_that_cannot_regain_coherency_this_move_must_close_the_gap()
    {
        var sim = Advancing(Enforcement.Strict, out var unit, gap: 20);                      // 18.74" apart: no 6" move joins them
        var away = Assert.IsType<Refused>(Answer(sim, At(sim, unit.Models[1].Id, 20, 46)));
        Assert.Equal("Veterans is out of coherency: its models must end this move closer together", away.Detail);
        Assert.IsType<Refused>(Answer(sim));
        Assert.IsType<Applied>(Answer(sim, At(sim, unit.Models[1].Id, 20, 38.5)));           // 5.5" closer
    }

    [Fact]
    public void Under_warn_staying_out_of_coherency_is_applied_with_the_breach_named()
    {
        var sim = Advancing(Enforcement.Warn, out var unit, gap: 5);
        var applied = Assert.IsType<Applied>(Answer(sim, At(sim, unit.Models[1].Id, 22, 29)));
        Assert.Equal(GfNames.MoveCoherencyRule, applied.Events.OfType<AskAnswered>().Single().Breach);
    }

    /// <summary>The clients measure their ghosts by the coherency the server judges with: p.7's 1" and 9", p.11's 3" at different elevation.</summary>
    [Fact]
    public async Task The_welcome_carries_the_unit_coherency()
    {
        var db = new InMemorySeeder();
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2);
        await using var client = await TestClient.OpenAsync(factory);
        var welcome = await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(new UnitCoherency(1, 3, 9), welcome.Coherency);
        Assert.Equal(GrimdarkFutureRules.V351.Coherency, GrimdarkFutureRules.V360.Coherency);
    }
}
