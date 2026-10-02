using Refused = Tabletop.GameServer.Dispatch.Refused;
using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Under warn and silent an answer outside the recorded legal set is accepted and logged, so every step must read it through the
/// Ask it recorded: a throw inside Advance is replayed by every rehydrate. Each fact answers with garbage (the wrong type, unknown
/// ids, an unknown option, an empty answer) and asserts that nothing throws and the outcome is sane.
/// </summary>
public class OutOfSetAnswerTests
{
    static readonly Guid Nobody = Guid.CreateVersion7();
    static Placement At(double x) => new(new Point3(x, 0, 0), new Yaw(0));
    static IEnumerable<ModelRelocation> Relocations(Sim sim) => sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Changes).OfType<ModelsMoved>().SelectMany(m => m.Moves);

    // ---- C2: RulesEngine.Execute is total ----

    static readonly Sequence Roller = new("roller", [new LambdaStep("roll", ctx => { ctx.Roll(new RollPlan("hit", 4, [], new RollConvention(6, null), null, new HashSet<int>()), 3); return new Complete([]); }),
        new LambdaStep("wait", ctx => ctx.History.LastClosed() is null ? new OpenAsks([ctx.Ask("go", "Go", ctx.ActorTeam!.Value, new Options([new AskOption("go", "Go", [])]))]) : new Complete([]))]);

    [Fact]
    public void A_dice_command_naming_a_missing_die_or_an_impossible_face_is_refused_under_warn_without_throwing()
    {
        var (a, b) = (Unit("A", "4+", "4+", [], 1, []), Unit("B", "4+", "4+", [], 1, []));
        var sim = new Sim(State([a], [b], enforcement: Enforcement.Warn), new StubRules(Roller), TestProperties.Core, new ScriptedDice(2, 5, 6));
        sim.Start("roller", new SequenceContext(a.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        var seq = sim.State.Projection.LastSeq;
        foreach (var command in new Command[]
        {
            new RerollDice(frame, roll.Id, [99]), new RerollDice(frame, roll.Id, [-1]), new RerollDice(frame, roll.Id, [0, 0]),
            new SetDice(frame, roll.Id, [new DieValue(7, 3)]), new SetDice(frame, roll.Id, [new DieValue(0, 3), new DieValue(0, 4)]),
            new SetDice(frame, roll.Id, [new DieValue(0, 0)]), new SetDice(frame, roll.Id, [new DieValue(0, 7)]),
        })
            Assert.IsType<Refused>(sim.Dispatch(command, Samples.P1, Samples.Team1));
        Assert.Equal(seq, sim.State.Projection.LastSeq);                                     // nothing was written
        Assert.IsType<Applied>(sim.Dispatch(new SetDice(frame, roll.Id, [new DieValue(0, 6)]), Samples.P1, Samples.Team1));
    }

    // ---- C1: deal-wounds ----

    static Sim Wounding(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target, int wounds)
    {
        var sim = new Sim(State([attacker], [target]), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "wounds")), TestProperties.Core, new ScriptedDice(1));
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(attacker.Id, [target.Id], [], GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target.Id], [], GfNames.Hold));
        return sim;
    }

    [Fact]
    public void An_allocation_naming_unknown_or_ineligible_models_or_too_many_wounds_keeps_only_the_offered_models_and_the_wounds_owed()
    {
        var (a, t) = (Unit("A", "4+", "4+", [], 1, []), Unit("T", "4+", "4+", [], 5, []));
        var sim = Wounding(a, t, 2);
        var (first, second) = (t.Models[0].Id, t.Models[1].Id);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [Nobody] = 5, [a.Models[0].Id] = 5, [first] = 99, [second] = -4 }));
        Assert.Equal((4, 1), (sim.Alive(t.Id), sim.Unit(t.Id).WoundsThisActivation));       // 99 is cut to the two owed: the first dies, one wound spills to be asked for again
        Assert.Equal(1, sim.Alive(a.Id));                                                   // the attacker's model was not offered, so it is untouched
        Assert.Equal(1, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void An_allocation_of_the_wrong_type_or_an_empty_one_asks_again(int kind)
    {
        var (a, t) = (Unit("A", "4+", "4+", [], 1, []), Unit("T", "4+", "4+", [], 3, []));
        var sim = Wounding(a, t, 1);
        AskAnswer garbage = kind switch { 0 => new Passed(), 1 => new Chosen("x"), _ => new Allocated(new Dictionary<Guid, int>()) };
        sim.Answer(Samples.Team2, garbage);
        Assert.Equal(3, sim.Alive(t.Id));
        Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal);                          // nothing was allocated, so the wound is asked for again
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 1 }));
        Assert.Equal(2, sim.Alive(t.Id));
    }

    // ---- C1: charge ----

    static (Sim Sim, Tabletop.Content.Unit Charger, Tabletop.Content.Unit Target) Charging(Enforcement enforcement = Enforcement.Warn)
    {
        var warriors = Unit("Warriors", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 2, [], Melee("Hammer", 1));
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -8, x2: 0, enforcement: enforcement), 1, 1, 1, 1, 1, 1, 1, 1);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(warriors.Id, [], [], GfNames.Charge));
        return (sim, warriors, dwarves);
    }

    public static TheoryData<int> ChargeTargetGarbage => new() { 0, 1, 2, 3 };

    [Theory]
    [MemberData(nameof(ChargeTargetGarbage))]
    public void A_charge_target_that_was_not_offered_ends_the_activation_like_no_target(int kind)
    {
        var (sim, warriors, _) = Charging();
        AskAnswer garbage = kind switch { 0 => new Passed(), 1 => new SelectedPieces([Nobody]), 2 => new SelectedPieces([warriors.Id]), _ => new SelectedPieces([]) };
        sim.Answer(Samples.Team1, garbage);
        Assert.Empty(sim.State.Projection.Stack);
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.Melee);
    }

    [Fact]
    public void A_charge_move_naming_unknown_models_or_containers_moves_nothing_and_does_not_throw()
    {
        var (sim, warriors, dwarves) = Charging();
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        var mine = warriors.Models[0].Id;
        sim.Answer(Samples.Team1, new Destinations([
            new ModelRelocation(Nobody, new OnShelf(), new OnTable(At(-2))),
            new ModelRelocation(mine, new OnTable(At(-8)), new Inside(Nobody)),
            new ModelRelocation(mine, new Inside(Nobody), new OnTable(At(-2))),
        ]));
        Assert.DoesNotContain(Relocations(sim), r => r.ModelId == Nobody);
        Assert.IsType<OnTable>(sim.Model(mine).Location);
    }

    [Fact]
    public void A_close_in_that_moves_an_unknown_model_is_ignored()
    {
        var (sim, warriors, dwarves) = Charging();
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(warriors.Models[0].Id, sim.Model(warriors.Models[0].Id).Location, new OnTable(At(-1.3)))]));
        Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal);
        sim.Answer(Samples.Team2, new Destinations([new ModelRelocation(Nobody, new OnShelf(), new OnTable(At(1))),
            new ModelRelocation(warriors.Models[0].Id, new OnShelf(), new OnTable(At(1)))]));                       // a model outside the Ask's ids is not the defender's to move
        Assert.DoesNotContain(Relocations(sim), r => r.ModelId == Nobody);
        Assert.DoesNotContain(Relocations(sim).Skip(1), r => r.ModelId == warriors.Models[0].Id && r.To is OnTable { Placement.Position.X: 1 });
    }

    [Fact]
    public void A_strike_back_answer_that_is_not_an_option_is_no_strike()
    {
        var (sim, warriors, dwarves) = Charging();
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        var facing = ((OnTable)sim.Model(dwarves.Models[0].Id).Location).Placement.Position;                   // into contact, so the melee is fought
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(warriors.Models[0].Id, sim.Model(warriors.Models[0].Id).Location, new OnTable(new Placement(facing with { X = facing.X - 1.3 }, new Yaw(0))))]));
        sim.Answer(Samples.Team2, new Passed());
        var guard = 0;
        while (sim.State.Projection.Stack.Count > 0 && guard++ < 10)
        {
            var open = sim.State.Projection.OpenAsks.Values.Single().Ask;
            var team = open.Teams[0];
            sim.Answer(team, open.Legal switch { Options => new Chosen("bogus"), Allocation => new Passed(), _ => new Passed() });
        }
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Contains(sim.Emitted.OfType<AskOpened>(), o => o.Ask.Question == "strike_back");                  // the Ask was answered with the bogus id
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == dwarves.Id);
        Assert.DoesNotContain(GfNames.Fatigued, sim.Unit(dwarves.Id).Conditions);
    }

    [Fact]
    public void A_consolidation_naming_unknown_models_moves_nobody()
    {
        var (sim, warriors, dwarves) = Charging();
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(warriors.Models[0].Id, sim.Model(warriors.Models[0].Id).Location, new OnTable(At(-1.3)))]));
        sim.Answer(Samples.Team2, new Passed());
        for (var guard = 0; sim.State.Projection.Stack.Count > 0 && guard < 10; guard++)
        {
            var open = sim.State.Projection.OpenAsks.Values.Single().Ask;
            sim.Answer(open.Teams[0], open.Legal is MoveRegion
                ? new Destinations([new ModelRelocation(Nobody, new OnShelf(), new OnTable(At(3))), new ModelRelocation(Nobody, new Inside(Nobody), new OnTable(At(3)))])
                : new Passed());
        }
        Assert.Empty(sim.State.Projection.Stack);
        Assert.DoesNotContain(Relocations(sim), r => r.ModelId == Nobody);
    }

    // ---- C1: casting ----

    static EffectDocument Fx(EffectKind kind, int? amount = null) =>
        new(EffectSubject.Self, 10, kind, null, null, null, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, [], []);
    static readonly EntryDocument Fireball = new("Fireball", CatalogKind.Spell, false, null, 2, "Test Book", "Target enemy unit within 12\" takes 3 hits.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12), [Fx(EffectKind.AddHit, 3)]);

    static (Sim Sim, Tabletop.Content.Unit Wizard) Casting(params int[] dice)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]); var grunts = Unit("Grunts", "4+", "5+", [], 3, []);
        var sim = new Sim(State([wizard], [grunts], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), TestProperties.WithSpells(Fireball), new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        return (sim, wizard);
    }

    [Theory]
    [InlineData("garbage")] [InlineData("")] [InlineData("Fireball|not-a-guid")] [InlineData("Nope|00000000-0000-0000-0000-000000000001")] [InlineData("Fireball|00000000-0000-0000-0000-000000000001")] [InlineData("|")]
    public void A_spell_pick_that_is_not_an_option_casts_nothing_and_spends_nothing(string option)
    {
        var (sim, wizard) = Casting(5);
        sim.Answer(Samples.Team1, new Chosen(option));
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal(3, sim.Model(wizard.Models[0].Id).SpellTokens);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void A_spell_pick_of_the_wrong_type_casts_nothing_and_spends_nothing(int kind)
    {
        var (sim, wizard) = Casting(5);
        sim.Answer(Samples.Team1, kind switch { 0 => new Passed(), 1 => new SelectedPieces([Nobody]), _ => new Destinations([]) });
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal(3, sim.Model(wizard.Models[0].Id).SpellTokens);
    }

    [Fact]
    public void A_token_spend_naming_unknown_models_or_impossible_counts_spends_only_what_is_held()
    {
        var (sim, wizard) = Casting(5, 1, 1, 1);
        var model = wizard.Models[0].Id;
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{model}"));
        sim.Answer(Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [Nobody] = 5, [model] = -3 }));
        Assert.Equal(1, sim.Model(model).SpellTokens);                                        // the cost left one; nothing named was spendable
        Assert.Equal(0, sim.Steps("spend-tokens").Single().Values!["net_tokens"]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void A_spell_target_that_was_not_offered_hits_nothing(int kind)
    {
        var (sim, wizard) = Casting(5, 6, 6, 6);
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, kind switch { 0 => new Passed(), 1 => new SelectedPieces([Nobody]), 2 => new SelectedPieces([wizard.Id]), _ => new SelectedPieces([]) });
        Assert.Empty(sim.State.Projection.Stack);
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.SpellHitsSeq);
    }

    [Fact]
    public void A_reaction_that_is_not_an_option_is_a_pass()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]); var grunts = Unit("Grunts", "4+", "5+", [], 3, []);
        var bolt = new EntryDocument("Bolt", CatalogKind.Spell, false, null, 1, "Test Book", "Target enemy unit within 12\" takes 1 hit.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12), []);
        var sim = new Sim(State([wizard], [grunts], x1: -4, x2: 4), GrimdarkFutureRules.Instance, TestProperties.WithSpells(bolt), new ScriptedDice([5]));   // the window opens only with a spell to cast
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(wizard.Id, [], [], GfNames.Hold));
        Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
        sim.Answer(Samples.Team1, new Chosen("bogus"));
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.CastingSeq);
        Assert.Empty(sim.State.Projection.Stack);
    }

    // ---- C1: shooting and movement ----

    public static TheoryData<int> Assignments => new() { 0, 1, 2, 3, 4 };

    [Theory]
    [MemberData(nameof(Assignments))]
    public void A_target_declaration_the_Ask_did_not_offer_fires_nothing(int kind)
    {
        var squad = Unit("Squad", "4+", "4+", [], 2, [], Gun("Rifle", "24\"", 1)); var enemy = Unit("Enemy", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([squad], [enemy], x1: -6, x2: 6), 6, 6, 6);
        sim.Start(GfNames.Shooting, new SequenceContext(squad.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, kind switch
        {
            0 => new Assigned(new Dictionary<string, Guid> { ["Rifle"] = Nobody }),
            1 => new Assigned(new Dictionary<string, Guid> { ["Nope"] = enemy.Id }),
            2 => new Assigned(new Dictionary<string, Guid> { ["Rifle"] = squad.Id }),
            3 => new Assigned(new Dictionary<string, Guid>()),
            _ => new Passed(),
        });
        Assert.Empty(sim.State.Projection.Stack);
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.AttackSequence);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void A_move_naming_unknown_models_or_containers_moves_nobody(int kind)
    {
        var squad = Unit("Squad", "4+", "4+", [], 2, []); var enemy = Unit("Enemy", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([squad], [enemy], x1: -6, x2: 6), 6);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(squad.Id, [], [], GfNames.Advance));
        var mine = squad.Models[0].Id;
        sim.Answer(Samples.Team1, kind switch
        {
            0 => new Destinations([new ModelRelocation(Nobody, new OnShelf(), new OnTable(At(-5)))]),
            1 => new Destinations([new ModelRelocation(mine, new OnTable(At(-6)), new Inside(Nobody))]),
            2 => new Destinations([new ModelRelocation(mine, new Inside(Nobody), new OnTable(At(-5)))]),
            _ => new Destinations([]),
        });
        Assert.DoesNotContain(Relocations(sim), r => r.ModelId == Nobody);
        Assert.IsType<OnTable>(sim.Model(mine).Location);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Survivors_of_a_wreck_placed_by_unknown_ids_are_destroyed_not_thrown_at()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]); var riders = Unit("Riders", "4+", "4+", [], 2, []);
        var sim = new Sim(State([gunners], [bus, riders]), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "wounds")), TestProperties.Core, new ScriptedDice(4, 4));
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved(riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(gunners.Id, [bus.Id], [], GfNames.Hold)), new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = 3 } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(gunners.Id, [bus.Id], [], GfNames.Hold));
        Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal);
        sim.Answer(Samples.Team2, new Destinations([new ModelRelocation(Nobody, new Inside(container), new OnTable(At(0))), new ModelRelocation(riders.Models[0].Id, new Inside(Nobody), new OnTable(At(0)))]));
        Assert.Equal(0, sim.Alive(riders.Id));
        Assert.Empty(sim.State.Projection.Stack);
    }
}
