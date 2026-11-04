using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's 3.6.0 shooting games (3.6.0 pp.13–15).</summary>
static partial class SpecialGames
{
    // ---- special-regrowth-under-rending-fire@3.6 ---------------------------------------------------------------------------------------

    /// <summary>
    /// AP, Bane, Rending, Regeneration. Blue unit 1 is a Tough(3) Hero without Regeneration and twelve models with it, on Defense 2+; it holds
    /// and has no gun. Red Holds and shoots it with unit 1 (three AP(4) Rifles), then unit 2 (three Rending Rifles), then unit 3 (three Bane
    /// Rifles of 8 attacks). The dice the checks read are fixed: AP hits [4,4,4] and defense [5,6,6] (one wound; Regeneration 5); Rending
    /// hits [6,4,1] and defense 1 for each hit; Bane hits all 4s. Bane's re-roll is made inside roll-to-block, before a fix can land, so its 24
    /// defense dice are left as rolled: each seed rolls 6s among them, and twelve one-wound models keep the unit's majority with Regeneration through every Bane wound.
    /// </summary>
    static TestGame Regrowth36()
    {
        const string name = "special-regrowth-under-rending-fire";
        var script = Plays((ask, _) => AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Activate(ask, "Red unit 3", "Hold")
            ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(1).Table(36, 24).Scripted(script)
                .Army("Red", 3, 3, weapons: [Gun("AP Rifle", 24, 1, Rule("AP", 4))], unitStats: Stats(4, 4),
                    models: [.. Armed(2, 3, Gun("Rending Rifle", 24, 1, Rule("Rending"))), .. Armed(3, 3, Gun("Bane Rifle", 24, 8, Rule("Bane")))],
                    anchors: new Dictionary<int, Point2> { [1] = new(4, 20), [2] = new(14, 20), [3] = new(24, 20) })
                .Army("Blue", 1, 13, weapons: [Blade("Blade", 1)], unitStats: Stats(4, 2),
                    models: [new ScenarioModel(1, 1, [new(0, "Hero"), new(0, "Tough", 3)]), .. Enumerable.Range(2, 12).Select(m => new ScenarioModel(1, m, [new(0, "Regeneration")]))],
                    anchors: new Dictionary<int, Point2> { [1] = new(12, 4) })
                .Build(),
            ["AP", "Bane", "Rending", "Regeneration"],
            [
                Exact("3.6.0 p.13 AP(X): \"Targets get -X to Defense rolls when blocking hits from weapons with this special rule\" (AP(4): -4; at 2+ a raw 5 fails)", ["AP"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Weapon == "AP Rifle" && a.Block is not null).SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense")).ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Count(m => m.Source == "AP" && m.Amount == -4) != 1) is { } wrong ? $"defense roll {Show(wrong)} lacks AP(4)"
                        : Judged(rolls, "an AP Rifle defense roll", -1);
                }),
                Exact("3.6.0 p.14 Rending: \"on unmodified results of 6 to hit, those hits get AP(+4)\"", ["Rending"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Rending Rifle" && Sixes(a.Hit) > 0 && a.Block is not null), "a Rending Rifle 6 to hit", a =>
                    {
                        var ap4 = a.Block!.Rolls.Where(r => r.Roll == "defense" && r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4)).Sum(r => r.Dice.Count);
                        return ap4 == Sixes(a.Hit) ? null : $"{Sixes(a.Hit)} sixes to hit but {ap4} defense dice at AP(4)";
                    })),
                Exact("3.6.0 p.13 Bane: \"the target must re-roll unmodified Defense results of 6\"", ["Bane"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Bane Rifle" && a.Block is not null)
                            .SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense" && r.Dice.Any(d => d.Raw == 6))), "a Bane hit blocked on a 6", roll =>
                    {
                        var sixes = roll.Dice.Select((d, i) => (d, i)).Where(x => x.d.Raw == 6).Select(x => x.i).ToList();
                        var rerolled = Payloads(run).OfType<DiceRerolled>().Where(x => x.RollId == roll.Id).SelectMany(x => x.Replaced.Select(r => r.Index)).ToList();
                        return sixes.Order().SequenceEqual(rerolled.Order()) ? null : $"defense {Show(roll)}: sixes at [{string.Join(",", sixes)}], re-rolled [{string.Join(",", rerolled)}]";
                    })),
                Exact("3.6.0 p.14 Regeneration: \"When a unit where most models have this rule takes wounds, roll one die for each. On a 5+ it is ignored\" (twelve of thirteen models, the Hero without it)", ["Regeneration"], run =>
                {
                    var hit = Attacks(run).Where(a => a.Weapon == "AP Rifle" && Unblocked(a) > 0).ToList();
                    var hero = run.Final.Models.Values.Single(m => m.Slot.Properties.Any(p => p.Name == "Hero")).Id;
                    return Each(hit, "an AP Rifle wound", a =>
                    {
                        if (!Placed(run, a.At).ContainsKey(hero)) return "the Hero without Regeneration was not in the unit";
                        var regen = a.Assigns.SelectMany(s => s.Rolls).Where(r => r.Roll == "Regeneration").ToList();
                        if (regen.Any(r => r.Threshold != 5)) return $"Regeneration rolls {string.Join(" ", regen.Select(Show))} are not at 5+";
                        var dice = regen.Sum(r => r.Dice.Count);
                        var ignored = regen.Sum(r => r.Dice.Count(d => d.Raw >= 5));
                        var taken = WoundsSet(a.Assigns).Count(w => w.Wounds > 0);   // one-wound models: each wound that stands sets 1
                        return dice == Unblocked(a) && taken == dice - ignored ? null
                            : $"{Unblocked(a)} wounds, {dice} Regeneration dice, {ignored} of 5+, but {taken} taken";
                    }) ?? Judged(hit.SelectMany(a => a.Assigns).SelectMany(s => s.Rolls).Where(r => r.Roll == "Regeneration"), "a Regeneration roll", 0);
                }),
                Exact("3.6.0 p.13 Bane, p.14 Rending: \"Ignores Regeneration\"", ["Bane", "Rending", "Regeneration"], run =>
                    Each(Attacks(run).Where(a => a.Weapon is "Bane Rifle" or "Rending Rifle").SelectMany(a => a.Assigns.Select(s => (a.Weapon, s)))
                            .Where(x => Value(x.s, "wounds_dealt") > 0), "a Bane or Rending wound", x =>
                        x.s.Rolls.Any(r => r.Roll == "Regeneration") ? $"{x.Weapon} wounds rolled Regeneration" :
                        x.s.Applied?.Contains(x.Weapon.Split(' ')[0]) == true ? null : $"{x.Weapon}'s assign step does not list {x.Weapon.Split(' ')[0]}")),
            ],
            dice:
            [
                new("hit", 0, [4, 4, 4], new RollKey(Weapon: "AP Rifle")),
                new("defense", 0, [5, 6, 6], new RollKey(Weapon: "AP Rifle")),
                new("Regeneration", 0, [5], new RollKey(Weapon: "AP Rifle")),
                new("hit", 0, [6, 4, 1], new RollKey(Weapon: "Rending Rifle")),
                new("defense", 0, [1], new RollKey(Weapon: "Rending Rifle")),
                new("defense", 1, [1], new RollKey(Weapon: "Rending Rifle")),
                new("hit", 0, [.. Enumerable.Repeat(4, 24)], new RollKey(Weapon: "Bane Rifle")),
            ]);
    }

    // ---- special-long-range-gunline@3.6 ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Relentless, Surge, Reliable, Stealth, Unstoppable. Blue unit 1 is a Tough(3) Hero without Stealth and eight models with it (Defense 2+),
    /// holding 14" from Red. Red Holds and shoots it: unit 1 (three Relentless Tough(3) models: a Surge Gun and a Reliable Gun of 2 attacks each),
    /// then unit 2 (three Bad Shot models with an Unstoppable Rail Rifle). Fixed: the Surge Gun's hits [6,6,1,1,1,1], the Reliable Gun's
    /// [3,2,6] (2+, -1 from Stealth: a raw 3 hits, a raw 2 misses).
    /// </summary>
    static TestGame Gunline36()
    {
        const string name = "special-long-range-gunline";
        var script = Plays((ask, _) => AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(1).Table(36, 24).Scripted(script)
                .Army("Red", 2, 3, modelRules: [new(1, "Relentless"), new(1, "Tough", 3), new(2, "Bad Shot")],
                    weapons: [Gun("Surge Gun", 48, 2, Rule("Surge")), Gun("Reliable Gun", 48, 2, Rule("Reliable"))], unitStats: Stats(5, 4),
                    models: [.. Armed(2, 3, Gun("Rail Rifle", 48, 1, Rule("Unstoppable")))],
                    anchors: new Dictionary<int, Point2> { [1] = new(8, 20), [2] = new(20, 20) })
                .Army("Blue", 1, 9, weapons: [Blade("Blade", 1)], unitStats: Stats(4, 2),
                    models: [new ScenarioModel(1, 1, [new(0, "Hero"), new(0, "Tough", 3)]), .. Enumerable.Range(2, 8).Select(m => new ScenarioModel(1, m, [new(0, "Stealth")]))],
                    anchors: new Dictionary<int, Point2> { [1] = new(10, 4) })
                .Build(),
            ["Relentless", "Surge", "Reliable", "Stealth", "Unstoppable"],
            [
                Exact("3.6.0 p.14 Reliable: \"Models attacks at Quality 2+ with this weapon\" (with Stealth's -1, a raw 3 hits and a raw 2 misses)", ["Reliable"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Weapon == "Reliable Gun").Select(HitRoll).OfType<RollRecord>().ToList();
                    return rolls.FirstOrDefault(r => r.Threshold != 2) is { } wrong ? $"hit roll {Show(wrong)} is not at 2+" : Judged(rolls, "a Reliable Gun hit roll", 0, -1);
                }),
                Exact("3.6.0 p.15 Stealth: \"When units where most models have this rule are shot from over 9\" away, enemy units get -1 to hit rolls\" (eight of nine models, the Hero without it)", ["Stealth"], run =>
                {
                    var hero = run.Final.Models.Values.Single(m => m.Slot.Properties.Any(p => p.Name == "Hero")).Id;
                    var shots = Attacks(run).Where(a => a.Target == "Blue unit 1" && a.Weapon != "Rail Rifle" && HitRoll(a) is not null).ToList();
                    return Each(shots, "a shot at the Stealth unit", a =>
                    {
                        if (!Placed(run, a.At).ContainsKey(hero)) return "the Hero without Stealth was not in the unit";
                        var stealth = HitRoll(a)!.Modifiers.Where(m => m.Source == "Stealth").ToList();
                        return Gap(run, a.Actor, a.Target, a.At) is > 9 && stealth.Count == 1 && stealth[0].Amount == -1 ? null
                            : $"{Gap(run, a.Actor, a.Target, a.At):0.0}\" away: {Show(HitRoll(a)!)}";
                    }) ?? Judged(shots.Select(HitRoll).OfType<RollRecord>(), "a hit roll against Stealth", 0, -1);
                }),
                Exact("3.6.0 p.15 Surge: \"For each unmodified result of 6 to hit, this weapon deals 1 extra hit\"; 3.6.0 p.14 Relentless: \"When this model shoots at enemies over 9\" away, unmodified result of 6 to hit deal 1 extra hit\"", ["Surge", "Relentless"], run =>
                    Each(Attacks(run).Where(a => a.Actor == "Red unit 1" && Sixes(a.Hit) > 0), "a 6 to hit from the gunline", a =>
                    {
                        if (a.Hit!.Applied?.Contains("Relentless") != true) return $"{Gap(run, a.Actor, a.Target, a.At):0.0}\" away, Relentless not applied";
                        var per = (a.Weapon == "Surge Gun" ? 1 : 0) + 1;
                        return Value(a.Hit, "extra_hits") == Sixes(a.Hit) * per ? null
                            : $"{a.Weapon}: {Sixes(a.Hit)} sixes, {per} extra each, extra_hits {Value(a.Hit, "extra_hits")}";
                    }, need: 2)),
                Exact("3.6.0 p.15 Unstoppable: \"ignores all negative modifiers to this weapon which are not from this model's special rules\" (Stealth's -1 ignored, the model's own Bad Shot -1 kept)", ["Unstoppable"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Rail Rifle" && HitRoll(a) is not null), "a Rail Rifle shot", a =>
                    {
                        var roll = HitRoll(a)!;
                        var kept = roll.Modifiers.Select(m => $"{m.Source}{m.Amount:+0;-0}").ToList();
                        var ignored = roll.Ignored?.Where(i => i.By == "Unstoppable").Select(i => i.Source).ToList() ?? [];
                        return kept.SequenceEqual(["Bad Shot-1"]) && ignored.Contains("Stealth") ? null : $"kept [{string.Join(",", kept)}], ignored by Unstoppable [{string.Join(",", ignored)}]";
                    })),
            ],
            dice:
            [
                new("hit", 0, [6, 6, 1, 1, 1, 1], new RollKey(Weapon: "Surge Gun")),
                new("hit", 0, [3, 2, 6], new RollKey(Weapon: "Reliable Gun")),
            ]);
    }

    // ---- special-artillery-emplacement@3.6 ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Artillery, Immobile, Limited, Unstoppable over two rounds. Red unit 1 (two Tough(6) Artillery models) Holds in round 1 and Advances 1"
    /// in round 2; Red unit 2 (two Tough(6) Immobile models) Holds. Both fire a Limited Missile Pod and an Autocannon at Blue unit 1, 16.7" away.
    /// Blue (two units of two Tough(6) models on Defense 2+, a Lascannon and an Unstoppable Rail Rifle each) Holds and fires at Red unit 1; in
    /// round 2 Blue unit 2 activates only once Red unit 1 has moved, Blue unit 1 before that. Fixed: Red unit 1's first Hold hit roll [3,2]
    /// (+1: a raw 3 hits, a raw 2 misses), Blue unit 1's first Lascannon roll at it [5,4] (-1: a raw 5 hits, a raw 4 misses).
    /// </summary>
    static TestGame Artillery36()
    {
        const string name = "special-artillery-emplacement";
        static bool Moved(Projection p, string unit) => p.Units.Values.Single(u => u.Definition.Name == unit).Conditions.Contains("moved this round");
        var script = Plays((ask, p) =>
                (p.Scheduler.Round == 1 ? AskPicks.Activate(ask, "Red unit 1", "Hold") : AskPicks.Activate(ask, "Red unit 1", "Advance"))
                ?? AskPicks.Activate(ask, "Red unit 2", "Hold")
                ?? (p.Scheduler.Round == 1 || !Moved(p, "Red unit 1") ? AskPicks.Activate(ask, "Blue unit 1", "Hold") : null)
                ?? (p.Scheduler.Round == 1 || Moved(p, "Red unit 1") ? AskPicks.Activate(ask, "Blue unit 2", "Hold") : null)
                ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"),
            ("move", (ask, p) => ask.Legal is MoveRegion { ContactWith: null, Containment: null } region && Scripts36.UnitOf(p, region) == "Red unit 1"
                ? new Destinations([.. region.ModelIds.Select(id => Scripts36.MoveBy(p, id, 0, 1))]) : null),
            ("declare_targets", (ask, p) => AskPicks.ShooterOf(ask, p) is { } shooter ? AskPicks.AllAt(ask, p, shooter.StartsWith("Red", StringComparison.Ordinal) ? "Blue unit 1" : "Red unit 1") : null));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(2).Table(36, 24).Scripted(script)
                .Army("Red", 2, 2, modelRules: [new(1, "Artillery"), new(1, "Tough", 6), new(2, "Immobile"), new(2, "Tough", 6)],
                    weapons: [Gun("Missile Pod", 48, 2, Rule("Limited")), Gun("Autocannon", 48, 1)], unitStats: Stats(4, 4),
                    anchors: new Dictionary<int, Point2> { [1] = new(10, 21), [2] = new(20, 21) })
                .Army("Blue", 2, 2, modelRules: [new(1, "Tough", 6), new(2, "Tough", 6)], weapons: [Gun("Lascannon", 48, 1), Gun("Rail Rifle", 48, 1, Rule("Unstoppable"))],
                    unitStats: Stats(4, 2), anchors: new Dictionary<int, Point2> { [1] = new(10, 3), [2] = new(20, 3) })
                .Build(),
            ["Artillery", "Immobile", "Limited", "Unstoppable"],
            [
                Exact("3.6.0 p.13 Artillery: may use any action (\"When this model uses Hold actions\"); 3.6.0 p.14 Immobile: \"may only use Hold actions\"", ["Artillery", "Immobile"], run =>
                    Each(ActivateOptions(run).Where(o => o.Unit is "Red unit 1" or "Red unit 2" && !Shaken(run, o.Unit, o.At)), "an activation of the emplacement", o =>
                        o.Unit == "Red unit 2" ? (o.Actions.SequenceEqual(["Hold"]) ? null : $"Red unit 2 offered {string.Join(",", o.Actions)}")
                        : o.Actions.Contains("Advance") ? null : $"the Artillery was offered {string.Join(",", o.Actions)}", need: 2)
                    ?? (Moves(run).Any(m => m.Own && m.Unit == "Red unit 1" && m.Action == "Advance") ? null : "the Artillery never advanced")),
                Exact("3.6.0 p.13 Artillery: \"When this model uses Hold actions and shoots at enemies over 12” away, it gets +1 to hit rolls\" (at 4+ a raw 3 hits, a raw 2 misses; none after an Advance)", ["Artillery"], run =>
                {
                    var shots = Attacks(run).Where(a => a.Actor == "Red unit 1" && HitRoll(a) is not null).ToList();
                    var held = shots.Where(a => a.Action == "Hold").ToList();
                    if (shots.Where(a => a.Action == "Advance").Select(HitRoll).FirstOrDefault(r => r!.Modifiers.Any(m => m.Source == "Artillery")) is { } moved) return $"a shot after an Advance {Show(moved)}";
                    if (!shots.Any(a => a.Action == "Advance")) return "the Artillery never shot after an Advance";
                    return Each(held, "an Artillery shot from a Hold", a => Gap(run, a.Actor, a.Target, a.At) is > 12 && HitRoll(a)!.Modifiers.Count(m => m.Source == "Artillery" && m.Amount == 1) == 1 ? null
                        : $"{Gap(run, a.Actor, a.Target, a.At):0.0}\" away: {Show(HitRoll(a)!)}") ?? Judged(held.Select(HitRoll).OfType<RollRecord>(), "an Artillery shot from a Hold", 0, -1);
                }),
                Exact("3.6.0 p.13 Artillery: \"as long as they haven't moved during this round, enemy units shooting at them from over 12” away get -1 to hit rolls\" (at 4+ a raw 5 hits, a raw 4 misses)", ["Artillery"], run =>
                {
                    var q = LogQueries.Of(run);
                    var artillery = q.UnitNamed("Red unit 1");
                    var shots = Attacks(run).Where(a => a.Target == "Red unit 1" && a.Weapon == "Lascannon" && HitRoll(a) is not null)
                        .Select(a => (Attack: a, Moved: q.HasCondition(artillery, "moved this round", a.At))).ToList();
                    if (!shots.Any(s => s.Moved)) return "nobody shot at the Artillery after it moved";
                    return Each(shots, "a Lascannon shot at the Artillery", s =>
                    {
                        var penalty = HitRoll(s.Attack)!.Modifiers.Where(m => m.Source == "Artillery").ToList();
                        return Gap(run, s.Attack.Actor, s.Attack.Target, s.Attack.At) is > 12 && (s.Moved ? penalty.Count == 0 : penalty.Count == 1 && penalty[0].Amount == -1) ? null
                            : $"{(s.Moved ? "moved" : "unmoved")}, {Gap(run, s.Attack.Actor, s.Attack.Target, s.Attack.At):0.0}\" away: {Show(HitRoll(s.Attack)!)}";
                    }) ?? Judged(shots.Where(s => !s.Moved).Select(s => HitRoll(s.Attack)!), "a shot at the unmoved Artillery", 0, -1);
                }),
                Exact("3.6.0 p.15 Unstoppable: \"ignores all negative modifiers to this weapon which are not from this model's special rules\" (the Artillery's -1)", ["Unstoppable"], run =>
                {
                    var q = LogQueries.Of(run);
                    var artillery = q.UnitNamed("Red unit 1");
                    return Each(Attacks(run).Where(a => a.Weapon == "Rail Rifle" && a.Target == "Red unit 1" && HitRoll(a) is not null && !q.HasCondition(artillery, "moved this round", a.At)),
                        "a Rail Rifle shot at the unmoved Artillery", a => HitRoll(a)!.Modifiers.Any(m => m.Amount < 0) || HitRoll(a)!.Ignored?.Any(i => i.Source == "Artillery" && i.By == "Unstoppable") != true
                            ? $"Rail Rifle hit roll {Show(HitRoll(a)!)}" : null);
                }),
                Exact("3.6.0 p.14 Limited: \"Weapons with this rule may only be used once per game\"", ["Limited"], run =>
                {
                    var twice = Attacks(run).Where(a => a.Weapon == "Missile Pod").SelectMany(a => a.Seq.Context.WeaponIds).GroupBy(id => id).Count(g => g.Count() > 1);
                    var later = Steps(run).Count(s => s.Step.Applied?.Contains("Limited") == true);
                    return twice > 0 ? $"{twice} Missile Pods fired more than once" : later == 0 ? "the Missile Pod was never left out of a later shooting" : null;
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Immobile"] = log => OnlyOffered(log, "Red unit 2", "Hold") },
            dice:
            [
                new("hit", 0, [3, 2], new RollKey(Actor: "Red unit 1", Action: "Hold")),
                new("hit", 0, [5, 4], new RollKey(Actor: "Blue unit 1", Target: "Red unit 1", Weapon: "Lascannon")),
            ]);
    }

    // ---- special-indirect-on-hold@3.6 --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Indirect over two rounds, with no model moving. Red unit 1 (one mortar at (6, 22)) faces Blue unit 1 (one model at (6, 2)) across a
    /// 4"-tall sight-blocking wall (x 4–8, y 11.5–12.5); Red unit 2 (one mortar at (24, 22)) faces Blue unit 2 (one model at (24, 2)) past Blue unit
    /// 3 (one model at (24.3, 8)), which hides most of it. Both Red units Hold and fire in round 1 and Advance without moving in round 2; Blue
    /// holds and has no gun. Red unit 2's hit rolls are fixed to a 6 so its target rolls a defense die each round.
    /// </summary>
    static TestGame IndirectOnHold36()
    {
        const string name = "special-indirect-on-hold";
        var script = Plays((ask, p) => p.Scheduler.Round == 1
                ? AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Option(ask, c => c.Label.StartsWith("Blue", StringComparison.Ordinal) && c.Label.EndsWith(": Hold", StringComparison.Ordinal))
                : AskPicks.Activate(ask, "Red unit 1", "Advance") ?? AskPicks.Activate(ask, "Red unit 2", "Advance") ?? AskPicks.Option(ask, c => c.Label.StartsWith("Blue", StringComparison.Ordinal) && c.Label.EndsWith(": Hold", StringComparison.Ordinal)),
            ("move", (ask, p) => ask.Legal is MoveRegion region && Scripts36.UnitOf(p, region) is "Red unit 1" or "Red unit 2" ? new Destinations([]) : null),
            ("declare_targets", (ask, p) => AskPicks.ShooterOf(ask, p) switch { "Red unit 1" => AskPicks.AllAt(ask, p, "Blue unit 1"), "Red unit 2" => AskPicks.AllAt(ask, p, "Blue unit 2"), _ => null }));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(2).Table(36, 24).Scripted(script)
                .Army("Red", 2, 1, weapons: [Gun("Mortar", 36, 1, Rule("Indirect"))], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(6, 22), [2] = new(24, 22) })
                .Army("Blue", 3, 1, modelRules: [new(1, "Tough", 6), new(2, "Tough", 6), new(3, "Tough", 6)], weapons: [Blade("Blade", 1)], unitStats: Stats(4, 6),
                    anchors: new Dictionary<int, Point2> { [1] = new(6, 2), [2] = new(24, 2), [3] = new(24.3, 8) })
                .Terrain("wall", new Point3(6, 12, 0), volumes:
                    [new Volume(VolumeKind.Solid, Footprint.Rect(4, 1), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null)])
                .Build(),
            ["Indirect"],
            [
                Exact("3.6.0 p.14 Indirect: \"When using Hold actions and shooting, may target enemies that are not in line of sight as if in line of sight\" (after an Advance, the unit behind the wall is no target)", ["Indirect"], run =>
                {
                    var log = LogQueries.Of(run);
                    var hidden = log.UnitNamed("Blue unit 1");
                    var asks = log.AsksOf("declare_targets").Where(a => a.Frame is { } f && f.Context.ActorUnitId == log.UnitNamed("Red unit 1")).ToList();
                    var held = asks.Where(a => a.Frame!.Nearest("hold", "advance")?.Sequence == "hold").ToList();
                    var advanced = asks.Where(a => a.Frame!.Nearest("hold", "advance")?.Sequence == "advance").ToList();
                    if (!held.Any(a => a.Ask.Legal is Assignment t && t.Targets.Contains(hidden))) return "the mortar holding was not offered the unit behind the wall";
                    if (!Attacks(run).Any(a => a.Actor == "Red unit 1" && a.Target == "Blue unit 1" && a.Action == "Hold" && a.Determine?.Applied?.Contains("Indirect") == true))
                        return "the mortar holding did not shoot the unit behind the wall with Indirect";
                    var shotAfterAdvance = Attacks(run).Any(a => a.Actor == "Red unit 1" && a.Target == "Blue unit 1" && a.Action == "Advance");
                    return shotAfterAdvance || advanced.Any(a => a.Ask.Legal is Assignment t && t.Targets.Contains(hidden)) ? "the mortar was offered or shot the unit behind the wall after an Advance" : null;
                }),
                Exact("3.6.0 p.14 Indirect: \"When using Hold actions and shooting ... ignores cover from Sight Blockers\"; 3.6.0 p.12 Cover: \"behind sight blockers ... they get +1 to Defense rolls\"", ["Indirect"], run =>
                {
                    var blocks = Attacks(run).Where(a => a.Actor == "Red unit 2" && a.Target == "Blue unit 2" && a.Block is not null).SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense").Select(r => (a.Action, Roll: r))).ToList();
                    var held = blocks.Where(b => b.Action == "Hold").ToList();
                    var advanced = blocks.Where(b => b.Action == "Advance").ToList();
                    if (held.Count == 0 || advanced.Count == 0) return $"{held.Count} defense roll(s) after a Hold and {advanced.Count} after an Advance; both are needed";
                    if (held.FirstOrDefault(b => b.Roll.Cover is not { InCover: true, IgnoredBy: "Indirect" } || b.Roll.Modifiers.Any(m => m.Source == "cover")) is { Roll: not null } h)
                        return $"after a Hold: cover {h.Roll.Cover}, {Show(h.Roll)}";
                    return advanced.FirstOrDefault(b => b.Roll.Cover is not { InCover: true, IgnoredBy: null } || !b.Roll.Modifiers.Any(m => m.Source == "cover" && m.Amount == 1)) is { Roll: not null } v
                        ? $"after an Advance: cover {v.Roll.Cover}, {Show(v.Roll)}" : null;
                }),
            ],
            dice:
            [
                new("hit", 0, [6], new RollKey(Actor: "Red unit 2", Action: "Hold")),
                new("hit", 0, [6], new RollKey(Actor: "Red unit 2", Action: "Advance")),
            ]);
    }

    // ---- special-assault-and-cumbersome@3.6 --------------------------------------------------------------------------------------------

    /// <summary>
    /// Assault, Cumbersome. Blue unit 1 (two Tough(6) models from (10, 6), blades) holds. Red unit 1 (one Tough(6) model armed only with a 12"
    /// Assault Pistol) charges it, landing above its first model; Red unit 2 (the same, at (14, 16)) Holds and shoots it. Red units 3 and 4 (one
    /// Cumbersome model each, 24" rifles, at (24, 18) and (30, 18)): unit 3 Advances 2" straight back, unit 4 Holds.
    /// </summary>
    static TestGame AssaultAndCumbersome36()
    {
        const string name = "special-assault-and-cumbersome";
        var script = Plays((ask, _) => AskPicks.Activate(ask, "Red unit 1", "Charge") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Activate(ask, "Red unit 3", "Advance")
                ?? AskPicks.Activate(ask, "Red unit 4", "Hold") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"),
            ("charge_target", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 1")),
            ("move", (ask, p) => Scripts36.Charging(new Dictionary<string, Point2> { ["Red unit 1"] = new(10, 6 + Scripts36.Touch) })(ask, p)
                ?? (ask.Legal is MoveRegion { ContactWith: null, Containment: null } region && Scripts36.UnitOf(p, region) == "Red unit 3"
                    ? new Destinations([.. region.ModelIds.Select(id => Scripts36.MoveBy(p, id, 0, 2))]) : null)),
            ("declare_targets", (ask, p) => AskPicks.AllAt(ask, p, "Blue unit 1")));
        var pistol = Gun("Assault Pistol", 12, 1, Rule("Assault"));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(1).Table(36, 24).Scripted(script)
                .Army("Red", 4, 1, modelRules: [new(1, "Tough", 6), new(2, "Tough", 6), new(3, "Cumbersome"), new(4, "Cumbersome")], weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 4),
                    models: [new ScenarioModel(1, 1, Weapons: [pistol]), new ScenarioModel(2, 1, Weapons: [pistol])],
                    anchors: new Dictionary<int, Point2> { [1] = new(10, 18), [2] = new(14, 16), [3] = new(24, 18), [4] = new(30, 18) })
                .Army("Blue", 1, 2, modelRules: [new(1, "Tough", 6)], weapons: [Blade("Blade", 1)], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(10, 6) })
                .Build(),
            ["Assault", "Cumbersome"],
            [
                Exact("3.6.0 p.13 Assault: \"Counts as both a ranged weapon and a melee weapon\" (the pistol shoots from a Hold and strikes on the charge)", ["Assault"], run =>
                {
                    var log = LogQueries.Of(run);
                    var pistols = log.FramesOf("attack-sequence").Where(f => f.Context.WeaponIds.Count > 0 && log.WeaponName(f.Context.WeaponIds[0]) == "Assault Pistol").ToList();
                    var shot = pistols.Any(f => f.Parent?.Sequence == "shooting" && log.UnitName(f.Context.ActorUnitId!.Value) == "Red unit 2");
                    var struck = pistols.Any(f => f.Parent?.Sequence == "melee" && log.UnitName(f.Context.ActorUnitId!.Value) == "Red unit 1"
                        && f.Steps.Any(s => s.Name == "determine-attacks" && s.Value("attacks") == 1));
                    return shot && struck ? null : $"the Assault Pistol {(shot ? "shot" : "never shot")} and {(struck ? "struck" : "never struck")}";
                }),
                Exact("3.6.0 p.14 Cumbersome: \"May only use Hold or Advance actions\"", ["Cumbersome"], run =>
                    Each(ActivateOptions(run).Where(o => o.Unit is "Red unit 3" or "Red unit 4"), "an activation offer to a Cumbersome unit", o =>
                        o.Actions.SequenceEqual(["Hold", "Advance"]) ? null : $"{o.Unit} offered {string.Join(",", o.Actions)}", need: 2)),
                Exact("3.6.0 p.14 Cumbersome: \"but can’t shoot when moving\" (the Advance moved 2\": no shot; the Hold shot)", ["Cumbersome"], run =>
                {
                    var log = LogQueries.Of(run);
                    var (moving, holding) = (log.UnitNamed("Red unit 3"), log.UnitNamed("Red unit 4"));
                    var advance = log.FramesOf("advance").FirstOrDefault(f => f.Context.ActorUnitId == moving);
                    if (advance is null) return "the Cumbersome unit never advanced";
                    if (!log.Steps.Any(s => s.Frame == advance && s.Name == "move" && s.Step.Changes.OfType<ModelsMoved>().Any())) return "the Advance moved nothing";
                    if (log.Descendants(advance).Any(f => f.Sequence == "shooting") || log.AsksOf("shoot_or_not").Any(a => a.Frame == advance)) return "the Cumbersome unit was offered a shot after moving";
                    return log.FramesOf("shooting").Any(f => f.Context.ActorUnitId == holding) ? null : "the holding Cumbersome unit never shot";
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
            {
                // Neither rule completes a step of its own. The charger's only weapon is the pistol, so its attack inside the melee is Assault's
                // evidence; the narrowed activation menu is Cumbersome's.
                ["Assault"] = ChargerStruck,
                ["Cumbersome"] = log => Offers(log).Where(o => o.Unit is "Red unit 3" or "Red unit 4").ToList() is { Count: > 0 } offers && offers.All(o => o.Actions.SequenceEqual(["Hold", "Advance"])),
            });
    }

    /// <summary>Whether a melee frame started an attack-sequence for its own actor, the charger.</summary>
    static bool ChargerStruck(IReadOnlyList<GameEvent> log)
    {
        var stack = new List<SequenceStarted>();
        foreach (var e in log)
            switch (e.Payload)
            {
                case SequenceStarted s:
                    if (s.Sequence == "attack-sequence" && stack.Count > 0 && stack[^1] is { Sequence: "melee" } melee && melee.Context.ActorUnitId == s.Context.ActorUnitId) return true;
                    stack.Add(s);
                    break;
                case SequenceFinished f: stack.RemoveAll(x => x.FrameId == f.FrameId); break;
                case SequenceAbandoned a when stack.FindIndex(x => x.FrameId == a.FrameId) is var i and >= 0: stack.RemoveRange(i, stack.Count - i); break;
            }
        return false;
    }
}
