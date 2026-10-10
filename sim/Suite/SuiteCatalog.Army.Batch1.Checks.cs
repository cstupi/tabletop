using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1's checks beyond the army checks: what a rule takes away (cover, a penalty, a roll), read from the logged rolls.</summary>
    private static partial class ArmyGames
    {
        /// <summary>p.11 cover is "+1 to Defense rolls when blocking hits from shooting attacks": no defense roll of the occasion's attacks carries it.</summary>
        static GameCheck NoCover(string entry, Occasion occasion, string quote) =>
            Exact(entry, $"{quote}: no cover on defense", (run, log) =>
                from a in Of(run, log, occasion)
                from s in a.Blocks
                from r in s.Rolls("defense")
                select r.Modifiers.Any(m => m.Source == "cover") ? "a defense roll with cover +1" : null);

        /// <summary>"Ignores all negative modifiers to this weapon": no hit roll of the occasion's attacks carries a negative modifier (a fatigued
        /// unit's roll, p.9 "only hit on unmodified rolls of 6", carries none and is left out).</summary>
        static GameCheck NoPenalty(string entry, Occasion occasion, string quote) =>
            Exact(entry, $"{quote}: no negative modifier to hit", (run, log) =>
                from a in Of(run, log, occasion)
                where a.Hit is not null
                from r in a.Hit!.Rolls("hit")
                where !(r.Threshold == 6 && r.Modifiers.Count == 0)
                select r.Modifiers.Where(m => m.Amount < 0).Select(m => $"{m.Source} {m.Amount}").ToList() is { Count: > 0 } bad ? $"a hit roll under {string.Join(", ", bad)}" : null);

        /// <summary>A rule that ignores a roll-to-ignore rule: under every occasion attack whose block failed a die, no step rolls that rule's dice.</summary>
        static GameCheck NoRoll(string entry, Occasion occasion, string roll, string quote) =>
            Exact(entry, $"{quote}: no {roll} roll", (run, log) =>
                from a in Of(run, log, occasion)
                where a.Blocks.Any(b => b.Rolls("defense").Any(r => b.Now(r).Any(d => !d.Success)))
                let rolled = log.Steps.Count(s => s.Ancestors.Any(f => f.FrameId == a.Frame.FrameId) && s.Rolls(roll).Any())
                select rolled == 0 ? null : $"{rolled} steps rolled {roll}");

        /// <summary>
        /// A range bonus: the occasion's attacks made at a target beyond the weapon's printed range (as the attack began) are within the printed
        /// range plus the bonus. An attack beyond the printed range is the bonus's only evidence, so a game with none fails.
        /// </summary>
        static GameCheck RangeBonus(string entry, Occasion occasion, double printed, double bonus, string quote) =>
            Exact(entry, $"{quote}: {printed}\" + {bonus}\"", (run, log) =>
                from a in Of(run, log, occasion)
                where a.Attacker is not null && a.Target is not null && PatternLog.For(run).Attacks.ContainsKey(a.Frame.FrameId)
                let apart = UnitApart(run, PatternLog.For(run).Attacks[a.Frame.FrameId].At, a.Attacker!.Value, a.Target!.Value)
                where apart > printed
                select apart <= printed + bonus ? null : $"shot a unit {apart:0.##}\" away");

        /// <summary>Low ruins (a cover area, p.11) over the opponents' whole zone, so their units are in cover when shot.</summary>
        static ScenarioBuilder EnemyRuins(ScenarioBuilder b) => b
            .Terrain("ruin", new Point3(18, 6, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(34, 12), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Cover, null)]);
    }
}
