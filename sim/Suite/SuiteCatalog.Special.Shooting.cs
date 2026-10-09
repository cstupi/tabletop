using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's shooting games (pp.13–15).</summary>
static partial class SpecialGames
{
    /// <summary>AP, Bane, Rending, Regeneration: two Regeneration squads, one shooting Bane and Rending rifles (both ignore Regeneration).</summary>
    static TestGame Regrowth()
    {
        const string name = "special-regrowth-under-rending-fire";
        return Game(name, new ScenarioBuilder(name, 41).RoundCap(4).Table(24, 30).Scripted(ShootAndStrike)
                .Army("Red", 1, 5, modelRules: [new(1, "Regeneration")],
                    weapons: [Gun("Bane Rifle", 24, 1, Rule("Bane")), Gun("Rending Rifle", 24, 1, Rule("Rending"))], unitStats: Stats(4, 4))
                .Army("Blue", 1, 5, modelRules: [new(1, "Regeneration")], weapons: [Gun("AP Rifle", 24, 1, Rule("AP", 2))], unitStats: Stats(4, 4))
                .Build(),
            ["AP", "Bane", "Rending", "Regeneration"],
            [
                Exact("p.13 AP(X): \"Targets get -X to Defense rolls\" (AP(2): -2; at 4+ a raw 5 fails)", ["AP"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Weapon == "AP Rifle" && a.Block is not null).SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense")).ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Count(m => m.Source == "AP" && m.Amount == -2) != 1) is { } wrong ? $"defense roll {Show(wrong)} lacks AP -2"
                        : Judged(rolls, "an AP Rifle defense roll", -1);
                }),
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
                {
                    var hit = Attacks(run).Where(a => a.Weapon == "AP Rifle" && Unblocked(a) > 0).ToList();
                    return Each(hit, "an AP Rifle wound", a =>
                    {
                        var regen = a.Assigns.SelectMany(s => s.Rolls).Where(r => r.Roll == "Regeneration").ToList();
                        if (regen.Any(r => r.Threshold != 5)) return $"Regeneration rolls {string.Join(" ", regen.Select(Show))} are not at 5+";
                        var dice = regen.Sum(r => r.Dice.Count);
                        var ignored = regen.Sum(r => r.Dice.Count(d => d.Raw >= 5));
                        var taken = WoundsSet(a.Assigns).Sum(w => w.Wounds);   // one-wound models: each wound that stands sets 1
                        return dice == Unblocked(a) && taken == dice - ignored ? null
                            : $"{Unblocked(a)} wounds, {dice} Regeneration dice, {ignored} of 5+, but {taken} taken";
                    }) ?? Judged(hit.SelectMany(a => a.Assigns).SelectMany(s => s.Rolls).Where(r => r.Roll == "Regeneration"), "a Regeneration roll", 0, -1);
                }),
                Exact("p.13 Bane, p.14 Rending: \"Ignores Regeneration\"", ["Bane", "Rending", "Regeneration"], run =>
                    Each(Attacks(run).Where(a => a.Weapon is "Bane Rifle" or "Rending Rifle").SelectMany(a => a.Assigns.Select(s => (a.Weapon, s)))
                            .Where(x => Value(x.s, "wounds_dealt") > 0), "a Bane or Rending wound", x =>
                        x.s.Rolls.Any(r => r.Roll == "Regeneration") ? $"{x.Weapon} wounds rolled Regeneration" :
                        x.s.Applied?.Contains(x.Weapon.Split(' ')[0]) == true ? null : $"{x.Weapon}'s assign step does not list {x.Weapon.Split(' ')[0]}")),
            ],
            dice: [.. RegrowthFixes]);
    }

    /// <summary>In the regrowth's seed: an AP Rifle defense roll with a raw 5 (3 after AP(2), one below 4+).</summary>
    static DiceFix[] RegrowthFixes => [new("defense", 0, [5])];

    /// <summary>Blast, Deadly, Tough, Takedown: a heavy-weapons team into a squad of Tough(3) models and a squad of one-wound models.</summary>
    static TestGame BlastDeadly()
    {
        const string name = "special-blast-deadly-takedown";
        return Game(name, new ScenarioBuilder(name, 11).RoundCap(4).Table(24, 30).Scripted(ShootAndStrike)
                .Army("Red", 1, 2, modelRules: [new(1, "Tough", 3)],
                    weapons: [Gun("Frag Launcher", 24, 1, Rule("Blast", 3)), Gun("Lascannon", 24, 1, Rule("Deadly", 3)), Gun("Sniper Rifle", 24, 1, Rule("Takedown"))],
                    unitStats: Stats(4, 5))
                .Army("Blue", 2, 5, modelRules: [new(1, "Tough", 3)], weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 5))
                .Build(),
            ["Blast", "Deadly", "Tough", "Takedown"],
            [
                Exact("p.13 Blast(X): \"each hit is multiplied by X, where X is up to as many hits as models in the target unit\" (Blast(3))", ["Blast"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Frag Launcher" && Hits(a.Hit) > 0 && a.Block is not null), "a Frag Launcher hit", a =>
                    {
                        var models = Living(run, a.Target, Attacks(run).First(x => x.Volley == a.Volley).At);   // p.8: a unit's weapons fire at once
                        var x = Math.Min(3, models);
                        var dice = a.Block!.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count);
                        return Value(a.Hit!, "hit_multiplier") == x && dice == Hits(a.Hit) * x ? null
                            : $"{Hits(a.Hit)} hits on {models} models: multiplier {Value(a.Hit!, "hit_multiplier")}, {dice} defense dice";
                    })),
                Exact("p.13 Deadly(X): \"Assign each wound to one model, and multiply it by X ... these wounds don't carry over to other models\" (Deadly(3))", ["Deadly"], run =>
                {
                    var hits = Attacks(run).Where(a => a.Weapon == "Lascannon" && Unblocked(a) > 0).ToList();
                    return Each(hits, "a Lascannon wound", a =>
                    {
                        var touched = WoundsSet(a.Assigns).Where(w => w.Wounds > 0).Select(w => w.ModelId).Distinct().ToList();
                        var removed = a.Assigns.SelectMany(s => s.Changes.OfType<ModelsMoved>()).SelectMany(m => m.Moves).Count(m => m.To is OnShelf);
                        var expected = Math.Min(Unblocked(a), Living(run, a.Target, a.At));
                        return touched.Count == expected && removed == expected ? null
                            : $"{Unblocked(a)} unblocked hits on {a.Target}: {touched.Count} models wounded, {removed} removed, expected {expected} of each";
                    }) ?? (hits.Any(a => a.Target == "Blue unit 2") ? null : "no Lascannon wound on the one-wound squad, where its excess could carry");
                }),
                Exact("p.15 Tough(X): \"This model must take X wounds before being killed\" (Tough(3))", ["Tough"], run =>
                    Each(Steps(run).Where(s => s.Step.Changes.OfType<WoundsSet>().Any(w => UnitOf(run, w.ModelId) is "Red unit 1" or "Blue unit 1")), "a wound on a Tough(3) model", s =>
                    {
                        var killed = s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(m => m.To is OnShelf).Select(m => m.ModelId).ToHashSet();
                        var wrong = s.Step.Changes.OfType<WoundsSet>().Where(w => UnitOf(run, w.ModelId) is "Red unit 1" or "Blue unit 1" && killed.Contains(w.ModelId) != (w.Wounds >= 3)).ToList();
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
                Exact("p.14 Takedown: \"must be resolved before other weapons\"; p.13 Deadly: \"Hits from Deadly must be resolved first\"", ["Takedown", "Deadly"], run =>
                    Each(Attacks(run).Where(a => a.Actor == "Red unit 1").GroupBy(a => a.Volley).Where(v => v.Select(a => a.Weapon).Distinct().Count() == 3), "a volley of all three weapons", v =>
                    {
                        var order = v.Select(a => a.Weapon switch { "Sniper Rifle" => 0, "Lascannon" => 1, _ => 2 }).ToList();
                        return order.SequenceEqual(order.Order()) ? null : $"resolved in the order {string.Join(", ", v.Select(a => a.Weapon))}";
                    })),
            ]);
    }

    /// <summary>Relentless, Surge, Reliable, Stealth: a long-range gunline across a 48" table at a squad that is hard to see.</summary>
    static TestGame Gunline()
    {
        const string name = "special-long-range-gunline";
        return Game(name, new ScenarioBuilder(name, 41).RoundCap(4).Table(24, 48).Scripted(ShootAndStrike)
                .Army("Red", 1, 3, modelRules: [new(1, "Relentless"), new(1, "Tough", 3)],
                    weapons: [Gun("Surge Gun", 48, 2, Rule("Surge")), Gun("Reliable Gun", 48, 2, Rule("Reliable"))], unitStats: Stats(5, 4))
                .Army("Blue", 1, 5, modelRules: [new(1, "Stealth")], weapons: [Gun("Rifle", 48, 1)], unitStats: Stats(4, 5))
                .Build(),
            ["Relentless", "Surge", "Reliable", "Stealth"],
            [
                Exact("p.14 Reliable: \"Models attacks at Quality 2+ with this weapon\" (with Stealth's -1, a raw 3 hits and a raw 2 misses)", ["Reliable"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Weapon == "Reliable Gun").Select(HitRoll).OfType<RollRecord>().ToList();
                    return rolls.FirstOrDefault(r => r.Threshold != 2) is { } wrong ? $"hit roll {Show(wrong)} is not at 2+" : Judged(rolls, "a Reliable Gun hit roll", 0, -1);
                }),
                Exact("p.14 Stealth: \"When units where all models have this rule are shot from over 9\" away, enemy units get -1 to hit rolls\"", ["Stealth"], run =>
                {
                    var shots = Attacks(run).Where(a => a.Actor == "Red unit 1" && HitRoll(a) is not null).ToList();
                    return Each(shots, "a shot at the Stealth squad", a =>
                    {
                        var stealth = HitRoll(a)!.Modifiers.Where(m => m.Source == "Stealth").ToList();
                        var gap = Gap(run, a.Actor, a.Target, a.At);
                        if (stealth.Any(m => m.Amount != -1)) return $"Stealth modifier {Show(HitRoll(a)!)}";
                        return gap > 9.5 && stealth.Count != 1 || gap < 8.5 && stealth.Count != 0 ? $"{gap:0.0}\" away: {Show(HitRoll(a)!)}" : null;
                    }) ?? Judged(shots.Select(HitRoll).OfType<RollRecord>().Where(r => r.Modifiers.Any(m => m.Source == "Stealth")), "a hit roll against Stealth", 0, -1);
                }),
                Exact("p.14 Surge: \"unmodified results of 6 to hit ... deal 1 extra hit\"; p.14 Relentless: the same \"when this model shoots at enemies over 9\" away\"", ["Surge", "Relentless"], run =>
                {
                    var edited = EditedRolls(run);
                    return Each(Attacks(run).Where(a => a.Actor == "Red unit 1" && Sixes(a.Hit) > 0 && !edited.Contains(HitRoll(a)!.Id)), "a 6 to hit from the gunline", a =>
                    {
                        var relentless = a.Hit!.Applied?.Contains("Relentless") == true;
                        var gap = Gap(run, a.Actor, a.Target, a.At);
                        if (gap > 9.5 != relentless && (gap > 9.5 || gap < 8.5)) return $"{gap:0.0}\" away, Relentless {(relentless ? "applied" : "not applied")}";
                        var per = (a.Weapon == "Surge Gun" ? 1 : 0) + (relentless ? 1 : 0);
                        return Value(a.Hit, "extra_hits") == Sixes(a.Hit) * per ? null
                            : $"{a.Weapon}: {Sixes(a.Hit)} sixes, {per} extra each, extra_hits {Value(a.Hit, "extra_hits")}";
                    }, need: 2);
                }),
            ],
            dice: [new DiceFix("hit", GunlineReliableHit, [3, 2])]);
    }

    /// <summary>In the gunline's seed: the first Reliable Gun hit roll (log order, every hit roll counted), set to a raw 3 and a raw 2 (2 and 1 after Stealth, at 2+).</summary>
    const int GunlineReliableHit = 1;

    /// <summary>Artillery, Immobile, Indirect, Limited, Unstoppable: an emplaced gun line that can only hold, against a mobile mortar squad.</summary>
    static TestGame Artillery()
    {
        const string name = "special-artillery-emplacement";
        return Game(name, new ScenarioBuilder(name, 40).RoundCap(4).Table(24, 48).Scripted(ShootAndStrike)
                .Army("Red", 2, 2, modelRules: [new(1, "Artillery"), new(1, "Tough", 3), new(2, "Immobile"), new(2, "Tough", 3)],
                    weapons: [Gun("Missile Pod", 48, 2, Rule("Limited")), Gun("Autocannon", 48, 1)], unitStats: Stats(4, 4))
                .Army("Blue", 3, 2, weapons: [Gun("Rail Rifle", 48, 1, Rule("Unstoppable")), Gun("Mortar", 48, 1, Rule("Indirect"))], unitStats: Stats(4, 5))
                .Build(),
            ["Artillery", "Immobile", "Indirect", "Limited", "Unstoppable"],
            [
                Exact("p.13 Artillery, p.14 Immobile: \"May only use Hold actions\" (p.10: a Shaken unit stays idle instead)", ["Artillery", "Immobile"], run =>
                    Each(ActivateOptions(run).Where(o => o.Unit is "Red unit 1" or "Red unit 2"), "an activation of the emplacement", o =>
                        o.Actions.SequenceEqual([Shaken(run, o.Unit, o.At) ? "Idle" : "Hold"]) ? null
                            : $"{o.Unit}{(Shaken(run, o.Unit, o.At) ? " (Shaken)" : "")} offered {string.Join(",", o.Actions)}", need: 2)),
                Exact("p.13 Artillery: \"When this model shoots at enemies over 9\" away, it gets +1 to hit rolls\" (at 4+ a raw 3 hits, a raw 2 misses)", ["Artillery"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Actor == "Red unit 1").Select(HitRoll).OfType<RollRecord>().Where(r => r.Modifiers.Any(m => m.Source == "Artillery")).ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Single(m => m.Source == "Artillery").Amount != 1) is { } wrong ? $"Artillery's own shot {Show(wrong)}"
                        : Judged(rolls, "an Artillery shot", 0, -1);
                }),
                Exact("p.13 Artillery: \"When enemy units shoot at this model from over 9\" away, they get -2 to hit rolls\" (at 4+ a raw 5 misses)", ["Artillery"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Target == "Red unit 1" && a.Weapon != "Rail Rifle").Select(HitRoll).OfType<RollRecord>().Where(r => r.Modifiers.Any(m => m.Source == "Artillery")).ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Single(m => m.Source == "Artillery").Amount != -2) is { } wrong ? $"a shot at the Artillery {Show(wrong)}"
                        : Judged(rolls, "a shot at the Artillery", -1);
                }),
                Exact("p.15 Unstoppable: \"ignores all negative modifiers to this weapon\"", ["Unstoppable"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Weapon == "Rail Rifle" && a.Target == "Red unit 1" && Gap(run, a.Actor, a.Target, a.At) > 9.5).Select(HitRoll).OfType<RollRecord>().ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Any(m => m.Amount < 0)) is { } wrong ? $"Rail Rifle hit roll {Show(wrong)}"
                        : Judged(rolls, "a Rail Rifle shot at the Artillery from over 9\"");
                }),
                Exact("p.14 Indirect: \"Gets -1 to hit rolls when shooting after moving\" (at 4+ a raw 5 hits, a raw 4 misses)", ["Indirect"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Weapon == "Mortar" && a.Action == "Advance").Select(HitRoll).OfType<RollRecord>().ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Count(m => m.Source == "Indirect" && m.Amount == -1) != 1) is { } wrong ? $"Mortar hit roll {Show(wrong)}"
                        : Judged(rolls, "a Mortar shot after an Advance", 0, -1);
                }),
                Exact("p.14 Limited: \"may only be used once per game\"", ["Limited"], run =>
                {
                    var twice = Attacks(run).Where(a => a.Weapon == "Missile Pod").SelectMany(a => a.Seq.Context.WeaponIds).GroupBy(id => id).Count(g => g.Count() > 1);
                    var later = Steps(run).Count(s => s.Step.Applied?.Contains("Limited") == true);
                    return twice > 0 ? $"{twice} Missile Pods fired more than once" : later == 0 ? "the Missile Pod was never left out of a later shooting" : null;
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Immobile"] = log => OnlyOffered(log, "Red unit 2", "Hold") },
            dice: [.. ArtilleryFixes]);
    }

    /// <summary>
    /// Stealth at 9-10": two thirty-model units that only Hold, in frontline zones 9.1" apart on a 33.1" table, so each unit's six rows
    /// leave under 2" of play and the seed puts the closest bases between 9" and 10" apart. p.5: "When measuring the distance between two
    /// units you always measure from/to the closest model in each unit", base to base; 9.6" is over 9" (D11: the engine floors it to 9).
    /// </summary>
    static TestGame StealthAtNineAndAHalf()
    {
        const string name = "special-stealth-at-nine-and-a-half";
        return Game(name, new ScenarioBuilder(name, StealthSeed).RoundCap(1).Table(10, 33.1).Scripted(ShootAndStrike)
                .Army("Red", 1, 30, modelRules: [new(1, "Immobile")], weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 4))
                .Army("Blue", 1, 30, modelRules: [new(1, "Stealth"), new(1, "Immobile")], weapons: [Blade("Blade", 1)], unitStats: Stats(4, 4))
                .Build(),
            ["Stealth"],
            [
                Exact("p.14 Stealth: \"When units where all models have this rule are shot from over 9\" away, enemy units get -1 to hit rolls\" (shots from 9-10\")", ["Stealth"], run =>
                {
                    var all = Attacks(run).Where(a => a.Actor == "Red unit 1" && HitRoll(a) is not null)
                        .Select(a => (Attack: a, Gap: Gap(run, a.Actor, a.Target, a.At) ?? 0)).ToList();
                    var shots = all.Where(x => x.Gap > 9 && x.Gap < 10).ToList();
                    if (shots.Count == 0) return $"no shot from 9-10\" (shots from {string.Join(", ", all.Select(x => $"{x.Gap:0.00}\""))})";
                    return Each(shots, "a shot from 9-10\" at the Stealth unit", x =>
                        HitRoll(x.Attack)!.Modifiers.Count(m => m.Source == "Stealth" && m.Amount == -1) == 1 ? null
                            : $"{x.Gap:0.00}\" away: {Show(HitRoll(x.Attack)!)}");
                }),
            ]);
    }

    /// <summary>The seed whose placements put the two units' closest bases between 9" and 10" apart.</summary>
    const ulong StealthSeed = 14;

    /// <summary>In the artillery's seed: the Artillery's first own hit roll (a raw 3 and a raw 2), a hit roll at it (a raw 5), and a Mortar roll after an Advance (5 and 4).</summary>
    static DiceFix[] ArtilleryFixes => [new("hit", 0, [3, 2]), new("hit", 2, [5]), new("hit", 7, [5, 4])];
}
