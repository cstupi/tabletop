using Tabletop.GameServer.Properties;
using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Shock Speed (Human Defense Force; 3.5: "Pick up to three friendly units within 12"", 3.6: "Pick one friendly unit within 12"", "which moves +2" when
/// using Advance actions and +4" when using Rush/Charge actions once (next time the effect would apply)"). p.7 / 3.6.0 p.7: "When taking/using an
/// Advance action, all models in the unit may move by up to 6”": one action, which a stop does not end, so the once grant holds for every segment
/// of the action's move and is spent when the move ends.
/// </summary>
public class GfMoveOnceGrantTests
{
    const string Book = "z65fgu0l29i4lnlu";
    const string ShockSpeed = "Shock Speed";

    static Sim Game(string rules, IReadOnlyList<Unit> team1, IReadOnlyList<Unit> team2, double x1, double x2) =>
        Sim.Gf(rules, State(team1, team2, x1: x1, x2: x2), 6, 6, 6, 6, 6, 6);

    /// <summary>Every once row of the shipped spell as a grant on the unit, the shape the casting's effects step writes.</summary>
    static void Grant(Sim sim, Guid unit)
    {
        var props = sim.State.Info.GameRules == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        sim.Append([.. props.Entry(ShockSpeed)!.Effects.Where(e => e.Duration == EffectDuration.Once)
            .Select(e => (StateChange)new GrantAdded(unit, new Grant(Guid.CreateVersion7(), e.Id, ShockSpeed, unit, 1)))]);
    }

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    static void Stop(Sim sim, params ModelRelocation[] moves) => sim.Answer(Samples.Team1, new Destinations(moves, Continues: true));

    static double Allowance(Sim sim)
    {
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal("move", ask.Question);
        return Assert.IsType<MoveRegion>(ask.Legal).Allowance;
    }

    /// <summary>Passes every window the move opens until it asks for a move again.</summary>
    static void PassToMove(Sim sim)
    {
        for (var i = 0; i < 10 && sim.AskForOrNull(Samples.Team1) is { Question: not "move" }; i++) sim.Answer(Samples.Team1, new Passed());
    }

    /// <summary>Casts Shock Speed on the caster's own unit at the open window, then answers what the cast asks until the move asks again.</summary>
    static void CastShockSpeed(Sim sim, Unit caster)
    {
        sim.Answer(Samples.Team1, new Chosen("cast"));
        for (var i = 0; i < 12 && sim.AskForOrNull(Samples.Team1) is { Question: not "move" } ask; i++)
            sim.Answer(Samples.Team1, ask switch
            {
                { Question: "cast", Legal: Options o } => new Chosen(o.Choices.First(c => c.Id.StartsWith($"{ShockSpeed}|")).Id),
                { Question: "spell_targets" } => new SelectedPieces([caster.Id]),
                _ => new Passed(),
            });
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Shock_speed_cast_at_a_stop_holds_for_the_rest_of_the_advance_and_is_spent_at_its_end(string rules)
    {
        var wizard = InBook(Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6), P("Slow")]), Book);
        var sim = Game(rules, [wizard], [Unit("B", "4+", "4+", [], 1, [])], -20, 20);
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(wizard.Id, [], [], GfNames.Advance));
        var m = wizard.Models[0].Id;
        Assert.Equal(4.0, Allowance(sim));                                   // Slow
        Stop(sim);
        CastShockSpeed(sim, wizard);
        Assert.Equal(6.0, Allowance(sim));
        Stop(sim, Shifted(sim, m, 1));
        PassToMove(sim);
        Assert.Equal(5.0, Allowance(sim));
        Assert.Equal(3, sim.Unit(wizard.Id).Grants.Count);                   // held, not spent, while the action's move goes on
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, m, 5)]));
        PassToMove(sim);
        Assert.Empty(sim.Unit(wizard.Id).Grants);
        Assert.Contains(sim.State.Applier.Log.Select(e => e.Payload).OfType<StepCompleted>(), s => s.Step == "move" && (s.Applied?.Contains(ShockSpeed) ?? false));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_grant_held_before_the_advance_lasts_every_segment_and_not_into_the_next_move(string rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var sim = Game(rules, [a], [Unit("B", "4+", "4+", [], 1, [])], -20, 20);
        Grant(sim, a.Id);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        var m = a.Models[0].Id;
        Assert.Equal(8.0, Allowance(sim));
        Stop(sim, Shifted(sim, m, 1));
        Assert.Equal(7.0, Allowance(sim));
        Stop(sim, Shifted(sim, m, 2));
        Assert.Equal(5.0, Allowance(sim));
        Assert.Equal(3, sim.Unit(a.Id).Grants.Count);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, m, 1)]));   // ends with 4" unused: the grant still goes with the action
        sim.Run();
        Assert.Empty(sim.Unit(a.Id).Grants);
        Assert.Empty(sim.State.Projection.Stack);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        Assert.Equal(6.0, Allowance(sim));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_rush_gets_four_more_inches_across_its_segments(string rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var sim = Game(rules, [a], [Unit("B", "4+", "4+", [], 1, [])], -20, 20);
        Grant(sim, a.Id);
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        var m = a.Models[0].Id;
        Assert.Equal(16.0, Allowance(sim));
        Stop(sim, Shifted(sim, m, 1));
        Assert.Equal(15.0, Allowance(sim));
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, m, 15)]));
        sim.Run();
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_charge_gets_four_more_inches_across_its_segments(string rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Game(rules, [a], [b], 0, 15);                             // 15" apart: out of a 12" charge, inside 16"
        Grant(sim, a.Id);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(a.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        Assert.Equal(16.0, Allowance(sim));
        Stop(sim, Shifted(sim, a.Models[0].Id, 1));
        Assert.Equal(15.0, Allowance(sim));
        Assert.Equal(3, sim.Unit(a.Id).Grants.Count);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, a.Models[0].Id, 15 - 1 - 1.26)]));   // into base contact: 14" past 12"
        Assert.Contains(sim.State.Applier.Log.Select(e => e.Payload).OfType<StepCompleted>(), s => s.Step == "move" && (s.Applied?.Contains(ShockSpeed) ?? false));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }
}
