using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>
        /// A second activation in a round, once per game: the activation's own write records the action and then clears Fatigued (the engine's
        /// activation changes), so the unit is read as the payloads that follow its second action leave it, before anything else happens.
        /// </summary>
        static GameCheck SecondActivationClears(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var verdicts = new List<string?>();
                var actions = new Dictionary<(Guid, int), int>();
                var seconds = new Dictionary<Guid, int>();
                var fatigued = new HashSet<Guid>();
                Guid? pending = null;
                foreach (var (p, round) in Rounds(run))
                {
                    var conditions = p is StepCompleted s ? s.Changes.OfType<ConditionSet>() : p is ConditionSet cs ? [cs] : [];
                    if (pending is { } u && p is not ConditionSet)
                    {
                        verdicts.Add(fatigued.Contains(u) ? "still Fatigued once its second activation began" : null);
                        pending = null;
                    }
                    foreach (var c in conditions)
                        if (c.Condition == "fatigued") { if (c.Present) fatigued.Add(c.Subject.Id); else fatigued.Remove(c.Subject.Id); }
                    if (p is RoundStarted) fatigued.Clear();
                    if (p is not ActionRecorded a || !Holds(run, a.UnitId, entry)) continue;
                    var n = actions[(a.UnitId, round)] = actions.GetValueOrDefault((a.UnitId, round)) + 1;
                    if (n < 2) continue;
                    seconds[a.UnitId] = seconds.GetValueOrDefault(a.UnitId) + 1;
                    if (seconds[a.UnitId] > 1) verdicts.Add("activated a second time in two rounds"); else pending = a.UnitId;
                }
                if (pending is { } last) verdicts.Add(fatigued.Contains(last) ? "still Fatigued once its second activation began" : null);
                return verdicts;
            });

        /// <summary>A pick entry whose target is a model: every rule_target Ask of the entry offers enemy models, never units.</summary>
        static GameCheck PicksModels(string entry, string quote) =>
            Exact(entry, $"{quote}: models offered", (run, log) =>
                from a in log.Asks
                where a.Ask.Question == "rule_target" && a.Ask.Prompt.StartsWith(entry + ":", StringComparison.Ordinal)
                select a.Ask.Legal is PieceSelection p && p.Eligible.Count > 0 && p.Eligible.All(run.Final.Models.ContainsKey) ? null : "offered no model or a unit");
    }
}
