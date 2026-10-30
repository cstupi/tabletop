using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>
        /// "as long as they aren't Fatigued": every melee hit roll at a unit carrying the entry carries its -1 when that unit was not Fatigued as the roll
        /// was made, and none when it was. A fatigued attacker's roll (p.9, unmodified 6s only) is left out.
        /// </summary>
        static GameCheck DodgeFresh(string quote) =>
            GameCheck.Each($"Dodge: {quote}: only while not Fatigued", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = (from s in log.StepsOf(AttackSequence, "roll-to-hit")
                                where s.Frame.Nearest(MeleeSequence) is not null && s.Frame.Context.TargetUnitIds is [var t, ..] && Holds(run, t, "Dodge")
                                let target = s.Frame.Context.TargetUnitIds[0]
                                from r in s.Rolls("hit")
                                where !(r.Roll.Threshold == 6 && r.Roll.Modifiers.Count == 0)
                                let fatigued = log.HasCondition(target, "fatigued", s.Seq)
                                let dodged = r.Roll.Modifiers.Any(m => m.Source == "Dodge")
                                select dodged == !fatigued ? null : fatigued ? "-1 at a Fatigued unit" : "no -1 at a unit not Fatigued").ToList();
                return Reasons(verdicts);
            }, ["Dodge"]);

        /// <summary>
        /// "+1 to hit rolls in melee on the round in which it deploys via this rule": every melee hit roll of a holding unit carries the entry's +1 in the
        /// round it arrived from Ambush and not in a later one; at least one melee falls in the arrival round.
        /// </summary>
        static GameCheck PrecisionOnArrival(string quote) =>
            GameCheck.Each($"Infiltrating Precision Strike: {quote}", run =>
            {
                const string entry = "Infiltrating Precision Strike";
                var log = LogQueries.Of(run);
                var arrived = Arrivals(run, u => Holds(run, u, entry)).GroupBy(a => run.Final.Models[a.Model].UnitId).ToDictionary(g => g.Key, g => g.Min(a => a.Round));
                var rolls = (from s in log.StepsOf(AttackSequence, "roll-to-hit")
                             where s.Frame.Nearest(MeleeSequence) is not null && s.Frame.Context.ActorUnitId is { } u && arrived.ContainsKey(u)
                             from r in s.Rolls("hit")
                             where !(r.Roll.Threshold == 6 && r.Roll.Modifiers.Count == 0)
                             select (Arrival: s.Frame.Round == arrived[s.Frame.Context.ActorUnitId!.Value], Plus: r.Roll.Modifiers.Any(m => m.Source == entry && m.Amount == 1), s.Frame.Round)).ToList();
                var reasons = Reasons([.. rolls.Select(x => x.Arrival == x.Plus ? null : x.Plus ? $"+1 in round {x.Round}, after the arrival round" : $"no +1 in the arrival round {x.Round}")]).ToList();
                if (!rolls.Any(x => x.Arrival)) reasons.Add("no melee in the arrival round");
                return reasons;
            }, ["Infiltrating Precision Strike"]);

        /// <summary>
        /// The unit behind the wall shoots the marked unit: every attack shot at the opponents is made while they hold the Mark's grant and lists Indirect as
        /// acting (p.14's "may target enemies that are not in line of sight"), and there is at least one.
        /// </summary>
        static GameCheck HiddenUnitShot(string entry, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                foreach (var attack in log.FramesOf(AttackSequence).Where(f => f.Context.TargetUnitIds.Count > 0 && f.Nearest(ShootingSequence) is not null))
                {
                    var granted = GrantsBeforeH(log, entry, attack.Context.TargetUnitIds[0], attack.Seq) > 0;
                    var shooting = attack.Nearest(ShootingSequence)!;
                    var indirect = log.Descendants(shooting).Prepend(shooting).SelectMany(f => f.Steps).Any(s => s.Lists("Indirect"));
                    verdicts.Add(!granted ? "shot a unit holding no grant" : indirect ? null : "shot while marked, Indirect not acting");
                }
                return Reasons(verdicts);
            }, [entry]);
    }
}
