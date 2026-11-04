using System.Runtime.CompilerServices;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly ConditionalWeakTable<SuiteGameRun, LogQueries> QueriesM = new();
        static LogQueries QueryM(SuiteGameRun run) => QueriesM.GetValue(run, LogQueries.Of);

        /// <summary>The range the attack measured (determine-attacks' range_hundredths, p.5 base to base), in inches; null when it recorded none.</summary>
        static double? MeasuredM(SuiteGameRun run, LoggedAttack a) =>
            QueryM(run).Frame(a.Frame.FrameId)?.Steps.FirstOrDefault(s => s.Name == "determine-attacks")?.Step.Values is { } v
                && v.TryGetValue("range_hundredths", out var h) ? h / 100.0 : null;

        /// <summary>The occasion's attacks measured from over that many inches ("over half of this weapon's range away").</summary>
        static Occasion BeyondM(Occasion occasion, double half) => (run, a) => occasion(run, a) && MeasuredM(run, a) > half;

        /// <summary>The occasion's attacks measured from that many inches or less.</summary>
        static Occasion WithinM(Occasion occasion, double half) => (run, a) => occasion(run, a) && MeasuredM(run, a) <= half;

        /// <summary>
        /// The target's size as the attack began: a unit of several models, its models on the table against its starting count; a single model, its
        /// Tough value less its wounds (0 on the shelf) against its Tough value.
        /// </summary>
        static (int Remaining, int Starting)? SizeAtM(SuiteGameRun run, LoggedAttack a)
        {
            var q = QueryM(run);
            if (a.Target is not { } t || !run.Final.Units.TryGetValue(t, out var unit) || q.Frame(a.Frame.FrameId) is not { } f) return null;
            var models = unit.Definition.Models;
            if (models.Count > 1) return (q.Living(t, f.Seq), models.Count);
            var tough = models[0].Properties.FirstOrDefault(p => p.Name == "Tough")?.Parameter ?? 1;
            return q.LocationsBefore(f.Seq)[models[0].Id] is OnShelf ? (0, tough) : (tough - q.WoundsOn(models[0].Id, f.Seq), tough);
        }

        /// <summary>The occasion's attacks at a target with more than half its size or Tough value left (overHalf), or half or less.</summary>
        static Occasion SizedM(Occasion occasion, bool overHalf) => (run, a) => occasion(run, a) && SizeAtM(run, a) is var (r, s) && r * 2 > s == overHalf;

        static IReadOnlyList<(LoggedStep Block, RollRecord Roll)> DefenseRollsM(SuiteGameRun run, Occasion occasion) =>
            [.. from a in Of(run, ArmyLog.Of(run.EffectiveLog), occasion) from s in a.Blocks from r in s.Rolls("defense") select (s, r)];

        /// <summary>
        /// "must re-roll unmodified Defense results of 6": on every defense roll of the occasion's attacks exactly the dice that came up 6 are re-rolled,
        /// and at least one did.
        /// </summary>
        static GameCheck RerollsSeen(string entry, Occasion occasion, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var rolls = DefenseRollsM(run, occasion);
                var reasons = Reasons([.. rolls.Select(x =>
                {
                    var expected = x.Roll.Dice.Select((d, i) => (d, i)).Where(y => y.d.Raw == 6).Select(y => y.i).ToList();
                    var rerolled = x.Block.Rerolls.Where(z => z.RollId == x.Roll.Id).SelectMany(z => z.Replaced).Select(z => z.Index).Order().ToList();
                    return expected.SequenceEqual(rerolled) ? null : $"re-rolled dice [{string.Join(",", rerolled)}] of {Faces(x.Roll.Dice)}, expected [{string.Join(",", expected)}]";
                })]).ToList();
                if (rolls.Count > 0 && !rolls.Any(x => x.Roll.Dice.Any(d => d.Raw == 6))) reasons.Add("no unmodified 6 to block came up");
                return reasons;
            }, [entry]);

        /// <summary>The rule idle where its condition fails: no defense die of the occasion's attacks is re-rolled, and at least one came up 6.</summary>
        static GameCheck NoReroll(string entry, Occasion occasion, string quote) =>
            GameCheck.Each($"{entry}: {quote}: no re-roll", run =>
            {
                var rolls = DefenseRollsM(run, occasion);
                var reasons = Reasons([.. rolls.Select(x => x.Block.Rerolls.Any(z => z.RollId == x.Roll.Id) ? $"re-rolled dice of {Faces(x.Roll.Dice)}" : null)]).ToList();
                if (rolls.Count > 0 && !rolls.Any(x => x.Roll.Dice.Any(d => d.Raw == 6))) reasons.Add("no unmodified 6 to block came up");
                return reasons;
            }, [entry]);

        /// <summary>
        /// A positive defense modifier taken away: no defense roll of the occasion's attacks carries the source, and every one whose cover verdict holds
        /// (RollRecord.Cover) lists the source's +1 as ignored by the entry; at least one does.
        /// </summary>
        static GameCheck IgnoredModifier(string entry, string source, Occasion occasion, string quote) =>
            GameCheck.Each($"{entry}: {quote}: {source} +1 ignored", run =>
            {
                var rolls = DefenseRollsM(run, occasion);
                var reasons = Reasons([.. rolls.Select(x => x.Roll.Modifiers.Any(m => m.Source == source) ? $"{source} on the defense roll"
                    : x.Roll.Cover is { InCover: true } && !(x.Roll.Ignored ?? []).Any(i => i.Source == source && i.By == entry && i.Amount == 1)
                        ? $"{source} not listed as ignored by {entry}: [{string.Join(", ", (x.Roll.Ignored ?? []).Select(i => $"{i.Source} by {i.By}"))}]" : null)]).ToList();
                if (!rolls.Any(x => x.Roll.Cover is { InCover: true })) reasons.Add("no defense roll in cover");
                return reasons;
            }, [entry]);

        /// <summary>
        /// The dice of every roll of that kind the expectation names (its count, and the case it is), against the count; at least one roll of the needed case.
        /// </summary>
        static GameCheck RollCount(string entry, string roll, Func<SuiteGameRun, LogQueries, Tabletop.Sim.Suite.LoggedStep, (int Dice, string Case)?> expected, string need, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var q = QueryM(run);
                var cases = (from s in q.Steps from r in s.Rolls(roll) let e = expected(run, q, s) where e is not null select (Roll: r, e.Value.Dice, e.Value.Case)).ToList();
                var reasons = Reasons([.. cases.Select(c => c.Roll.Dice.Count == c.Dice ? null : $"{c.Roll.Dice.Count} {roll} dice {c.Case}, expected {c.Dice}")]).ToList();
                if (!cases.Any(c => c.Case == need)) reasons.Add($"no {roll} roll {need}");
                return reasons;
            }, [entry]);

        /// <summary>
        /// Brace(X) on Impact: an impact step in a melee whose defender's weapons carry the entry rolls the charger's Impact(X) dice over its models on the
        /// table, less the defender's X unless it is Fatigued (or Shaken, counting as Fatigued).
        /// </summary>
        static (int Dice, string Case)? BraceDice(SuiteGameRun run, LogQueries q, Tabletop.Sim.Suite.LoggedStep s)
        {
            if (s.Name != "impact" || s.Frame.Sequence != MeleeSequence || s.Frame.Context.ActorUnitId is not { } charger || s.Frame.Context.TargetUnitIds is not [var defender, ..]) return null;
            var brace = WeaponParameterM(run, defender, "Brace");
            if (brace is null) return null;
            var at = q.LocationsBefore(s.Seq);
            var impact = run.Final.Units[charger].Definition.Models.Where(m => at[m.Id] is not OnShelf)
                .Sum(m => m.Properties.FirstOrDefault(p => p.Name == "Impact")?.Parameter ?? 0);
            var fatigued = q.HasCondition(defender, "fatigued", s.Seq) || q.HasCondition(defender, "shaken", s.Seq);
            return fatigued ? (impact, "at a Fatigued unit") : (Math.Max(0, impact - brace.Value), "at a unit not Fatigued");
        }

        /// <summary>The highest parameter of the entry on any weapon of the unit's models; null when none carries it.</summary>
        static int? WeaponParameterM(SuiteGameRun run, Guid unit, string entry) =>
            run.Final.Units[unit].Definition.Models.SelectMany(m => m.Weapons).SelectMany(w => w.Properties).Where(p => p.Name == entry)
                .Select(p => (int?)(p.Parameter ?? 0)).Max();

        /// <summary>
        /// Parry(X): every resolve-hits of an attack in a melee at a unit whose weapons carry the entry rolls the lesser of X per model on the table
        /// (each model's highest X) and the attack's hits, and its roll-to-block rolls the hits less the Parry dice that show 6+ as they stand; at
        /// least one hit is ignored.
        /// </summary>
        static GameCheck Parried(string entry, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var q = QueryM(run);
                var verdicts = new List<string?>();
                var ignoredAll = 0;
                foreach (var f in q.FramesOf("resolve-hits").Where(f => f.Parent is { Sequence: AttackSequence } p && p.Parent?.Sequence == MeleeSequence))
                {
                    var defender = f.Context.TargetUnitIds[0];
                    if (WeaponParameterM(run, defender, entry) is null) continue;
                    var attack = f.Parent!;
                    var hits = attack.Steps.SelectMany(s => s.Rolls("hit")).Sum(r => r.Successes)
                        + attack.Steps.Where(s => s.Name == "score-hits").Select(s => s.Value("extra_hits")).DefaultIfEmpty(0).Max();
                    var at = q.LocationsBefore(f.Seq);
                    var perModel = run.Final.Units[defender].Definition.Models.Where(m => at[m.Id] is not OnShelf)
                        .Sum(m => m.Weapons.SelectMany(w => w.Properties).Where(p => p.Name == entry).Select(p => p.Parameter ?? 0).DefaultIfEmpty(0).Max());
                    var dice = Math.Min(perModel, hits);
                    var rolls = f.Steps.Where(s => s.Name == "parry").SelectMany(s => s.Rolls(entry)).ToList();
                    var ignored = rolls.Sum(r => r.Successes);
                    ignoredAll += ignored;
                    var blocked = f.Steps.Where(s => s.Name == "roll-to-block").SelectMany(s => s.Rolls("defense")).Sum(r => r.Dice.Count);
                    verdicts.Add(rolls.Sum(r => r.Dice.Count) != dice ? $"{rolls.Sum(r => r.Dice.Count)} {entry} dice for {hits} hits and {perModel} per model, expected {dice}"
                        : rolls.Any(r => r.Dice.Any(d => d.Success != d.Raw >= 6)) ? $"{entry} dice {string.Join(" ", rolls.SelectMany(r => r.Dice).Select(d => $"{d.Raw}{(d.Success ? "" : "x")}"))}: success is 6+"
                        : blocked != Math.Max(0, hits - ignored) ? $"{blocked} dice blocked for {hits} hits less {ignored} ignored" : null);
                }
                var reasons = Reasons(verdicts).ToList();
                if (ignoredAll == 0) reasons.Add("no hit ignored");
                return reasons;
            }, [entry]);

        /// <summary>The raw 1s, as the dice stand, the unit rolled to block the attacks under the frame whose actor's weapons (those the attack fires) carry the entry.</summary>
        static int OnesUnderM(SuiteGameRun run, LogQueries q, LoggedFrame frame, Guid target, string entry) =>
            (from f in q.Descendants(frame).Prepend(frame)
             where f.Sequence == AttackSequence && f.Context.TargetUnitIds is [var t, ..] && t == target
                && f.Context.ActorUnitId is { } actor && run.Final.Units[actor].Definition.Models.SelectMany(m => m.Weapons).Any(w => f.Context.WeaponIds.Contains(w.Id) && w.Properties.Any(p => p.Name == entry))
             from r in q.Descendants(f).Where(x => x.Sequence == "resolve-hits").SelectMany(x => x.Steps).Where(s => s.Name == "roll-to-block").SelectMany(s => s.Rolls("defense"))
             select r.Raw(1)).Sum();

        /// <summary>
        /// "enemies count as having taken N extra wounds when checking who won melee": every melee's compare holds, for each unit, the wounds it took in the
        /// activation plus N per raw 1 it rolled to block the entry's hits; and in one melee the charger won on those wounds alone (the defender took no
        /// more wounds than the charger, its count is higher) and the defender took the loser's morale test.
        /// </summary>
        static GameCheck CountedWounds(string entry, int perOne, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var q = QueryM(run);
                var verdicts = new List<string?>();
                var won = false;
                foreach (var compare in q.StepsOf(MeleeSequence, "compare").Where(s => s.Step.Values?.ContainsKey("charger_wounds") == true))
                {
                    var melee = compare.Frame;
                    if (melee.Context.ActorUnitId is not { } charger || melee.Context.TargetUnitIds is not [var defender, ..]) continue;
                    var from = melee.Ancestors.LastOrDefault()?.Seq ?? melee.Seq;
                    int Expected(Guid unit, Guid enemy) => q.WoundsTaken(unit, from, compare.Seq) + perOne * OnesUnderM(run, q, melee, unit, entry);
                    var (c, d) = (Expected(charger, defender), Expected(defender, charger));
                    verdicts.Add(compare.Value("charger_wounds") != c || compare.Value("defender_wounds") != d
                        ? $"charger_wounds {compare.Value("charger_wounds")}, defender_wounds {compare.Value("defender_wounds")}, expected {c} and {d}" : null);
                    var tested = q.Descendants(melee).Any(f => f.Sequence == "morale-test" && f.Context.ActorUnitId == defender);
                    if (compare.Value("loser") == 2 && q.WoundsTaken(defender, from, compare.Seq) <= q.WoundsTaken(charger, from, compare.Seq) && d > c && tested) won = true;
                }
                var reasons = Reasons(verdicts).ToList();
                if (!won) reasons.Add("no melee won on the counted wounds alone with the defender's morale test");
                return reasons;
            }, [entry]);

        /// <summary>
        /// "or if shooting wounds would trigger Morale Tests" (3.6.0 p.10: wounds that leave the unit with half or less of its starting size): on a target of
        /// one-wound models, a shooting attack with the entry is followed in its activation by the target's morale test exactly when its models left, less
        /// N per raw 1 it rolled to block, are half its starting size or less; with `tests`, in one the models left alone are over half and it tests; without, in one
        /// the models left less N per raw 1 are over half and it does not (a failed block that is not a 1 counts nothing).
        /// </summary>
        static GameCheck CountedMorale(string entry, int perOne, string quote, bool tests = true) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var q = QueryM(run);
                var verdicts = new List<string?>();
                var shown = false;
                foreach (var attack in q.FramesOf(AttackSequence).Where(f => f.Nearest(ShootingSequence) is not null && f.Context.TargetUnitIds.Count > 0))
                {
                    var target = attack.Context.TargetUnitIds[0];
                    var ones = OnesUnderM(run, q, attack, target, entry);
                    if (ones == 0) continue;
                    var next = q.Activations.FirstOrDefault(a => a.Seq > attack.Seq)?.Seq ?? long.MaxValue;
                    var test = q.FramesOf("morale-test").FirstOrDefault(f => f.Seq > attack.Seq && f.Seq < next && f.Context.ActorUnitId == target && f.Context.Action == "wounds");
                    var starting = run.Final.Units[target].Definition.Models.Count;
                    var left = q.Living(target, test?.Seq ?? next);
                    var expected = Math.Max(0, left - perOne * ones) * 2 <= starting;
                    verdicts.Add((test is not null) == expected ? null : $"{left} of {starting} left, {ones} ones: {(test is null ? "no test" : "a test")}");
                    if (tests ? test is not null && left * 2 > starting : test is null && expected == false) shown = true;
                }
                var reasons = Reasons(verdicts).ToList();
                if (!shown) reasons.Add(tests ? "no morale test taken on the counted wounds alone" : "no attack judged without a test");
                return reasons;
            }, [entry]);
    }
}
