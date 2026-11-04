using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's melee, movement and command games (pp.13–15).</summary>
static partial class SpecialGames
{
    /// <summary>
    /// Furious, Impact, Fear, Thrust, Counter: lancers who charge home against a spear wall that strikes first. Red wins the roll-off (fixed); its units
    /// deploy 4.5" from Blue's and charge whenever they may, Blue's Hold. Five lancers a unit outlast the three spears' strikes and Tough(6) spearmen
    /// outlast the Impact hits, so each charge rolls the
    /// Lance dice of a model in reach: Red 1's first two are fixed to 3 and 2 (with Thrust's +1, at the threshold and one below it), Red 2's first to a 6.
    /// In round 2 everyone Holds but Blue 1, which charges the lancers beside it, so Red also fights a melee as the defender (Fear has no charging
    /// condition); Red charges again in rounds 3 and 4.
    /// </summary>
    static TestGame Charge()
    {
        const string name = "special-charge-into-spears";
        var script = Plays((ask, p) => p.Scheduler.Round != 2
            ? AskPicks.Activate(ask, "Red unit 1", "Charge") ?? AskPicks.Activate(ask, "Red unit 2", "Charge")
                ?? AskPicks.Option(ask, c => c.Label.StartsWith("Blue ", StringComparison.Ordinal) && c.Label.EndsWith(": Hold", StringComparison.Ordinal))
            : AskPicks.Activate(ask, "Blue unit 1", "Charge") ?? AskPicks.Option(ask, c => c.Label.EndsWith(": Hold", StringComparison.Ordinal)));
        return Game(name, new ScenarioBuilder(name, 3).RoundCap(4).Table(24, 30).Scripted(script)
                .Army("Red", 2, 5, modelRules: [new(1, "Furious"), new(1, "Impact", 2), new(1, "Fear", 2), new(2, "Furious"), new(2, "Impact", 2), new(2, "Fear", 2)],
                    weapons: [Blade("Lance", 2, Rule("Thrust"))], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(13, 6), [2] = new(13, 20) })
                .Army("Blue", 2, 3, modelRules: [new(1, "Tough", 6), new(2, "Tough", 6)], weapons: [Blade("Spear", 1, Rule("Counter"))], unitStats: Stats(4, 4),
                    anchors: new Dictionary<int, Point2> { [1] = new(5, 6), [2] = new(5, 20) })
                .Seeking(NearestEnemy)
                .Build(),
            ["Furious", "Impact", "Fear", "Thrust", "Counter"],
            [
                Exact("p.14 Thrust: \"When charging, gets +1 to hit rolls and AP(+1) in melee\" (at 4+ a raw 3 hits, a raw 2 misses; p.9: a fatigued unit hits only on unmodified 6s)", ["Thrust"], run =>
                {
                    var charges = Attacks(run).Where(a => a.Weapon == "Lance" && a.Action == "Charge" && HitRoll(a) is not null).ToList();
                    return Each(charges, "a Lance charge", a =>
                    {
                        var hit = Fatigued(run, a) || HitRoll(a)!.Modifiers.Count(m => m.Source == "Thrust" && m.Amount == 1) == 1;
                        var ap = a.Block is null || a.Block.Rolls.Where(r => r.Roll == "defense").All(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -1));
                        return hit && ap ? null : $"hit {Show(HitRoll(a)!)}, defense {string.Join(" ", a.Block?.Rolls.Select(Show) ?? [])}";
                    }) ?? Judged(charges.Where(a => !Fatigued(run, a)).Select(a => HitRoll(a)!), "a Lance hit roll on the charge", 0, -1)
                        ?? Judged(charges.Where(a => a.Block is not null).SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense")), "a defense roll against a Lance charge");
                }),
                Exact("p.14 Furious: \"When charging, unmodified results of 6 to hit in melee deal 1 extra hit\"", ["Furious"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Lance" && a.Action == "Charge" && Sixes(a.Hit) > 0), "a Lance 6 on the charge", a =>
                    {
                        var dice = a.Block?.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count) ?? 0;
                        return Value(a.Hit!, "extra_hits") == Sixes(a.Hit) && dice == Hits(a.Hit) + Sixes(a.Hit) ? null
                            : $"{Sixes(a.Hit)} sixes, extra_hits {Value(a.Hit!, "extra_hits")}, {dice} defense dice for {Hits(a.Hit)} hits";
                    })),
                Exact("p.14 Impact(X): \"Roll X dice when attacking after charging, unless fatigued. For each 2+ the target takes one hit\"; p.13 Counter: \"-1 total Impact rolls per model with Counter\"", ["Impact", "Counter"], run =>
                    Each(Melees(run).Where(m => m.Charger.StartsWith("Red") && m.Impact is not null && !Fatigued(run, m.Seq)), "an Impact roll", m =>
                    {
                        var roll = m.Impact!.Step.Rolls.SingleOrDefault(r => r.Roll == "Impact");
                        var (chargers, counters) = (Living(run, m.Charger, m.Impact.Seq), Living(run, m.Defender, m.Impact.Seq));
                        var expected = Math.Max(0, 2 * chargers - counters);
                        var dice = roll?.Dice.Count ?? 0;
                        var hits = roll?.Dice.Count(d => d.Raw >= 2) ?? 0;
                        return dice == expected && Value(m.Impact.Step, "hits") == hits && (roll is null || roll.Threshold == 2) ? null
                            : $"{chargers} Impact(2) models against {counters} Counter models: {dice} dice (expected {expected}), hits {Value(m.Impact.Step, "hits")}";
                    })),
                Exact("p.13 Counter: \"Strikes first with this weapon when charged\"", ["Counter"], run =>
                    Each(Melees(run).Where(m => m.Charger.StartsWith("Red") && m.Attacks.Count > 0), "a charge into the spears", m =>
                        m.Attacks[0].Actor == m.Defender ? null : $"{m.Attacks[0].Actor} struck first")),
                Exact("p.13 Fear(X): \"counts as having dealt +X wounds when checking who won melee\" (Fear(2))", ["Fear"], run =>
                    Each(Melees(run).Select(m => (m, red: m.Charger.StartsWith("Red") ? m.Charger : m.Defender, blue: m.Charger.StartsWith("Red") ? m.Defender : m.Charger))
                            .Where(x => x.m.Compare is not null && x.m.Attacks.Any(a => a.Actor == x.red)), "a melee the lancers fought in", x =>
                    {
                        var (m, red, blue) = x;
                        var dealt = WoundsTaken(run, blue, m.Seq, m.Compare!.Seq);
                        var fear = Living(run, red, m.Compare.Seq);
                        var key = red == m.Charger ? "charger_wounds" : "defender_wounds";
                        return Value(m.Compare.Step, key) == dealt + 2 * fear ? null
                            : $"{red} dealt {dealt} with {fear} Fear(2) models: {key} {Value(m.Compare.Step, key)}, expected {dealt + 2 * fear}";
                    })),
            ],
            dice: [.. ChargeFixes]);
    }

    /// <summary>Red 1's first Lance hit roll on a charge, not fatigued: a raw 3 and a raw 2; Red 2's first: a raw 6.</summary>
    static DiceFix[] ChargeFixes => [new("roll-off", 0, [6, 1]), new("hit", 0, [3, 2], new RollKey(Actor: "Red unit 1", Weapon: "Lance", Action: "Charge")),
        new("hit", 0, [6], new RollKey(Actor: "Red unit 2", Weapon: "Lance", Action: "Charge"))];

    /// <summary>
    /// Fast, Slow, Strider, Flying: four squads crossing a swamp that is difficult and dangerous. Each squad deploys on its anchor across the swamp from an
    /// enemy and Rushes at it in round 1 (the script), so all four move into the swamp before anyone shoots.
    /// </summary>
    static TestGame Swift()
    {
        const string name = "special-swift-and-slow";
        var script = Plays((ask, p) => p.Scheduler.Round == 1
            ? new[] { "Red unit 1", "Red unit 2", "Blue unit 1", "Blue unit 2" }.Select(u => AskPicks.Activate(ask, u, "Rush")).FirstOrDefault(a => a is not null) : null);
        return Game(name, new ScenarioBuilder(name, 5).RoundCap(4).Table(36, 30).Scripted(script).Seeking(NearestEnemy)
                .Army("Red", 2, 2, modelRules: [new(1, "Fast"), new(2, "Slow")], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(6, 25), [2] = new(26, 25) })
                .Army("Blue", 2, 2, modelRules: [new(1, "Strider"), new(2, "Flying")], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(6, 5), [2] = new(26, 5) })
                .Terrain("swamp", new Point3(18, 15, 0), volumes:
                    [new Volume(VolumeKind.Area, Footprint.Rect(36, 14), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult | TerrainDeclarations.Dangerous, null)])
                .Build(),
            ["Fast", "Slow", "Strider", "Flying"],
            [
                Exact("p.13 Fast: \"move +2\" when using Advance and +4\" when using Rush/Charge\"", ["Fast"], run =>
                    Each(Moves(run).Where(m => m.Own && m.Unit == "Red unit 1" && m.Action is "Advance" or "Rush" or "Charge"), "a Fast move", m =>
                        m.Region.Allowance == (m.Action == "Advance" ? 8 : 16) ? null : $"{m.Action}: allowance {m.Region.Allowance}")),   // p.7: Advance 6", Rush and Charge 12"
                Exact("p.14 Slow: \"move -2\" when using Advance, and -4\" when using Rush/Charge\"", ["Slow"], run =>
                    Each(Moves(run).Where(m => m.Own && m.Unit == "Red unit 2" && m.Action is "Advance" or "Rush" or "Charge"), "a Slow move", m =>
                        m.Region.Allowance == (m.Action == "Advance" ? 4 : 8) ? null : $"{m.Action}: allowance {m.Region.Allowance}")),
                Exact("p.14 Strider: \"May ignore the effects of difficult terrain when moving\"; p.13 Flying: \"ignore terrain effects whilst moving\"; p.11: others are capped at 6\"", ["Strider", "Flying"], run =>
                {
                    var into = Moves(run).Where(m => m.Region.DifficultVolumeIds.Count > 0).ToList();
                    return Each(into, "a move into the swamp", m => (m.Unit is "Blue unit 1" or "Blue unit 2") == (m.Region.DifficultCap is null) && (m.Region.DifficultCap ?? 6) == 6 ? null
                            : $"{m.Unit} {m.Action}: difficult cap {m.Region.DifficultCap?.ToString() ?? "none"}")
                        ?? new[] { "Blue unit 1", "Blue unit 2", "Red unit 1" }.Select(u => into.Any(m => m.Unit == u) ? null : $"{u} never moved into the swamp").FirstOrDefault(f => f is not null);
                }),
                Exact("p.13 Flying: \"ignore terrain effects whilst moving\" (no dangerous test for a move)", ["Flying"], run =>
                {
                    var tested = Steps(run).Where(s => s.Step.Step == "move" && s.Frame?.Context.ActorUnitId is { } u && UnitName(run, u) == "Blue unit 2"
                        && Value(s.Step, "dangerous_dice") > 0).ToList();
                    var skipped = Steps(run).Count(s => s.Step.Applied?.Contains("Flying") == true);
                    return tested.Count > 0 ? $"the Flying squad's move rolled {Value(tested[0].Step, "dangerous_dice")} dangerous dice" : skipped == 0 ? "Flying never skipped a terrain effect" : null;
                }),
            ]);
    }

    /// <summary>
    /// p.36 Banner of Eternal Flame (Relic Wargear): Blue unit 1's Hero carries it for its whole unit; both sides cross a swamp that is difficult and
    /// dangerous. Blue unit 2, without it, is held to the 6" cap.
    /// </summary>
    static TestGame EternalFlame()
    {
        const string name = "special-banner-of-eternal-flame";
        return Game(name, new ScenarioBuilder(name, 5).RoundCap(4).Table(36, 30).Scripted(ShootAndStrike).Seeking(NearestEnemy)
                .Army("Red", 2, 2, unitStats: Stats(4, 4))
                .Army("Blue", 2, 3, unitStats: Stats(4, 4),
                    models: [new ScenarioModel(1, 1, [new(0, "Hero"), new(0, "Banner of Eternal Flame")])])
                .Terrain("swamp", new Point3(18, 15, 0), volumes:
                    [new Volume(VolumeKind.Area, Footprint.Rect(36, 14), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult | TerrainDeclarations.Dangerous, null)])
                .Build(),
            ["Banner of Eternal Flame"],
            [
                Exact("p.36 Banner of Eternal Flame: \"The Hero and its unit may ignore all terrain effects\"; p.11: others are capped at 6\" in difficult terrain", ["Banner of Eternal Flame"], run =>
                {
                    var into = Moves(run).Where(m => m.Region.DifficultVolumeIds.Count > 0 && m.Unit is "Blue unit 1" or "Blue unit 2").ToList();
                    return Each(into, "a Blue move into the swamp", m => (m.Unit == "Blue unit 1") == (m.Region.DifficultCap is null) ? null
                            : $"{m.Unit} {m.Action}: difficult cap {m.Region.DifficultCap?.ToString() ?? "none"}")
                        ?? new[] { "Blue unit 1", "Blue unit 2" }.Select(u => into.Any(m => m.Unit == u) ? null : $"{u} never moved into the swamp").FirstOrDefault(f => f is not null);
                }),
                Exact("p.36 Banner of Eternal Flame: \"may ignore all terrain effects\"; p.11: \"If a model moves in or through dangerous terrain, or is activated in it, then it must take a dangerous terrain test\"", ["Banner of Eternal Flame"], run =>
                {
                    var owed = Steps(run).Where(s => s.Step.Step is "move" or "activation-terrain" && s.Frame?.Context.ActorUnitId is not null && Value(s.Step, "dangerous_dice") > 0)
                        .Select(s => UnitName(run, s.Frame!.Context.ActorUnitId!.Value)).ToList();
                    var skipped = Steps(run).Count(s => s.Step.Applied?.Contains("Banner of Eternal Flame") == true);
                    return owed.Contains("Blue unit 1") ? "the banner's unit owed a dangerous terrain test"
                        : skipped == 0 ? "the banner never skipped a terrain effect" : null;
                }),
            ]);
    }

    /// <summary>
    /// Caster, Transport, Fearless: casters who ride in a Transport, and a Fearless squad on each side, in a firefight. Red deploys in x 12–24,
    /// Blue in x 0–12 (frontline on a 24" by 30" table), every unit within its rifles' 24" of the enemy. Red activates the Transport first, Holding
    /// and firing at Blue unit 2 (Fearless), then its casters (Red unit 1: Hold and cast in round 1, Advance into the Transport in round 2, Advance
    /// out of it in round 3, Hold and cast in round 4), then Red unit 3 (Fearless). Blue activates Blue unit 1 first, Holding and firing at Red
    /// unit 3 (the Transport once it is gone), and Blue unit 2 Holds and fires at the Transport, so the casters are never shot at. Each side's
    /// opening volley at the Fearless squad has its dice fixed (CommandFixes) to kill two of its three models, so each takes a morale test.
    /// </summary>
    static TestGame Command()
    {
        const string name = "special-command-squad";
        var script = AnswerScript.Build();
        for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
        script.Match(AskQuestions.Activate, (ask, p) =>
            AskPicks.Activate(ask, "Red unit 2", "Hold")
            ?? AskPicks.Activate(ask, "Red unit 1", p.Scheduler.Round is 2 or 3 ? "Advance" : "Hold")
            ?? AskPicks.Activate(ask, "Red unit 3", "Hold")
            ?? AskPicks.Activate(ask, "Blue unit 1", "Hold") ?? AskPicks.Activate(ask, "Blue unit 2", "Hold"));
        script.Match("react", (ask, p) => ask.Prompt == "Before attacking" && ask.Legal is Options o && o.Choices.Any(c => c.Id == "cast") ? new Chosen("cast") : null);
        script.Match("move", (ask, p) => ask.Legal is MoveRegion { Containment.Enterable: [var transport, ..] } m && m.ModelIds.All(id => AskPicks.UnitNameOf(p, id) == "Red unit 1")
            ? new Destinations([.. m.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(transport)))]) : null);
        script.Match("declare_targets", (ask, p) => AskPicks.ShooterOf(ask, p) switch
        {
            "Red unit 2" => AskPicks.AllAt(ask, p, "Blue unit 2"),
            "Blue unit 1" => AskPicks.AllAt(ask, p, "Red unit 3") ?? AskPicks.AllAt(ask, p, "Red unit 2"),
            "Blue unit 2" => AskPicks.AllAt(ask, p, "Red unit 2"),
            _ => null,
        });
        return Game(name, new ScenarioBuilder(name, 115).RoundCap(4).Table(24, 30).Scripted(script.Build())
                .Army("Red", 3, 3, modelRules: [new(1, "Caster", 2), new(2, "Transport", 6), new(2, "Tough", 6), new(3, "Fearless")],
                    weapons: [Gun("Rifle", 24, 1), Blade("Bayonet", 1)], unitStats: Stats(5, 5), armyBook: "z65fgu0l29i4lnlu",
                    anchors: new Dictionary<int, Point2> { [1] = new(14, 8), [2] = new(14, 11), [3] = new(14, 22) })
                .Army("Blue", 2, 3, modelRules: [new(2, "Fearless")], weapons: [Gun("Rifle", 24, 2, Rule("AP", 1)), Blade("Bayonet", 1)], unitStats: Stats(4, 4),
                    anchors: new Dictionary<int, Point2> { [1] = new(2, 22), [2] = new(2, 8) })
                .Seeking(ToTransportElseEnemy)
                .Build(),
            ["Caster", "Transport", "Fearless"],
            [
                Exact("p.13 Caster(X): \"Gets X spell tokens at the start of each round, but can't hold more than 6\" (Caster(2)); \"on 4+ resolve\"", ["Caster"], run =>
                {
                    var held = new Dictionary<Guid, int>();
                    var failures = new List<string>();
                    var rounds = 0;
                    foreach (var e in Events(run))
                    {
                        if (e.Payload is RoundStarted { Round: > 0 } r)
                        {
                            rounds++;
                            foreach (var t in r.Changes.OfType<SpellTokensSet>())
                                if (t.Tokens != Math.Min(held.GetValueOrDefault(t.ModelId) + 2, 6))
                                    failures.Add($"round {r.Round}: {ModelName(run, t.ModelId)} {held.GetValueOrDefault(t.ModelId)} -> {t.Tokens}");
                        }
                        foreach (var t in ChangesOf(e.Payload).OfType<SpellTokensSet>()) held[t.ModelId] = t.Tokens;
                    }
                    var casts = Payloads(run).OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == "casting").ToList();
                    if (casts.Any(c => c.Threshold != 4)) failures.Add($"casting rolls {string.Join(" ", casts.Select(Show))}");
                    if (!Payloads(run).OfType<RoundStarted>().Any(r => r.Round > 0 && r.Changes.OfType<SpellTokensSet>().Any())) failures.Add("no round start gave tokens");
                    return rounds == 0 || casts.Count == 0 ? "no round started with a cast" : failures.Count > 0 ? string.Join("; ", failures) : Judged(casts, "a casting roll");
                }),
                Exact("p.15 Transport(X): \"units may enter/exit by using any move action, but must stay fully within 6\" of it when exiting\"", ["Transport"], run =>
                {
                    var embarks = Steps(run).Select(s => (s.Seq, into: s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(m => m.To is Inside).ToList()))
                        .Where(x => x.into.Count > 0).ToList();
                    var exits = Payloads(run).OfType<AskOpened>().Select(a => a.Ask.Legal).OfType<MoveRegion>().Where(m => m.Containment?.ExitingId is not null).ToList();
                    if (embarks.Count == 0) return "nobody embarked";
                    foreach (var (seq, into) in embarks)
                        if (into.Select(m => UnitName(run, run.Final.Models[m.ModelId].UnitId)).Distinct().ToList() is not [var unit] || into.Count != Living(run, unit, seq))
                            return $"an embark moved {into.Count} models, not one whole unit";
                    if (exits.Count == 0) return "nobody was offered an exit";
                    if (exits.FirstOrDefault(m => m.Containment!.ExitWithin != 6) is { } wide) return $"exit distance {wide.Containment!.ExitWithin}";
                    foreach (var (seq, step, _) in Steps(run))
                        foreach (var m in step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(m => m.From is Inside && m.To is OnTable))
                        {
                            var container = ((Inside)m.From).ContainerId;
                            if (Placed(run, seq).GetValueOrDefault(container) is not { Radius: > 0 } transport) continue;
                            var at = ((OnTable)m.To).Placement.Position.Horizontal;
                            var reach = at.DistanceTo(transport.At) - transport.Radius + Radius(run, m.ModelId);   // the model's far edge from the transport's base
                            if (reach > 6.01) return $"{ModelName(run, m.ModelId)} placed {reach:0.00}\" from the transport";
                        }
                    return null;
                }),
                Exact("p.13 Fearless: \"fails a morale test, roll one die. On a 4+ it counts as passed\"; p.10: \"If the test is failed, the unit is Shaken\"", ["Fearless"], run =>
                {
                    // One morale-test frame's three steps: the test die, the override (Fearless) dice, and the consequence that read both.
                    var failedTests = Steps(run).Where(s => s.Frame?.Sequence == "morale-test" && s.Frame.Context.ActorUnitId is { } u && UnitName(run, u) is "Red unit 3" or "Blue unit 2")
                        .GroupBy(s => s.Step.FrameId).Select(g => g.Select(x => x.Step).ToList())
                        .Where(f => f.Any(s => s.Step == "test" && s.Rolls.Any(r => r.Roll == "morale" && !r.Dice[0].Success)))
                        .Select(f => (Rolls: f.SelectMany(s => s.Rolls).ToList(), Consequence: f.FirstOrDefault(s => s.Step == "consequence"))).ToList();
                    var outcomes = failedTests.Select(t => t.Rolls.FirstOrDefault(r => r.Roll == "Fearless")?.Dice[0].Raw >= 4).Distinct().Count();
                    return Each(failedTests, "a failed morale test by a Fearless squad", t =>
                    {
                        var fearless = t.Rolls.SingleOrDefault(r => r.Roll == "Fearless");
                        if (fearless is null || fearless.Threshold != 4 || fearless.Dice.Count != 1) return $"no one-die 4+ Fearless roll: {string.Join(" ", t.Rolls.Select(Show))}";
                        if (t.Consequence is not { } s) return "no consequence step";
                        var passed = fearless.Dice[0].Raw >= 4;
                        var shaken = s.Changes.OfType<ConditionSet>().Any(c => c.Condition == "shaken" && c.Present) || Value(s, "routed") == 1;
                        return Value(s, "passed") == (passed ? 1 : 0) && shaken != passed ? null : $"Fearless {Show(fearless)}: passed={Value(s, "passed")}, shaken or routed {shaken}";
                    }) ?? (outcomes < 2 ? "the Fearless die never gave both a 4+ and a fail" : null);
                }),
            ],
            dice: CommandFixes);
    }

    /// <summary>
    /// Blue unit 1's first volley at Red unit 3 (six Rifle dice) and the Transport's first at Blue unit 2 (three) each hit twice and are blocked
    /// by neither defense die, killing two of three models; each Fearless squad's first morale test rolls a 1, and its Fearless die a 4 for Red
    /// unit 3 (counts as passed) and a 2 for Blue unit 2 (Shaken).
    /// </summary>
    static DiceFix[] CommandFixes =>
    [
        new("hit", 0, [6, 6, 1, 1, 1, 1], new RollKey(Actor: "Blue unit 1", Target: "Red unit 3")),
        new("defense", 0, [1, 1], new RollKey(Actor: "Blue unit 1", Target: "Red unit 3")),
        new("morale", 0, [1], new RollKey(Actor: "Red unit 3")),
        new("Fearless", 0, [4], new RollKey(Actor: "Red unit 3")),
        new("hit", 0, [6, 6, 1], new RollKey(Actor: "Red unit 2", Target: "Blue unit 2")),
        new("defense", 0, [1, 1], new RollKey(Actor: "Red unit 2", Target: "Blue unit 2")),
        new("morale", 0, [1], new RollKey(Actor: "Blue unit 2")),
        new("Fearless", 0, [2], new RollKey(Actor: "Blue unit 2")),
    ];
}
