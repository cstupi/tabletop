using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// opr-sequences.md → Dice edits: what a face decides after the rolling step is read off the dice as they stand when it is used, so an edit
/// at the boundary that follows the roll turns the decision. Every game here runs with the wait setting on, so each boundary is a confirm gate.
/// </summary>
public class FaceDecisionEditTests
{
    static Sim Game(IReadOnlyList<Unit> mine, IReadOnlyList<Unit> theirs, double x1, double x2, IReadOnlyList<TerrainPlacement>? terrain, params int[] dice) =>
        new(State(mine, theirs, x1: x1, x2: x2, waits: true, terrain: terrain), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(dice));

    static bool Gated(Sim sim) => sim.State.Projection.OpenAsks.Values.Any(a => a.Ask.Question == AskQuestions.Confirm);

    /// <summary>Release the one open step-boundary gate, whoever holds it.</summary>
    static void Pass(Sim sim)
    {
        var gate = sim.State.Projection.OpenAsks.Values.Single(a => a.Ask.Question == AskQuestions.Confirm).Ask;
        sim.Answer(gate.Teams[0], new Confirmed());
    }

    static void PassAll(Sim sim) { while (Gated(sim)) Pass(sim); }

    /// <summary>Set the faces of the last roll the step made.</summary>
    static void Edit(Sim sim, string step, params int[] faces)
    {
        var record = sim.Steps(step).Last(s => s.Rolls.Count > 0).Rolls[^1];
        var frame = sim.State.Projection.Stack.Single(f => f.Steps.Any(s => s.Rolls.Any(r => r.Id == record.Id))).Id;
        Assert.IsType<Dispatch.Applied>(sim.Dispatch(new SetDice(frame, record.Id, faces.Select((f, i) => new DieValue(i, f)).ToList()), Samples.P1, Samples.Team1));
    }

    static SequenceContext Shoot(Unit a, Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold);

    /// <summary>The attack up to the gate after roll-to-block: pick-target-model, roll-to-hit, score-hits and resolve each pass a gate.</summary>
    static Sim Blocked(Unit shooter, Unit target, params int[] dice)
    {
        var sim = Game([shooter], [target], -4, 4, null, dice);
        sim.Start(GfNames.AttackSequence, Shoot(shooter, target));
        for (var i = 0; i < 4; i++) Pass(sim);
        Assert.Single(sim.Steps("roll-to-block"));
        Assert.Empty(sim.Steps("assign"));
        return sim;
    }

    // ---- defence extra wounds (Shred: "On unmodified results of 1 to block hits, this weapon deals 1 extra wound") ----------------------

