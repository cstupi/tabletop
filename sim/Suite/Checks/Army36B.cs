using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>
        /// "This model and its unit get Counter on its weapons in melee." Each melee a holding unit is charged in, after its team chose to strike first: the
        /// defender's attacks before the charger's first carry the melee weapons of every model of the unit then on the table, not only the carrier's.
        /// </summary>
        static GameCheck CounterInMeleeForTheUnit(string entry) =>
            GameCheck.Each($"{entry}: \"This model and its unit get Counter on its weapons in melee\"", run =>
            {
                var log = LogQueries.Of(run);
                var holders = run.Final.Units.Values.Where(u => u.Definition.Models.Any(m => m.Properties.Any(p => p.Name == entry))).Select(u => u.Id).ToHashSet();
                var verdicts = (
                    from melee in log.FramesOf("melee")
                    where melee.Context.TargetUnitIds is [var d, ..] && holders.Contains(d)
                    let defender = melee.Context.TargetUnitIds[0]
                    where log.AsksOf("counter").Any(a => a.Frame?.Id == melee.Id && a.Answer is Chosen { OptionId: "strike" })
                    let first = log.Descendants(melee).Where(f => f.Sequence == "attack-sequence").OrderBy(f => f.Seq).TakeWhile(f => f.Context.ActorUnitId == defender)
                        .SelectMany(f => f.Context.WeaponIds).ToHashSet()
                    let living = log.LocationsBefore(melee.Seq).Where(kv => kv.Value is not OnShelf && log.UnitOf(kv.Key) == defender).Select(kv => kv.Key).ToHashSet()
                    let expected = run.Final.Units[defender].Definition.Models.Where(m => living.Contains(m.Id)).SelectMany(m => m.Weapons.Where(w => !w.Stats.ContainsKey("Range")).Select(w => w.Id)).ToHashSet()
                    select first.SetEquals(expected) ? null : $"struck first with {first.Count} of {expected.Count} melee weapons").ToList();
                return verdicts.Count == 0 ? ["no occasion in the log"]
                    : verdicts.OfType<string>().GroupBy(v => v).Select(g => $"{g.Count()} of {verdicts.Count} occasions: {g.Key}");
            }, [entry]);
    }
}
