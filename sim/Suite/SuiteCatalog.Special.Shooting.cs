using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's shooting games (pp.13–15).</summary>
static partial class SpecialGames
{
    /// <summary>AP, Bane, Rending, Regeneration: two Regeneration squads, one shooting Bane and Rending rifles (both ignore Regeneration).</summary>
    static TestGame Regrowth()
    {
        const string name = "special-regrowth-under-rending-fire";
        return Game(name, new ScenarioBuilder(name, 64).RoundCap(4).Table(24, 30).Scripted(ShootAndStrike)
                .Army("Red", 1, 5, modelRules: [new(1, "Regeneration")],
                    weapons: [Gun("Bane Rifle", 24, 1, Rule("Bane")), Gun("Rending Rifle", 24, 1, Rule("Rending"))], unitStats: Stats(4, 4))
                .Army("Blue", 1, 5, modelRules: [new(1, "Regeneration")], weapons: [Gun("AP Rifle", 24, 1, Rule("AP", 2))], unitStats: Stats(4, 4))
                .Build(),
            ["AP", "Bane", "Rending", "Regeneration"],
            [
                Exact("p.13 AP(X): \"Targets get -X to Defense rolls\" (AP(2): -2; at 4+ a raw 5 fails)", ["AP"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Weapon == "AP Rifle" && a.Block is not null).SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense")).ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Count(m => m.Source == "AP" && m.Amount == -2) != 1) is { } wrong ? $"defense roll {Show(wrong)} lacks AP(2)"
                        : Judged(rolls, "an AP Rifle defense roll", -1);
                }),
                Exact("p.14 Rending: \"on unmodified results of 6 to hit, those hits get AP(+4)\"", ["Rending"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Rending Rifle" && Sixes(a.Hit) > 0 && a.Block is not null), "a Rending Rifle 6 to hit", a =>
                    {
                        var ap4 = a.Block!.Rolls.Where(r => r.Roll == "defense" && r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4)).Sum(r => r.Dice.Count);
                        return ap4 == Sixes(a.Hit) ? null : $"{Sixes(a.Hit)} sixes to hit but {ap4} defense dice at AP(4)";
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
        // Everyone Holds on anchors and shoots; Red wins the roll-off (fixed) and puts all three weapons at the one-wound squad (Blue unit 2). Each Red
        // weapon's first die hits, the Lascannon's and the Sniper Rifle's blocks fail; Blue unit 1's first die hits Red's Tough(3) models and the block fails.
        var script = Plays((ask, _) => AskPicks.Option(ask, c => c.Label.EndsWith(": Hold", StringComparison.Ordinal)),
            ("declare_targets", (ask, p) => AskPicks.ShooterOf(ask, p) == "Red unit 1" ? AskPicks.AllAt(ask, p, "Blue unit 2") : null));
        return Game(name, new ScenarioBuilder(name, 11).RoundCap(4).Table(24, 30).Scripted(script)
                .Army("Red", 1, 2, modelRules: [new(1, "Tough", 3)],
                    weapons: [Gun("Frag Launcher", 24, 1, Rule("Blast", 3)), Gun("Lascannon", 24, 1, Rule("Deadly", 3)), Gun("Sniper Rifle", 24, 1, Rule("Takedown"))],
                    unitStats: Stats(4, 5), anchors: new Dictionary<int, Point2> { [1] = new(16, 15) })
                .Army("Blue", 2, 5, modelRules: [new(1, "Tough", 3)], weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 5),
                    anchors: new Dictionary<int, Point2> { [1] = new(2, 6), [2] = new(2, 22) })
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
                        if (picked is null && Living(run, a.Target, a.At) == 1 && hit.Count == 1) return null;   // one model left: nothing to pick, the allocation reaches it
                        return picked is not null && hit.All(m => m == picked) ? null
                            : $"picked {(picked is { } p ? ModelName(run, p) : "none")}, wounded {string.Join(",", hit.Select(m => ModelName(run, m)))}";
                    })),
                Exact("p.14 Takedown: \"must be resolved before other weapons\"; p.13 Deadly: \"Hits from Deadly must be resolved first\"", ["Takedown", "Deadly"], run =>
                    Each(Attacks(run).Where(a => a.Actor == "Red unit 1").GroupBy(a => a.Volley).Where(v => v.Select(a => a.Weapon).Distinct().Count() == 3), "a volley of all three weapons", v =>
                    {
                        var order = v.Select(a => a.Weapon switch { "Sniper Rifle" => 0, "Lascannon" => 1, _ => 2 }).ToList();
                        return order.SequenceEqual(order.Order()) ? null : $"resolved in the order {string.Join(", ", v.Select(a => a.Weapon))}";
                    })),
            ],
            dice:
            [
                new("roll-off", 0, [6, 1]), new("hit", 0, [6], new RollKey(Actor: "Red unit 1", Weapon: "Frag Launcher")),
                new("hit", 0, [6], new RollKey(Actor: "Red unit 1", Weapon: "Lascannon")), new("defense", 0, [1], new RollKey(Actor: "Red unit 1", Weapon: "Lascannon")),
                new("hit", 0, [6], new RollKey(Actor: "Red unit 1", Weapon: "Sniper Rifle")), new("defense", 0, [1], new RollKey(Actor: "Red unit 1", Weapon: "Sniper Rifle")),
                new("hit", 0, [6], new RollKey(Actor: "Blue unit 1")), new("defense", 0, [1], new RollKey(Actor: "Blue unit 1")),
            ]);
    }

    /// <summary>
    /// Takedown, Blast, Tough, Hero: beside a plain Frag Launcher (Blast(3)), Red's two Seeker Launchers (Blast(3), Takedown) pick Blue's Hero (Tough(3), its own Quality 3+ and Defense 2+ in a unit on 4+/5+)
    /// while it lives, any model after. The pick is "resolved as if it was a unit of [1]", so Blast's cap is one model and the Hero blocks
    /// at its own Defense.
    /// </summary>
    static TestGame TakedownPicksOne()
    {
        const string name = "special-takedown-picks-one";
        // Both sides Hold 14" apart (anchors), inside the 24" guns; Red's first Seeker Launcher die is fixed to hit and the picked Hero's block of it to fail.
        var script = Plays((ask, _) => AskPicks.Option(ask, c => c.Label.EndsWith(": Hold", StringComparison.Ordinal)),
            ("takedown_target", (ask, p) => ask.Legal is PieceSelection s && s.Eligible.FirstOrDefault(id => p.Models[id].Slot.Properties.Any(r => r.Name == "Hero")) is var h && h != Guid.Empty
                ? new SelectedPieces([h]) : null));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(4).Table(24, 30).Scripted(script)
                .Army("Red", 1, 2, modelRules: [new(1, "Tough", 3)], weapons: [Gun("Seeker Launcher", 24, 2, Rule("Blast", 3), Rule("Takedown")), Gun("Frag Launcher", 24, 1, Rule("Blast", 3))], unitStats: Stats(4, 4),
                    anchors: new Dictionary<int, Point2> { [1] = new(18, 15) })
                .Army("Blue", 1, 5, weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 5),
                    models: [new ScenarioModel(1, 1, [new(0, "Hero"), new(0, "Tough", 3)], Stats(3, 2))], anchors: new Dictionary<int, Point2> { [1] = new(1, 15) })
                .Build(),
            ["Takedown", "Blast", "Tough", "Hero"],
            [
                Exact("p.14 Takedown: \"resolved as if it was a unit of [1]\"; p.13 Blast(X): \"X is up to as many hits as models in the target unit\" (one)", ["Takedown", "Blast"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Seeker Launcher" && a.Pick?.Answer is SelectedPieces && Hits(a.Hit) > 0 && a.Block is not null), "a picked Seeker Launcher hit", a =>
                    {
                        var dice = a.Block!.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count);
                        return Value(a.Hit!, "hit_multiplier") == 1 && dice == Hits(a.Hit) ? null
                            : $"{Hits(a.Hit)} hits on the picked model: multiplier {Value(a.Hit!, "hit_multiplier")}, {dice} defense dice";
                    })),
                Exact("p.14 Takedown: \"resolved as if it was a unit of [1]\"; p.14 Hero: \"must use the unit's Defense until all other models have been killed\" (the picked Hero alone: its own 2+)", ["Takedown", "Hero"], run =>
                {
                    var hero = run.Final.Models.Values.Single(m => UnitName(run, m.UnitId) == "Blue unit 1" && m.Slot.Properties.Any(p => p.Name == "Hero")).Id;
                    return Each(Attacks(run).Where(a => a.Weapon == "Seeker Launcher" && a.Block is not null && (a.Pick?.Answer as SelectedPieces)?.PieceIds.SingleOrDefault() == hero)
                        .SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense")), "a defense roll by the picked Hero", r =>
                        r.Threshold == 2 ? null : $"defense {Show(r)} is not at the Hero's own 2+");
                }),
            ],
            dice: [new("hit", 0, [6], new RollKey(Actor: "Red unit 1", Weapon: "Seeker Launcher")), new("defense", 0, [1], new RollKey(Actor: "Red unit 1", Weapon: "Seeker Launcher"))]);
    }

    /// <summary>ShootAndStrike, and a takedown pick names Blue's Hero whenever it is offered.</summary>
    static AnswerScript PickTheHero
    {
        get
        {
            var script = AnswerScript.Build();
            for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
            return script
                .Match("takedown_target", (ask, p) => ask.Legal is PieceSelection s && s.Eligible.FirstOrDefault(id => p.Models[id].Slot.Properties.Any(r => r.Name == "Hero")) is var h && h != Guid.Empty
                    ? new SelectedPieces([h]) : null)
                .Build();
        }
    }

    /// <summary>
    /// Relentless, Surge, Reliable, Stealth: a long-range gunline across a 48" table at a squad that is hard to see. Both units Hold on anchors about 37"
    /// apart and shoot. Red's first Reliable Gun roll is fixed to 3, 2, 6 (2 and 1 after Stealth, at 2+, and a 6 Relentless adds to) and its first
    /// Surge Gun roll to a 6; Red's Tough(3) models stand to roll every die of that first volley.
    /// </summary>
    static TestGame Gunline()
    {
        const string name = "special-long-range-gunline";
        return Game(name, new ScenarioBuilder(name, 42).RoundCap(4).Table(24, 48).Scripted(Plays((ask, _) => AskPicks.Option(ask, c => c.Label.EndsWith(": Hold", StringComparison.Ordinal))))
                .Army("Red", 1, 3, modelRules: [new(1, "Relentless"), new(1, "Tough", 3)],
                    weapons: [Gun("Surge Gun", 48, 2, Rule("Surge")), Gun("Reliable Gun", 48, 2, Rule("Reliable"))], unitStats: Stats(5, 4), anchors: new Dictionary<int, Point2> { [1] = new(18, 40) })
                .Army("Blue", 1, 5, modelRules: [new(1, "Stealth")], weapons: [Gun("Rifle", 48, 1)], unitStats: Stats(4, 5), anchors: new Dictionary<int, Point2> { [1] = new(3, 5) })
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
                    return Each(Attacks(run).Where(a => a.Actor == "Red unit 1" && Sixes(a.Hit) > 0), "a 6 to hit from the gunline", a =>
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
            dice: [new DiceFix("hit", 0, [3, 2, 6], new RollKey(Actor: "Red unit 1", Weapon: "Reliable Gun")), new DiceFix("hit", 0, [6], new RollKey(Actor: "Red unit 1", Weapon: "Surge Gun"))]);
    }

    /// <summary>
    /// Artillery, Immobile, Indirect, Limited, Unstoppable: an emplaced gun line that can only hold, against a mobile mortar squad. Red
    /// deploys in x 12–24, Blue in x 0–12 (frontline on a 24" by 48" table), every Blue base 17" from every Red base, so every shot is from
    /// over 9" and nothing charges. Red activates Red unit 1 first and fires at Blue unit 3 (Blue unit 2 once it is gone). In round 1 Blue unit
    /// 2 Advances 1" along the table's length and fires at Red unit 2, and Blue unit 1 Holds and fires at Red unit 1; Blue unit 3, and every Blue
    /// unit after round 1, holds without shooting. Red's Tough(6) outlasts Blue's at most four wounds a unit, so no Red unit takes a morale test and both keep their
    /// Hold-only activations, and the Artillery shoots again after its Limited Missile Pod is spent.
    /// </summary>
    static TestGame Artillery()
    {
        const string name = "special-artillery-emplacement";
        var script = AnswerScript.Build();
        for (var i = 0; i < 80; i++) script.At("strike_back", i, new Chosen("strike"));
        static bool Blue(string? unit) => unit?.StartsWith("Blue ", StringComparison.Ordinal) == true;
        script.Match("shoot_or_not", (ask, p) => ask.Legal is Options { Choices: [var shoot, ..] } && shoot.Pieces is [var unit]
            ? new Chosen(AskPicks.UnitNameOf(p, unit) is var shooter && Blue(shooter) && (p.Scheduler.Round > 1 || shooter == "Blue unit 3") ? "pass" : "shoot") : null);
        script.Match(AskQuestions.Activate, (ask, p) =>
            AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold")
            ?? (p.Scheduler.Round == 1 ? AskPicks.Activate(ask, "Blue unit 2", "Advance") : AskPicks.Activate(ask, "Blue unit 2", "Hold"))
            ?? AskPicks.Activate(ask, "Blue unit 1", "Hold") ?? AskPicks.Activate(ask, "Blue unit 3", "Hold"));
        script.Match("move", (ask, p) => ask.Legal is MoveRegion { Containment: null } region && region.ModelIds.All(id => AskPicks.UnitNameOf(p, id) == "Blue unit 2")
            ? new Destinations([.. region.ModelIds.Where(id => p.Models[id].Location is OnTable).Select(id => (Id: id, At: ((OnTable)p.Models[id].Location).Placement))
                .Select(m => new ModelRelocation(m.Id, p.Models[m.Id].Location, new OnTable(m.At with { Position = m.At.Position with { Y = m.At.Position.Y + 1 } })))])
            : null);
        script.Match("declare_targets", (ask, p) => AskPicks.ShooterOf(ask, p) switch
        {
            "Red unit 1" or "Red unit 2" => AskPicks.AllAt(ask, p, "Blue unit 3") ?? AskPicks.AllAt(ask, p, "Blue unit 2"),
            "Blue unit 2" => AskPicks.AllAt(ask, p, "Red unit 2"),
            "Blue unit 1" => AskPicks.AllAt(ask, p, "Red unit 1"),
            _ => null,
        });
        return Game(name, new ScenarioBuilder(name, 122).RoundCap(4).Table(24, 48).Scripted(script.Build())
                .Army("Red", 2, 2, modelRules: [new(1, "Artillery"), new(1, "Tough", 6), new(2, "Immobile"), new(2, "Tough", 6)],
                    weapons: [Gun("Missile Pod", 48, 2, Rule("Limited")), Gun("Autocannon", 48, 1)], unitStats: Stats(4, 4),
                    anchors: new Dictionary<int, Point2> { [1] = new(21, 20), [2] = new(21, 28) })
                .Army("Blue", 3, 2, weapons: [Gun("Rail Rifle", 48, 1, Rule("Unstoppable")), Gun("Mortar", 48, 1, Rule("Indirect"))], unitStats: Stats(4, 5),
                    anchors: new Dictionary<int, Point2> { [1] = new(1, 12), [2] = new(1, 24), [3] = new(1, 36) })
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
    /// Stealth at 9-10": two one-model units that only Hold, anchored with their bases 9.6" apart. p.5: "When measuring the distance between two
    /// units you always measure from/to the closest model in each unit", base to base; 9.6" is over 9", measured unrounded. A
    /// defect exhibit: it covers nothing (special-long-range-gunline covers Stealth), and its check reports against Stealth.
    /// </summary>
    static TestGame StealthAtNineAndAHalf()
    {
        const string name = "special-stealth-at-nine-and-a-half";
        return Game(name, new ScenarioBuilder(name, StealthSeed).RoundCap(1).Table(10, 33.1).Scripted(ShootAndStrike)
                .Army("Red", 1, 1, modelRules: [new(1, "Immobile")], weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(5, 4) })
                .Army("Blue", 1, 1, modelRules: [new(1, "Stealth"), new(1, "Immobile")], weapons: [Blade("Blade", 1)], unitStats: Stats(4, 4),
                    anchors: new Dictionary<int, Point2> { [1] = new(5, 4 + 9.6 + 1.26) })
                .Build(),
            [],
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

    const ulong StealthSeed = 14;

    /// <summary>
    /// The Artillery's first own hit roll (a raw 3 and a raw 2), Blue unit 1's first Mortar roll at it from a Hold (a raw 5), and Blue unit 2's
    /// first Mortar roll after an Advance (5 and 4).
    /// </summary>
    static DiceFix[] ArtilleryFixes =>
    [
        new("hit", 0, [3, 2], new RollKey(Actor: "Red unit 1")),
        new("hit", 0, [5], new RollKey(Actor: "Blue unit 1", Target: "Red unit 1", Weapon: "Mortar", Action: "Hold")),
        new("hit", 0, [5, 4], new RollKey(Actor: "Blue unit 2", Weapon: "Mortar", Action: "Advance")),
    ];

}