    [Fact]
    public void A_block_edited_to_a_one_against_shred_deals_its_extra_wound()
    {
        var shooter = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Shred")));
        var sim = Blocked(shooter, Unit("B", "4+", "4+", [], 5, []), 5, 5, 4, 6);
        Edit(sim, "roll-to-block", 1, 6);
        Pass(sim);
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);    // the failed 1 and Shred's wound
    }

    [Fact]
    public void A_shred_one_edited_to_a_four_deals_neither_wound()
    {
        var shooter = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Shred")));
        var target = Unit("B", "4+", "4+", [], 5, []);
        var sim = Blocked(shooter, target, 5, 5, 1, 6);
        Edit(sim, "roll-to-block", 4, 6);
        Pass(sim);
        Assert.DoesNotContain(sim.State.Projection.OpenAsks.Values, a => a.Ask.Question == "remove_casualties");
        Assert.Equal(5, sim.Alive(target.Id));
    }

    // ---- wound-ignoring rolls (Regeneration, p.14) ----------------------------------------------------------------------------------------

    /// <summary>Two wounds on a lone Regeneration troll: deal-wounds' assign rolls two dice and waits at the gate before it applies them.</summary>
    static (Sim Sim, Guid Troll) Regenerating(params int[] ignore)
    {
        var troll = Unit("Troll", "4+", "4+", [P("Regeneration"), P("Tough", 6)], 1, []);
        var sim = Blocked(Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 2)), troll, [5, 5, 1, 1, .. ignore]);
        Pass(sim);                                                                                  // deal
        Assert.Equal("Regeneration", sim.Steps("assign").Single().Rolls.Single().Roll);
        Assert.Equal(0, sim.Model(troll.Models[0].Id).Wounds);
        return (sim, troll.Models[0].Id);
    }

    [Fact]
    public void A_regeneration_die_edited_to_a_five_ignores_its_wound()
    {
        var (sim, troll) = Regenerating(1, 1);
        Edit(sim, "assign", 5, 1);
        Pass(sim);
        Assert.Equal(1, sim.Model(troll).Wounds);
        Assert.Equal(1, sim.Steps("assign").Last().Values!["ignored_" + troll.ToString("N")]);
    }

    [Fact]
    public void A_regeneration_five_edited_to_a_one_lets_its_wound_through()
    {
        var (sim, troll) = Regenerating(5, 5);
        Edit(sim, "assign", 1, 5);
        Pass(sim);
        Assert.Equal(1, sim.Model(troll).Wounds);
    }

    [Fact]
    public void Unedited_regeneration_dice_apply_as_rolled()
    {
        var (sim, troll) = Regenerating(5, 2);
        Pass(sim);
        Assert.Equal(1, sim.Model(troll).Wounds);
        Assert.Equal(2, sim.Steps("assign").Count);                                                 // the roll, then the wounds
    }

    // ---- dangerous terrain (p.11: "roll one die ... on a 1 it takes a wound") --------------------------------------------------------------

    static TerrainPlacement DangerousPatch() =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("patch"), new Point3(-20, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(4, 4), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);

    /// <summary>A Tough(3) model standing in dangerous terrain takes the test: one die, then the gate before `deal`.</summary>
    static (Sim Sim, Guid Model) InDangerous(int face)
    {
        var u = Unit("U", "4+", "4+", [P("Tough", 3)], 1, []);
        var sim = Game([u], [Unit("B", "4+", "4+", [], 1, [])], -20, 20, [DangerousPatch()], face);
        sim.Start(GfNames.DangerousTerrain, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.Single(sim.Steps("test").Single().Rolls.Single().Dice);
        Assert.True(Gated(sim));
        return (sim, u.Models[0].Id);
    }

    [Fact]
    public void A_dangerous_terrain_die_edited_to_a_one_wounds()
    {
        var (sim, model) = InDangerous(3);
        Edit(sim, "test", 1);
        Pass(sim);
        Assert.Equal(1, sim.Model(model).Wounds);
    }

    [Fact]
    public void A_dangerous_terrain_one_edited_to_a_three_wounds_nobody()
    {
        var (sim, model) = InDangerous(1);
        Edit(sim, "test", 3);
        Pass(sim);
        Assert.Equal(0, sim.Model(model).Wounds);
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.DealWounds);
    }

    // ---- Impact (p.14: "roll X dice ... for each 2+ the target takes one hit") -----------------------------------------------------------

    /// <summary>A melee whose charger has Impact(2): melee-start and counter pass their gates, impact rolls and waits.</summary>
    static Sim Impacting(params int[] dice)
    {
        var riders = Unit("Riders", "4+", "4+", [P("Impact", 2)], 1, [], Melee("Thrust", 1));
        var foot = Unit("Foot", "4+", "6+", [], 3, [], Melee("Club", 1));
        var sim = Game([riders], [foot], 0, 1.3, null, [.. dice, .. Enumerable.Repeat(1, 20)]);
        sim.Start(GfNames.Melee, new SequenceContext(riders.Id, [foot.Id], riders.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        Pass(sim); Pass(sim);
        Assert.Equal(2, sim.Steps("impact").Single().Rolls.Single().Dice.Count);
        Assert.True(Gated(sim));
        return sim;
    }

    static int ImpactBlocks(Sim sim) =>
        sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.ResolveHits && s.Context.WeaponIds.Count == 0)
            .Sum(s => sim.Emitted.OfType<StepCompleted>().Where(c => c.FrameId == s.FrameId && c.Step == "roll-to-block").Sum(c => c.Rolls.Sum(r => r.Dice.Count)));

    [Fact]
    public void Impact_dice_edited_to_hits_resolve_those_hits()
    {
        var sim = Impacting(1, 1);
        Edit(sim, "impact", 3, 1);
        Pass(sim);
        PassAll(sim);
        Assert.Equal(1, ImpactBlocks(sim));
    }

    [Fact]
    public void Impact_hits_edited_to_ones_resolve_nothing()
    {
        var sim = Impacting(3, 3);
        Edit(sim, "impact", 1, 1);
        Pass(sim);
        Assert.DoesNotContain(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.ResolveHits && s.Context.WeaponIds.Count == 0);
    }

    // ---- attack-start faces (Unpredictable Shooter: "on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead") --------

    static Sim Unpredictable(int face)
    {
        var a = Unit("A", "4+", "4+", [P("Unpredictable Shooter")], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Game([a], [b], -4, 4, null, [face, .. Enumerable.Repeat(5, 10)]);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Pass(sim);                                                                                  // before attack-start
        Assert.Single(sim.Steps("attack-start").Single().Rolls);
        Assert.Empty(sim.Steps("roll-to-hit"));
        return sim;
    }

    static bool PlusOneToHit(Sim sim) => sim.Steps("roll-to-hit").SelectMany(s => s.Rolls).SelectMany(r => r.Modifiers).Any(m => m.Source == "Unpredictable Shooter");
    static bool ApOnBlocks(Sim sim) => sim.Steps("roll-to-block").SelectMany(s => s.Rolls).SelectMany(r => r.Modifiers).Any(m => m.Source == "AP");

    [Fact]
    public void An_unpredictable_face_edited_to_a_five_gives_plus_one_to_hit_and_no_ap()
    {
        var sim = Unpredictable(2);
        Edit(sim, "attack-start", 5);
        PassAll(sim);
        Assert.True(PlusOneToHit(sim));
        Assert.False(ApOnBlocks(sim));
    }

    [Fact]
    public void An_unpredictable_face_edited_to_a_two_gives_ap_and_no_plus_one_to_hit()
    {
        var sim = Unpredictable(5);
        Edit(sim, "attack-start", 2);
        PassAll(sim);
        Assert.False(PlusOneToHit(sim));
        Assert.True(ApOnBlocks(sim));
    }

    // ---- dice-distance placement (Rapid Blink: "anywhere fully within D3"; p.4: a D6 halved, rounded up) --------------------------------

    /// <summary>Rapid Blink chosen at activation start: the D3 is rolled and the placement waits at the gate before it asks.</summary>
    static (Sim Sim, Unit Unit) Blinking(int face)
    {
        var a = Unit("A", "4+", "4+", [P("Rapid Blink")], 1, []);
        var sim = Game([a], [Unit("B", "4+", "4+", [], 1, [])], -20, 20, null, face);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Pass(sim);                                                                                  // before activation-start
        sim.Answer(Samples.Team1, new Chosen($"Rapid Blink|{a.Id:N}"));
        Assert.Single(sim.Steps("reposition").Single().Rolls);
        Assert.True(Gated(sim));
        return (sim, a);
    }

    [Fact]
    public void A_blink_die_edited_up_reaches_further()
    {
        var (sim, a) = Blinking(1);
        Edit(sim, "reposition", 5);
        Pass(sim);
        Assert.Equal(3.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void A_blink_die_edited_down_reaches_less_far()
    {
        var (sim, a) = Blinking(5);
        Edit(sim, "reposition", 1);
        Pass(sim);
        Assert.Equal(1.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
    }
}
