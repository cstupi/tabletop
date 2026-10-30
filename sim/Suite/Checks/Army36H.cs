using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>"Place one token on them": every step that places the entry's tokens places one on each unit it marks.</summary>
        static GameCheck OneTokenH(string entry, string quote) =>
            Exact(entry, $"{quote}: one token a unit", (run, _) =>
                from x in Marked(run, entry)
                let added = x.Step.Changes.OfType<GrantAdded>().Where(g => g.Grant.EntryName == entry).ToList()
                where added.Count > 0
                from unit in added.GroupBy(g => g.UnitId)
                select unit.Count() == 1 ? null : $"{unit.Count()} tokens placed on one unit");

        /// <summary>The grants of the entry the unit held before that seq, read off the log's grant changes.</summary>
        static int GrantsBeforeH(LogQueries log, string entry, Guid unit, long seq)
        {
            var held = new Dictionary<Guid, Guid>();
            foreach (var e in log.Events.TakeWhile(e => e.Seq < seq))
                foreach (var c in LogQueries.ChangesOf(e.Payload))
                    switch (c)
                    {
                        case GrantAdded g when g.Grant.EntryName == entry: held[g.Grant.Id] = g.UnitId; break;
                        case GrantRemoved r: held.Remove(r.GrantId); break;
                    }
            return held.Values.Count(u => u == unit);
        }

        /// <summary>
        /// "which gets Counter in Melee" (3.6.0 p.13 Counter: "May strike first with this weapon when charged"): each melee a holding unit is charged in while
        /// it holds the Buff's grant, its team is asked to strike first, and after "strike" its attacks before the charger's carry the melee weapons (no Range,
        /// or Assault) of every model of the unit then on the table; charged with no grant held, it is not asked.
        /// </summary>
        static GameCheck CounterBuffH(string entry, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                var granted = 0;
                foreach (var melee in log.FramesOf("melee").Where(m => m.Context.TargetUnitIds is [var d, ..] && Holds(run, d, entry)))
                {
                    var defender = melee.Context.TargetUnitIds[0];
                    var asks = log.AsksOf("counter").Where(a => a.Frame?.Id == melee.Id).ToList();
                    if (GrantsBeforeH(log, entry, defender, melee.Seq) == 0) { verdicts.Add(asks.Count == 0 ? null : "asked to strike first with no grant held"); continue; }
                    granted++;
                    if (!asks.Any(a => a.Answer is Chosen { OptionId: "strike" })) { verdicts.Add("not asked to strike first while holding the grant"); continue; }
                    var first = log.Descendants(melee).Where(f => f.Sequence == "attack-sequence").OrderBy(f => f.Seq).TakeWhile(f => f.Context.ActorUnitId == defender)
                        .SelectMany(f => f.Context.WeaponIds).ToHashSet();
                    var living = log.LocationsBefore(melee.Seq).Where(kv => kv.Value is not OnShelf && log.UnitOf(kv.Key) == defender).Select(kv => kv.Key).ToHashSet();
                    var expected = run.Final.Units[defender].Definition.Models.Where(m => living.Contains(m.Id))
                        .SelectMany(m => m.Weapons.Where(w => !w.Stats.ContainsKey("Range") || w.Properties.Any(p => p.Name == "Assault")).Select(w => w.Id)).ToHashSet();
                    verdicts.Add(first.SetEquals(expected) ? null : $"struck first with {first.Count} of {expected.Count} melee weapons");
                }
                return granted == 0 ? ["no melee charged at a unit holding the grant"] : Reasons(verdicts);
            }, [entry]);
    }
}
