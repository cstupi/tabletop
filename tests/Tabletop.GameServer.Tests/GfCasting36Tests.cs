using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>3.6.0 p.13 Caster(X): "Once per activation, before attacking, spend … Pick a target and roll one die, and on 4+ resolve the spell’s effect.
/// Models within 18” of the Caster may spend any number of spell tokens"; 3.5.1 p.13: "At any point before attacking … on a target in line of sight.
/// Models within 18” in line of sight of the caster’s unit may spend".</summary>
public class GfCasting36Tests
{
    static EffectDocument Fx(EffectKind kind, string? targetEntry = null, int? amount = null, EffectDuration duration = EffectDuration.Interaction) =>
        new(EffectSubject.Self, 10, kind, null, null, targetEntry, null, null, null, null, amount, false, null, null, null, duration, [], []);
    static readonly EntryDocument Fireball = new("Fireball", CatalogKind.Spell, false, null, 2, "Test Book", "Target enemy unit within 12\" takes 3 hits.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12), [Fx(EffectKind.AddHit, amount: 3)]);
    /// <summary>A spell whose own text says "in line of sight".</summary>
    static readonly EntryDocument Bolt = Fireball with { Name = "Bolt", RuleText = "Target enemy unit within 12\" in line of sight takes 3 hits.",
        Targeting = new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12, LineOfSight: true) };
    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24), [Fx(EffectKind.GrantProperty, "Regeneration", null, EffectDuration.Once)]);
    static readonly EntryDocument Shroud = Ward with { Name = "Shroud" };
    static readonly PropertySet Book = TestProperties.WithSpells(Fireball, Bolt, Ward, Shroud);

    static (GameState State, IGameRules Rules) Under(string rules, GameState state) =>
        rules == RuleSets.Gf360 ? (state.Under(RuleSets.Gf360), GrimdarkFutureRules.V360) : (state, GrimdarkFutureRules.V351);

    static Sim Casting(string rules, Unit wizard, Unit[] friends, Unit[] enemies, params int[] dice)
    {
        var (state, inner) = Under(rules, State([wizard, .. friends], enemies, x1: -4, x2: 4));
        var sim = new Sim(state, new WithSequences(inner, WithSequences.Parent("test-parent", "w")), Book, new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        return sim;
    }

    static void Put(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    [Theory]
    [InlineData(RuleSets.Gf351, "UnitOnly")]
    [InlineData(RuleSets.Gf360, "Hidden")]
    public void A_token_holder_out_of_sight_within_18_of_the_caster_model_may_spend_under_3_6(string rules, string reaches)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 2, [P("Caster", 3)]);
        var near = Unit("Near", "4+", "4+", [], 1, [P("Caster", 1)]); var far = Unit("Far", "4+", "4+", [], 1, [P("Caster", 1)]);
        var hidden = Unit("Hidden", "4+", "4+", [], 1, [P("Caster", 1)]); var wall = Unit("Wall", "4+", "4+", [], 1, []);
        var unitOnly = Unit("UnitOnly", "4+", "4+", [], 1, [P("Caster", 1)]); var grunts = Unit("G", "4+", "4+", [], 1, []);
        var sim = Casting(rules, wizard, [near, far, hidden, wall, unitOnly], [grunts], 2);
        foreach (var m in new[] { near, far, hidden, unitOnly }) sim.Append([new SpellTokensSet(m.Models[0].Id, 1)]);
        Put(sim, wizard.Models[0].Id, -4, -20); Put(sim, grunts.Models[0].Id, 4, 10);
        Put(sim, near.Models[0].Id, -16, -20);                                       // 12" west of the caster, clear
        Put(sim, far.Models[0].Id, -24, -20);                                        // 20" west: out of range in both
        Put(sim, wall.Models[0].Id, -4, -14); Put(sim, hidden.Models[0].Id, -4, -8); // 12" north of the caster, behind the wall
        Put(sim, wizard.Models[1].Id, 12, -20); Put(sim, unitOnly.Models[0].Id, 28, -20);   // 16" from the caster's other model, 32" from the caster
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Ward|{wizard.Models[0].Id}"));
        var caps = Assert.IsType<TokenSpend>(sim.AskFor(Samples.Team1).Legal).MaxByModel;
        var named = new Dictionary<string, Guid> { ["UnitOnly"] = unitOnly.Models[0].Id, ["Hidden"] = hidden.Models[0].Id };
        Assert.Equal(new HashSet<Guid> { wizard.Models[0].Id, near.Models[0].Id, named[reaches] }, caps.Keys.ToHashSet());
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Fireball", false)]
    [InlineData(RuleSets.Gf360, "Fireball", true)]
    [InlineData(RuleSets.Gf351, "Bolt", false)]
    [InlineData(RuleSets.Gf360, "Bolt", false)]
    public void A_spell_target_needs_no_line_of_sight_under_3_6_unless_the_spell_says_so(string rules, string spell, bool hiddenOffered)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var near = Unit("Near", "4+", "4+", [], 1, []); var wall = Unit("Wall", "4+", "4+", [], 1, []); var hidden = Unit("Hidden", "4+", "4+", [], 1, []);
        var sim = Casting(rules, wizard, [], [near, wall, hidden], 5);
        Put(sim, near.Models[0].Id, 4, -20);                                            // 8" east, clear
        Put(sim, wall.Models[0].Id, -4, -15); Put(sim, hidden.Models[0].Id, -4, -9);    // 11" north, behind the wall
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(hiddenOffered, targets.Eligible.Contains(hidden.Id));
        Assert.Contains(near.Id, targets.Eligible);
        // A pick entry's null targeting is a Mark's or Debuff's "in line of sight" in both editions: the hidden unit is no pick target.
        Assert.DoesNotContain(hidden.Id, GfCasting.Targets(sim.State, wizard.Models[0].Id, Fireball.Targeting!));
    }

    [Fact]
    public void A_spells_own_line_of_sight_survives_the_parse_while_a_picks_reads_as_the_default()
    {
        var doc = PropertiesJson.Parse("""
            { "name": "T", "gameRules": "grimdark-future", "entries": [
              { "name": "S", "kind": "spell", "cost": 1, "ruleText": "S.", "targeting": { "side": "enemy", "grain": "unit", "count": 1, "range": 12, "lineOfSight": true } },
              { "name": "B", "kind": "model", "aggregation": "none", "ruleText": "B.", "targeting": { "side": "enemy", "grain": "unit", "count": 1, "range": 18, "lineOfSight": true } } ] }
            """);
        Assert.Equal((true, (bool?)null), (doc.Entries[0].Targeting!.LineOfSight, doc.Entries[1].Targeting!.LineOfSight));
    }

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

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

    /// <summary>A Rush stops to open the window, casts (a failed Ward) or passes, and then moves on: 3.5.1's "at any point before attacking" opens the
    /// window again after the move (Shroud is untried); 3.6.0's "once per activation" does not after a cast. Between spells the one window stays open, so
    /// a stop is still offered; a pass casts nothing, so the window is still the unit's to use.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, true, true)]
    [InlineData(RuleSets.Gf360, true, false)]
    [InlineData(RuleSets.Gf351, false, true)]
    [InlineData(RuleSets.Gf360, false, true)]
    public void The_cast_window_opens_once_per_activation_under_3_6(string rules, bool cast, bool offeredAfterMove)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var (state, inner) = Under(rules, State([wizard], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20));
        var sim = new Sim(state, inner, Book, new ScriptedDice(1));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.RushSeq, new SequenceContext(wizard.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Destinations([], Continues: true));
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);
        if (cast) CastAndFail(sim); else sim.Answer(Samples.Team1, new Passed());
        Assert.True(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Stoppable);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, wizard.Models[0].Id, 3)]));
        var window = sim.AskForOrNull(Samples.Team1);
        Assert.Equal(offeredAfterMove, window?.Question == AskQuestions.React && Assert.IsType<Options>(window.Legal).Choices.Any(c => c.Id == GfCasting.CastOption));
        if (!offeredAfterMove) Assert.Empty(sim.State.Projection.Stack);
    }

    /// <summary>A caster whose only spell targets a friendly unit, with no enemy in reach, is offered the cast window on every action and may pick the spell
    /// (its own unit is a friendly unit within range).</summary>
    [Theory]
    [InlineData(RuleSets.Gf360, GfNames.HoldSeq, GfNames.Hold)]
    [InlineData(RuleSets.Gf360, GfNames.AdvanceSeq, GfNames.Advance)]
    [InlineData(RuleSets.Gf351, GfNames.HoldSeq, GfNames.Hold)]
    public void A_caster_with_only_a_friendly_spell_is_offered_the_cast_window(string rules, string sequence, string action)
    {
        var shaman = Unit("Shaman", "4+", "4+", [], 1, [P("Caster", 1)]);
        var (state, inner) = Under(rules, State([shaman], [Unit("B", "4+", "4+", [], 1, [])], x1: -30, x2: 30));
        var sim = new Sim(state, inner, TestProperties.WithSpells(Shroud), new ScriptedDice(5));
        sim.Append([new SpellTokensSet(shaman.Models[0].Id, 1)]);
        sim.Start(sequence, new SequenceContext(shaman.Id, [], [], action));
        if (sequence == GfNames.AdvanceSeq) sim.Answer(Samples.Team1, new Destinations([Shifted(sim, shaman.Models[0].Id, 3)]));   // the window follows the move
        var window = sim.AskFor(Samples.Team1);
        Assert.Equal(AskQuestions.React, window.Question);
        Assert.Contains(Assert.IsType<Options>(window.Legal).Choices, c => c.Id == GfCasting.CastOption);
        sim.Answer(Samples.Team1, new Chosen(GfCasting.CastOption));
        var pick = sim.AskFor(Samples.Team1);
        Assert.Equal("cast", pick.Question);
        Assert.Equal([$"Shroud|{shaman.Models[0].Id}"], Assert.IsType<Options>(pick.Legal).Choices.Select(c => c.Id));
    }

    /// <summary>An Aircraft's checkpoint before its one segment, passed, leaves the cast to the checkpoint after it; a cast before the move ends the window.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void An_aircraft_that_passes_before_its_move_may_cast_after_it_under_3_6(bool castBefore, bool offeredAfter)
    {
        var jet = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, [P("Caster", 3)]);
        var (state, inner) = Under(RuleSets.Gf360, State([jet], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 30));
        var sim = new Sim(state, inner, Book, new ScriptedDice(1));
        sim.Append([new SpellTokensSet(jet.Models[0].Id, 3)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(jet.Id, [], [], GfNames.Advance));
        Assert.Equal(AskQuestions.React, sim.AskFor(Samples.Team1).Question);
        if (castBefore) CastAndFail(sim);
        sim.Answer(Samples.Team1, new Passed());                                       // the window (reopened after a cast: Shroud is untried)
        Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, jet.Models[0].Id, 31)]));
        Assert.Equal(offeredAfter, sim.AskForOrNull(Samples.Team1)?.Question == AskQuestions.React);
    }
}
