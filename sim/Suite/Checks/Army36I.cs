namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>A rule bounded by range, in a game that stands inside the bound: every block of the occasion's attacks rolls under no AP.</summary>
        static GameCheck NoAp(string entry, Occasion occasion, string quote) =>
            Exact(entry, $"{quote}: no AP", (run, log) =>
                from a in Of(run, log, occasion)
                from s in a.Blocks
                where s.Rolls("defense").Any()
                let aps = s.Rolls("defense").SelectMany(r => r.Modifiers).Where(m => m.Source == "AP" && m.Amount != 0).Select(m => m.Amount).ToList()
                select aps.Count == 0 ? null : $"defense under AP [{string.Join(",", aps)}]");

        /// <summary>An Enhanced rule replaces its base's modifier: every roll the Enhanced rule modified carries no modifier of the base.</summary>
        static GameCheck ModifierReplaced(string enhanced, string baseRule, string quote) =>
            Exact(enhanced, quote, (_, log) =>
                from s in log.Steps
                from r in s.Step.Rolls
                where r.Modifiers.Any(m => m.Source == enhanced)
                select r.Modifiers.Any(m => m.Source == baseRule) ? $"{baseRule} modified the {r.Roll} roll beside {enhanced}" : null);
    }
}
