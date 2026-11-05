namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>
        /// An Enhanced rule replaces its base: every deal-wounds frame where the Enhanced rule rolled to ignore wounds has no roll of the base rule.
        /// </summary>
        static GameCheck BaseReplaced(string enhanced, string baseRule, string quote) =>
            Exact(enhanced, quote, (_, log) =>
                from frame in log.Steps.Where(s => s.Step.Step == "assign").GroupBy(s => s.Frame.FrameId)
                where frame.Any(s => s.Rolls(enhanced).Any())
                select frame.Any(s => s.Rolls(baseRule).Any()) ? $"{baseRule} rolled beside {enhanced}" : null);

        /// <summary>
        /// A wound-ignoring rule bounded by range: every deal-wounds frame whose block dealt wounds (the shooter 9" or less away by construction)
        /// has no roll of the rule.
        /// </summary>
        static GameCheck NoIgnoreWithin(string entry, string quote) =>
            Exact(entry, quote, (_, log) =>
                from frame in log.Steps.Where(s => s.Step.Step == "assign").GroupBy(s => s.Frame.FrameId)
                let parent = frame.First().Ancestors.FirstOrDefault()?.FrameId
                let block = log.Steps.Take(frame.First().Index).LastOrDefault(s => s.Frame.FrameId == parent && s.Step.Step == "roll-to-block")
                where block is not null && block.Rolls("defense").Sum(r => block.Now(r).Count(d => !d.Success)) + block.Value("extra_wounds") > 0
                select frame.Any(s => s.Rolls(entry).Any()) ? $"{entry} rolled for a shot from 9\" or less" : null);
    }
}
