using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's 3.6.0 melee and command games (3.6.0 pp.13–15).</summary>
static partial class SpecialGames
{
    // ---- special-charge-into-spears@3.6 ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Furious, Impact, Fear, Thrust, Counter. Blue unit 1 (five spearmen in a row along y 6, Defense 2+, Counter Spears) holds. Red unit 1 and
    /// Red unit 2 (one Tough(6) lancer each: Furious, Impact(2), Fear(2), a Thrust Lance of 2 attacks) charge it in turn, landing above its first
    /// and fifth model; Blue passes its close-in, strikes first with Counter when asked, and strikes back. Fixed: Red unit 1's Lance [3,2]
    /// (+1 from Thrust: a raw 3 hits, a raw 2 misses), Red unit 2's [6,6].
    /// </summary>
    static TestGame Charge36()
    {
        const string name = "special-charge-into-spears";
        var script = Plays((ask, _) => AskPicks.Activate(ask, "Red unit 1", "Charge") ?? AskPicks.Activate(ask, "Red unit 2", "Charge") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"),
            ("charge_target", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 1")),
            ("counter", (ask, _) => new Chosen("strike")),
            ("move", Scripts36.Charging(new Dictionary<string, Point2> { ["Red unit 1"] = new(10, 6 + Scripts36.Touch), ["Red unit 2"] = new(10 + 4 * 1.76, 6 + Scripts36.Touch) })));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(1).Table(36, 24).Scripted(script)
                .Army("Red", 2, 1, modelRules: [.. new[] { 1, 2 }.SelectMany(u => new ScenarioRule[] { new(u, "Furious"), new(u, "Impact", 2), new(u, "Fear", 2), new(u, "Tough", 6) })],
                    weapons: [Blade("Lance", 2, Rule("Thrust"))], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(10, 16), [2] = new(18, 16) })
                .Army("Blue", 1, 5, weapons: [Blade("Spear", 1, Rule("Counter"))], unitStats: Stats(4, 2), anchors: new Dictionary<int, Point2> { [1] = new(10, 6) })
                .Build(),
            ["Furious", "Impact", "Fear", "Thrust", "Counter"],
            [
                Exact("3.6.0 p.15 Thrust: \"When charging, gets +1 to hit rolls and AP(+1) in melee\" (at 4+ a raw 3 hits, a raw 2 misses)", ["Thrust"], run =>
                {
                    var charges = Attacks(run).Where(a => a.Weapon == "Lance" && a.Action == "Charge" && HitRoll(a) is not null).ToList();
                    return Each(charges, "a Lance charge", a =>
                    {
                        var hit = HitRoll(a)!.Modifiers.Count(m => m.Source == "Thrust" && m.Amount == 1) == 1;
                        var ap = a.Block is null || a.Block.Rolls.Where(r => r.Roll == "defense").All(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -1));
                        return hit && ap ? null : $"hit {Show(HitRoll(a)!)}, defense {string.Join(" ", a.Block?.Rolls.Select(Show) ?? [])}";
                    }, need: 2) ?? Judged(charges.Select(a => HitRoll(a)!), "a Lance hit roll on the charge", 0, -1);
                }),
                Exact("3.6.0 p.14 Furious: \"When charging, unmodified result of 6 to hit deal 1 extra hit\"", ["Furious"], run =>
                    Each(Attacks(run).Where(a => a.Weapon == "Lance" && a.Action == "Charge" && Sixes(a.Hit) > 0), "a Lance 6 on the charge", a =>
                    {
                        var dice = a.Block?.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count) ?? 0;
                        return Value(a.Hit!, "extra_hits") == Sixes(a.Hit) && dice == Hits(a.Hit) + Sixes(a.Hit) ? null
                            : $"{Sixes(a.Hit)} sixes, extra_hits {Value(a.Hit!, "extra_hits")}, {dice} defense dice for {Hits(a.Hit)} hits";
                    })),
                Exact("3.6.0 p.14 Impact(X): \"When charging, before either unit attacks, roll X dice, unless this unit is Fatigued. For each 2+ the target takes 1 hit\" (Impact(2), no die lost to Counter)", ["Impact"], run =>
                    Each(Melees(run), "a charge with Impact", m =>
                    {
                        if (m.Impact is null) return $"{m.Charger}'s charge rolled no Impact";
                        var roll = m.Impact.Step.Rolls.SingleOrDefault(r => r.Roll == "Impact");
                        var expected = 2 * Living(run, m.Charger, m.Impact.Seq);
                        if (roll is null || roll.Dice.Count != expected || roll.Threshold != 2 || Value(m.Impact.Step, "hits") != roll.Dice.Count(d => d.Raw >= 2))
                            return $"{m.Charger}: Impact {(roll is null ? "none" : Show(roll))}, hits {Value(m.Impact.Step, "hits")}, expected {expected} dice";
                        return m.Attacks.FirstOrDefault() is { } first && first.At < m.Impact.Seq ? $"{first.Actor} attacked before Impact was rolled" : null;
                    }, need: 2)),
                Exact("3.6.0 p.13 Counter: \"May strike first with this weapon when charged, unless this unit is Fatigued\"", ["Counter"], run =>
                {
                    var asks = LogQueries.Of(run).AsksOf("counter").ToList();
                    return Each(Melees(run), "a charge into the spears", m =>
                    {
                        var fatigued = DefenderFatigued(run, m);
                        var asked = asks.FirstOrDefault(a => a.Frame?.Id == m.Seq.FrameId);
                        if (m.Attacks.Count == 0) return $"nobody struck in {m.Charger}'s melee";
                        if (fatigued) return asked is not null ? "a Fatigued defender was asked to strike first" : m.Attacks[0].Actor == m.Charger ? null : $"the Fatigued {m.Attacks[0].Actor} struck first";
                        if (asked?.Answer is not Chosen { OptionId: "strike" }) return "the defender was not asked, or did not answer strike";
                        return m.Attacks[0].Actor == m.Defender && m.Attacks[0].Weapon == "Spear" ? null : $"{m.Attacks[0].Actor} struck first";
                    }, need: 2) ?? (Melees(run).Any(m => DefenderFatigued(run, m))
                        ? null : "the spearmen were never charged while Fatigued");
                }),
                Exact("3.6.0 p.14 Fear(X): \"When in melee with this model, enemies count as having taken +X wounds when checking who won\" (Fear(2))", ["Fear"], run =>
                    Each(Melees(run).Where(m => m.Compare is not null), "a melee the lancers fought in", m =>
                    {
                        var taken = WoundsTaken(run, m.Defender, ChargeOf(run, m), m.Compare!.Seq);
                        var fear = Living(run, m.Charger, m.Compare.Seq);
                        return Value(m.Compare.Step, "defender_wounds") == taken + 2 * fear ? null
                            : $"{m.Defender} took {taken} with {fear} Fear(2) models against it: defender_wounds {Value(m.Compare.Step, "defender_wounds")}, expected {taken + 2 * fear}";
                    }, need: 2)),
            ],
            dice:
            [
                new("hit", 0, [3, 2], new RollKey(Actor: "Red unit 1", Weapon: "Lance")),
                new("hit", 0, [6, 6], new RollKey(Actor: "Red unit 2", Weapon: "Lance")),
            ]);
    }

    // ---- special-command-squad@3.6 -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Caster, Fearless. Red unit 1 (five models in a row along y 22 from x 2, its first a Caster(2)) Holds first and casts Shock Speed on itself.
    /// Red unit 2 (one Caster(1) model at (5, 14)) stands 7.3" from the caster model behind a wall (y 17–18, x 0–12, 6" tall, sight blocking)
    /// that hides it from all of Red unit 1; Red unit 3 (one Caster(1) model at (27.8, 22)) stands 17.5" from Red unit 1's fifth model, in its
    /// sight, and 24.5" from the caster model. Red unit 2 spends its token. Red unit 4 is a Tough(3) Hero without Fearless and five Fearless models;
    /// Blue unit 1 Holds and shoots it, its hits fixed to three and its defense to three wounds, so it is left at half and tests: the test is
    /// fixed to 1 and the Fearless die to 4.
    /// </summary>
    static TestGame Command36()
    {
        const string name = "special-command-squad";
        static Guid ModelOf(Projection p, string unit) => p.Models.Values.First(m => AskPicks.UnitNameOf(p, m.Id) == unit).Id;
        var script = Plays((ask, _) => AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Activate(ask, "Red unit 3", "Hold")
                ?? AskPicks.Activate(ask, "Red unit 4", "Hold") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"),
            ("react", (ask, p) => ask.Prompt != "Before attacking" ? null
                : ShooterOf(ask, p) == "Red unit 1" && ask.Legal is Options o && o.Choices.Any(c => c.Id == "cast") ? new Chosen("cast") : new Passed()),
            ("cast", (ask, p) => AskPicks.Spell(ask, p, "Shock Speed", "Red unit 1")),
            ("spell_targets", (ask, p) => AskPicks.Pieces(ask, p, "Red unit 1")),
            (AskQuestions.SpendTokens, (ask, p) => new TokensSpent(ask.Legal is TokenSpend t && t.MaxByModel.ContainsKey(ModelOf(p, "Red unit 2"))
                ? new Dictionary<Guid, int> { [ModelOf(p, "Red unit 2")] = 1 } : new Dictionary<Guid, int>())),
            ("declare_targets", (ask, p) => ShooterOf(ask, p) == "Blue unit 1" ? AllAt(ask, p, "Red unit 4") : null));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(1).Table(36, 24).Scripted(script)
                .Army("Red", 4, 1, modelRules: [new(2, "Caster", 1), new(3, "Caster", 1)], weapons: [Blade("Bayonet", 1)], unitStats: Stats(4, 4), armyBook: "z65fgu0l29i4lnlu",
                    sizes: new Dictionary<int, int> { [1] = 5, [4] = 6 },
                    models: [new ScenarioModel(1, 1, [new(0, "Caster", 2)]), new ScenarioModel(4, 1, [new(0, "Hero"), new(0, "Tough", 3)]),
                        .. Enumerable.Range(2, 5).Select(m => new ScenarioModel(4, m, [new(0, "Fearless")]))],
                    anchors: new Dictionary<int, Point2> { [1] = new(2, 22), [2] = new(5, 14), [3] = new(27.8, 22), [4] = new(16, 14) })
                .Army("Blue", 1, 3, modelRules: [new(1, "Tough", 6)], weapons: [Gun("Rifle", 24, 2)], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(16, 3) })
                .Terrain("wall", new Point3(6, 17.5, 0), volumes:
                    [new Volume(VolumeKind.Solid, Footprint.Rect(12, 1), new Point2(0, 0), new Yaw(0), 0, 6, TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight, null)])
                .Build(),
            ["Caster", "Fearless"],
            [
                Exact("3.6.0 p.13 Caster(X): \"Models within 18” of the Caster may spend any number of spell tokens at the same time before rolling to give the Caster +1/-1 to the roll per token\" (out of sight counts; within 18\" of another model of its unit does not)", ["Caster"], run =>
                {
                    var q = LogQueries.Of(run);
                    var (hidden, far) = (run.Final.Models.Values.Single(m => UnitOf(run, m.Id) == "Red unit 2").Id, run.Final.Models.Values.Single(m => UnitOf(run, m.Id) == "Red unit 3").Id);
                    return LogQueries.Each(q.FramesOf("casting").Where(f => q.AsksOf("cast").Any(a => a.Frame == f && a.Answer is Chosen c && c.OptionId.StartsWith("Shock Speed|", StringComparison.Ordinal))).ToList(),
                        "Red unit 1's cast of Shock Speed", f =>
                    {
                        var spend = q.AsksOf(AskQuestions.SpendTokens).FirstOrDefault(a => a.Frame == f);
                        if (spend?.Ask.Legal is not TokenSpend caps) return "no spend was asked";
                        if (!caps.MaxByModel.ContainsKey(hidden)) return "the out-of-sight model within 18\" of the Caster could not spend";
                        if (caps.MaxByModel.ContainsKey(far)) return "a model over 18\" from the Caster could spend";
                        var roll = q.Rolls("casting").FirstOrDefault(r => r.Frame == f);
                        return roll is not null && roll.Roll.Threshold == 4 && roll.Roll.Modifiers.Sum(m => m.Amount) == 1 ? null : $"casting roll {(roll is null ? "none" : LogQueries.Show(roll))}, expected 4+ at +1";
                    });
                }),
                Exact("3.6.0 p.14 Fearless: \"When a unit where most models have this rule fails a Morale Test, roll one die. On a 4+ it counts as passed instead\" (the Hero without it)", ["Fearless"], run =>
                {
                    var q = LogQueries.Of(run);
                    var unit = q.UnitNamed("Red unit 4");
                    return LogQueries.Each(q.FramesOf("morale-test").Where(f => f.Context.ActorUnitId == unit).ToList(), "a morale test by the Fearless unit", f =>
                    {
                        var test = q.Rolls("morale").FirstOrDefault(r => r.Frame == f);
                        var fearless = q.Rolls("Fearless").FirstOrDefault(r => r.Frame == f);
                        var consequence = f.Steps.FirstOrDefault(s => s.Name == "consequence");
                        if (test is null || test.Dice[0].Success) return "the test was not failed";
                        if (fearless is null || fearless.Roll.Threshold != 4 || fearless.Dice.Count != 1 || fearless.Dice[0].Raw != 4) return $"Fearless {(fearless is null ? "not rolled" : LogQueries.Show(fearless))}";
                        if (q.Living(unit, f.Seq) != 3 || q.LocationsBefore(f.Seq).Count(kv => kv.Value is OnTable && q.UnitOf(kv.Key) == unit && run.Final.Models[kv.Key].Slot.Properties.Any(p => p.Name == "Hero")) != 1)
                            return "the unit was not the Hero and two Fearless models";
                        return consequence is not null && consequence.Value("passed") == 1 && !consequence.Step.Changes.OfType<ConditionSet>().Any(c => c.Condition == "shaken" && c.Present) ? null
                            : "the Fearless 4 did not count as passed";
                    });
                }),
            ],
            dice:
            [
                new("hit", 0, [6, 6, 6, 1, 1, 1], new RollKey(Actor: "Blue unit 1", Target: "Red unit 4")),
                new("defense", 0, [1, 1, 1], new RollKey(Actor: "Blue unit 1", Target: "Red unit 4")),
                new("morale", 0, [1], new RollKey(Actor: "Red unit 4")),
                new("Fearless", 0, [4], new RollKey(Actor: "Red unit 4")),
            ]);
    }
}
