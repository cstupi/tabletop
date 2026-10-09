using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>What the special games' checks read from the effective log and the final projection.</summary>
static partial class SpecialGames
{

    /// <summary>One weapon group's attack: its attack-sequence frame and steps, the resolve-hits block and the deal-wounds assign steps.</summary>
    /// <param name="At">Its log position.</param><param name="Volley">Which shooting or melee frame it was made in, counted in log order.</param>
    sealed record Attack(SequenceStarted Seq, string Weapon, string Actor, string Target, string? Action, long At, int Volley)
    {
        public StepCompleted? Determine { get; set; }
        public StepCompleted? Pick { get; set; }
        public StepCompleted? Hit { get; set; }
        public StepCompleted? Block { get; set; }
        public List<StepCompleted> Assigns { get; } = [];
    }

    sealed record At(long Seq, StepCompleted Step);

    /// <summary>One melee frame: who charged whom, its impact and compare steps, and the attacks made inside it in order.</summary>
    sealed record Melee(SequenceStarted Seq, string Charger, string Defender)
    {
        public At? Impact { get; set; }
        public At? Compare { get; set; }
        public List<Attack> Attacks { get; } = [];
    }

    /// <summary>Own: the moving unit is the frame's actor in its advance, rush or charge sequence (not a defender closing in or a consolidation).</summary>
    sealed record Move(string Unit, string? Action, MoveRegion Region, bool Own);

    sealed record Offer(string Unit, IReadOnlyList<string> Actions, long At);

    /// <summary>
    /// The effective log with every DiceEdited (a game's DiceFix) folded into the roll it edits, so a check reads the dice the game went on
    /// with. Re-rolls stay separate events: Bane's check reads the die that was re-rolled.
    /// </summary>
    static IReadOnlyList<GameEvent> Events(SuiteGameRun run)
    {
        var edits = new Dictionary<(Guid Roll, int Index), Die>();
        foreach (var d in run.EffectiveLog.Select(e => e.Payload).OfType<DiceEdited>())
            foreach (var r in d.Replaced) edits[(d.RollId, r.Index)] = r.New;
        if (edits.Count == 0) return run.EffectiveLog;
        return run.EffectiveLog.Select(e => e.Payload is StepCompleted s && s.Rolls.Any(r => edits.Keys.Any(k => k.Roll == r.Id))
            ? e with { Payload = s with { Rolls = s.Rolls.Select(r => r with { Dice = r.Dice.Select((d, i) => edits.GetValueOrDefault((r.Id, i), d)).ToList() }).ToList() } }
            : e).ToList();
    }

    /// <summary>The rolls a DiceFix edited: a step's Values were settled from the dice before the edit, so a check that reads them skips these.</summary>
    static HashSet<Guid> EditedRolls(SuiteGameRun run) => run.EffectiveLog.Select(e => e.Payload).OfType<DiceEdited>().Select(d => d.RollId).ToHashSet();

    /// <summary>
    /// p.4 Quality tests and p.5 Defense: a die's modified value is its raw value plus the roll's modifiers; "a 6 is always a success" and "a 1
    /// is always a failure"; otherwise it succeeds when the modified value is at or over the threshold. Every die of every roll is judged, and
    /// each offset (modified minus threshold: 0 at the threshold, -1 one below) must occur on a die the threshold decided (raw 2 to 5).
    /// </summary>
    static string? Judged(IEnumerable<RollRecord> rolls, string what, params int[] offsets)
    {
        var all = rolls.ToList();
        if (all.Count == 0) return $"no {what} happened";
        foreach (var r in all)
            foreach (var d in r.Dice)
            {
                if (d.Modified != d.Raw + r.Modifiers.Sum(m => m.Amount)) return $"{what}: {Show(r)}: raw {d.Raw} with its modifiers is not {d.Modified}";
                if (r.Threshold is { } t && d.Success != (d.Raw == 6 || d.Raw != 1 && d.Modified >= t))
                    return $"{what}: {Show(r)}: {d.Raw}/{d.Modified} judged a {(d.Success ? "success" : "failure")} at {t}+";
            }
        foreach (var o in offsets)
            if (!all.Any(r => r.Threshold is { } t && r.Dice.Any(d => d.Raw is > 1 and < 6 && d.Modified - t == o)))
                return $"{what}: no die the threshold decided {(o == 0 ? "at the threshold" : $"{-o} below it")}";
        return null;
    }

