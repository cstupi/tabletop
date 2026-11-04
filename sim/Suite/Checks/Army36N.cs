using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch N's log reading: the once-per-round pick, tokens spent one an attack, the defender's choice per attack, the dice move's count and
    /// movers, Ravage's dice under a grant, spell tokens lost, and Indirect off a Hold.</summary>
    private static partial class ArmyGames
    {
        /// <summary>The pick-rule frames a use of the entry started.</summary>
        static IEnumerable<LoggedFrame> UseFrames(LogQueries log, string entry) =>
            log.FramesOf(PickRule).Where(f => f.Context.Action is { } k && k.StartsWith(entry + "|", StringComparison.Ordinal));

        static Guid? TeamOfN(LogQueries log, Guid unit) => log.Deployments.FirstOrDefault(d => d.Unit == unit)?.Team;

        static bool Under(LoggedFrame frame, LoggedFrame root) => frame == root || frame.Ancestors.Contains(root);

        /// <summary>"you may move it": every move Ask of a use's spell-test is asked of the user's team and moves the tested unit's models.</summary>
        static GameCheck ForcedMoveByCaster(string entry, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                foreach (var use in UseFrames(log, entry))
                {
                    var tested = log.Descendants(use).FirstOrDefault(f => f.Sequence == "morale-test")?.Context.ActorUnitId;
                    var team = use.Context.ActorUnitId is { } u ? TeamOfN(log, u) : null;
                    foreach (var a in log.Asks.Where(a => a.Ask.Question == "move" && a.Frame is { Sequence: "spell-test" } f && Under(f, use)))
                        verdicts.Add(a.Ask.Teams is not [var t] || t != team ? "the move asked of another team"
                            : a.Ask.Legal is not MoveRegion m || m.ModelIds.Any(id => log.UnitOf(id) != tested) ? "the move is not the tested unit's" : null);
                }
                return Reasons(verdicts);
            }, [entry]);

        /// <summary>"Units may only be moved once per round in this way": no use's target Ask offers a unit a use of the entry moved earlier that round, and the
        /// game has a target Ask after such a move.</summary>
        static GameCheck MovedOncePerRound(string entry, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var log = LogQueries.Of(run);
                var uses = UseFrames(log, entry).ToList();
                var moved = (from s in log.Steps
                             where s.Frame.Sequence == "spell-test" && uses.Any(u => Under(s.Frame, u))
                             from mv in s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves)
                             where mv.To is OnTable
                             select (s.Seq, s.Frame.Round, Unit: log.UnitOf(mv.ModelId))).Distinct().ToList();
                var verdicts = new List<string?>();
                foreach (var a in log.Asks.Where(a => a.Ask.Question == "rule_target" && a.Ask.Prompt.StartsWith(entry + ":", StringComparison.Ordinal) && a.Frame is not null))
                {
                    var before = moved.Where(m => m.Seq < a.Seq && m.Round == a.Frame!.Round).Select(m => m.Unit).ToHashSet();
                    if (before.Count == 0) continue;
                    verdicts.Add(a.Ask.Legal is PieceSelection p && p.Eligible.Any(before.Contains) ? "offered a unit moved this round" : null);
                }
                return verdicts.Count == 0 ? ["no target offered after a unit was moved that round"] : Reasons(verdicts);
            }, [entry]);

        /// <summary>
        /// "remove one token from their target before rolling to block/hit": every attack at a unit holding tokens of the entry rolls under one token's worth
        /// (AP(+1), or +1 to hit) and spends exactly one by its end; an attack at a unit holding none gets nothing; the game shows an attack at two tokens and
        /// one at the last.
        /// </summary>
        static GameCheck OneTokenAnAttack(string entry, bool ap, string quote) =>
            GameCheck.Each($"{entry}: {quote}: one token an attack", run =>
            {
                var log = LogQueries.Of(run);
                var kind = ap ? "defense" : "hit";
                var grants = log.Events.SelectMany(e => LogQueries.ChangesOf(e.Payload)).OfType<GrantAdded>().Where(g => g.Grant.EntryName == entry).Select(g => g.Grant.Id).ToHashSet();
                var verdicts = new List<string?>();
                var read = new HashSet<int>();
                foreach (var attack in log.FramesOf(AttackSequence).Where(f => f.Context.TargetUnitIds.Count > 0))
                {
                    var held = GrantsBeforeH(log, entry, attack.Context.TargetUnitIds[0], attack.Seq);
                    var steps = log.Steps.Where(s => Under(s.Frame, attack)).ToList();
                    var rolls = steps.SelectMany(s => s.Rolls(kind)).Where(r => !(r.Roll.Threshold == 6 && r.Roll.Modifiers.Count == 0)).ToList();
                    int Granted(LoggedRoll r) => r.Roll.Modifiers.Where(m => ap ? m.Source == "AP" && m.Via == entry : m.Source == entry).Sum(m => m.Amount);
                    var removed = steps.SelectMany(s => s.Step.Changes).OfType<GrantRemoved>().Count(r => grants.Contains(r.GrantId));
                    if (held == 0) { verdicts.Add(rolls.Any(r => Granted(r) != 0) ? "a roll under the entry with no token held" : null); continue; }
                    if (rolls.Count == 0) { verdicts.Add(removed == 0 ? null : $"{removed} tokens spent with no {kind} roll"); continue; }
                    read.Add(held);
                    var want = ap ? -1 : 1;
                    verdicts.Add(rolls.FirstOrDefault(r => Granted(r) != want) is { } bad ? $"{Granted(bad):+0;-0} with {held} tokens, expected {want:+0;-0}"
                        : removed != 1 ? $"{removed} tokens spent by one attack with {held} held" : null);
                }
                var reasons = Reasons(verdicts).ToList();
                if (!read.Contains(2)) reasons.Add("no attack at a unit holding two tokens");
                if (!read.Contains(1)) reasons.Add("no attack at a unit holding its last token");
                return reasons;
            }, [entry]);

        /// <summary>
        /// "pick one effect … you may pick a different effect each time the unit is attacked": every attack at a unit carrying the entry asks the defender's team
        /// choose_effect once, and its rolls carry the chosen condition's modifier and no other option's; the game shows each option taken.
        /// </summary>
        static GameCheck Chosen(string entry, IReadOnlyList<(string Condition, string Roll, int Amount)> effects, string quote) =>
            GameCheck.Each($"{entry}: {quote}: the defender's pick each attack", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                var shown = new HashSet<string>();
                foreach (var attack in log.FramesOf(AttackSequence).Where(f => f.Context.TargetUnitIds is [var t, ..] && Holds(run, t, entry)))
                {
                    var defender = attack.Context.TargetUnitIds[0];
                    var asks = log.Asks.Where(a => a.Ask.Question == "choose_effect" && a.Frame == attack).ToList();
                    if (asks is not [var ask]) { verdicts.Add($"{asks.Count} choose_effect Asks in one attack"); continue; }
                    if (ask.Ask.Teams is not [var team] || team != TeamOfN(log, defender)) { verdicts.Add("asked of a team other than the defender's"); continue; }
                    var picked = (ask.Answer as Tabletop.Protocol.Chosen)?.OptionId;
                    var rolls = log.Steps.Where(s => Under(s.Frame, attack)).SelectMany(s => s.AllRolls).ToList();
                    string? bad = null;
                    foreach (var (condition, roll, amount) in effects)
                        foreach (var r in rolls.Where(r => r.Kind == roll))
                        {
                            var actual = r.Roll.Modifiers.Where(m => m.Source == entry).Sum(m => m.Amount);
                            var want = condition == picked ? amount : 0;
                            if (actual != want) bad ??= $"{roll} {actual:+0;-0} after \"{picked}\", expected {want:+0;-0}";
                            else if (condition == picked) shown.Add(condition);
                        }
                    verdicts.Add(bad);
                }
                var reasons = Reasons(verdicts).ToList();
                reasons.AddRange(effects.Where(e => !shown.Contains(e.Condition)).Select(e => $"no attack rolled under \"{e.Condition}\""));
                return reasons;
            }, [entry]);

        /// <summary>The rule's condition fails on every attack at its holders: no Ask of the question is opened, no roll carries the entry, and there is an attack.</summary>
        static GameCheck NoAsk(string question, string entry, string quote) =>
            GameCheck.Each($"{entry}: {quote}: no {question}", run =>
            {
                var log = LogQueries.Of(run);
                var attacks = log.FramesOf(AttackSequence).Where(f => f.Context.TargetUnitIds is [var t, ..] && Holds(run, t, entry)).ToList();
                var reasons = new List<string>();
                if (attacks.Count == 0) reasons.Add("no attack at a holder");
                if (log.AsksOf(question).Count() is var n and > 0) reasons.Add($"{n} {question} Asks");
                if (log.AllRolls.Any(r => r.Roll.Modifiers.Any(m => m.Source == entry))) reasons.Add($"a roll under {entry}");
                return reasons;
            }, [entry]);

        /// <summary>"up to 2D3"": every reposition of the entry rolls that many dice and allows their D3s' sum (p.4: a D6 halved, rounding up).</summary>
        static GameCheck RollCount(string entry, int dice, string quote) =>
            Exact(entry, $"{quote}: {dice} dice", (_, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
                let roll = log.Steps.LastOrDefault(s => s.Frame.FrameId == a.Frame!.FrameId && s.Rolls(entry).Any())?.Rolls(entry).Single()
                select roll is null ? "no distance roll" : roll.Dice.Count != dice ? $"{roll.Dice.Count} dice"
                    : a.Ask.Legal is MoveRegion m && m.Allowance == roll.Dice.Sum(d => (d.Raw + 1) / 2) ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\" after {Faces(roll.Dice)}");

        /// <summary>"move all models with this rule in it": every reposition of the entry offers its unit's living carriers alone, at most maxInches, and each
        /// model it moved is one of them, moved no further than the allowance; at least one model moves.</summary>
        static GameCheck MovedBy(string entry, double maxInches, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                var any = false;
                foreach (var frame in log.FramesOf("reposition").Where(f => f.Context.Action == entry && f.Context.ActorUnitId is not null))
                {
                    var ask = log.Asks.FirstOrDefault(a => a.Ask.Question == "move" && a.Frame == frame);
                    if (ask?.Ask.Legal is not MoveRegion region) continue;
                    var unit = frame.Context.ActorUnitId!.Value;
                    var carriers = log.LocationsBefore(ask.Seq).Where(kv => kv.Value is OnTable && log.UnitOf(kv.Key) == unit
                        && run.Final.Models[kv.Key].Slot.Properties.Any(p => p.Name == entry)).Select(kv => kv.Key).ToHashSet();
                    if (!carriers.SetEquals(region.ModelIds)) { verdicts.Add($"{region.ModelIds.Count} movers offered for {carriers.Count} carriers"); continue; }
                    if (region.Allowance > maxInches) { verdicts.Add($"allowed {region.Allowance}\""); continue; }
                    var moves = log.Steps.Where(s => s.Frame == frame).SelectMany(s => s.Step.Changes.OfType<ModelsMoved>()).SelectMany(m => m.Moves)
                        .Where(m => m.From is OnTable && m.To is OnTable).ToList();
                    any |= moves.Count > 0;
                    verdicts.Add(moves.FirstOrDefault(m => !carriers.Contains(m.ModelId)) is not null ? "moved a model without the rule"
                        : moves.Select(m => ((OnTable)m.From).Placement.Position.Horizontal.DistanceTo(((OnTable)m.To).Placement.Position.Horizontal))
                            .FirstOrDefault(d => d > region.Allowance + 0.01) is var far && far > 0 ? $"moved {far:0.00}\" of {region.Allowance}\"" : null);
                }
                var reasons = Reasons(verdicts).ToList();
                if (!any) reasons.Add("no model moved");
                return reasons;
            }, [entry]);

        /// <summary>"This model and its unit get Ravage(+1)": each Ravage roll of a unit carrying the Aura rolls its living models' own X plus one die per living
        /// model while the Aura's model lives, judges 6+, and deals a wound per 6.</summary>
        static GameCheck RavageAuraDice(string quote) =>
            GameCheck.Each($"Ravage Aura: {quote}", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                foreach (var w in log.FramesOf("rule-wounds").Where(w => Dealt(w.Start) is { Rule: "Ravage" } && Holds(run, w.Context.ActorUnitId, "Ravage Aura")))
                {
                    var step = w.Steps.FirstOrDefault(s => s.Rolls("Ravage").Any());
                    if (step is null) { verdicts.Add("no Ravage roll"); continue; }
                    var roll = step.Rolls("Ravage").Single();
                    var living = log.LocationsBefore(w.Seq).Where(kv => kv.Value is not OnShelf && log.UnitOf(kv.Key) == w.Context.ActorUnitId).Select(kv => run.Final.Models[kv.Key].Slot).ToList();
                    var own = living.Sum(m => m.Properties.Where(p => p.Name == "Ravage").Sum(p => p.Parameter ?? 0));
                    var expected = own + (living.Any(m => m.Properties.Any(p => p.Name == "Ravage Aura")) ? living.Count : 0);
                    var sixes = roll.Dice.Count(d => d.Raw >= 6);
                    verdicts.Add(roll.Dice.Count != expected ? $"{roll.Dice.Count} dice for own X {own} and {living.Count} living models"
                        : roll.Roll.Threshold != 6 ? $"threshold {roll.Roll.Threshold}" : step.Value("wounds") != sixes ? $"{step.Value("wounds")} wounds from {Faces(roll.Dice)}" : null);
                }
                return Reasons(verdicts);
            }, ["Ravage Aura"]);

        /// <summary>"If failed, it loses N spell tokens": after each use's failed test the picked model's tokens are N fewer (to none), after a passed one unchanged;
        /// the game shows a failed test on a model holding at least N.</summary>
        static GameCheck TokensLost(string entry, int n, string quote) =>
            GameCheck.Each($"{entry}: {quote}: {n} tokens lost", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                var full = false;
                foreach (var use in UseFrames(log, entry))
                {
                    var test = log.Descendants(use).FirstOrDefault(f => f.Sequence == "morale-test")?.Steps.FirstOrDefault(s => s.Name == "consequence");
                    var target = log.Asks.FirstOrDefault(a => a.Ask.Question == "rule_target" && a.Frame is { } f && Under(f, use))?.Answer as SelectedPieces;
                    if (test is null || target?.PieceIds is not [var model]) continue;
                    var before = log.Events.TakeWhile(e => e.Seq < use.Seq).SelectMany(e => LogQueries.ChangesOf(e.Payload)).OfType<SpellTokensSet>()
                        .Where(t => t.ModelId == model).Select(t => t.Tokens).LastOrDefault();
                    var set = log.Steps.Where(s => s.Frame.Sequence == "spell-test" && Under(s.Frame, use)).SelectMany(s => s.Step.Changes).OfType<SpellTokensSet>().ToList();
                    var failed = test.Value("passed") == 0;
                    if (failed && before >= n) full = true;
                    verdicts.Add(!failed ? (set.Count == 0 ? null : "tokens lost after a passed test")
                        : set is not [var s] || s.ModelId != model ? $"{set.Count} token changes after a failed test"
                        : s.Tokens != Math.Max(0, before - n) ? $"{before} tokens became {s.Tokens}" : null);
                }
                var reasons = Reasons(verdicts).ToList();
                if (!full) reasons.Add($"no failed test on a model holding {n} or more tokens");
                return reasons;
            }, [entry]);

        /// <summary>Indirect off a Hold: each Advance of a unit carrying the entry shoots, its shooting listing the entry and Indirect; each Advance of a unit of the
        /// same army without it shoots nothing; the game shows both.</summary>
        static GameCheck OutOfSightOffHold(string entry, string quote) =>
            GameCheck.Each($"{entry}: {quote}", run =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                var (with, without) = (0, 0);
                foreach (var advance in log.FramesOf("advance").Where(f => f.Context.ActorUnitId is { } u && Named(run, Books36.RatmenClans.Army)(u)))
                {
                    var shootings = log.Descendants(advance).Where(f => f.Sequence == ShootingSequence).ToList();
                    if (Holds(run, advance.Context.ActorUnitId, entry))
                    {
                        with++;
                        var steps = shootings.SelectMany(f => log.Descendants(f).Prepend(f)).SelectMany(f => f.Steps).ToList();
                        var attacked = shootings.Any(f => log.Descendants(f).Any(d => d.Sequence == AttackSequence));
                        verdicts.Add(!attacked ? "an Advance with no attack" : !steps.Any(s => s.Lists(entry)) ? $"{entry} not listed"
                            : !steps.Any(s => s.Lists("Indirect")) ? "Indirect not listed" : null);
                    }
                    else
                    {
                        without++;
                        verdicts.Add(shootings.Count == 0 ? null : "a unit without the rule shot after an Advance");
                    }
                }
                var reasons = Reasons(verdicts).ToList();
                if (with == 0) reasons.Add($"no Advance of a unit with {entry}");
                if (without == 0) reasons.Add($"no Advance of a unit without {entry}");
                return reasons;
            }, [entry]);
    }
}
