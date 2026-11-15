using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0, written from the rulebook and army-book text.
///
/// Takedown (p.15): "This model may pick any model in the target unit as its individual target for this weapon's attacks, which is resolved as if it was
/// a unit of [1]. Takedown attacks must be resolved before other weapons. Note that other models in the target's unit don't block line of sight or
/// provide cover to the target model in the unit." Example: a Hero with Stealth in a unit without it: -1 to hit (ArmyRuleAuditFinishTakedownTests).
///   1. "may pick": a pick that can be declined.  2. any model, a joined Hero too (heroes-last does not apply).  3. a unit of [1]: Blast capped at 1,
///   the picked model's own rules (Example).  4. before other weapons, Deadly ones included, shooting and melee.  5. the target's own models block no sight.
/// Takedown Shot (army rule): "Once per game, when this model shoots, it may make X extra attacks against the target at Quality 2+ with AP(2), Deadly(3),
/// and Takedown." Engine side: the import's `Takedown Shot` weapon (Attacks 1, the model's longest Range, AP(2), Deadly(3), Reliable, Takedown, Limited).
///   1. Quality 2+, AP(2), Deadly(3).  2. once per game.  3. "may": left undeclared, it stays available.  4. "against the target": the model's target.
///   5. X attacks: the import writes Attacks 1 whatever X (management side, not tested here).
/// Takedown Strike (army rule): "Once per game, when it's this model's turn to strike in melee, it may make X attacks at Quality 2+ with AP(2), Deadly(3),
/// and Takedown."
///   1. "may": the player chooses whether to use it.  2. at the model's turn to strike: a charged model's strike back.  3. once per game.
/// Limited (p.14): "Weapons with this rule may only be used once per game."
///   1. a fired Limited weapon is not offered again (shooting; melee: Takedown Strike).  2. a Limited weapon not used stays available.
///   3. once per game across kinds: an Assault Limited weapon shot is not struck with later.  4. a weapon whose model could not strike was not used.
/// </summary>
public class ClausesAttackTakedownLimitedTests
{
    static SequenceContext Fire(Unit a, Unit t, string action = GfNames.Hold) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), action);

    static Unit Squad(int models, string defense = "4+", params PropertyInstance[] props) => Unit("Squad", "4+", defense, [], models, props);

    static Weapon Shot(int attacks = 1) => Gun("Takedown Shot", "24\"", attacks, P("AP", 2), P("Deadly", 3), P("Reliable"), P("Takedown"), P("Limited"));
    static Weapon Strike() => Melee("Takedown Strike", 1, P("AP", 2), P("Deadly", 3), P("Reliable"), P("Takedown"), P("Limited"));

    /// <summary>Answers whatever the two teams are asked until nothing is open: strike back, no Counter, no move, the first models for wounds and picks.</summary>
    static void Drain(Sim sim)
    {
        for (var i = 0; i < 60; i++)
        {
            var open = sim.State.Projection.OpenAsks.Values.FirstOrDefault(a => a.Ask.Teams.Any(t => !a.Answers.ContainsKey(t)));
            if (open is null) return;
            var team = open.Ask.Teams.First(t => !open.Answers.ContainsKey(t));
            AskAnswer answer = open.Ask switch
            {
                { Question: "strike_back" } => new Chosen("strike"),
                { Question: GfMelee.TakedownStrikeQuestion } => new Chosen("yes"),
                { Question: var q } when q.StartsWith(GfMelee.LimitedQuestion, StringComparison.Ordinal) => new Chosen("yes"),
                { Question: "counter" } => new Chosen("pass"),
                { Legal: Allocation al } => new Allocated(new Dictionary<Guid, int> { [al.Targets[0]] = al.Amount }),
                { Legal: PieceSelection ps } when ps.Min > 0 => new SelectedPieces([ps.Eligible[0]]),
                { Question: AskQuestions.Confirm } => new Confirmed(),
                _ => new Passed(),
            };
            sim.Answer(team, answer);
        }
    }

    static List<SequenceStarted> Attacks(Sim sim) => [.. sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence)];
    static string NameOf(Unit u, Guid weapon) => u.Models.SelectMany(m => m.Weapons).Single(w => w.Id == weapon).Name;

    // ---- Takedown -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Takedown_may_pick_a_model_or_decline()
    {
        var sniper = Unit("Sniper", "4+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var squad = Squad(3);
        squad = squad with { Models = [squad.Models[0], squad.Models[1], squad.Models[2] with { Properties = [P("Hero"), P("Tough", 3)] }] };
        var sim = Sim.Gf36(State([sniper], [squad], x1: -6, x2: 6), 6, 1);
        sim.Start(GfNames.AttackSequence, Fire(sniper, squad));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal("takedown_target", ask.Question);
        Assert.True(ask.AllowPass);
        sim.Answer(Samples.Team1, new Passed());                                                        // declined: the unit is the target
        Drain(sim);
        Assert.Equal(0, sim.Model(squad.Models[2].Id).Wounds);                                           // the Hero takes wounds last again
        Assert.Equal(2, sim.Alive(squad.Id));
    }

    [Fact]
    public void Takedown_picks_a_joined_hero_that_would_otherwise_take_wounds_last()
    {
        var sniper = Unit("Sniper", "4+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var squad = Squad(3);
        squad = squad with { Models = [squad.Models[0], squad.Models[1], squad.Models[2] with { Properties = [P("Hero"), P("Tough", 3)] }] };
        var sim = Sim.Gf36(State([sniper], [squad], x1: -6, x2: 6), 6, 1);
        sim.Start(GfNames.AttackSequence, Fire(sniper, squad));
        sim.Answer(Samples.Team1, new SelectedPieces([squad.Models[2].Id]));
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal((1, 3), (sim.Model(squad.Models[2].Id).Wounds, sim.Alive(squad.Id)));
    }

    [Fact]
    public void A_takedown_blast_is_capped_at_one_as_a_unit_of_one()
    {
        var sniper = Unit("Sniper", "4+", "4+", [], 1, [], Gun("Bomb", "30\"", 1, P("Takedown"), P("Blast", 3)));
        var squad = Squad(3, "6+");
        var sim = Sim.Gf36(State([sniper], [squad], x1: -6, x2: 6), 6, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Fire(sniper, squad));
        sim.Answer(Samples.Team1, new SelectedPieces([squad.Models[0].Id]));
        Assert.Equal(1, sim.Steps("roll-to-hit").Single().Values!["hit_multiplier"]);
        Assert.Equal(2, sim.Alive(squad.Id));
    }

    [Fact]
    public void Takedown_shoots_before_other_weapons_deadly_ones_included()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Alpha Lance", "24\"", 1, P("Deadly", 2)), Gun("Zulu Rifle", "24\"", 1, P("Takedown")));
        var t = Squad(2, "4+", P("Tough", 9));
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6), Enumerable.Repeat(1, 10).ToArray());
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Alpha Lance"] = t.Id, ["Zulu Rifle"] = t.Id }));
        Drain(sim);
        Assert.Equal(["Zulu Rifle", "Alpha Lance"], Attacks(sim).Select(s => NameOf(a, s.Context.WeaponIds.Single())));
    }

    [Fact]
    public void Takedown_strikes_before_other_weapons_deadly_ones_included()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Alpha Axe", 1, P("Deadly", 2)), Melee("Zulu Blade", 1, P("Takedown")));
        var t = Squad(2, "4+", P("Tough", 9));
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), Enumerable.Repeat(1, 20).ToArray());
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        Drain(sim);
        Assert.Equal(["Zulu Blade", "Alpha Axe"], Attacks(sim).Where(s => s.Context.ActorUnitId == a.Id).Select(s => NameOf(a, s.Context.WeaponIds.Single())));
    }

    /// <summary>GfTakedownSightTests' layout under 3.6: the shooter at (-10, 0); squad model 0 at (10, 0) behind its squad-mate at (0, 0); model 2 behind a wall.</summary>
    [Fact]
    public void The_targets_own_models_do_not_block_the_takedown_pick()
    {
        var sniper = Unit("Sniper", "4+", "4+", [], 1, [], Gun("Long Rifle", "36\"", 1, P("Takedown")));
        var squad = Squad(3);
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(0, -6, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null))]);
        var sim = Sim.Gf36(State([sniper], [squad], terrain: [wall]), 6, 1);
        Point3[] at = [new(10, 0, 0), new(0, 0, 0), new(10, -12, 0)];
        sim.Append([new ModelsMoved([new ModelRelocation(sniper.Models[0].Id, sim.Model(sniper.Models[0].Id).Location, new OnTable(new Placement(new Point3(-10, 0, 0), new Yaw(0)))),
            .. squad.Models.Select((m, i) => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnTable(new Placement(at[i], new Yaw(0)))))])]);
        sim.Start(GfNames.AttackSequence, Fire(sniper, squad));
        Assert.Equal(new HashSet<Guid> { squad.Models[0].Id, squad.Models[1].Id }, ((PieceSelection)sim.AskFor(Samples.Team1).Legal).Eligible.ToHashSet());
    }

    // ---- Takedown Shot --------------------------------------------------------------------------------------------------------------------

    static (Sim Sim, Unit Model, Unit Target, Unit Other) ShotGame(params int[] dice)
    {
        var a = Unit("Hunter", "5+", "4+", [], 1, [P("Takedown Shot", 1)], Gun("Rifle", "24\"", 1), Shot());
        var t = Squad(2, "4+", P("Tough", 3));
        var other = Unit("Other", "4+", "4+", [], 1, [P("Tough", 9)]);
        return (Sim.Gf36(State([a], [t, other], x1: -6, x2: 6), dice), a, t, other);
    }

    static Assignment Declare(Sim sim, Unit a)
    {
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        return (Assignment)sim.AskFor(Samples.Team1).Legal;
    }

    [Fact]
    public void Takedown_shot_hits_at_quality_two_with_ap_two_and_deadly_three()
    {
        var (sim, a, t, _) = ShotGame(2, 5);                                                            // a 2 hits at 2+; 5 - 2 = 3 fails the 4+ block
        Declare(sim, a);
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Takedown Shot"] = t.Id }));
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[1].Id]));
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Equal(2, hit.Threshold);
        Assert.Equal(-2, sim.Steps("roll-to-block").Single().Rolls.Single().Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.IsType<OnShelf>(sim.Model(t.Models[1].Id).Location);                                       // one wound ×3 on a Tough(3) model
        Assert.Equal(0, sim.Model(t.Models[0].Id).Wounds);
        Assert.Contains("Takedown Shot", sim.Steps("fire-groups").SelectMany(s => s.Applied ?? []));
    }

    [Fact]
    public void Takedown_shot_is_once_per_game_and_an_undeclared_one_stays_available()
    {
        var (sim, a, t, _) = ShotGame(Enumerable.Repeat(1, 20).ToArray());
        Assert.Contains(Declare(sim, a).Items, i => i.Id == "Takedown Shot");
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = t.Id }));        // declined
        Drain(sim);
        Assert.Contains(Declare(sim, a).Items, i => i.Id == "Takedown Shot");
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = t.Id, ["Takedown Shot"] = t.Id }));
        Drain(sim);
        Assert.DoesNotContain(Declare(sim, a).Items, i => i.Id == "Takedown Shot");
    }

    [Fact]
    public void Takedown_shot_attacks_only_the_target_the_model_shoots()
    {
        var (sim, a, t, other) = ShotGame(Enumerable.Repeat(1, 20).ToArray());
        Declare(sim, a);
        sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = t.Id, ["Takedown Shot"] = other.Id })), Samples.P1, Samples.Team1);
        sim.Run();
        Drain(sim);
        Assert.DoesNotContain(Attacks(sim), s => s.Context.TargetUnitIds[0] == other.Id && NameOf(a, s.Context.WeaponIds[0]) == "Takedown Shot");
    }

    // ---- Takedown Strike ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Takedown_strike_asks_before_it_is_used()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Takedown Strike", 1)], Melee("CCW", 1), Strike());
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), Enumerable.Repeat(1, 20).ToArray());
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        var strike = a.Models[0].Weapons[1].Id;
        Assert.DoesNotContain(Attacks(sim), s => s.Context.WeaponIds.Contains(strike));                 // no answer of the charger's team has been given
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal((GfMelee.TakedownStrikeQuestion, true), (ask.Question, ask.AllowPass));
        sim.Answer(Samples.Team1, new Chosen("no"));                                                     // saved for a later melee
        Drain(sim);
        Assert.Contains(Attacks(sim), s => s.Context.ActorUnitId == a.Id && s.Context.WeaponIds.Contains(a.Models[0].Weapons[0].Id));
        Assert.DoesNotContain(Attacks(sim), s => s.Context.WeaponIds.Contains(strike));
        Assert.Contains(strike, GfMelee.MeleeWeapons(sim.State, a.Id));
    }

    [Fact]
    public void Takedown_strike_strikes_at_the_charged_models_turn_and_only_once_per_game()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var d = Unit("D", "4+", "4+", [], 1, [P("Takedown Strike", 1), P("Tough", 9)], Melee("CCW", 1), Strike());
        var strike = d.Models[0].Weapons[1].Id;
        var sim = Sim.Gf36(State([charger], [d], x1: -1.3, x2: 0), Enumerable.Repeat(1, 60).ToArray());
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        Drain(sim);
        Assert.Contains(Attacks(sim), s => s.Context.ActorUnitId == d.Id && s.Context.WeaponIds.Contains(strike));
        var before = Attacks(sim).Count;
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        Drain(sim);
        var second = Attacks(sim).Skip(before).Where(s => s.Context.ActorUnitId == d.Id).ToList();
        Assert.NotEmpty(second);                                                                         // it struck back again, with the CCW
        Assert.DoesNotContain(second, s => s.Context.WeaponIds.Contains(strike));
    }

    // ---- Limited --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_limited_gun_is_offered_until_fired_and_never_after()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Missile", "24\"", 1, P("Limited")), Gun("Rifle", "24\"", 1));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6), Enumerable.Repeat(1, 20).ToArray());
        Assert.Contains(Declare(sim, a).Items, i => i.Id == "Missile");
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = t.Id }));
        Drain(sim);
        Assert.Contains(Declare(sim, a).Items, i => i.Id == "Missile");
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Missile"] = t.Id }));
        Drain(sim);
        Assert.Equal(["Rifle"], Declare(sim, a).Items.Select(i => i.Id));
    }

    /// <summary>The owner, 2026-11-14: "The demo charges limited dont seem to ever show up as an option to use". p.14: "Weapons with this rule
    /// may only be used once per game", so the player picks the melee: a Limited weapon that could strike is asked before it does. Saved, it
    /// does not strike and stays unused; used, it strikes and is gone.</summary>
    [Fact]
    public void A_limited_weapon_asks_before_it_strikes_in_melee()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Knife", 1), Gun("Demo Charge", "6\"", 1, P("AP", 4), P("Limited"), P(GfNames.Assault)));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var charge = a.Models[0].Weapons[1].Id;
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), Enumerable.Repeat(1, 40).ToArray());
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal((GfMelee.LimitedQuestion + "Demo Charge", "Use Demo Charge?", true), (ask.Question, ask.Prompt, ask.AllowPass));
        Assert.DoesNotContain(Attacks(sim), s => s.Context.WeaponIds.Contains(charge));
        sim.Answer(Samples.Team1, new Chosen("no"));                                                     // saved for a later melee
        Drain(sim);
        Assert.Contains(Attacks(sim), s => s.Context.ActorUnitId == a.Id && s.Context.WeaponIds.Contains(a.Models[0].Weapons[0].Id));   // the knife struck
        Assert.DoesNotContain(Attacks(sim), s => s.Context.WeaponIds.Contains(charge));
        Assert.Contains(charge, GfMelee.MeleeWeapons(sim.State, a.Id));

        var again = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), Enumerable.Repeat(1, 40).ToArray());
        again.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(again.State, a.Id), GfNames.Charge));
        again.Answer(Samples.Team1, new Chosen("yes"));
        Drain(again);
        Assert.Contains(Attacks(again), s => s.Context.WeaponIds.Contains(charge));
        Assert.DoesNotContain(charge, GfMelee.MeleeWeapons(again.State, a.Id));                          // once per game
    }

    [Fact]
    public void A_limited_assault_weapon_shot_is_not_struck_with_in_a_later_melee()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Flamer", "12\"", 1, P(GfNames.Assault), P("Limited")), Melee("Knife", 1));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), Enumerable.Repeat(1, 20).ToArray());
        var flamer = a.Models[0].Weapons[0].Id;
        Assert.Contains(flamer, GfMelee.MeleeWeapons(sim.State, a.Id));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Flamer"] = t.Id }));
        Drain(sim);
        Assert.Equal([a.Models[0].Weapons[1].Id], GfMelee.MeleeWeapons(sim.State, a.Id));
    }

    /// <summary>The Great Axe's model stands 6" back from the melee, out of the 3" strike reach: it strikes nothing, so the axe was not used.</summary>
    [Fact]
    public void A_limited_melee_weapon_whose_model_could_not_strike_stays_available()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Tough", 9)], Melee("Knife", 1));
        var axe = Melee("Great Axe", 1, P("Limited"));
        a = a with { Models = [a.Models[0], a.Models[1] with { Weapons = [.. a.Models[1].Weapons, axe] }] };
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), Enumerable.Repeat(1, 40).ToArray());
        sim.Append([new ModelsMoved([new ModelRelocation(a.Models[1].Id, sim.Model(a.Models[1].Id).Location, new OnTable(new Placement(new Point3(-7.3, -18.5, 0), new Yaw(0))))])]);
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        Drain(sim);
        Assert.Contains(axe.Id, GfMelee.MeleeWeapons(sim.State, a.Id));
    }

    /// <summary>Two models carry a Limited Missile; the second stands out of its 24" range, so only the first's Missile is used.</summary>
    [Fact]
    public void A_limited_gun_whose_model_could_not_shoot_stays_available()
    {
        var a = Unit("A", "4+", "4+", [], 2, [], Gun("Missile", "24\"", 1, P("Limited")));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6), Enumerable.Repeat(1, 20).ToArray());
        sim.Append([new ModelsMoved([new ModelRelocation(a.Models[1].Id, sim.Model(a.Models[1].Id).Location, new OnTable(new Placement(new Point3(-30, -18, 0), new Yaw(0))))])]);
        Declare(sim, a);
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Missile"] = t.Id }));
        Drain(sim);
        Assert.Equal(1, sim.Steps("determine-attacks").Sum(s => s.Values!["attacks"]));
        var used = GfAttack.FiredLimited(sim.State, null);
        Assert.Equal([a.Models[0].Weapons[0].Id], used);
    }
}