    static IEnumerable<EventPayload> Payloads(SuiteGameRun run) => Events(run).Select(e => e.Payload);

    static string UnitName(SuiteGameRun run, Guid unit) => run.Final.Units.TryGetValue(unit, out var u) ? u.Definition.Name : unit.ToString();

    static string ModelName(SuiteGameRun run, Guid model) => run.Final.Models.TryGetValue(model, out var m) ? m.Slot.Name : model.ToString();

    static string WeaponName(SuiteGameRun run, Guid weapon) =>
        run.Final.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == weapon)?.Name ?? weapon.ToString();

    static IEnumerable<StateChange> ChangesOf(EventPayload payload) => payload switch
    {
        StateChange c => [c],
        StepCompleted s => s.Changes,
        RoundStarted r => r.Changes,
        RoundEnded r => r.Changes,
        TurnEnded t => t.Changes,
        _ => [],
    };

    /// <summary>Every completed step with its log position and the frame it ran in.</summary>
    static IEnumerable<(long Seq, StepCompleted Step, SequenceStarted? Frame)> Steps(SuiteGameRun run)
    {
        var frames = new Dictionary<Guid, SequenceStarted>();
        foreach (var e in Events(run))
        {
            if (e.Payload is SequenceStarted s) frames[s.FrameId] = s;
            if (e.Payload is StepCompleted step) yield return (e.Seq, step, frames.GetValueOrDefault(step.FrameId));
        }
    }

    static List<Attack> Attacks(SuiteGameRun run) => Read(run).Attacks;

    static List<Melee> Melees(SuiteGameRun run) => Read(run).Melees;

    /// <summary>
    /// Every weapon group's attack and every melee, in log order. A resolve-hits frame belongs to the latest attack with the same weapons that
    /// has not blocked yet, a deal-wounds frame to the latest that has; an attack started while a melee is open (before its compare) is that melee's.
    /// </summary>
    static (List<Attack> Attacks, List<Melee> Melees) Read(SuiteGameRun run)
    {
        var attacks = new List<Attack>();
        var melees = new List<Melee>();
        var attackOf = new Dictionary<Guid, Attack>();
        var meleeOf = new Dictionary<Guid, Melee>();
        var volley = 0;
        foreach (var e in Events(run))
        {
            if (e.Payload is SequenceStarted { Sequence: "shooting" or "melee" }) volley++;
            switch (e.Payload)
            {
                case SequenceStarted { Sequence: "attack-sequence" } s:
                    var attack = new Attack(s, s.Context.WeaponIds.Count > 0 ? WeaponName(run, s.Context.WeaponIds[0]) : "",
                        s.Context.ActorUnitId is { } actor ? UnitName(run, actor) : "",
                        s.Context.TargetUnitIds.Count > 0 ? UnitName(run, s.Context.TargetUnitIds[0]) : "", s.Context.Action, e.Seq, volley);
                    attacks.Add(attack);
                    attackOf[s.FrameId] = attack;
                    melees.LastOrDefault(m => m.Compare is null)?.Attacks.Add(attack);
                    break;
                case SequenceStarted { Sequence: "resolve-hits" } s:
                    if (attacks.LastOrDefault(x => x.Block is null && x.Seq.Context.WeaponIds.SequenceEqual(s.Context.WeaponIds)) is { } blocking) attackOf[s.FrameId] = blocking;
                    break;
                case SequenceStarted { Sequence: "deal-wounds" } s:
                    if (attacks.LastOrDefault(x => x.Block is not null && x.Seq.Context.WeaponIds.SequenceEqual(s.Context.WeaponIds)) is { } wounding) attackOf[s.FrameId] = wounding;
                    break;
                case SequenceStarted { Sequence: "melee" } s when s.Context.ActorUnitId is { } charger && s.Context.TargetUnitIds.Count > 0:
                    var melee = new Melee(s, UnitName(run, charger), UnitName(run, s.Context.TargetUnitIds[0]));
                    melees.Add(melee);
                    meleeOf[s.FrameId] = melee;
                    break;
                case StepCompleted step when attackOf.TryGetValue(step.FrameId, out var owner):
                    switch (step.Step)
                    {
                        case "determine-attacks": owner.Determine = step; break;
                        case "pick-target-model": owner.Pick = step; break;
                        case "roll-to-hit": owner.Hit = step; break;
                        case "roll-to-block": owner.Block = step; break;
                        case "assign": owner.Assigns.Add(step); break;
                    }
                    break;
                case StepCompleted step when meleeOf.TryGetValue(step.FrameId, out var m):
                    if (step.Step == "impact" && m.Impact is null) m.Impact = new At(e.Seq, step);
                    if (step.Step == "compare" && m.Compare is null) m.Compare = new At(e.Seq, step);
                    break;
            }
        }
        return (attacks, melees);
    }

    static RollRecord? HitRoll(Attack a) => a.Hit?.Rolls.FirstOrDefault(r => r.Roll == "hit");

    /// <summary>Unmodified 6s in the step's hit roll.</summary>
    static int Sixes(StepCompleted? hit) => hit?.Rolls.Where(r => r.Roll == "hit").Sum(r => r.Dice.Count(d => d.Raw == 6)) ?? 0;

    static int Hits(StepCompleted? hit) => hit?.Rolls.Where(r => r.Roll == "hit").Sum(r => r.Dice.Count(d => d.Success)) ?? 0;

    static int Unblocked(Attack a) => a.Block?.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count(d => !d.Success)) ?? 0;

    static IEnumerable<WoundsSet> WoundsSet(IEnumerable<StepCompleted> steps) => steps.SelectMany(s => s.Changes.OfType<WoundsSet>());

    static int Value(StepCompleted step, string key) => step.Values?.GetValueOrDefault(key) ?? 0;

    /// <summary>Whether the attacking unit was fatigued (p.9) when the attack started: its last fatigued condition before then.</summary>
    static bool Fatigued(SuiteGameRun run, Attack attack) => Fatigued(run, attack.Seq);

    /// <summary>Whether the frame's actor was fatigued when the frame started.</summary>
    static bool Fatigued(SuiteGameRun run, SequenceStarted frame)
    {
        var fatigued = false;
        var unit = frame.Context.ActorUnitId;
        foreach (var e in Events(run))
        {
            if (e.Payload == frame) return fatigued;
            foreach (var c in ChangesOf(e.Payload).OfType<ConditionSet>().Where(c => c.Condition == "fatigued" && c.Subject.Id == unit)) fatigued = c.Present;
        }
        return fatigued;
    }

    /// <summary>The wounds the unit's models took between the two log positions: the rise of each model's WoundsSet.</summary>
    static int WoundsTaken(SuiteGameRun run, string unit, SequenceStarted from, long toSeq)
    {
        var wounds = new Dictionary<Guid, int>();
        var taken = 0;
        var started = false;
        foreach (var e in Events(run).TakeWhile(e => e.Seq < toSeq))
        {
            started |= e.Payload == from;
            foreach (var w in ChangesOf(e.Payload).OfType<WoundsSet>())
            {
                if (started && UnitName(run, run.Final.Models[w.ModelId].UnitId) == unit) taken += Math.Max(0, w.Wounds - wounds.GetValueOrDefault(w.ModelId));
                wounds[w.ModelId] = w.Wounds;
            }
        }
        return taken;
    }

    /// <summary>The unit's models on the table or inside a container just before that log position.</summary>
    static int Living(SuiteGameRun run, string unit, long beforeSeq)
    {
        var where = new Dictionary<Guid, Location>();
        foreach (var e in Events(run).TakeWhile(e => e.Seq < beforeSeq))
            foreach (var m in ChangesOf(e.Payload).OfType<ModelsMoved>().SelectMany(m => m.Moves))
                where[m.ModelId] = m.To;
        return where.Count(kv => kv.Value is not OnShelf && UnitName(run, run.Final.Models[kv.Key].UnitId) == unit);
    }

    /// <summary>Every move Ask, with the unit it moves and the action of the frame it was asked in.</summary>
    static IEnumerable<Move> Moves(SuiteGameRun run)
    {
        var frames = new Dictionary<Guid, SequenceStarted>();
        foreach (var p in Payloads(run))
        {
            if (p is SequenceStarted s) frames[s.FrameId] = s;
            if (p is AskOpened { Ask.Legal: MoveRegion { ModelIds.Count: > 0 } region } ask)
            {
                var unit = run.Final.Models[region.ModelIds[0]].UnitId;
                var frame = ask.Ask.FrameId is { } f ? frames.GetValueOrDefault(f) : null;
                yield return new Move(UnitName(run, unit), frame?.Context.Action, region,
                    frame is { Sequence: "advance" or "rush" or "charge" } && frame.Context.ActorUnitId == unit);
            }
        }
    }

    /// <summary>For every activate Ask, each unit offered and the actions it was offered (option labels are "&lt;unit&gt;: &lt;action&gt;").</summary>
    static IEnumerable<Offer> ActivateOptions(SuiteGameRun run) => Offers(Events(run));

    static IEnumerable<Offer> Offers(IReadOnlyList<GameEvent> log) =>
        log.Where(e => e.Payload is AskOpened { Ask: { Question: AskQuestions.Activate, Legal: Options } })
            .SelectMany(e => ((Options)((AskOpened)e.Payload).Ask.Legal).Choices.Select(c => c.Label.Split(": ", 2)).Where(p => p.Length == 2)
                .GroupBy(p => p[0]).Select(g => new Offer(g.Key, g.Select(p => p[1]).ToList(), e.Seq)));

    /// <summary>Whether the unit was Shaken just before that log position: its last shaken condition.</summary>
    static bool Shaken(SuiteGameRun run, string unit, long beforeSeq)
    {
        var shaken = false;
        foreach (var e in Events(run).TakeWhile(e => e.Seq < beforeSeq))
            foreach (var c in ChangesOf(e.Payload).OfType<ConditionSet>().Where(c => c.Condition == "shaken" && UnitName(run, c.Subject.Id) == unit)) shaken = c.Present;
        return shaken;
    }

    /// <summary>Every model on the table just before that log position, at its centre, with its base radius.</summary>
    static Dictionary<Guid, (Point2 At, double Radius)> Placed(SuiteGameRun run, long beforeSeq)
    {
        var where = new Dictionary<Guid, Location>();
        foreach (var e in Events(run).TakeWhile(e => e.Seq < beforeSeq))
            foreach (var m in ChangesOf(e.Payload).OfType<ModelsMoved>().SelectMany(m => m.Moves)) where[m.ModelId] = m.To;
        return where.Where(kv => kv.Value is OnTable).ToDictionary(kv => kv.Key,
            kv => (((OnTable)kv.Value).Placement.Position.Horizontal, Radius(run, kv.Key)));
    }

    static double Radius(SuiteGameRun run, Guid model) => run.Final.Models[model].Slot.Base is var b ? Math.Max(b.A, b.B) / 2 : 0;

    static string UnitOf(SuiteGameRun run, Guid model) => UnitName(run, run.Final.Models[model].UnitId);

    /// <summary>The closest base-to-base distance between two units' models on the table just before that log position; null when either has none.</summary>
    static double? Gap(SuiteGameRun run, string a, string b, long beforeSeq)
    {
        var placed = Placed(run, beforeSeq);
        var ours = placed.Where(kv => UnitOf(run, kv.Key) == a).Select(kv => kv.Value).ToList();
        var theirs = placed.Where(kv => UnitOf(run, kv.Key) == b).Select(kv => kv.Value).ToList();
        return ours.Count == 0 || theirs.Count == 0 ? null : ours.SelectMany(x => theirs.Select(y => x.At.DistanceTo(y.At) - x.Radius - y.Radius)).Min();
    }

    static bool OnlyOffered(IReadOnlyList<GameEvent> log, string unit, string action) =>
        Offers(log).Where(o => o.Unit == unit).ToList() is { Count: > 0 } offers && offers.All(o => o.Actions.SequenceEqual([action]));

    /// <summary>Null when every case passes and at least <paramref name="need"/> happened; else the first failure, or what never happened.</summary>
    static string? Each<T>(IEnumerable<T> cases, string what, Func<T, string?> failure, int need = 1)
    {
        var all = cases.ToList();
        if (all.Count < need) return need == 1 ? $"no {what} happened" : $"{all.Count} of the {need} needed happened: {what}";
        return all.Select(failure).FirstOrDefault(f => f is not null);
    }

    static string Show(RollRecord r) =>
        $"{r.Roll} {r.Threshold}+ [{string.Join(",", r.Modifiers.Select(m => $"{m.Source}{m.Amount:+0;-0}"))}] {string.Join(" ", r.Dice.Select(d => $"{d.Raw}/{d.Modified}"))}";
}