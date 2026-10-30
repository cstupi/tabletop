using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 2's log reading (army-book rules spec → the pick): where a pick entry's grants are held and spent, and what its use did to its target.</summary>
    private static partial class ArmyGames
    {
        const string PickRule = "pick-rule";

        /// <summary>
        /// Every rule_target Ask of a carried pick entry is answered by the script: a Buff's with the army's unit buffUnit, any other's by the policy;
        /// every react Ask that offers a use of one of the entries takes it. Both read only labels, names and the option ids the server sent.
        /// </summary>
        static Action<ScriptBuilder> UsePicks(string army, int buffUnit, params string[] entries) => s => s
            .Match(AskQuestions.React, (ask, _) => AskPicks.Option(ask, c => entries.Any(e => c.Id.StartsWith(e + "|", StringComparison.Ordinal))))
            .Match("rule_target", (ask, projection) => entries.Any(e => e.EndsWith(" Buff") && ask.Prompt.StartsWith(e + ":", StringComparison.Ordinal))
                ? AskPicks.Pieces(ask, projection, $"{army} unit {buffUnit}") : null);

        /// <summary>
        /// "Once (next time the effect would apply)", read off the grants: every step outside the pick that the entry acted in ran while a grant of it
        /// was held, and a grant that acted is spent by the end of the activation it acted in.
        /// </summary>
        static GameCheck Spent(string entry, string quote) =>
            Exact(entry, $"{quote}: acts only through its grant, which it spends", (_, log) =>
            {
                var held = new Dictionary<Guid, string>();
                var verdicts = new List<string?>();
                Guid? acted = null;
                foreach (var s in log.Steps)
                {
                    var outside = s.Frame.Sequence != PickRule && s.Ancestors.All(a => a.Sequence != PickRule);
                    var root = s.Ancestors.LastOrDefault() ?? s.Frame;
                    if (acted is { } a && root.FrameId != a && held.ContainsValue(entry)) { verdicts.Add("a grant that acted outlived its activation"); acted = null; }
                    if (outside && (s.Step.Applied?.Contains(entry) ?? false))
                    {
                        verdicts.Add(held.ContainsValue(entry) ? null : "acted with no grant held");
                        acted = root.FrameId;
                    }
                    foreach (var c in s.Step.Changes)
                        switch (c)
                        {
                            case GrantAdded g: held[g.Grant.Id] = g.Grant.EntryName; break;
                            case GrantRemoved r when held.Remove(r.GrantId, out var name) && name == entry: acted = null; break;
                        }
                }
                return verdicts;
            });

        /// <summary>The use's frames of an entry: each pick-rule frame it started, with its steps and every step under it.</summary>
        static IEnumerable<(SequenceStarted Frame, IReadOnlyList<LoggedStep> Steps)> Uses(ArmyLog log, string entry) =>
            log.Steps.Select(s => s.Frame.Sequence == PickRule ? s.Frame : s.Ancestors.FirstOrDefault(a => a.Sequence == PickRule)).OfType<SequenceStarted>()
                .Where(f => f.Context.Action is { } k && k.StartsWith(entry + "|", StringComparison.Ordinal)).DistinctBy(f => f.FrameId)
                .Select(f => (f, (IReadOnlyList<LoggedStep>)[.. log.Steps.Where(s => s.Frame.FrameId == f.FrameId || s.Ancestors.Any(a => a.FrameId == f.FrameId))]));

        /// <summary>
        /// Dice that deal hits ("roll 3 dice. For each 2+ one enemy unit … takes 3 hits"): per use, the entry's dice succeed on the threshold, one
        /// spell-hits frame per unit picked and no more than the successes, each dealing the stated hits, its blocks under the stated AP when it has one.
        /// </summary>
        static GameCheck HitsPerSuccess(string entry, int dice, int threshold, int hits, string quote, int? ap = null) =>
            Exact(entry, quote, (_, log) =>
                from use in Uses(log, entry)
                let roll = use.Steps.Where(s => s.Step.Step == "rule-roll").SelectMany(s => s.Rolls(entry)).FirstOrDefault()
                let successes = roll?.Dice.Count(d => d.Raw >= threshold) ?? -1
                let volleys = use.Steps.Where(s => s.Frame.Sequence == "spell-hits" && s.Step.Step == "hit").ToList()
                let blocks = use.Steps.Where(s => s.Step.Step == "roll-to-block").SelectMany(s => s.Rolls("defense")).ToList()
                select roll is null ? "no dice rolled" : roll.Dice.Count != dice ? $"{roll.Dice.Count} dice"
                    : roll.Dice.Any(d => d.Success != d.Raw >= threshold) ? $"dice {Faces(roll.Dice)} judged against {roll.Threshold}"
                    : volleys.Count > successes ? $"{volleys.Count} units hit for {successes} successes"
                    : volleys.FirstOrDefault(v => v.Value("hits") + v.Rolls("hit").Sum(r => r.Dice.Count) != hits) is { } v ? $"{v.Value("hits") + v.Rolls("hit").Sum(r => r.Dice.Count)} hits"
                    : ap is { } x && blocks.Any(b => !b.Modifiers.Any(m => m.Source == "AP" && m.Amount == -x)) ? "a block without its AP"
                    : null);

        /// <summary>A heal ("remove D3 wounds from it"): each use's D3 (p.4: a D6 halved, rounding up) comes off the picked model's wounds, to none.</summary>
        static GameCheck Healed(string entry, string quote) =>
            Exact(entry, quote, (run, log) =>
            {
                var wounds = new Dictionary<Guid, int>();
                var verdicts = new List<string?>();
                foreach (var s in log.Steps)
                {
                    var use = s.Frame.Sequence == PickRule && s.Frame.Context.Action is { } k && k.StartsWith(entry + "|", StringComparison.Ordinal) && s.Step.Step == "rule-effects";
                    if (use && s.Rolls(entry).FirstOrDefault() is { } d3)
                    {
                        var set = s.Step.Changes.OfType<WoundsSet>().ToList();
                        var heal = d3.Dice.Sum(d => (d.Raw + 1) / 2);
                        verdicts.Add(set.Count > 1 ? "more than one model healed"
                            : set is [var w] && w.Wounds != Math.Max(0, wounds.GetValueOrDefault(w.ModelId) - heal) ? $"{wounds.GetValueOrDefault(w.ModelId)} wounds became {w.Wounds} after D3 {heal}" : null);
                    }
                    foreach (var w in s.Step.Changes.OfType<WoundsSet>()) wounds[w.ModelId] = w.Wounds;
                }
                if (!verdicts.Any(v => v is null) || !log.Steps.Any(s => s.Step.Step == "rule-effects" && s.Step.Changes.OfType<WoundsSet>().Any())) verdicts.Add("no wound removed");
                return verdicts;
            });

        /// <summary>
        /// "Must take a morale test. If failed, …": every spell-test a use of the entry pushed tests the picked unit, and its consequence follows the
        /// test: on a failure the stated condition is set or the stated move is offered to the user's side, on a pass neither.
        /// </summary>
        static GameCheck TestThen(string entry, string quote, string? condition = null, int? move = null) =>
            Exact(entry, quote, (_, log) =>
                from use in Uses(log, entry)
                let test = use.Steps.FirstOrDefault(s => s.Frame.Sequence == "morale-test" && s.Step.Step == "consequence")
                where test is not null
                let failed = test.Value("passed") == 0
                let consequence = use.Steps.FirstOrDefault(s => s.Frame.Sequence == "spell-test" && s.Step.Step == "consequence")
                let set = consequence?.Step.Changes.OfType<ConditionSet>().Any(c => c.Condition == condition && c.Present) ?? false
                let offered = log.Asks.Any(a => a.Ask.Question == "move" && a.Frame?.Sequence == "spell-test" && use.Steps.Any(s => s.Frame.FrameId == a.Frame.FrameId)
                    && a.Ask.Legal is MoveRegion r && r.Allowance == move)
                select condition is not null && set != failed ? $"{(failed ? "failed" : "passed")} test, {condition} {(set ? "set" : "not set")}"
                    : move is not null && offered != failed ? $"{(failed ? "failed" : "passed")} test, move {(offered ? "offered" : "not offered")}" : null);

        /// <summary>A move under a grant of the entry: the first move Ask of each action of a unit holding one allows the action's p.7 distance plus the stated change.</summary>
        static GameCheck GrantedAllowance(string entry, string quote, params (string Action, double Inches)[] allowed) =>
            Exact(entry, $"{quote}: {string.Join(", ", allowed.Select(a => $"{a.Action} {a.Inches}\""))}", (run, log) =>
            {
                var held = new Dictionary<Guid, (Guid Unit, string Entry)>();
                var verdicts = new List<string?>();
                var seen = new HashSet<Guid>();
                var at = 0;
                foreach (var a in log.Asks)
                {
                    for (; at < a.Steps; at++)
                        foreach (var c in log.Steps[at].Step.Changes)
                            switch (c)
                            {
                                case GrantAdded g: held[g.Grant.Id] = (g.UnitId, g.Grant.EntryName); break;
                                case GrantRemoved r: held.Remove(r.GrantId); break;
                            }
                    if (a.Ask.Question != "move" || a.Frame is not { } f || !allowed.Any(x => x.Action == f.Sequence) || !seen.Add(f.FrameId)) continue;
                    if (!held.Values.Any(h => h.Unit == f.Context.ActorUnitId && h.Entry == entry)) continue;
                    var inches = allowed.Single(x => x.Action == f.Sequence).Inches;
                    verdicts.Add(a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"{f.Sequence} allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");
                }
                return verdicts;
            });

        /// <summary>The occasion's blocks roll defense under no AP but the stated one (none for 0): "loses AP(+1)" of a weapon with AP(1).</summary>
        static GameCheck BlocksUnder(string entry, Occasion occasion, int ap, string quote) =>
            Exact(entry, $"{quote}: AP {ap}", (run, log) =>
                from a in Of(run, log, occasion)
                from s in a.Blocks
                where s.Rolls("defense").Any()
                let aps = s.Rolls("defense").SelectMany(r => r.Modifiers).Where(m => m.Source == "AP" && m.Amount != 0).Select(m => -m.Amount).Distinct().ToList()
                select (ap == 0 ? aps.Count == 0 : aps is [var x] && x == ap) ? null : $"defense under AP [{string.Join(",", aps)}]");

        /// <summary>
        /// "Counts as being in Dangerous Terrain once": the step that spends a grant of the entry is the unit's activation-terrain step, which owes a
        /// test for every model (p.11: "roll one die for each model"), and the dangerous-terrain roll under it rolls that many dice. Under 3.6 the
        /// test is the unit's (sequence dangerous-terrain-36, 3.6.0 p.12).
        /// </summary>
        static GameCheck DangerousOnce(string entry, string quote, string sequence = "dangerous-terrain") =>
            Exact(entry, quote, (_, log) =>
            {
                var held = new Dictionary<Guid, string>();
                var verdicts = new List<string?>();
                foreach (var s in log.Steps)
                {
                    foreach (var c in s.Step.Changes)
                        switch (c)
                        {
                            case GrantAdded g: held[g.Grant.Id] = g.Grant.EntryName; break;
                            case GrantRemoved r when held.Remove(r.GrantId, out var name) && name == entry:
                                var owed = s.Value("dangerous_dice");
                                var rolled = log.Steps.Skip(s.Index + 1).FirstOrDefault(x => x.Frame.Sequence == sequence && x.Ancestors.Any(a => a.FrameId == s.Frame.FrameId));
                                var dice = rolled?.Rolls("dangerous-terrain").Sum(x => x.Dice.Count) ?? 0;
                                verdicts.Add(s.Step.Step != "activation-terrain" ? $"spent at {s.Step.Step}" : owed == 0 ? "no test owed" : dice != owed ? $"{dice} dice for {owed} owed" : null);
                                break;
                        }
                }
                return verdicts;
            });

        /// <summary>"Gets Unwieldy in melee once", Unwieldy being "Strikes last when charging": a charger that held a grant of the entry as its melee began
        /// strikes after the strike back.</summary>
        static GameCheck StrikesLast(string entry, string quote) =>
            Exact(entry, quote, (run, log) =>
            {
                var held = new Dictionary<Guid, (Guid Unit, string Entry)>();
                var verdicts = new List<string?>();
                foreach (var s in log.Steps)
                {
                    if (s.Frame.Sequence == MeleeSequence && s.Step.Step == "melee-start" && s.Frame.Context.ActorUnitId is { } charger
                        && held.Values.Any(h => h.Unit == charger && h.Entry == entry))
                    {
                        var melee = log.Steps.Where(x => x.Frame.FrameId == s.Frame.FrameId || x.Ancestors.Any(a => a.FrameId == s.Frame.FrameId)).ToList();
                        var back = melee.FirstOrDefault(x => x.Frame.FrameId == s.Frame.FrameId && x.Step.Step == "strike-back")?.Index;
                        var first = melee.FirstOrDefault(x => x.Step.Step == "roll-to-hit" && x.Frame.Context.ActorUnitId == charger)?.Index;
                        verdicts.Add(back is null || first is null ? null : first > back ? null : "the charger struck before the strike back");
                    }
                    foreach (var c in s.Step.Changes)
                        switch (c)
                        {
                            case GrantAdded g: held[g.Grant.Id] = (g.UnitId, g.Grant.EntryName); break;
                            case GrantRemoved r: held.Remove(r.GrantId); break;
                        }
                }
                return verdicts.Where(v => v is not null).DefaultIfEmpty(verdicts.Count > 0 ? null : "no charge under the grant");
            });
    }
}
