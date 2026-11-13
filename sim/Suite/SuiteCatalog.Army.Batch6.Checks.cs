using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 6's log reading (army-book rules spec → Authored rules 6): how many markers of a rule a unit held as each roll was made, read off the
    /// grants the log adds and removes (a marker is one grant), and the round-start rolls.</summary>
    private static partial class ArmyGames
    {
        /// <summary>Every completed step with the markers of the entry each unit held as it ran, and the step's frame.</summary>
        static IEnumerable<(StepCompleted Step, SequenceStarted Frame, IReadOnlyDictionary<Guid, int> Held)> Marked(SuiteGameRun run, string entry)
        {
            var grants = new Dictionary<Guid, Guid>();   // grant → unit
            var frames = new Dictionary<Guid, SequenceStarted>();
            IReadOnlyDictionary<Guid, int> Held() => grants.Values.GroupBy(u => u).ToDictionary(g => g.Key, g => g.Count());
            void Apply(IEnumerable<StateChange> changes)
            {
                foreach (var c in changes)
                    switch (c)
                    {
                        case GrantAdded g when g.Grant.EntryName == entry: grants[g.Grant.Id] = g.UnitId; break;
                        case GrantRemoved r: grants.Remove(r.GrantId); break;
                    }
            }
            foreach (var e in EventApplier.EffectiveLog(run.EffectiveLog))
                switch (e.Payload)
                {
                    case SequenceStarted s: frames[s.FrameId] = s; break;
                    case RoundStarted r: Apply(r.Changes); break;
                    case StepCompleted s when frames.TryGetValue(s.FrameId, out var f):
                        yield return (s, f, Held());
                        Apply(s.Changes);
                        break;
                }
        }

        /// <summary>
        /// "For each marker" (perTwo: "for every two markers"): every roll of the kind made by (or, onTarget, against) a unit holding the entry's markers
        /// carries the entry's modifier at the stated size per marker (or pair), and none without; AP rows are read as the AP modifiers the entry granted
        /// (RollModifier.Via). The unit is the roll step's frame's actor (attacker) or its target.
        /// </summary>
        static GameCheck PerMarker(string entry, string roll, int amount, bool onTarget, string quote, bool perTwo = false, bool ap = false) =>
            Exact(entry, $"{quote}: {roll} {amount:+0;-0} per {(perTwo ? "two markers" : "marker")}", (run, _) =>
                from x in Marked(run, entry)
                from r in x.Step.Rolls.Where(r => r.Roll == roll)
                let unit = onTarget ? x.Frame.Context.TargetUnitIds.FirstOrDefault() : x.Frame.Context.ActorUnitId ?? Guid.Empty
                let held = x.Held.GetValueOrDefault(unit)
                let expected = amount * (perTwo ? held / 2 : held)
                let actual = r.Modifiers.Where(m => ap ? m.Source == "AP" && m.Via == entry : m.Source == entry).Sum(m => m.Amount)
                // A fatigued unit's melee roll ("only hit on unmodified rolls of 6", p.9) takes no modifier.
                where expected != 0 && !(r.Threshold == 6 && r.Modifiers.Count == 0)
                select actual == expected ? null : $"{actual:+0;-0} with {held} markers, expected {expected:+0;-0}");

        /// <summary>Every remove_tokens Ask is answered with the most tokens offered.</summary>
        static void RemoveAll(ScriptBuilder s) =>
            s.Match("remove_tokens", (ask, _) => ask.Legal is Options o && o.Choices.MaxBy(c => int.TryParse(c.Id, out var n) ? n : -1) is { } most ? new Chosen(most.Id) : null);

        /// <summary>"Where X is the number of removed markers": every roll of the kind in an attack whose team removed the entry's markers (its attack-sequence's
        /// `spotter-tokens` value `tokens_&lt;entry&gt;`) carries the entry's modifier at the stated size per removed marker, and none in an attack that removed none;
        /// AP rows are read as the AP modifiers the entry granted (RollModifier.Via).</summary>
        static GameCheck PerRemoved(string entry, string roll, int amount, string quote, bool ap = false) =>
            Exact(entry, $"{quote}: {roll} {amount:+0;-0} per removed marker", (run, _) =>
                from r in LogQueries.Of(run).Rolls(roll)
                let attack = r.Frame.Nearest("attack-sequence")
                where attack is not null
                let removed = attack.Steps.Where(s => s.Name == "spotter-tokens").Sum(s => s.Value("tokens_" + entry))
                let expected = amount * removed
                let actual = r.Roll.Modifiers.Where(m => ap ? m.Source == "AP" && m.Via == entry : m.Source == entry).Sum(m => m.Amount)
                // A fatigued unit's melee roll ("only hit on unmodified rolls of 6", p.9) takes no modifier.
                where (expected != 0 || actual != 0) && !(r.Roll.Threshold == 6 && r.Roll.Modifiers.Count == 0)
                select actual == expected ? null : $"{actual:+0;-0} with {removed} removed, expected {expected:+0;-0}");

        /// <summary>"May remove markers from their target": the attack that asked removes exactly the markers its team chose, and an attack of the same
        /// unit at the same target that shares that choice (a weapon group in parts) removes none.</summary>
        static GameCheck RemovedAsChosen(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
                from s in LogQueries.Of(run).Steps.Where(s => s.Name == "spotter-tokens")
                let asked = s.Asks.Any(a => a.Question == "remove_tokens" && a.Prompt.StartsWith(entry + ":", StringComparison.Ordinal))
                let chosen = s.Value("tokens_" + entry)
                where asked || chosen > 0
                let removed = s.Step.Changes.OfType<GrantRemoved>().Count()
                select removed == (asked ? chosen : 0) ? null : $"{removed} removed, {chosen} chosen{(asked ? "" : " by an earlier attack")}");

        /// <summary>Round-start rallies: each rally die is one die at 4+, rolled for a Shaken unit, and the unit stops being Shaken exactly when it scores.</summary>
        static GameCheck Rallies(string entry, string quote) =>
            Exact(entry, quote, (_, log) =>
                from s in log.Steps.Where(s => s.Frame.Sequence == "rally" && s.Frame.Context.Action == entry && s.Rolls(entry).Any())
                let roll = s.Rolls(entry).Single()
                let cleared = s.Step.Changes.OfType<ConditionSet>().Any(c => c.Condition == "shaken" && !c.Present)
                select roll.Dice.Count != 1 ? $"{roll.Dice.Count} dice" : roll.Threshold != 4 ? $"threshold {roll.Threshold}"
                    : cleared != (roll.Dice[0].Raw >= 4) ? $"rolled {roll.Dice[0].Raw}, {(cleared ? "cleared" : "kept")} Shaken" : null);

        /// <summary>The Growths' markers: one per round start while the unit is on the table and not Shaken, at most four.</summary>
        static GameCheck GrowsEachRound(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var verdicts = new List<string?>();
                var held = new Dictionary<Guid, int>();
                foreach (var e in EventApplier.EffectiveLog(run.EffectiveLog))
                    if (e.Payload is RoundStarted r && r.Round >= 1)
                        foreach (var g in r.Changes.OfType<GrantAdded>().Where(g => g.Grant.EntryName == entry).GroupBy(g => g.UnitId))
                        {
                            var n = held[g.Key] = held.GetValueOrDefault(g.Key) + g.Count();
                            verdicts.Add(g.Count() != 1 ? $"{g.Count()} markers in one round" : n > 4 ? $"{n} markers" : null);
                        }
                return verdicts;
            });

        /// <summary>Regenerative Strength: in each melee attack of a unit carrying it, the attacks are one per model (each model's Blade) plus the wounds its models
        /// had ignored before it (assign's ignored_&lt;model&gt;).</summary>
        static GameCheck StrengthAttacks(string quote) =>
            Exact("Regenerative Strength", quote, (run, log) =>
            {
                var ignored = new Dictionary<Guid, int>();
                var dead = new HashSet<Guid>();
                var verdicts = new List<string?>();
                foreach (var s in log.Steps)
                {
                    foreach (var mv in s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves)) if (mv.To is OnShelf) dead.Add(mv.ModelId); else dead.Remove(mv.ModelId);
                    if (s.Step.Step == "determine-attacks" && s.Frame.Context.ActorUnitId is { } u && Holds(run, u, "Regenerative Strength") && s.Nearest(MeleeSequence) is not null
                        && s.Value("eligible_models") == run.Final.Units[u].Definition.Models.Count(m => !dead.Contains(m.Id)))
                    {
                        var markers = run.Final.Units[u].Definition.Models.Where(m => !dead.Contains(m.Id)).Sum(m => ignored.GetValueOrDefault(m.Id));
                        if (markers > 0) verdicts.Add(s.Value("attacks") == s.Value("eligible_models") + markers ? null : $"{s.Value("attacks")} attacks for {s.Value("eligible_models")} models and {markers} markers");
                    }
                    if (s.Step.Step == "assign")
                        foreach (var (k, v) in s.Step.Values ?? new Dictionary<string, int>())
                            if (k.StartsWith("ignored_", StringComparison.Ordinal) && Guid.TryParseExact(k["ignored_".Length..], "N", out var m)) ignored[m] = ignored.GetValueOrDefault(m) + v;
                }
                return verdicts;
            });
    }
}
