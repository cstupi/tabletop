using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>The steps under an attack-sequence frame (its own and its descendants'), in log order.</summary>
        static IEnumerable<LoggedStep> UnderP(ArmyLog log, LoggedAttack a) =>
            log.Steps.Where(s => s.Frame.FrameId == a.Frame.FrameId || s.Ancestors.Any(f => f.FrameId == a.Frame.FrameId));

        /// <summary>Whether a block of the attack failed a die as its dice now stand: a wound to roll Regeneration for.</summary>
        static bool FailedBlockP(LoggedAttack a) => a.Blocks.Any(b => b.Rolls("defense").Any(r => b.Now(r).Any(d => !d.Success)));

        /// <summary>
        /// Thrust from the Buff's grant: every charge the buffed unit makes while holding the grant has its hit rolls at Thrust +1 and its blocks at AP(1)
        /// (the blades carry no AP), and the grant is removed by that attack's attack-end step; at least one such attack rolled blocks.
        /// </summary>
        static GameCheck ThrustBothP(string quote) =>
            Exact("Thrust in Melee Buff", $"{quote}: Thrust hit +1 and AP(1) in one attack, the grant gone at attack-end", (run, log) =>
            {
                var granted = Granted("Thrust in Melee Buff");
                var verdicts = new List<string?>();
                foreach (var a in Of(run, log, (r, x) => x.Charging && granted(r, x)).Where(x => x.Hit is not null))
                {
                    var hits = a.HitRolls("hit").Where(r => !(r.Threshold == 6 && r.Modifiers.Count == 0)).ToList();
                    var blocks = a.Blocks.SelectMany(b => b.Rolls("defense")).ToList();
                    var grant = PatternLog.For(run).Attacks[a.Frame.FrameId].AttackerGrants!.First(g => g.EntryName == "Thrust in Melee Buff");
                    var removed = log.Steps.Where(s => s.Frame.FrameId == a.Frame.FrameId && s.Step.Step == "attack-end")
                        .SelectMany(s => s.Step.Changes).OfType<GrantRemoved>().Any(g => g.GrantId == grant.Id);
                    verdicts.Add(blocks.Count == 0 ? "an attack with no block rolled"
                        : hits.Any(r => !r.Modifiers.Any(m => m.Source == "Thrust" && m.Amount == 1)) ? "a hit roll without Thrust +1"
                        : blocks.Any(r => !r.Modifiers.Where(m => m.Source == "AP").Select(m => m.Amount).SequenceEqual([-1])) ? "a block not at AP(1)"
                        : removed ? null : "the grant not removed at the attack's attack-end");
                }
                return verdicts.Count(v => v is not "an attack with no block rolled") == 0 ? [] : verdicts.Where(v => v is not "an attack with no block rolled");
            });

        /// <summary>
        /// A Mark that ignores Regeneration: under every attack at the marked unit holding the grant with a failed block, no step rolls Regeneration; under
        /// every attack at a Regeneration unit not holding it with a failed block, one does; at least one of each.
        /// </summary>
        static GameCheck MarkedRegenerationP(string entry, string quote) =>
            Exact(entry, $"{quote}: no Regeneration roll on the marked attack", (run, log) =>
            {
                var marked = Granted(entry, onTarget: true);
                var cases = Of(run, log, On("Regeneration")).Where(FailedBlockP)
                    .Select(a => (Marked: marked(run, a), Rolled: UnderP(log, a).Any(s => s.Rolls("Regeneration").Any()))).ToList();
                if (!cases.Any(c => c.Marked)) return ["no marked attack failed a block"];
                if (!cases.Any(c => !c.Marked)) return ["no unmarked attack failed a block"];
                return cases.Select(c => c.Marked == c.Rolled ? c.Marked ? "Regeneration rolled on a marked attack" : "no Regeneration roll on an unmarked attack" : null);
            });

        /// <summary>
        /// Unstoppable from the Mark's grant: every attack at the marked unit holding the grant has no negative modifier to hit and lists Melee Evasion's -1
        /// as ignored, and when a block failed no step under it rolls Regeneration; every unmarked charge at the Regeneration unit that failed a block rolls
        /// Regeneration; at least one of each failed a block.
        /// </summary>
        static GameCheck UnstoppableBothP(string quote) =>
            Exact("Unstoppable Mark", $"{quote}: Melee Evasion's -1 dropped and no Regeneration roll in one attack", (run, log) =>
            {
                var marked = Granted("Unstoppable Mark", onTarget: true);
                var attacks = Of(run, log, marked).Where(a => a.Hit is not null).ToList();
                if (!attacks.Any(FailedBlockP)) return ["no marked attack failed a block"];
                var unmarked = Of(run, log, On("Regeneration", when: When.Charging)).Where(a => !marked(run, a) && FailedBlockP(a)).ToList();
                if (unmarked.Count == 0) return ["no unmarked charge failed a block"];
                return attacks.Select(a =>
                {
                    var hits = a.HitRolls("hit").Where(r => !(r.Threshold == 6 && r.Modifiers.Count == 0)).ToList();
                    return hits.Any(r => r.Modifiers.Any(m => m.Amount < 0)) ? "a hit roll under a negative modifier"
                        : hits.Any(r => !(r.Ignored ?? []).Any(i => i.Source == "Melee Evasion" && i.Amount < 0)) ? "a hit roll with no Melee Evasion -1 ignored"
                        : FailedBlockP(a) && UnderP(log, a).Any(s => s.Rolls("Regeneration").Any()) ? "Regeneration rolled on the marked attack" : null;
                }).Concat(unmarked.Select(a => UnderP(log, a).Any(s => s.Rolls("Regeneration").Any()) ? null : "no Regeneration roll on an unmarked charge"));
            });

        /// <summary>
        /// "may roll +1 attack": every attack of a Predator unit whose first hit roll shows an unmodified 6 as rolled opens one extra_attacks Ask, answered
        /// as scripted (Yes or No), and one with none opens none; at least one offer came from two or more 6s.
        /// </summary>
        static GameCheck PredatorAsked(string quote, bool yes) =>
            Exact("Predator", $"{quote}: one extra_attacks Ask per attack with a 6, answered {(yes ? "Yes" : "No")}", (run, log) =>
            {
                var attacks = Of(run, log, By("Predator", when: When.Shooting)).Where(a => a.HitRolls("hit").Any()).ToList();
                var verdicts = attacks.Select(a =>
                {
                    var sixes = Count(a.HitRolls("hit").Take(1), [6]);
                    var asks = log.Asks.Where(x => x.Ask.Question == "extra_attacks" && x.Frame?.FrameId == a.Frame.FrameId).ToList();
                    return sixes == 0 ? asks.Count == 0 ? null : "asked with no 6"
                        : asks.Count != 1 ? $"{asks.Count} Asks for {sixes} sixes"
                        : asks[0].Yes == yes ? null : $"answered {(asks[0].Yes ? "Yes" : "No")}";
                }).ToList();
                if (!attacks.Any(a => Count(a.HitRolls("hit").Take(1), [6]) >= 2)) verdicts.Add("no attack rolled two 6s");
                return verdicts;
            });
    }
}
