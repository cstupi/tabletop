using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 4's log reading (army-book rules spec → Authored rules 4): placements, choices, who activated when. Read from the log; nothing here computes a rule.</summary>
    private static partial class ArmyGames
    {
        /// <summary>Every choose_effect Ask of the entry is answered with the option whose name holds the text; every use of it is taken (UsePicks).</summary>
        static Action<ScriptBuilder> Choose(string entry, string option) => s => s
            .Match("choose_effect", (ask, _) => ask.Prompt.StartsWith(entry + ":", StringComparison.Ordinal) ? AskPicks.Option(ask, c => c.Id.Contains(option, StringComparison.Ordinal)) : null);

        /// <summary>A placement "anywhere fully within D3+1"": each of the entry's placements allows its die's D3 (p.4: a D6 halved, rounding up) plus one.</summary>
        static GameCheck PlacedWithinD3Plus1(string entry, string quote) =>
            Exact(entry, quote, (_, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
                let roll = log.Steps.LastOrDefault(s => s.Frame.FrameId == a.Frame!.FrameId && s.Rolls(entry).Any())?.Rolls(entry).Single()
                let inches = roll?.Dice.Sum(d => (d.Raw + 1) / 2 + 1)
                select roll is null ? "no distance roll" : a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\" after {Faces(roll.Dice)}");

        /// <summary>The events of the effective log in order, with the round each falls in.</summary>
        static IEnumerable<(EventPayload Payload, int Round)> Rounds(SuiteGameRun run)
        {
            var round = 0;
            foreach (var e in EventApplier.EffectiveLog(run.EffectiveLog))
            {
                if (e.Payload is RoundStarted r) round = r.Round;
                yield return (e.Payload, round);
            }
        }

        /// <summary>Coordinate: every unit a coordinate frame activated is another unit of the same side that had no action on record that round.</summary>
        static GameCheck CoordinateActivates(string quote) =>
            Exact("Coordinate", quote, (run, log) =>
            {
                var recorded = new Dictionary<Guid, int>();
                var verdicts = new List<string?>();
                var frames = new Dictionary<Guid, SequenceStarted>();
                foreach (var (p, round) in Rounds(run))
                {
                    switch (p)
                    {
                        case SequenceStarted s: frames[s.FrameId] = s; break;
                        case ActionRecorded a: recorded[a.UnitId] = round; break;
                        case StepCompleted { Step: "coordinate-pick" } c when frames.TryGetValue(c.FrameId, out var f) && f.Context.ActorUnitId is { } by:
                            foreach (var a in c.Changes.OfType<ActionRecorded>())
                                verdicts.Add(a.UnitId == by ? "activated itself"
                                    : run.Final.Units[a.UnitId].ParticipantId != run.Final.Units[by].ParticipantId ? "activated an enemy unit"
                                    : recorded.GetValueOrDefault(a.UnitId, -1) == round ? "activated a unit that had activated this round" : null);
                            foreach (var a in c.Changes.OfType<ActionRecorded>()) recorded[a.UnitId] = round;
                            break;
                    }
                }
                return verdicts;
            });

        /// <summary>Delayed Action: each Delay answer ends the turn with the unit unrecorded, at most once a round for a unit.</summary>
        static GameCheck Delays(string quote) =>
            Exact("Delayed Action", quote, (run, _) =>
            {
                var verdicts = new List<string?>();
                var used = new HashSet<(Guid, int)>();
                Guid? waiting = null;
                foreach (var (p, round) in Rounds(run))
                    switch (p)
                    {
                        case AskClosed c when c.Answers.FirstOrDefault()?.Answer is Chosen { OptionId: var id } && id.EndsWith("/Delay", StringComparison.Ordinal)
                            && Guid.TryParseExact(id[..32], "N", out var unit):
                            verdicts.Add(used.Add((unit, round)) ? null : "delayed twice in a round");
                            waiting = unit;
                            break;
                        case ActionRecorded a when waiting == a.UnitId: verdicts.Add("recorded an action on its delay"); waiting = null; break;
                        case TurnEnded: waiting = null; break;
                    }
                return verdicts;
            });

        /// <summary>A second activation ("once per game … may be activated even if it had already activated this round … stops being fatigued"): at most one round
        /// of the game records two actions for a unit, and a fatigued unit's second action comes with fatigue cleared.</summary>
        static GameCheck SecondActivation(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var verdicts = new List<string?>();
                var actions = new Dictionary<(Guid, int), int>();
                var seconds = new Dictionary<Guid, int>();
                var fatigued = new HashSet<Guid>();
                ConditionSet? last = null;
                foreach (var (p, round) in Rounds(run))
                {
                    foreach (var c in p is StepCompleted s ? s.Changes.OfType<ConditionSet>() : p is ConditionSet cs ? [cs] : [])
                        if (c.Condition == "fatigued") { if (c.Present) fatigued.Add(c.Subject.Id); else fatigued.Remove(c.Subject.Id); last = c; }
                    if (p is RoundStarted) fatigued.Clear();
                    if (p is not ActionRecorded a || !Holds(run, a.UnitId, entry)) continue;
                    var n = actions[(a.UnitId, round)] = actions.GetValueOrDefault((a.UnitId, round)) + 1;
                    if (n < 2) continue;
                    seconds[a.UnitId] = seconds.GetValueOrDefault(a.UnitId) + 1;
                    verdicts.Add(seconds[a.UnitId] > 1 ? "activated a second time in two rounds" : fatigued.Contains(a.UnitId) ? "still fatigued on its second activation" : null);
                }
                return verdicts;
            });

        /// <summary>Instinctive's "closest valid target": every declare_targets Ask of a unit holding it offers one target.</summary>
        static GameCheck ClosestOnly(string entry, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in log.Asks
                where a.Ask.Question == "declare_targets" && a.Frame is { } f && Holds(run, f.Context.ActorUnitId, entry)
                select a.Ask.Legal is Assignment s && s.Targets.Count == 1 ? null : $"{(a.Ask.Legal as Assignment)?.Targets.Count} targets offered");

        /// <summary>A pick entry's move: every move Ask a use of the entry opened allows the stated distance, for models of a unit holding the required rule.</summary>
        static GameCheck PickMove(string entry, double inches, string required, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: PickRule } f && f.Context.Action is { } k && k.StartsWith(entry + "|", StringComparison.Ordinal)
                let region = a.Ask.Legal as MoveRegion
                select region is null ? "no move region" : region.Allowance != inches ? $"allowed {region.Allowance}\""
                    : !region.ModelIds.All(m => Holds(run, run.Final.Models[m].UnitId, required)) ? $"moved models without {required}" : null);

        /// <summary>
        /// A chosen effect's move ("you may use this rule so that they move +2"…"): the first move Ask of each action of a unit while it holds the
        /// condition the entry's choice set allows the action's p.7 distance plus the stated change; at most `uses` choices per unit in the game.
        /// </summary>
        static GameCheck ChosenAllowance(string entry, string condition, int? uses, string quote, params (string Action, double Inches)[] allowed) =>
            Exact(entry, $"{quote}: {string.Join(", ", allowed.Select(a => $"{a.Action} {a.Inches}\""))}", (run, log) =>
            {
                var held = new HashSet<Guid>();
                var chosen = new Dictionary<Guid, int>();
                var verdicts = new List<string?>();
                var seen = new HashSet<Guid>();
                var at = 0;
                foreach (var a in log.Asks)
                {
                    for (; at < a.Steps; at++)
                        foreach (var c in log.Steps[at].Step.Changes.OfType<ConditionSet>().Where(c => c.Condition == condition))
                        {
                            if (c.Present) { held.Add(c.Subject.Id); chosen[c.Subject.Id] = chosen.GetValueOrDefault(c.Subject.Id) + 1; }
                            else held.Remove(c.Subject.Id);
                        }
                    if (a.Ask.Question != "move" || a.Frame is not { } f || !allowed.Any(x => x.Action == f.Sequence) || !seen.Add(f.FrameId)) continue;
                    if (f.Context.ActorUnitId is not { } unit || !held.Contains(unit)) continue;
                    var inches = allowed.Single(x => x.Action == f.Sequence).Inches;
                    verdicts.Add(uses is { } u && chosen[unit] > u ? $"chosen {chosen[unit]} times"
                        : a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"{f.Sequence} allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");
                }
                return verdicts;
            });
    }
}
