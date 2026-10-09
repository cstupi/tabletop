using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial IEnumerable<TestGame> Special() => SpecialGames.All();
}

/// <summary>
/// The special group: the 33 core special rules (grimdark-future-core.properties.json, rulebook pp.13–15), three to five a game, chosen to
/// collide. Each exact check quotes its rule and states the rule's number as a literal; it reads the effective log and the final projection
/// and never computes a rules outcome. Every check also requires the case it judges to have happened, so a seed that never brings it into
/// play fails rather than passing empty.
/// </summary>
static class SpecialGames
{
    public static IEnumerable<TestGame> All() =>
    [
        Regrowth(), BlastDeadly(), Gunline(), Artillery(), Charge(), Swift(), Command(), Deployment(),
    ];

    // ---- Games -------------------------------------------------------------------------------------------------------------------------

    /// <summary>AP, Bane, Rending, Regeneration: two Regeneration squads, one shooting Bane and Rending rifles (both ignore Regeneration).</summary>
    static TestGame Regrowth()
    {
        const string name = "special-regrowth-under-rending-fire";
        return Game(name, new ScenarioBuilder(name, 23).RoundCap(4).Table(24, 30).Scripted(ShootAndStrike)
                .Army("Red", 1, 5, modelRules: [new(1, "Regeneration")],
                    weapons: [Gun("Bane Rifle", 24, 1, Rule("Bane")), Gun("Rending Rifle", 24, 1, Rule("Rending"))], unitStats: Stats(4, 4))
                .Army("Blue", 1, 5, modelRules: [new(1, "Regeneration")], weapons: [Gun("AP Rifle", 24, 1, Rule("AP", 2))], unitStats: Stats(4, 4))
                .Build(),
            ["AP", "Bane", "Rending", "Regeneration"],
            [
                Exact("p.13 AP(X): \"Targets get -X to Defense rolls\" (AP(2): -2)", ["AP"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "AP Rifle" && a.Block is not null), "an AP Rifle hit to block", a =>
                        a.Block!.Rolls.Where(r => r.Roll == "defense").Select(r =>
                            r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -2) && r.Dice.All(d => d.Modified == d.Raw - 2)
                                ? null : $"defense roll {Show(r)} lacks AP -2").FirstOrDefault(f => f is not null))),
                Exact("p.14 Rending: \"on unmodified results of 6 to hit, those hits get AP(+4)\"", ["Rending"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Rending Rifle" && Sixes(a.Hit) > 0 && a.Block is not null), "a Rending Rifle 6 to hit", a =>
                    {
                        var ap4 = a.Block!.Rolls.Where(r => r.Roll == "defense" && r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4)).Sum(r => r.Dice.Count);
                        return ap4 == Sixes(a.Hit) ? null : $"{Sixes(a.Hit)} sixes to hit but {ap4} defense dice at AP -4";
                    })),
                Exact("p.13 Bane: \"the target must re-roll unmodified Defense results of 6\"", ["Bane"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Bane Rifle" && a.Block is not null)
                            .SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense" && r.Dice.Any(d => d.Raw == 6))), "a Bane hit blocked on a 6", roll =>
                    {
                        var sixes = roll.Dice.Select((d, i) => (d, i)).Where(x => x.d.Raw == 6).Select(x => x.i).ToList();
                        var rerolled = Payloads(run).OfType<DiceRerolled>().Where(x => x.RollId == roll.Id).SelectMany(x => x.Replaced.Select(r => r.Index)).ToList();
                        return sixes.Order().SequenceEqual(rerolled.Order()) ? null : $"defense {Show(roll)}: sixes at [{string.Join(",", sixes)}], re-rolled [{string.Join(",", rerolled)}]";
                    })),
                Exact("p.14 Regeneration: \"roll one die for each [wound]. On a 5+ it is ignored\"", ["Regeneration"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "AP Rifle" && a.Assigns.Count > 0).Select(a => a.Assigns[0]).Where(s => s.Rolls.Any(r => r.Roll == "Regeneration")), "a Regeneration roll", s =>
                    {
                        var regen = s.Rolls.Where(r => r.Roll == "Regeneration").ToList();
                        if (regen.Any(r => r.Threshold != 5 || r.Dice.Count != 1)) return $"Regeneration rolls {string.Join(" ", regen.Select(Show))} are not one die at 5+";
                        var ignored = regen.Count(r => r.Dice[0].Success);
                        var dealt = Value(s, "wounds_dealt") + Value(s, "wounds_left");
                        return dealt == regen.Count - ignored ? null : $"{regen.Count} wounds, {ignored} ignored on a 5+, but {dealt} dealt";
                    })),
                Exact("p.13 Bane, p.14 Rending: \"Ignores Regeneration\"", ["Bane", "Rending", "Regeneration"], run =>
                    Each(Attacks(run).Where(a => a.Weapon is "Bane Rifle" or "Rending Rifle").SelectMany(a => a.Assigns.Select(s => (a.Weapon, s)))
                            .Where(x => Value(x.s, "wounds_dealt") > 0), "a Bane or Rending wound", x =>
                        x.s.Rolls.Any(r => r.Roll == "Regeneration") ? $"{x.Weapon} wounds rolled Regeneration" :
                        x.s.Applied?.Contains(x.Weapon.Split(' ')[0]) == true ? null : $"{x.Weapon}'s assign step does not list {x.Weapon.Split(' ')[0]}")),
            ]);
    }

    /// <summary>Blast, Deadly, Tough, Takedown: a heavy-weapons team into a squad of Tough(3) models.</summary>
    static TestGame BlastDeadly()
    {
        const string name = "special-blast-deadly-takedown";
        return Game(name, new ScenarioBuilder(name, 2).RoundCap(4).Table(24, 30).Scripted(ShootAndStrike)
                .Army("Red", 1, 2, modelRules: [new(1, "Tough", 3)],
                    weapons: [Gun("Frag Launcher", 24, 1, Rule("Blast", 3)), Gun("Lascannon", 24, 1, Rule("Deadly", 3)), Gun("Sniper Rifle", 24, 1, Rule("Takedown"))],
                    unitStats: Stats(4, 5))
                .Army("Blue", 1, 5, modelRules: [new(1, "Tough", 3)], weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 5))
                .Build(),
            ["Blast", "Deadly", "Tough", "Takedown"],
            [
                Exact("p.13 Blast(X): \"each hit is multiplied by X, where X is up to as many hits as models in the target unit\" (Blast(3))", ["Blast"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Frag Launcher" && Hits(a.Hit) > 0 && a.Block is not null), "a Frag Launcher hit", a =>
                    {
                        var x = Math.Min(3, Value(a.Determine!, "defender_models"));
                        var dice = a.Block!.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count);
                        return Value(a.Hit!, "hit_multiplier") == x && dice == Hits(a.Hit) * x ? null
                            : $"{Hits(a.Hit)} hits on {Value(a.Determine!, "defender_models")} models: multiplier {Value(a.Hit!, "hit_multiplier")}, {dice} defense dice";
                    })),
                Exact("p.13 Deadly(X): \"Assign each wound to one model, and multiply it by X ... these wounds don't carry over\" (Deadly(3), Tough(3))", ["Deadly", "Tough"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Lascannon" && Unblocked(a) > 0), "a Lascannon wound", a =>
                    {
                        var hit = WoundsSet(a.Assigns).ToList();
                        return hit.Count > 0 && hit.Count <= Unblocked(a) && hit.All(w => w.Wounds == 3) ? null
                            : $"{Unblocked(a)} unblocked hits set wounds [{string.Join(",", hit.Select(w => w.Wounds))}]";
                    })),
                Exact("p.15 Tough(X): \"This model must take X wounds before being killed\" (Tough(3))", ["Tough"], run =>
                    Each(Steps(run).Where(s => s.Step.Changes.OfType<WoundsSet>().Any()), "a wound on a Tough(3) model", s =>
                    {
                        var killed = s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(m => m.To is OnShelf).Select(m => m.ModelId).ToHashSet();
                        var wrong = s.Step.Changes.OfType<WoundsSet>().Where(w => killed.Contains(w.ModelId) != (w.Wounds >= 3)).ToList();
                        return wrong.Count == 0 ? null : string.Join("; ", wrong.Select(w => $"{ModelName(run, w.ModelId)} at {w.Wounds} wounds {(killed.Contains(w.ModelId) ? "removed" : "kept")}"));
                    })),
                Exact("p.14 Takedown: \"may pick any model in the target unit as its individual target\"", ["Takedown"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Sniper Rifle" && WoundsSet(a.Assigns).Any()), "a Takedown wound", a =>
                    {
                        var picked = (a.Pick?.Answer as SelectedPieces)?.PieceIds.SingleOrDefault();
                        var hit = WoundsSet(a.Assigns).Select(w => w.ModelId).Distinct().ToList();
                        return picked is not null && hit.All(m => m == picked) ? null
                            : $"picked {(picked is { } p ? ModelName(run, p) : "none")}, wounded {string.Join(",", hit.Select(m => ModelName(run, m)))}";
                    })),
            ]);
    }

    /// <summary>Relentless, Surge, Reliable, Stealth: a long-range gunline across a 48" table at a squad that is hard to see.</summary>
    static TestGame Gunline()
    {
        const string name = "special-long-range-gunline";
        return Game(name, new ScenarioBuilder(name, 2).RoundCap(4).Table(24, 48).Scripted(ShootAndStrike)
                .Army("Red", 1, 3, modelRules: [new(1, "Relentless"), new(1, "Tough", 3)],
                    weapons: [Gun("Surge Gun", 48, 2, Rule("Surge")), Gun("Reliable Gun", 48, 2, Rule("Reliable"))], unitStats: Stats(5, 4))
                .Army("Blue", 1, 5, modelRules: [new(1, "Stealth")], weapons: [Gun("Rifle", 48, 1)], unitStats: Stats(4, 5))
                .Build(),
            ["Relentless", "Surge", "Reliable", "Stealth"],
            [
                Exact("p.14 Reliable: \"Models attacks at Quality 2+ with this weapon\"", ["Reliable"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Reliable Gun" && a.Hit is not null), "a Reliable Gun attack", a =>
                        HitRoll(a) is { } r && r.Threshold == 2 ? null : $"hit roll {(HitRoll(a) is { } h ? Show(h) : "missing")} is not at 2+")),
                Exact("p.14 Stealth: \"enemy units get -1 to hit rolls\"", ["Stealth"], run =>
                    Each(Attacks(run).Where(a => a.Actor == "Red unit 1" && HitRoll(a) is { } r && r.Modifiers.Any(m => m.Source == "Stealth")), "a shot at Stealth", a =>
                        HitRoll(a)!.Modifiers.Where(m => m.Source == "Stealth").All(m => m.Amount == -1) ? null : $"Stealth modifier {Show(HitRoll(a)!)}")),
                Exact("p.14 Surge: \"unmodified results of 6 to hit ... deal 1 extra hit\"; p.14 Relentless: the same over 9\"", ["Surge", "Relentless"], run =>
                    Each(Attacks(run).Where(a => a.Actor == "Red unit 1" && Sixes(a.Hit) > 0), "a 6 to hit from the gunline", a =>
                    {
                        var per = (a.Weapon == "Surge Gun" ? 1 : 0) + (a.Hit!.Applied?.Contains("Relentless") == true ? 1 : 0);
                        return Value(a.Hit, "extra_hits") == Sixes(a.Hit) * per ? null
                            : $"{a.Weapon}: {Sixes(a.Hit)} sixes, {per} extra each, extra_hits {Value(a.Hit, "extra_hits")} (applied {string.Join(",", a.Hit.Applied ?? [])})";
                    }, need: 2)),
            ]);
    }

    /// <summary>Artillery, Immobile, Indirect, Limited, Unstoppable: an emplaced gun line that can only hold, against a mobile mortar squad.</summary>
    static TestGame Artillery()
    {
        const string name = "special-artillery-emplacement";
        return Game(name, new ScenarioBuilder(name, 4).RoundCap(4).Table(24, 48).Scripted(ShootAndStrike)
                .Army("Red", 2, 2, modelRules: [new(1, "Artillery"), new(1, "Tough", 3), new(2, "Immobile"), new(2, "Tough", 3)],
                    weapons: [Gun("Missile Pod", 48, 2, Rule("Limited")), Gun("Autocannon", 48, 1)], unitStats: Stats(4, 4))
                .Army("Blue", 2, 3, weapons: [Gun("Rail Rifle", 48, 1, Rule("Unstoppable")), Gun("Mortar", 48, 1, Rule("Indirect"))], unitStats: Stats(4, 5))
                .Build(),
            ["Artillery", "Immobile", "Indirect", "Limited", "Unstoppable"],
            [
                Exact("p.13 Artillery, p.14 Immobile: \"May only use Hold actions\" (p.10: a Shaken unit stays idle instead)", ["Artillery", "Immobile"], run =>
                    Each(ActivateOptions(run).Where(o => o.Unit is "Red unit 1" or "Red unit 2"), "an activation of the emplacement", o =>
                        o.Actions.SequenceEqual(["Hold"]) || o.Actions.SequenceEqual(["Idle"]) ? null : $"{o.Unit} offered {string.Join(",", o.Actions)}", need: 2)),
                Exact("p.13 Artillery: \"+1 to hit rolls\" shooting over 9\"; enemies \"get -2 to hit rolls\"", ["Artillery"], run =>
                    Each(Attacks(run).Select(a => (a, r: HitRoll(a))).Where(x => x.r?.Modifiers.Any(m => m.Source == "Artillery") == true), "an Artillery modifier", x =>
                    {
                        var expected = x.a.Actor == "Red unit 1" ? 1 : -2;
                        return x.r!.Modifiers.Where(m => m.Source == "Artillery").All(m => m.Amount == expected) ? null : $"{x.a.Actor}'s {x.a.Weapon}: {Show(x.r)}";
                    }, need: 2)),
                Exact("p.15 Unstoppable: \"ignores all negative modifiers to this weapon\"", ["Unstoppable"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Rail Rifle" && a.Target == "Red unit 1" && HitRoll(a) is not null), "a Rail Rifle shot at the Artillery", a =>
                        HitRoll(a)!.Modifiers.All(m => m.Amount >= 0) ? null : $"Rail Rifle hit roll {Show(HitRoll(a)!)}")),
                Exact("p.14 Indirect: \"Gets -1 to hit rolls when shooting after moving\"", ["Indirect"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Mortar" && a.Action == "Advance" && HitRoll(a) is not null), "a Mortar shot after an Advance", a =>
                        HitRoll(a)!.Modifiers.Count(m => m.Source == "Indirect" && m.Amount == -1) == 1 ? null : $"Mortar hit roll {Show(HitRoll(a)!)}")),
                Exact("p.14 Limited: \"may only be used once per game\"", ["Limited"], run =>
                {
                    var twice = Attacks(run).Where(a => a.Weapon == "Missile Pod").SelectMany(a => a.Seq.Context.WeaponIds).GroupBy(id => id).Count(g => g.Count() > 1);
                    var later = Steps(run).Count(s => s.Step.Applied?.Contains("Limited") == true);
                    return twice > 0 ? $"{twice} Missile Pods fired more than once" : later == 0 ? "the Missile Pod was never left out of a later shooting" : null;
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Immobile"] = log => OnlyOffered(log, "Red unit 2", "Hold") });
    }

    /// <summary>Furious, Impact, Fear, Thrust, Counter: lancers who charge home against a spear wall that strikes first.</summary>
    static TestGame Charge()
    {
        const string name = "special-charge-into-spears";
        return Game(name, new ScenarioBuilder(name, 3).RoundCap(4).Table(24, 30).Scripted(ShootAndStrike)
                .Army("Red", 2, 3, modelRules: [new(1, "Furious"), new(1, "Impact", 2), new(1, "Fear", 2), new(2, "Furious"), new(2, "Impact", 2), new(2, "Fear", 2)],
                    weapons: [Blade("Lance", 2, Rule("Thrust"))], unitStats: Stats(4, 4))
                .Army("Blue", 2, 3, weapons: [Blade("Spear", 1, Rule("Counter"))], unitStats: Stats(4, 4))
                .Seeking(NearestEnemy)
                .Build(),
            ["Furious", "Impact", "Fear", "Thrust", "Counter"],
            [
                Exact("p.14 Thrust: \"When charging, gets +1 to hit rolls and AP(+1) in melee\" (p.9: a fatigued unit hits only on unmodified 6s)", ["Thrust"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Lance" && a.Action == "Charge" && HitRoll(a) is not null), "a Lance charge", a =>
                    {
                        var hit = Fatigued(run, a) || HitRoll(a)!.Modifiers.Count(m => m.Source == "Thrust" && m.Amount == 1) == 1;
                        var ap = a.Block is null || a.Block.Rolls.Where(r => r.Roll == "defense").All(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -1));
                        return hit && ap ? null : $"hit {Show(HitRoll(a)!)}, defense {string.Join(" ", a.Block?.Rolls.Select(Show) ?? [])}";
                    })),
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
            ]);
    }

    /// <summary>Fast, Slow, Strider, Flying: four squads crossing a swamp that is difficult and dangerous.</summary>
    static TestGame Swift()
    {
        const string name = "special-swift-and-slow";
        return Game(name, new ScenarioBuilder(name, 5).RoundCap(4).Table(36, 30).Scripted(ShootAndStrike).Seeking(NearestEnemy)
                .Army("Red", 2, 2, modelRules: [new(1, "Fast"), new(2, "Slow")], unitStats: Stats(4, 4))
                .Army("Blue", 2, 2, modelRules: [new(1, "Strider"), new(2, "Flying")], unitStats: Stats(4, 4))
                .Terrain("swamp", new Point3(18, 15, 0), volumes:
                    [new Volume(VolumeKind.Area, Footprint.Rect(36, 14), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult | TerrainDeclarations.Dangerous, null)])
                .Build(),
            ["Fast", "Slow", "Strider", "Flying"],
            [
                Exact("p.13 Fast: \"move +2\" when using Advance and +4\" when using Rush/Charge\"; p.14 Slow: \"-2\" ... and -4\"\"", ["Fast", "Slow"], run =>
                    Each(Moves(run).Where(m => m.Own && m.Unit is "Red unit 1" or "Red unit 2" && m.Action is "Advance" or "Rush" or "Charge"), "a Fast or Slow move", m =>
                    {
                        var (advance, rush) = m.Unit == "Red unit 1" ? (8, 16) : (4, 8);   // p.7: Advance 6", Rush and Charge 12"
                        var expected = m.Action == "Advance" ? advance : rush;
                        return m.Region.Allowance == expected ? null : $"{m.Unit} {m.Action}: allowance {m.Region.Allowance}, expected {expected}";
                    }, need: 2)),
                Exact("p.14 Strider: \"May ignore the effects of difficult terrain when moving\"; p.13 Flying: \"ignore terrain effects whilst moving\"", ["Strider", "Flying"], run =>
                    Each(Moves(run).Where(m => m.Region.DifficultVolumeIds.Count > 0), "a move into the swamp", m =>
                        (m.Unit is "Blue unit 1" or "Blue unit 2") == (m.Region.DifficultCap is null) ? null
                            : $"{m.Unit} {m.Action}: difficult cap {m.Region.DifficultCap?.ToString() ?? "none"}", need: 2)),
                Exact("p.13 Flying: \"ignore terrain effects whilst moving\" (no dangerous test for a move)", ["Flying"], run =>
                {
                    var tested = Steps(run).Where(s => s.Step.Step == "move" && s.Frame?.Context.ActorUnitId is { } u && UnitName(run, u) == "Blue unit 2"
                        && Value(s.Step, "dangerous_dice") > 0).ToList();
                    var skipped = Steps(run).Count(s => s.Step.Applied?.Contains("Flying") == true);
                    return tested.Count > 0 ? $"the Flying squad's move rolled {Value(tested[0].Step, "dangerous_dice")} dangerous dice" : skipped == 0 ? "Flying never skipped a terrain effect" : null;
                }),
            ]);
    }

    /// <summary>Caster, Transport, Fearless: casters who ride in a Transport, and a Fearless squad on each side, in a firefight that closes to melee.</summary>
    static TestGame Command()
    {
        const string name = "special-command-squad";
        return Game(name, new ScenarioBuilder(name, 203).RoundCap(4).Table(24, 30).Scripted(ShootAndStrike)
                .Army("Red", 3, 3, modelRules: [new(1, "Caster", 2), new(2, "Transport", 6), new(2, "Tough", 6), new(3, "Fearless")],
                    weapons: [Gun("Rifle", 24, 1), Blade("Bayonet", 1)], unitStats: Stats(5, 5), armyBook: "z65fgu0l29i4lnlu")
                .Army("Blue", 2, 3, modelRules: [new(2, "Fearless")], weapons: [Gun("Rifle", 24, 2, Rule("AP", 1)), Blade("Bayonet", 1)], unitStats: Stats(4, 4))
                .Seeking(ToTransportElseEnemy)
                .Build(),
            ["Caster", "Transport", "Fearless"],
            [
                Exact("p.13 Caster(X): \"Gets X spell tokens at the start of each round, but can't hold more than 6\" (Caster(2)); \"on 4+ resolve\"", ["Caster"], run =>
                {
                    var held = new Dictionary<Guid, int>();
                    var failures = new List<string>();
                    var rounds = 0;
                    foreach (var e in run.EffectiveLog)
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
                    return rounds == 0 || casts.Count == 0 ? "no round started with a cast" : failures.Count == 0 ? null : string.Join("; ", failures);
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
                    return exits.Count == 0 ? "nobody was offered an exit" : exits.All(m => m.Containment!.ExitWithin == 6) ? null
                        : $"exit distances {string.Join(",", exits.Select(m => m.Containment!.ExitWithin))}";
                }),
                Exact("p.13 Fearless: \"fails a morale test, roll one die. On a 4+ it counts as passed\"; p.10: \"If the test is failed, the unit is Shaken\"", ["Fearless"], run =>
                {
                    var failedTests = Steps(run).Where(s => s.Frame?.Sequence == "morale-test" && s.Step.Rolls.Any(r => r.Roll == "morale" && !r.Dice[0].Success)
                        && s.Frame.Context.ActorUnitId is { } u && UnitName(run, u) is "Red unit 3" or "Blue unit 2").Select(s => s.Step).ToList();
                    var outcomes = failedTests.Select(s => s.Rolls.FirstOrDefault(r => r.Roll == "Fearless")?.Dice[0].Raw >= 4).Distinct().Count();
                    return Each(failedTests, "a failed morale test by a Fearless squad", s =>
                    {
                        var fearless = s.Rolls.SingleOrDefault(r => r.Roll == "Fearless");
                        if (fearless is null || fearless.Threshold != 4 || fearless.Dice.Count != 1) return $"no one-die 4+ Fearless roll: {string.Join(" ", s.Rolls.Select(Show))}";
                        var passed = fearless.Dice[0].Raw >= 4;
                        var shaken = s.Changes.OfType<ConditionSet>().Any(c => c.Condition == "shaken" && c.Present) || Value(s, "routed") == 1;
                        return Value(s, "passed") == (passed ? 1 : 0) && shaken != passed ? null : $"Fearless {Show(fearless)}: passed={Value(s, "passed")}, shaken or routed {shaken}";
                    }) ?? (outcomes < 2 ? "the Fearless die never gave both a 4+ and a fail" : null);
                }),
            ]);
    }

    /// <summary>
    /// Scout, Ambush, Aircraft, Hero: the deployment rules and the Hero. Scout and Ambush need a unit set aside before deployment and Hero a
    /// hero joined to a unit without the rule; neither is something a sim scenario can express, so their checks stand failing until it can.
    /// </summary>
    static TestGame Deployment()
    {
        const string name = "special-scouts-and-ambushers";
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(3).Table(36, 36).Scripted(ShootAndStrike)
                .Army("Red", 3, 3, unitRules: [new(1, "Scout"), new(2, "Ambush")], modelRules: [new(3, "Hero")], unitStats: Stats(4, 4))
                .Army("Blue", 2, 2, unitRules: [new(1, "Aircraft"), new(2, "Ambush")], unitStats: Stats(4, 4))
                .Build(),
            ["Scout", "Ambush", "Aircraft", "Hero"],
            [
                Exact("p.14 Scout: \"After all other units are deployed, may be deployed\"", ["Scout"], run =>
                {
                    var asks = Payloads(run).OfType<AskOpened>().Select(a => a.Ask.Question).ToList();
                    var lastDeploy = asks.LastIndexOf(AskQuestions.Deploy);
                    var scouts = asks.Select((q, i) => (q, i)).Where(x => x.q == AskQuestions.DeployScouts).Select(x => x.i).ToList();
                    return scouts.Count == 0 ? "no Scout was deployed" : scouts.All(i => i > lastDeploy) ? null : "a Scout deployed before the other units";
                }),
                Exact("p.13 Ambush: \"At the start of any round after the first, may be deployed\"", ["Ambush"], run =>
                {
                    var round = 0;
                    var rounds = new List<int>();
                    foreach (var p in Payloads(run))
                    {
                        if (p is RoundStarted r) round = r.Round;
                        if (p is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }) rounds.Add(round);
                    }
                    return rounds.Count == 0 ? "no Ambusher was offered" : rounds.All(r => r >= 2) ? null : $"Ambushers offered in rounds {string.Join(",", rounds)}";
                }),
                Exact("p.13 Aircraft: \"May only use Advance actions ... adding 30\" to its total move\"", ["Aircraft"], run =>
                {
                    var offered = ActivateOptions(run).Where(o => o.Unit == "Blue unit 1").ToList();
                    if (offered.Count == 0) return "the Aircraft never activated";
                    if (offered.FirstOrDefault(o => !o.Actions.SequenceEqual(["Advance"])) is { } wrong) return $"the Aircraft was offered {string.Join(",", wrong.Actions)}";
                    return Moves(run).FirstOrDefault(m => m.Unit == "Blue unit 1" && m.Region.Allowance != 36) is { } move
                        ? $"the Aircraft's Advance allowance is {move.Region.Allowance}, expected 36 (6 + 30)" : null;
                }),
                Exact("p.14 Hero: \"must use the unit's Defense until all other models have been killed\"; p.15 Tough: \"heroes must be assigned wounds last\"", ["Hero"], run =>
                    Each(Attacks(run).Where(a => a.Target == "Red unit 3").SelectMany(a => a.Assigns).Where(s => s.Changes.OfType<WoundsSet>().Any()), "a wound on the Hero's unit", s =>
                        s.Applied?.Contains("Hero") == true ? null : "a wound on the Hero's unit was assigned without the Hero deferring it")),
                Exact("p.13 Aircraft: \"can't be charged\"", ["Aircraft"], run =>
                {
                    var aircraft = run.Final.Units.Values.Single(u => u.Definition.Name == "Blue unit 1").Id;
                    var targets = Payloads(run).OfType<AskOpened>().Where(a => a.Ask.Question == "charge_target").Select(a => a.Ask.Legal).OfType<PieceSelection>().ToList();
                    return targets.Count == 0 ? "nobody charged" : targets.Any(t => t.Eligible.Contains(aircraft)) ? "the Aircraft was offered as a charge target" : null;
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
            {
                // Scheduler and legality rules act where no step completes (TestGame.Evidence): the Ask the rule shapes is the evidence.
                ["Scout"] = log => log.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployScouts }),
                ["Ambush"] = log => log.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }),
                ["Aircraft"] = log => OnlyOffered(log, "Blue unit 1", "Advance"),
            });
    }

    // ---- Building blocks ---------------------------------------------------------------------------------------------------------------

    static TestGame Game(string name, Scenario scenario, IReadOnlyList<string> covers, IReadOnlyList<GameCheck> checks,
        IReadOnlyDictionary<string, Func<IReadOnlyList<GameEvent>, bool>>? evidence = null) =>
        new(name, SuiteCatalog.SpecialGroup, scenario, covers, [], checks, evidence);

    static GameCheck Exact(string name, IReadOnlyList<string> entries, Func<SuiteGameRun, string?> failure) => new(name, failure, entries);

    static ScenarioWeapon Gun(string name, int range, int attacks, params ScenarioRule[] rules) =>
        new(name, new Dictionary<string, string> { ["Range"] = $"{range}\"", ["Attacks"] = $"{attacks}" }, rules);

    static ScenarioWeapon Blade(string name, int attacks, params ScenarioRule[] rules) =>
        new(name, new Dictionary<string, string> { ["Attacks"] = $"{attacks}" }, rules);

    static ScenarioRule Rule(string name, int? parameter = null) => new(0, name, parameter);

    static IReadOnlyDictionary<string, string> Stats(int quality, int defense) =>
        new Dictionary<string, string> { ["Quality"] = $"{quality}+", ["Defense"] = $"{defense}+" };

    /// <summary>Every shoot_or_not and strike_back Ask answered yes: the option ids are the server's fixed ones, so the dice decide the rest.</summary>
    static AnswerScript ShootAndStrike
    {
        get
        {
            var script = AnswerScript.Build();
            for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
            return script.Build();
        }
    }

    /// <summary>The nearest enemy model on the table: moves close the gap, so charges come into range.</summary>
    static Point2? NearestEnemy(Guid modelId, PolicyView view)
    {
        if (view.PlacedOf(modelId) is not { } placed) return null;
        var team = view.TeamOf(view.ModelOf(modelId).UnitId);
        var at = placed.Placement.Position.Horizontal;
        return view.Projection.Models.Values
            .Where(m => m.Location is OnTable && view.TeamOf(m.UnitId) != team)
            .Select(m => (Point2?)((OnTable)m.Location).Placement.Position.Horizontal)
            .MinBy(p => p!.Value.DistanceTo(at));
    }

    /// <summary>Red unit 1 heads for Red unit 2's first model, so it reaches the Transport and may embark; every other unit for the nearest enemy.</summary>
    static Point2? ToTransportElseEnemy(Guid modelId, PolicyView view)
    {
        if (view.UnitOf(view.ModelOf(modelId).UnitId).Definition.Name != "Red unit 1") return NearestEnemy(modelId, view);
        var transport = view.Projection.Units.Values.FirstOrDefault(u => u.Definition.Name == "Red unit 2")?.Definition.Models[0].Id;
        return transport is { } id && view.PlacedOf(id) is { } placed ? placed.Placement.Position.Horizontal : null;
    }

    // ---- Reading the log ---------------------------------------------------------------------------------------------------------------

    /// <summary>One weapon group's attack: its attack-sequence frame and steps, the resolve-hits block and the deal-wounds assign steps.</summary>
    sealed record Attack(SequenceStarted Seq, string Weapon, string Actor, string Target, string? Action)
    {
        public StepCompleted? Determine { get; set; }
        public StepCompleted? Pick { get; set; }
        public StepCompleted? Hit { get; set; }
        public StepCompleted? Block { get; set; }
        public List<StepCompleted> Assigns { get; } = [];
    }

    sealed record At(long Seq, StepCompleted Step);

    /// <summary>One melee frame: who charged whom, its impact and compare steps, and the attacks made inside it in order.</summary>
    sealed record Melee(SequenceStarted Seq, string Charger, string Defender)
    {
        public At? Impact { get; set; }
        public At? Compare { get; set; }
        public List<Attack> Attacks { get; } = [];
    }

    /// <summary>Own: the moving unit is the frame's actor in its advance, rush or charge sequence (not a defender closing in or a consolidation).</summary>
    sealed record Move(string Unit, string? Action, MoveRegion Region, bool Own);

    sealed record Offer(string Unit, IReadOnlyList<string> Actions);

    static IEnumerable<EventPayload> Payloads(SuiteGameRun run) => run.EffectiveLog.Select(e => e.Payload);

    static string UnitName(SuiteGameRun run, Guid unit) => run.Final.Units.TryGetValue(unit, out var u) ? u.Definition.Name : unit.ToString();

    static string ModelName(SuiteGameRun run, Guid model) => run.Final.Models.TryGetValue(model, out var m) ? m.Slot.Name : model.ToString();

    static string WeaponName(SuiteGameRun run, Guid weapon) =>
        run.Final.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == weapon)?.Name ?? weapon.ToString();

    static IEnumerable<StateChange> ChangesOf(EventPayload payload) => payload switch
    {
        StateChange c => [c],
        StepCompleted s => s.Changes,
        RoundStarted r => r.Changes,
        RoundEnded r => r.Changes,
        TurnEnded t => t.Changes,
        _ => [],
    };

    /// <summary>Every completed step with its log position and the frame it ran in.</summary>
    static IEnumerable<(long Seq, StepCompleted Step, SequenceStarted? Frame)> Steps(SuiteGameRun run)
    {
        var frames = new Dictionary<Guid, SequenceStarted>();
        foreach (var e in run.EffectiveLog)
        {
            if (e.Payload is SequenceStarted s) frames[s.FrameId] = s;
            if (e.Payload is StepCompleted step) yield return (e.Seq, step, frames.GetValueOrDefault(step.FrameId));
        }
    }

    static List<Attack> Attacks(SuiteGameRun run) => Read(run).Attacks;

    static List<Melee> Melees(SuiteGameRun run) => Read(run).Melees;

    /// <summary>
    /// Every weapon group's attack and every melee, in log order. A resolve-hits frame belongs to the latest attack with the same weapons that
    /// has not blocked yet, a deal-wounds frame to the latest that has; an attack started while a melee is open (before its compare) is that melee's.
    /// </summary>
    static (List<Attack> Attacks, List<Melee> Melees) Read(SuiteGameRun run)
    {
        var attacks = new List<Attack>();
        var melees = new List<Melee>();
        var attackOf = new Dictionary<Guid, Attack>();
        var meleeOf = new Dictionary<Guid, Melee>();
        foreach (var e in run.EffectiveLog)
        {
            switch (e.Payload)
            {
                case SequenceStarted { Sequence: "attack-sequence" } s:
                    var attack = new Attack(s, s.Context.WeaponIds.Count > 0 ? WeaponName(run, s.Context.WeaponIds[0]) : "",
                        s.Context.ActorUnitId is { } actor ? UnitName(run, actor) : "",
                        s.Context.TargetUnitIds.Count > 0 ? UnitName(run, s.Context.TargetUnitIds[0]) : "", s.Context.Action);
                    attacks.Add(attack);
                    attackOf[s.FrameId] = attack;
                    melees.LastOrDefault(m => m.Compare is null)?.Attacks.Add(attack);
                    break;
                case SequenceStarted { Sequence: "resolve-hits" } s:
                    if (attacks.LastOrDefault(x => x.Block is null && x.Seq.Context.WeaponIds.SequenceEqual(s.Context.WeaponIds)) is { } blocking) attackOf[s.FrameId] = blocking;
                    break;
                case SequenceStarted { Sequence: "deal-wounds" } s:
                    if (attacks.LastOrDefault(x => x.Block is not null && x.Seq.Context.WeaponIds.SequenceEqual(s.Context.WeaponIds)) is { } wounding) attackOf[s.FrameId] = wounding;
                    break;
                case SequenceStarted { Sequence: "melee" } s when s.Context.ActorUnitId is { } charger && s.Context.TargetUnitIds.Count > 0:
                    var melee = new Melee(s, UnitName(run, charger), UnitName(run, s.Context.TargetUnitIds[0]));
                    melees.Add(melee);
                    meleeOf[s.FrameId] = melee;
                    break;
                case StepCompleted step when attackOf.TryGetValue(step.FrameId, out var owner):
                    switch (step.Step)
                    {
                        case "determine-attacks": owner.Determine = step; break;
                        case "pick-target-model": owner.Pick = step; break;
                        case "roll-to-hit": owner.Hit = step; break;
                        case "roll-to-block": owner.Block = step; break;
                        case "assign": owner.Assigns.Add(step); break;
                    }
                    break;
                case StepCompleted step when meleeOf.TryGetValue(step.FrameId, out var m):
                    if (step.Step == "impact" && m.Impact is null) m.Impact = new At(e.Seq, step);
                    if (step.Step == "compare" && m.Compare is null) m.Compare = new At(e.Seq, step);
                    break;
            }
        }
        return (attacks, melees);
    }

    static RollRecord? HitRoll(Attack a) => a.Hit?.Rolls.FirstOrDefault(r => r.Roll == "hit");

    /// <summary>Unmodified 6s in the step's hit roll.</summary>
    static int Sixes(StepCompleted? hit) => hit?.Rolls.Where(r => r.Roll == "hit").Sum(r => r.Dice.Count(d => d.Raw == 6)) ?? 0;

    static int Hits(StepCompleted? hit) => hit?.Rolls.Where(r => r.Roll == "hit").Sum(r => r.Dice.Count(d => d.Success)) ?? 0;

    static int Unblocked(Attack a) => a.Block?.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count(d => !d.Success)) ?? 0;

    static IEnumerable<WoundsSet> WoundsSet(IEnumerable<StepCompleted> steps) => steps.SelectMany(s => s.Changes.OfType<WoundsSet>());

    static int Value(StepCompleted step, string key) => step.Values?.GetValueOrDefault(key) ?? 0;

    /// <summary>Whether the attacking unit was fatigued (p.9) when the attack started: its last fatigued condition before then.</summary>
    static bool Fatigued(SuiteGameRun run, Attack attack) => Fatigued(run, attack.Seq);

    /// <summary>Whether the frame's actor was fatigued when the frame started.</summary>
    static bool Fatigued(SuiteGameRun run, SequenceStarted frame)
    {
        var fatigued = false;
        var unit = frame.Context.ActorUnitId;
        foreach (var e in run.EffectiveLog)
        {
            if (e.Payload == frame) return fatigued;
            foreach (var c in ChangesOf(e.Payload).OfType<ConditionSet>().Where(c => c.Condition == "fatigued" && c.Subject.Id == unit)) fatigued = c.Present;
        }
        return fatigued;
    }

    /// <summary>The wounds the unit's models took between the two log positions: the rise of each model's WoundsSet.</summary>
    static int WoundsTaken(SuiteGameRun run, string unit, SequenceStarted from, long toSeq)
    {
        var wounds = new Dictionary<Guid, int>();
        var taken = 0;
        var started = false;
        foreach (var e in run.EffectiveLog.TakeWhile(e => e.Seq < toSeq))
        {
            started |= e.Payload == from;
            foreach (var w in ChangesOf(e.Payload).OfType<WoundsSet>())
            {
                if (started && UnitName(run, run.Final.Models[w.ModelId].UnitId) == unit) taken += Math.Max(0, w.Wounds - wounds.GetValueOrDefault(w.ModelId));
                wounds[w.ModelId] = w.Wounds;
            }
        }
        return taken;
    }

    /// <summary>The unit's models on the table or inside a container just before that log position.</summary>
    static int Living(SuiteGameRun run, string unit, long beforeSeq)
    {
        var where = new Dictionary<Guid, Location>();
        foreach (var e in run.EffectiveLog.TakeWhile(e => e.Seq < beforeSeq))
            foreach (var m in ChangesOf(e.Payload).OfType<ModelsMoved>().SelectMany(m => m.Moves))
                where[m.ModelId] = m.To;
        return where.Count(kv => kv.Value is not OnShelf && UnitName(run, run.Final.Models[kv.Key].UnitId) == unit);
    }

    /// <summary>Every move Ask, with the unit it moves and the action of the frame it was asked in.</summary>
    static IEnumerable<Move> Moves(SuiteGameRun run)
    {
        var frames = new Dictionary<Guid, SequenceStarted>();
        foreach (var p in Payloads(run))
        {
            if (p is SequenceStarted s) frames[s.FrameId] = s;
            if (p is AskOpened { Ask.Legal: MoveRegion { ModelIds.Count: > 0 } region } ask)
            {
                var unit = run.Final.Models[region.ModelIds[0]].UnitId;
                var frame = ask.Ask.FrameId is { } f ? frames.GetValueOrDefault(f) : null;
                yield return new Move(UnitName(run, unit), frame?.Context.Action, region,
                    frame is { Sequence: "advance" or "rush" or "charge" } && frame.Context.ActorUnitId == unit);
            }
        }
    }

    /// <summary>For every activate Ask, each unit offered and the actions it was offered (option labels are "&lt;unit&gt;: &lt;action&gt;").</summary>
    static IEnumerable<Offer> ActivateOptions(SuiteGameRun run) => Offers(run.EffectiveLog);

    static IEnumerable<Offer> Offers(IReadOnlyList<GameEvent> log) =>
        log.Select(e => e.Payload).OfType<AskOpened>().Where(a => a.Ask.Question == AskQuestions.Activate && a.Ask.Legal is Options)
            .SelectMany(a => ((Options)a.Ask.Legal).Choices.Select(c => c.Label.Split(": ", 2)).Where(p => p.Length == 2)
                .GroupBy(p => p[0]).Select(g => new Offer(g.Key, g.Select(p => p[1]).ToList())));

    static bool OnlyOffered(IReadOnlyList<GameEvent> log, string unit, string action) =>
        Offers(log).Where(o => o.Unit == unit).ToList() is { Count: > 0 } offers && offers.All(o => o.Actions.SequenceEqual([action]));

    /// <summary>Null when every case passes and at least <paramref name="need"/> happened; else the first failure, or what never happened.</summary>
    static string? Each<T>(IEnumerable<T> cases, string what, Func<T, string?> failure, int need = 1)
    {
        var all = cases.ToList();
        if (all.Count < need) return need == 1 ? $"no {what} happened" : $"{all.Count} of the {need} needed happened: {what}";
        return all.Select(failure).FirstOrDefault(f => f is not null);
    }

    static string Show(RollRecord r) =>
        $"{r.Roll} {r.Threshold}+ [{string.Join(",", r.Modifiers.Select(m => $"{m.Source}{m.Amount:+0;-0}"))}] {string.Join(" ", r.Dice.Select(d => $"{d.Raw}/{d.Modified}"))}";
}
