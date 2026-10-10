using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>p.13 Caster(X): "At any point before attacking" — a move stops, the window opens, and the move goes on (casting during a move spec).</summary>
public class GfMoveStopTests
{
    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, "Regeneration", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);

    /// <summary>A second spell, so the window still has something once Ward is tried.</summary>
    static readonly EntryDocument Shroud = Ward with { Name = "Shroud" };

    /// <summary>A lone caster with 3 tokens starting the given action; every die is a 1, so each cast fails.</summary>
    static Sim Caster(string sequence, string action, PropertyInstance[] unitRules, out Unit wizard, out Unit enemy, double x2 = 20)
    {
        wizard = Unit("Wizard", "4+", "4+", unitRules, 1, [P("Caster", 3)]);
        enemy = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([wizard], [enemy], x1: -20, x2: x2), GrimdarkFutureRules.Instance, TestProperties.WithSpells(Ward, Shroud), new ScriptedDice(1));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(sequence, new SequenceContext(wizard.Id, [], [], action));
        return sim;
    }

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    static void Stop(Sim sim, params ModelRelocation[] moves) => sim.Answer(Samples.Team1, new Destinations(moves, Continues: true));

    /// <summary>Casts a Ward that fails at the open window, then answers whatever the cast asks until the window or the move asks again.</summary>
    static void CastAndFail(Sim sim)
    {
        sim.Answer(Samples.Team1, new Chosen("cast"));
        for (var i = 0; i < 10 && sim.AskFor(Samples.Team1).Question is not ("move" or AskQuestions.React); i++)
        {
            var ask = sim.AskFor(Samples.Team1);
            sim.Answer(Samples.Team1, ask.Legal switch
            {
                Options o when ask.Question == "cast" => new Chosen(o.Choices.First(c => c.Id.StartsWith("Ward|")).Id),
                Options o => new Chosen(o.Choices[0].Id),
                _ => new Passed(),
            });
        }
    }

    [Fact]
    public void A_stop_then_a_pass_gives_a_second_segment_with_the_distance_left()
    {
        var sim = Caster(GfNames.RushSeq, GfNames.Rush, [], out var wizard, out _);
        Assert.True(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Stoppable);
        Stop(sim, Shifted(sim, wizard.Models[0].Id, 3));
        var window = sim.AskFor(Samples.Team1);
        Assert.Equal((AskQuestions.React, "Before attacking"), (window.Question, window.Prompt));
        sim.Answer(Samples.Team1, new Passed());
        Assert.Equal(9.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void A_stop_before_moving_casts_then_moves_the_whole_distance_and_is_not_a_move()
    {
        var sim = Caster(GfNames.RushSeq, GfNames.Rush, [], out var wizard, out _);
        Stop(sim);
        Assert.DoesNotContain(GfNames.Moved, sim.Unit(wizard.Id).Conditions);
        CastAndFail(sim);
        Assert.Equal("move", sim.AskFor(Samples.Team1).Question);
        Assert.Equal(12.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        Assert.DoesNotContain(GfNames.Moved, sim.Unit(wizard.Id).Conditions);
        Assert.Equal(2, sim.State.Projection.Models[wizard.Models[0].Id].SpellTokens);
    }

    [Fact]
    public void A_stop_in_place_with_the_models_where_they_stand_is_not_a_move_either()
    {
        var sim = Caster(GfNames.RushSeq, GfNames.Rush, [], out var wizard, out _);
        Stop(sim, Shifted(sim, wizard.Models[0].Id, 0));
        Assert.DoesNotContain(GfNames.Moved, sim.Unit(wizard.Id).Conditions);
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);
    }

    [Fact]
    public void A_stop_whose_window_has_nothing_returns_to_the_move()
    {
        var plain = Unit("A", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([plain], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20));
        sim.Start(GfNames.RushSeq, new SequenceContext(plain.Id, [], [], GfNames.Rush));
        Assert.False(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Stoppable);
        Stop(sim, Shifted(sim, plain.Models[0].Id, 4));
        Assert.Equal(8.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void A_commit_then_a_pass_ends_the_move()
    {
        var sim = Caster(GfNames.RushSeq, GfNames.Rush, [], out var wizard, out _);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, wizard.Models[0].Id, 3)]));
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Passed());
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void An_aircraft_is_offered_the_window_before_its_one_segment_and_after_it()
    {
        var sim = Caster(GfNames.AdvanceSeq, GfNames.Advance, [P(GfNames.Aircraft)], out var jet, out _, x2: 30);
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);
        CastAndFail(sim);
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);   // the window reopens after a cast: Shroud is untried
        sim.Answer(Samples.Team1, new Passed());
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.False(region.Stoppable);
        Assert.Equal(30.0, region.MinDistance);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, jet.Models[0].Id, 31)], Continues: true));
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Passed());
        Assert.NotEqual("move", sim.AskForOrNull(Samples.Team1)?.Question);   // one segment: a Continues from an Aircraft is not a stop
    }

    [Fact]
    public void A_charge_split_by_a_cast_keeps_its_twelve_inches_across_both_segments()
    {
        var sim = Caster(GfNames.ChargeSeq, GfNames.Charge, [], out var wizard, out var enemy, x2: -10);
        sim.Answer(Samples.Team1, new SelectedPieces([enemy.Id]));
        Assert.True(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Stoppable);
        Stop(sim, Shifted(sim, wizard.Models[0].Id, 4));
        CastAndFail(sim);
        Assert.Equal(8.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void A_shaken_caster_is_not_stoppable()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = new Sim(State([wizard], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, TestProperties.WithSpells(Ward), new ScriptedDice(1));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3), new ConditionSet(new Subject(SubjectKind.Unit, wizard.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.RushSeq, new SequenceContext(wizard.Id, [], [], GfNames.Rush));
        Assert.False(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Stoppable);
    }
}
