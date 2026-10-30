using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 7 (army-book rules spec → Authored rules 7): who casts and with what, a Buff's extended reach, Reanimation.</summary>
    private static partial class ArmyGames
    {
        /// <summary>Caster Group's tokens at each round start: one model of the group, X the group's models in play, any earlier tokens lost.</summary>
        static GameCheck GroupTokens(string quote) =>
            Exact("Caster Group", quote, (run, _) =>
            {
                var verdicts = new List<string?>();
                var shelved = new HashSet<Guid>();
                foreach (var e in EventApplier.EffectiveLog(run.EffectiveLog))
                {
                    var changes = e.Payload switch { StepCompleted s => s.Changes, RoundStarted r => r.Changes, _ => [] };
                    if (e.Payload is RoundStarted { Round: >= 1 } round)
                        foreach (var unit in run.Final.Units.Values.Where(u => u.Definition.Models.Any(m => m.Properties.Any(p => p.Name == "Caster Group"))))
                        {
                            var group = unit.Definition.Models.Where(m => m.Properties.Any(p => p.Name == "Caster Group") && !shelved.Contains(m.Id)).Select(m => m.Id).ToList();
                            var set = round.Changes.OfType<SpellTokensSet>().Where(t => group.Contains(t.ModelId)).ToList();
                            if (group.Count > 0) verdicts.Add(set is [var one] && one.Tokens == Math.Min(6, group.Count) ? null : $"tokens {string.Join(",", set.Select(t => t.Tokens))} for a group of {group.Count}");
                        }
                    foreach (var mv in changes.OfType<ModelsMoved>().SelectMany(m => m.Moves)) if (mv.To is OnShelf) shelved.Add(mv.ModelId); else shelved.Remove(mv.ModelId);
                }
                return verdicts;
            });

        /// <summary>Spell Accumulator: a pick-spell that drew on an accumulator took no more than the spell's value, the caster's own tokens first.</summary>
        static GameCheck AccumulatorPays(string quote) =>
            Exact("Spell Accumulator", quote, (run, log) =>
                from s in log.Steps.Where(s => s.Step.Step == "pick-spell" && (s.Step.Applied?.Contains("Spell Accumulator") ?? false))
                let sets = s.Step.Changes.OfType<SpellTokensSet>().ToList()
                select sets.Count >= 2 && sets[0].Tokens == 0 ? null : "an accumulator paid while the caster kept tokens");

        /// <summary>Reanimation: each roll is at 5+, and its successes restore no more than that many models and wounds.</summary>
        static GameCheck Reanimates(string quote) =>
            Exact("Reanimation", quote, (_, log) =>
                from s in log.Steps.Where(s => s.Rolls("Reanimation").Any())
                let roll = s.Rolls("Reanimation").Single()
                let restored = s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Count(m => m.From is OnShelf && m.To is OnTable)
                let healed = s.Step.Changes.OfType<WoundsSet>().Count()
                select roll.Threshold != 5 ? $"threshold {roll.Threshold}" : restored > roll.Dice.Count(d => d.Raw >= 5) ? $"{restored} restored for {Faces(roll.Dice)}" : null);

        public static IReadOnlyList<TestGame> Batch7 =>
        [
            Game(HighElves, "high-elf-fleets-caster-group", Play.Shoots, ["Caster Group"], [R(1, "Caster Group")], [],
                [GroupTokens("\"Pick one model with this rule in this unit to have Caster(X), where X is the total number of models with this rule in this unit … loses all unspent spell tokens at the end of the round\"")],
                units: 1, more: Casts),
            Game(ChangeDaemons, "wormhole-daemons-of-change-spell-accumulator", Play.Shoots, ["Spell Accumulator"], [R(2, "Spell Accumulator", 3), R(3, "Spell Accumulator", 3), R(4, "Spell Accumulator", 3)], [],
                [AccumulatorPays("\"Casters from other friendly units within 12\" may spend this model's accumulator tokens as if they were their own spell tokens\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Caster", 0)])], units: 4, length: 26, more: Casts),
            Game(Sisters, "blessed-sisters-spell-conduit", Play.Shoots, ["Spell Conduit"], [R(2, "Spell Conduit"), R(3, "Spell Conduit"), R(4, "Spell Conduit")], [],
                [Modifier("Spell Conduit", "casting", 1, "\"may cast spells as if they were in this model's position, and get +1 to casting rolls when doing so\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Caster", 3)])], units: 4, length: 26,
                more: s => { Casts(s); s.Match("cast_from", (ask, _) => AskPicks.Option(ask, c => c.Id != "self")); }),
            // Played under Rebel Guerrillas, whose Courage Buff is the pick it extends (Human Defense Force prints the rule).
            ExtendedBuffRange(Guerrillas, "rebel-guerrillas-extended-buff-range", "Courage Buff"),
            Game(Robots, "robot-legions-reanimation", Play.Shot, ["Reanimation"], [R(1, "Reanimation"), R(1, "Tough", 3), R(2, "Reanimation"), R(2, "Tough", 3)], [],
                [Reanimates("\"roll as many dice as the max. number of models/wounds it could restore. For each 5+ you may restore one model/wound\"")], enemyAttacks: 4, units: 2),
        ];

        /// <summary>Extended Buff Range on a Hero that carries the book's pick Buff (buff), which it extends.</summary>
        static TestGame ExtendedBuffRange((string Uid, string Army) book, string name, string buff) =>
            Game(book, name, Play.Shoots, ["Extended Buff Range"], [R(1, "Extended Buff Range"), R(2, "Extended Buff Range"), R(3, "Extended Buff Range")], [],
                [Exact("Extended Buff Range", "\"that Hero may use special rules that allow it to pick friendly units within 12\" … on this unit as if it was in range\": a unit beyond 12\" offered holds it and is within 24\"", (run, _) =>
                    from p in PatternLog.For(run).Picks
                    where p.Entry == buff && p.Ask.Legal is PieceSelection
                    from unit in ((PieceSelection)p.Ask.Legal).Eligible
                    let apart = Apart(run, p.At, p.Carrier, unit)
                    where apart > 12
                    select !Holds(run, unit, "Extended Buff Range") ? "offered a unit beyond 12\" without the rule" : apart > 24 + 2 ? $"offered a unit {apart:0.##}\" away" : null)],
                carriers: [new ScenarioModel(1, 1, [R(1, "Extended Buff Range"), R(1, "Hero"), R(1, buff)])], units: 3,
                more: s => s.Match(AskQuestions.React, (ask, _) => AskPicks.Option(ask, c => c.Id.StartsWith(buff + "|", StringComparison.Ordinal)))
                    .Match("rule_target", (ask, p) => ask.Legal is PieceSelection sel ? new SelectedPieces([sel.Eligible[^1]]) : null));
    }
}
