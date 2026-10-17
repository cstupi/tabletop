using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// The army games' log reading and exact checks. Each check reads the logged dice, values and Asks and compares them with the literal of
    /// the rule's own text, which its name quotes (army books print no page); modifiers follow p.5's "add or subtract the value from the roll".
    /// The attack checks take as occasions every attack the game names for its rule (By, On, With), never the steps whose Applied lists it,
    /// so a rule that stops acting on some of its occasions fails on those.
    /// </summary>
    private static partial class ArmyGames
    {
        const string MeleeSequence = "melee", ShootingSequence = "shooting", AttackSequence = "attack-sequence";

        /// <summary>A completed step, the sequence frame it ran in with that frame's ancestors (nearest first), and the rerolls made of its rolls.</summary>
        sealed record LoggedStep(int Index, StepCompleted Step, SequenceStarted Frame, IReadOnlyList<SequenceStarted> Ancestors, List<DiceRerolled> Rerolls)
        {
            /// <summary>Every later replacement of this step's dice, edits and rerolls, in log order.</summary>
            public List<EventPayload> Changes { get; } = [];
            /// <summary>A roll's dice as they now stand (later steps read the record this way; the step's own Values were judged on the dice it rolled).</summary>
            public IReadOnlyList<Die> Now(RollRecord roll)
            {
                var dice = roll.Dice.ToArray();
                foreach (var change in Changes)
                    foreach (var r in change switch { DiceEdited e when e.RollId == roll.Id => e.Replaced, DiceRerolled x when x.RollId == roll.Id => x.Replaced, _ => [] })
                        dice[r.Index] = r.New;
                return dice;
            }
            public int Value(string key) => Step.Values?.GetValueOrDefault(key) ?? 0;
            public IEnumerable<RollRecord> Rolls(string kind) => Step.Rolls.Where(r => r.Roll == kind);
            public SequenceStarted? Nearest(params string[] sequences) => Ancestors.Prepend(Frame).FirstOrDefault(f => sequences.Contains(f.Sequence));
        }

        /// <summary>An Ask with its frame; Steps: how many steps the log had completed when it opened.</summary>
        sealed record LoggedAsk(Ask Ask, SequenceStarted? Frame, IReadOnlyList<SequenceStarted> Ancestors, int Steps = 0);

        /// <summary>
        /// One attack-sequence of the log: the attacking unit, its target, its weapon group, whether it was made shooting or in a melee the
        /// attacker started by charging, its roll-to-hit step, the score-hits step that read those dice as they then stood, and the
        /// roll-to-block steps of the resolve-hits frames under it.
        /// </summary>
        sealed record LoggedAttack(SequenceStarted Frame, Guid? Attacker, Guid? Target, IReadOnlyList<Guid> Weapons, bool Shooting, bool Charging,
            LoggedStep? Hit, LoggedStep? Score, IReadOnlyList<LoggedStep> Blocks)
        {
            /// <summary>The hit dice as they stand after every edit and reroll: what score-hits read.</summary>
            public IReadOnlyList<Die> HitDice => Hit is null ? [] : [.. Hit.Rolls("hit").SelectMany(Hit.Now)];
            public int Scored(string key) => Score?.Value(key) ?? 0;
        }

        /// <summary>The effective log's steps and Asks, each with its frame: sequences nest, so a frame's parent is the one open when it started.</summary>
        sealed record ArmyLog(IReadOnlyList<LoggedStep> Steps, IReadOnlyList<LoggedAsk> Asks)
        {
            /// <summary>Every attack-sequence that completed a step or had a block under it, in log order, read from its frame and its ancestors.</summary>
            public IReadOnlyList<LoggedAttack> Attacks { get; } = AttacksOf(Steps);

            static IReadOnlyList<LoggedAttack> AttacksOf(IReadOnlyList<LoggedStep> steps)
            {
                var frames = new List<(SequenceStarted Frame, IReadOnlyList<SequenceStarted> Ancestors)>();
                foreach (var s in steps)
                {
                    var chain = s.Ancestors.Prepend(s.Frame).ToList();      // the step's frame, then its ancestors nearest first
                    for (var i = 0; i < chain.Count; i++)
                        if (chain[i].Sequence == AttackSequence && frames.All(x => x.Frame.FrameId != chain[i].FrameId))
                            frames.Add((chain[i], chain.Skip(i + 1).ToList()));
                }
                return [.. frames.Select(x =>
                {
                    var melee = x.Ancestors.FirstOrDefault(a => a.Sequence == MeleeSequence);
                    var attacker = x.Frame.Context.ActorUnitId;
                    return new LoggedAttack(x.Frame, attacker, x.Frame.Context.TargetUnitIds.FirstOrDefault(), x.Frame.Context.WeaponIds,
                        Shooting: x.Ancestors.Any(a => a.Sequence == ShootingSequence),
                        Charging: melee is not null && attacker is not null && melee.Context.ActorUnitId == attacker,
                        Hit: steps.FirstOrDefault(s => s.Frame.FrameId == x.Frame.FrameId && s.Step.Step == "roll-to-hit"),
                        Score: steps.FirstOrDefault(s => s.Frame.FrameId == x.Frame.FrameId && s.Step.Step == "score-hits"),
                        Blocks: [.. steps.Where(s => s.Step.Step == "roll-to-block" && s.Frame.Sequence == "resolve-hits"
                            && s.Ancestors.FirstOrDefault(a => a.Sequence == AttackSequence)?.FrameId == x.Frame.FrameId)]);
                })];
            }

            public static ArmyLog Of(IReadOnlyList<GameEvent> log)
            {
                var frames = new Dictionary<Guid, SequenceStarted>();
                var parents = new Dictionary<Guid, Guid?>();
                var open = new List<Guid>();
                var steps = new List<LoggedStep>();
                var asks = new List<LoggedAsk>();
                IReadOnlyList<SequenceStarted> Ancestors(Guid frame)
                {
                    var list = new List<SequenceStarted>();
                    for (var p = parents.GetValueOrDefault(frame); p is { } id; p = parents.GetValueOrDefault(id)) list.Add(frames[id]);
                    return list;
                }
                foreach (var e in EventApplier.EffectiveLog(log))
                    switch (e.Payload)
                    {
                        case SequenceStarted s:
                            frames[s.FrameId] = s;
                            parents[s.FrameId] = open.Count > 0 ? open[^1] : null;
                            open.Add(s.FrameId);
                            break;
                        case SequenceFinished f: open.Remove(f.FrameId); break;
                        case SequenceAbandoned a: open.Remove(a.FrameId); break;
                        case StepCompleted s when frames.TryGetValue(s.FrameId, out var frame):
                            steps.Add(new LoggedStep(steps.Count, s, frame, Ancestors(s.FrameId), []));
                            break;
                        case DiceRerolled r when steps.LastOrDefault(s => s.Step.Rolls.Any(x => x.Id == r.RollId)) is { } rerolled:
                            rerolled.Rerolls.Add(r);
                            rerolled.Changes.Add(r);
                            break;
                        case DiceEdited d:
                            steps.LastOrDefault(s => s.Step.Rolls.Any(x => x.Id == d.RollId))?.Changes.Add(d);
                            break;
                        case AskOpened a:
                            var asked = a.Ask.FrameId is { } id && frames.TryGetValue(id, out var f2) ? f2 : null;
                            asks.Add(new LoggedAsk(a.Ask, asked, asked is null ? [] : Ancestors(asked.FrameId), steps.Count));
                            break;
                    }
                return new ArmyLog(steps, asks);
            }
        }

        /// <summary>
        /// An exact check over the occasions it finds: each yields null when the rule's literal held, else the reason. A check with no occasion
        /// fails, so a seed that never brings the rule's number into play cannot pass for it.
        /// </summary>
        static GameCheck Exact(string entry, string quote, Func<SuiteGameRun, ArmyLog, IEnumerable<string?>> occasions) =>
            GameCheck.Each($"{entry}: {quote}", run => Reasons(occasions(run, ArmyLog.Of(run.EffectiveLog)).ToList()), [entry]);

        /// <summary>One failure line per distinct reason, each with the number of occasions that failed for it; "no occasion" when there were none.</summary>
        static IEnumerable<string> Reasons(IReadOnlyList<string?> verdicts) =>
            verdicts.Count == 0 ? ["no occasion in the log"]
                : verdicts.OfType<string>().GroupBy(v => v).Select(g => $"{g.Count()} of {verdicts.Count} occasions: {g.Key}");

        /// <summary>
        /// The attacks an army rule's text speaks of, chosen from the log and the army definitions, never from the engine's Applied. The game
        /// names its rule's occasions with By, On or With; Without drops the attacks its Boost's check takes instead.
        /// </summary>
        delegate bool Occasion(SuiteGameRun run, LoggedAttack attack);

        /// <summary>Which attacks of a holder: all of them, only shooting ones, or only those in a melee it charged into.</summary>
        enum When { Attacking, Shooting, Charging }

        static bool Made(LoggedAttack a, When when) => when switch
        {
            When.Shooting => a.Shooting,
            When.Charging => a.Charging,
            _ => true,
        };

        /// <summary>The attacks made by a unit whose models carry the entry and not the Boost (a model rule of the attacker).</summary>
        static Occasion By(string entry, string? boost = null, When when = When.Attacking) =>
            (run, a) => Made(a, when) && Holds(run, a.Attacker, entry) && (boost is null || !Holds(run, a.Attacker, boost));

        /// <summary>The attacks made at a unit whose models carry the entry and not the Boost (a model rule of the target).</summary>
        static Occasion On(string entry, string? boost = null, When when = When.Attacking) =>
            (run, a) => Made(a, when) && Holds(run, a.Target, entry) && (boost is null || !Holds(run, a.Target, boost));

        /// <summary>The attacks whose weapon group has a weapon carrying the entry (a weapon rule).</summary>
        static Occasion With(string entry) =>
            (run, a) => a.Attacker is { } id && run.Final.Units.TryGetValue(id, out var unit)
                && unit.Definition.Models.SelectMany(m => m.Weapons).Any(w => a.Weapons.Contains(w.Id) && w.Properties.Any(p => p.Name == entry));

        static IEnumerable<LoggedAttack> Of(SuiteGameRun run, ArmyLog log, Occasion occasion) => log.Attacks.Where(a => occasion(run, a));

        static string Faces(IEnumerable<Die> dice) => $"[{string.Join(" ", dice.Select(d => d.Raw))}]";
        static int Count(IEnumerable<RollRecord> rolls, IReadOnlyCollection<int> faces, bool successOnly = true) => Count(rolls.SelectMany(r => r.Dice), faces, successOnly);
        static int Count(IEnumerable<Die> dice, IReadOnlyCollection<int> faces, bool successOnly = true) =>
            dice.Count(d => faces.Contains(d.Raw) && (d.Success || !successOnly));

        /// <summary>Whether the unit's models carry the entry in the army definition (what the scenario gave them, not a rules reading).</summary>
        static bool Holds(SuiteGameRun run, Guid? unitId, string entry) =>
            unitId is { } id && run.Final.Units.TryGetValue(id, out var unit) && unit.Definition.Models.Any(m => m.Properties.Any(p => p.Name == entry));

        /// <summary>A modify_roll: every roll it modified is of that kind, carries it at the stated size, and its dice are raw plus the roll's modifiers (p.5).</summary>
        static GameCheck Modifier(string entry, string roll, int amount, string quote, Func<LoggedStep, bool>? scope = null) =>
            Exact(entry, $"{quote}: {roll} {amount:+0;-0}", (_, log) =>
                from s in log.Steps
                from r in s.Step.Rolls
                let mine = r.Modifiers.Where(m => m.Source == entry).ToList()
                where mine.Count > 0
                let total = r.Modifiers.Sum(m => m.Amount)
                select r.Roll != roll ? $"on a {r.Roll} roll"
                    : mine.Any(m => m.Amount != amount) ? $"{string.Join(",", mine.Select(m => m.Amount))} on {r.Roll}"
                    : r.Dice.FirstOrDefault(d => d.Modified != d.Raw + total) is { } d ? $"die {d.Raw} became {d.Modified} under {total:+0;-0}"
                    : scope?.Invoke(s) == false ? $"in {s.Frame.Sequence}, outside where the rule applies"
                    : null);

        /// <summary>add_hit (hits): on every hit roll of the occasion's attacks, the extra hits equal its successful dice showing the faces.</summary>
        static GameCheck ExtraHits(string entry, Occasion occasion, IReadOnlyCollection<int> faces, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in Of(run, log, occasion)
                where a.Hit is not null
                let expected = Count(a.HitDice, faces)
                select a.Scored("extra_hits") == expected ? null : $"{a.Scored("extra_hits")} extra hits from {Faces(a.HitDice)}, expected {expected}");

        /// <summary>add_hit (attacks): one added die per successful pool die showing the faces as rolled (an edit draws no die), rolled once in the same
        /// step, and the added dice add none.</summary>
        static GameCheck ExtraAttacks(string entry, Occasion occasion, IReadOnlyCollection<int> faces, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in Of(run, log, occasion)
                where a.Hit is not null && a.Hit.Rolls("hit").Any()
                let s = a.Hit
                let hits = s.Rolls("hit").ToList()
                let expected = Count(hits.Take(1), faces)
                let added = hits.Skip(1).Sum(r => r.Dice.Count)
                select hits.Count > 2 ? $"{hits.Count} hit rolls: the added attacks generated more"
                    : added == expected ? null : $"{added} added attacks from {Faces(hits[0].Dice)}, expected {expected}");

        /// <summary>add_hit (wounds) on the defender's dice: the block's extra wounds equal its failed defense dice showing the faces ("failed defense rolls of 1-2"; a 1 always fails, p.5).</summary>
        static GameCheck ExtraWounds(string entry, Occasion occasion, IReadOnlyCollection<int> faces, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in Of(run, log, occasion)
                from s in a.Blocks
                let expected = s.Rolls("defense").SelectMany(r => r.Dice).Count(d => faces.Contains(d.Raw) && !d.Success)
                select s.Value("extra_wounds") == expected ? null : $"{s.Value("extra_wounds")} extra wounds from {Faces(s.Rolls("defense").SelectMany(r => r.Dice))}, expected {expected}");

        /// <summary>Hazardous's wound: the attack's self wounds equal its hit dice's unmodified 1s.</summary>
        static GameCheck SelfWounds(string entry, Occasion occasion, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in Of(run, log, occasion)
                where a.Hit is not null
                let expected = Count(a.HitDice, [1], successOnly: false)
                select a.Scored("self_wounds") == expected ? null : $"{a.Scored("self_wounds")} self wounds from {Faces(a.HitDice)}, expected {expected}");

        /// <summary>A granted or changed AP: every block of the occasion's attacks that rolls defense rolls it under AP of the stated size (p.13 AP: "-X to Defense rolls"), and under no other.</summary>
        static GameCheck ApGrant(string entry, Occasion occasion, int ap, string quote) =>
            Exact(entry, $"{quote}: AP({ap})", (run, log) =>
                from a in Of(run, log, occasion)
                from s in a.Blocks
                where s.Rolls("defense").Any()
                let aps = s.Rolls("defense").SelectMany(r => r.Modifiers).Where(m => m.Source == "AP").Select(m => m.Amount).ToList()
                select aps.Contains(-ap) && aps.All(a => a == -ap) ? null : $"defense under AP [{string.Join(",", aps)}]");

        /// <summary>A per-die AP grant on the hit's 6: score-hits grants it once per unmodified 6, and the block that follows rolls that many dice under it.</summary>
        static GameCheck PerDieAp(string entry, Occasion occasion, int ap, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in Of(run, log, occasion)
                where a.Hit is not null
                let sixes = Count(a.HitDice, [6])
                let under = a.Blocks.FirstOrDefault()?.Rolls("defense").Where(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -ap)).Sum(r => r.Dice.Count) ?? 0
                select a.Scored($"granted_AP_{ap}") != sixes ? $"AP({ap}) granted {a.Scored($"granted_AP_{ap}")} times for {Faces(a.HitDice)}"
                    : under != sixes ? $"{under} defense dice under AP({ap}) for {sixes} sixes" : null);

        /// <summary>A forced re-roll of the defender's dice: exactly the dice showing the faces (successful ones only when the rule says so) are re-rolled.</summary>
        static GameCheck Rerolls(string entry, Occasion occasion, IReadOnlyCollection<int> faces, bool successOnly, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in Of(run, log, occasion)
                from s in a.Blocks
                from r in s.Rolls("defense")
                let expected = r.Dice.Select((d, i) => (d, i)).Where(x => faces.Contains(x.d.Raw) && (x.d.Success || !successOnly)).Select(x => x.i).ToList()
                let rerolled = s.Rerolls.Where(x => x.RollId == r.Id).SelectMany(x => x.Replaced).Select(x => x.Index).Order().ToList()
                select expected.SequenceEqual(rerolled) ? null : $"re-rolled dice [{string.Join(",", rerolled)}] of {Faces(r.Dice)}, expected [{string.Join(",", expected)}]");

        /// <summary>
        /// A roll to ignore each wound, per deal-wounds frame: one die for each wound the block dealt (its wounds and extra wounds), each ignored
        /// at the stated number, and the wounds dealt (with any left over when the unit is gone) are the dice that failed.
        /// </summary>
        static GameCheck IgnoreWounds(string entry, int threshold, string quote) =>
            Exact(entry, quote, (_, log) =>
                from frame in log.Steps.Where(s => s.Step.Step == "assign").GroupBy(s => s.Frame.FrameId)
                let rolls = frame.SelectMany(s => s.Rolls(entry)).ToList()
                where rolls.Count > 0
                let parent = frame.First().Ancestors.FirstOrDefault()?.FrameId
                let block = log.Steps.Take(frame.First().Index).LastOrDefault(s => s.Frame.FrameId == parent && s.Step.Step == "roll-to-block")
                let incoming = block is null ? 0 : block.Rolls("defense").Sum(r => block.Now(r).Count(d => !d.Success)) + block.Value("extra_wounds")
                let dice = rolls.SelectMany(r => r.Dice).ToList()
                let dealt = frame.Last().Value("wounds_dealt") + frame.Last().Value("wounds_left")
                select rolls.Any(r => r.Threshold != threshold) ? $"threshold {string.Join(",", rolls.Select(r => r.Threshold))}"
                    : dice.FirstOrDefault(d => d.Success != d.Raw >= threshold) is { } d ? $"{d.Raw} {(d.Success ? "ignored" : "not ignored")}"
                    : dice.Count != incoming ? $"{dice.Count} dice {Faces(dice)} for {incoming} wounds"
                    : dealt != dice.Count(d => !d.Success) ? $"{dealt} wounds dealt after {Faces(dice)}"
                    : null);

        /// <summary>
        /// A move bonus: the first move Ask of each action of a unit holding the entry (and not the Boost that replaces it) allows the action's
        /// p.7 distance ("Advance - Moves 6", "Rush - Moves 12", "Charge - Moves 12") plus the bonus, as given per action.
        /// </summary>
        static GameCheck Allowance(string entry, string? boost, string quote, params (string Action, int Inches)[] allowed) =>
            Exact(entry, $"{quote}: {string.Join(", ", allowed.Select(a => $"{a.Action} {a.Inches}\""))}", (run, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { } f && allowed.Any(x => x.Action == f.Sequence)
                    && Holds(run, f.Context.ActorUnitId, entry) && (boost is null || !Holds(run, f.Context.ActorUnitId, boost))
                group a by a.Frame!.FrameId into frame
                let first = frame.First()
                let inches = allowed.Single(x => x.Action == first.Frame!.Sequence).Inches
                select first.Ask.Legal is MoveRegion m && m.Allowance == inches ? null
                    : $"{first.Frame!.Sequence} allowed {(first.Ask.Legal as MoveRegion)?.Allowance}\"");

        /// <summary>A move after attacking: each of the entry's reposition moves allows the stated distance.</summary>
        static GameCheck Reposition(string entry, int inches, string quote) =>
            Exact(entry, quote, (_, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
                select a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");

        /// <summary>Teleport: "fully within 3" of its position when using Advance/Charge actions, or fully within 6" ... when using Rush actions".</summary>
        static GameCheck Teleport() =>
            Exact("Teleport", "\"within 3\" ... when using Advance/Charge actions, or fully within 6\" ... when using Rush actions\"", (_, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == "Teleport"
                let action = a.Ancestors.FirstOrDefault(x => x.Sequence is "advance" or "charge" or "rush")?.Sequence
                let inches = action == "rush" ? 6 : 3
                select action is null ? "no action above the placement"
                    : a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"{action} allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");

        /// <summary>Rapid Blink: the placement allows the sum of the entry's dice, each "a D6 and halve the result, rounding up" (p.4).</summary>
        static GameCheck RapidBlink(string entry, int dice) =>
            Exact(entry, $"\"anywhere fully within {(dice == 1 ? "D3" : "2D3")}\"", (_, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
                let roll = log.Steps.LastOrDefault(s => s.Frame.FrameId == a.Frame!.FrameId && s.Rolls(entry).Any())?.Rolls(entry).Single()
                let inches = roll?.Dice.Sum(d => (d.Raw + 1) / 2)
                select roll is null ? "no distance roll" : roll.Dice.Count != dice ? $"{roll.Dice.Count} dice"
                    : a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\" after {Faces(roll.Dice)}");

        /// <summary>
        /// Unpredictable: one die at the attack's start for the unit; "on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead",
        /// read on that unit's hit rolls and blocks under the same shooting or melee.
        /// </summary>
        static GameCheck Unpredictable(string entry) =>
            Exact(entry, "\"on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead\"", (_, log) =>
                from start in log.Steps
                where start.Step.Step is "attack-start" or "melee-start"
                from key in (start.Step.Values ?? new Dictionary<string, int>()).Keys
                where key.StartsWith("face_") && key.EndsWith($"_{entry}")
                let unit = Guid.ParseExact(key["face_".Length..key.IndexOf('_', "face_".Length)], "N")
                let face = start.Step.Values![key]
                let attacks = log.Steps.Where(s => s.Ancestors.Any(x => x.FrameId == start.Frame.FrameId) && s.Nearest("attack-sequence")?.Context.ActorUnitId == unit).ToList()
                // p.9: a fatigued unit "only hit[s] on unmodified rolls of 6 in melee", so no modifier reaches its hit roll.
                let hits = attacks.Where(s => s.Step.Step == "roll-to-hit").SelectMany(s => s.Rolls("hit")).Where(r => !(r.Threshold == 6 && r.Modifiers.Count == 0)).ToList()
                let blocks = attacks.Where(s => s.Step.Step == "roll-to-block").SelectMany(s => s.Rolls("defense")).ToList()
                where hits.Count > 0
                select face >= 4
                    ? hits.All(r => r.Modifiers.Any(m => m.Source == entry && m.Amount == 1)) ? null : $"face {face}: a hit roll without +1"
                    : hits.Any(r => r.Modifiers.Any(m => m.Source == entry)) ? $"face {face}: a hit roll with +1"
                    : blocks.All(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -1)) ? null : $"face {face}: a block without AP(1)");

        /// <summary>
        /// The extra hits of an attack whose hit dice an edit (SetDice) replaced follow the dice as they now stand: one per successful
        /// unmodified 6, which "unmodified results of 6 to hit deal 1 extra hit" asks of the dice the game plays with.
        /// </summary>
        static GameCheck EditedExtraHits(string entry, string quote) =>
            Exact(entry, $"{quote}, after SetDice", (_, log) =>
                from a in log.Attacks
                where a.Hit is not null && a.Hit.Changes.OfType<DiceEdited>().Any()
                let expected = a.HitDice.Count(d => d.Raw == 6 && d.Success)
                select a.Scored("extra_hits") == expected ? null : $"{a.Scored("extra_hits")} extra hits for edited dice {Faces(a.HitDice)}, expected {expected}");
    }
}
