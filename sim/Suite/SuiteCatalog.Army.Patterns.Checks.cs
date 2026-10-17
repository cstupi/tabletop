using System.Runtime.CompilerServices;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// The pattern games' log reading (army-book rules spec → Coverage): what each unit held and where each model stood when an attack began,
    /// and each rule_target Ask with its carrying model. Read from the effective log in order; nothing here computes a rule.
    /// </summary>
    private static partial class ArmyGames
    {
        /// <summary>What the target held and where every placed model stood as an attack-sequence began.</summary>
        sealed record AttackStart(IReadOnlyList<Grant> TargetGrants, IReadOnlyDictionary<Guid, Placement> At, IReadOnlyList<Grant>? AttackerGrants = null);
        sealed record PickAsk(Ask Ask, string Entry, Guid Carrier, IReadOnlyDictionary<Guid, Placement> At);

        /// <summary>Shootings: each shooting frame's grants per unit as it began.</summary>
        sealed record PatternLog(IReadOnlyDictionary<Guid, AttackStart> Attacks, IReadOnlyList<PickAsk> Picks,
            IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, IReadOnlyList<Grant>>> Shootings)
        {
            static readonly ConditionalWeakTable<IReadOnlyList<GameEvent>, PatternLog> Read = new();

            /// <summary>The run's pattern log, read once per run.</summary>
            public static PatternLog For(SuiteGameRun run) => Read.GetValue(run.EffectiveLog, Of);

            static PatternLog Of(IReadOnlyList<GameEvent> log)
            {
                var grants = new Dictionary<Guid, List<Grant>>();
                var at = new Dictionary<Guid, Placement>();
                var frames = new Dictionary<Guid, SequenceStarted>();
                var attacks = new Dictionary<Guid, AttackStart>();
                var picks = new List<PickAsk>();
                var shootings = new Dictionary<Guid, IReadOnlyDictionary<Guid, IReadOnlyList<Grant>>>();
                void Apply(EventPayload p)
                {
                    switch (p)
                    {
                        case GrantAdded g: (grants.TryGetValue(g.UnitId, out var held) ? held : grants[g.UnitId] = []).Add(g.Grant); break;
                        case GrantRemoved r: grants.GetValueOrDefault(r.UnitId)?.RemoveAll(x => x.Id == r.GrantId); break;
                        case ModelsMoved m:
                            foreach (var mv in m.Moves) if (mv.To is OnTable t) at[mv.ModelId] = t.Placement; else at.Remove(mv.ModelId);
                            break;
                        case StepCompleted s: foreach (var c in s.Changes) Apply(c); break;
                        case SequenceStarted s:
                            frames[s.FrameId] = s;
                            if (s.Sequence == ShootingSequence)
                                shootings[s.FrameId] = grants.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Grant>)[.. kv.Value]);
                            if (s.Sequence == AttackSequence && s.Context.TargetUnitIds is [var target, ..])
                                attacks[s.FrameId] = new AttackStart([.. grants.GetValueOrDefault(target) ?? []], new Dictionary<Guid, Placement>(at),
                                    s.Context.ActorUnitId is { } by ? [.. grants.GetValueOrDefault(by) ?? []] : []);
                            break;
                        case AskOpened { Ask: { Question: "rule_target", FrameId: { } f } ask } when frames.TryGetValue(f, out var frame)
                            && frame.Context.Action is { } key && key.LastIndexOf('|') is var cut and > 0 && Guid.TryParseExact(key[(cut + 1)..], "N", out var carrier):
                            picks.Add(new PickAsk(ask, key[..cut], carrier, new Dictionary<Guid, Placement>(at)));
                            break;
                    }
                }
                foreach (var e in EventApplier.EffectiveLog(log)) Apply(e.Payload);
                return new PatternLog(attacks, picks, shootings);
            }
        }

        /// <summary>p.5 base to base: the nearest placed model of the unit to the model, at the placements given.</summary>
        static double Apart(SuiteGameRun run, IReadOnlyDictionary<Guid, Placement> at, Guid model, Guid unit)
        {
            PlacedModel Body(Guid m) => new(new ModelBody(run.Final.Models[m].Slot.Base, run.Final.Models[m].Slot.Height), at[m]);
            return at.Keys.Where(m => run.Final.Models[m].UnitId == unit).Select(m => Distance.EdgeToEdge(Body(model), Body(m))).DefaultIfEmpty(double.PositiveInfinity).Min();
        }

        /// <summary>The engine's range between two units (Battlefield.UnitDistance): the nearest pair of their placed models.</summary>
        static double UnitApart(SuiteGameRun run, IReadOnlyDictionary<Guid, Placement> at, Guid a, Guid b) =>
            at.Keys.Where(m => run.Final.Models[m].UnitId == a).Select(m => Apart(run, at, m, b)).DefaultIfEmpty(double.PositiveInfinity).Min();

        /// <summary>The occasion's attacks made within (or, with within false, beyond) the distance of their target as they began.</summary>
        static Occasion Within(Occasion occasion, double inches, bool within = true) => (run, a) =>
            occasion(run, a) && a.Attacker is { } u && a.Target is { } t && PatternLog.For(run).Attacks.TryGetValue(a.Frame.FrameId, out var s)
            && UnitApart(run, s.At, u, t) <= inches == within;

        /// <summary>
        /// An Aura: every roll of the kind made in an attack on a unit one of whose models carries the wrapper carries the base's modifier at the
        /// stated size while a carrier stands, and none once no carrier stands ("This model and its unit get X").
        /// </summary>
        static GameCheck AuraModifier(string wrapper, string source, string roll, int amount, string quote) =>
            Exact(wrapper, $"{quote}: {source} {roll} {amount:+0;-0}", (run, log) =>
            {
                var starts = PatternLog.For(run).Attacks;
                return from a in log.Attacks
                       where a.Hit is not null && a.Target is { } t && Holds(run, t, wrapper) && starts.ContainsKey(a.Frame.FrameId)
                       let carriers = run.Final.Units[a.Target!.Value].Definition.Models.Where(m => m.Properties.Any(p => p.Name == wrapper)).Select(m => m.Id)
                       let standing = carriers.Any(starts[a.Frame.FrameId].At.ContainsKey)
                       let rolls = a.Hit!.Rolls(roll).ToList()
                       let carrying = rolls.Count(r => r.Modifiers.Any(m => m.Source == source && m.Amount == amount))
                       select standing ? rolls.Count > 0 && carrying == rolls.Count ? null : $"{carrying} of {rolls.Count} {roll} rolls carry {source} with a carrier standing"
                           : carrying == 0 ? null : $"{carrying} {roll} rolls carry {source} with no carrier standing";
            });

        /// <summary>
        /// "Once (next time the effect would apply)": each attack where the effect could show (Shows non-null) shows it exactly when its target
        /// held the entry's grant as the attack began; the log holds an attack under the grant and a later one on that unit without it.
        /// </summary>
        static GameCheck Once(string entry, string quote, Func<LoggedAttack, bool?> shows) =>
            Exact(entry, quote, (run, log) =>
            {
                var starts = PatternLog.For(run).Attacks;
                var verdicts = new List<string?>();
                var granted = new HashSet<Guid>();
                var spent = false;
                foreach (var a in log.Attacks)
                {
                    if (a.Target is not { } t || !starts.TryGetValue(a.Frame.FrameId, out var s) || shows(a) is not { } shown) continue;
                    var held = s.TargetGrants.Any(g => g.EntryName == entry);
                    if (held) granted.Add(t); else if (granted.Contains(t)) spent = true;
                    verdicts.Add(held == shown ? null : held ? "under the grant without the effect" : "the effect without the grant");
                }
                if (granted.Count == 0) verdicts.Add("no attack under the grant");
                else if (!spent) verdicts.Add("no attack on the same unit after the grant");
                return verdicts;
            });

        /// <summary>The pick's reach: every unit a rule_target Ask of the entry offered is the carrier's side's (friendly) or the other's (enemy)
        /// and has a model within the distance of the carrying model as the Ask opened.</summary>
        static GameCheck PickWithin(string entry, double inches, bool friendly, string quote) =>
            Exact(entry, $"{quote}: {(friendly ? "friendly" : "enemy")} units within {inches}\"", (run, _) =>
                from p in PatternLog.For(run).Picks
                where p.Entry == entry && p.Ask.Legal is PieceSelection
                from unit in ((PieceSelection)p.Ask.Legal).Eligible
                let ours = run.Final.Units[unit].ParticipantId == run.Final.Units[run.Final.Models[p.Carrier].UnitId].ParticipantId
                let apart = Apart(run, p.At, p.Carrier, unit)
                select ours != friendly ? $"offered {(ours ? "a friendly" : "an enemy")} unit" : apart > inches ? $"offered a unit {apart:0.##}\" away" : null);

        /// <summary>
        /// Quick Shot through a Mark: every attack made in a shooting after Rush is at a unit that held the entry's grant as the shooting began
        /// (p.7: "Rush - Moves 12" but can't shoot", so the Mark is what let it shoot), and the grant is gone as the attack begins: that shooting
        /// spent it, once. A game with no shooting after Rush fails.
        /// </summary>
        static GameCheck RushShotMarked(string entry, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in log.Attacks
                let shooting = a.Hit?.Nearest(ShootingSequence)
                where a.Shooting && a.Target is not null && a.Hit?.Nearest("rush") is not null && shooting is not null
                let p = PatternLog.For(run)
                let before = p.Shootings.GetValueOrDefault(shooting.FrameId)?.GetValueOrDefault(a.Target!.Value)?.Any(g => g.EntryName == entry) == true
                let after = p.Attacks.TryGetValue(a.Frame.FrameId, out var s) && s.TargetGrants.Any(g => g.EntryName == entry)
                select !before ? "shot after Rush at a unit the Mark was not on" : after ? "the Mark outlived the shooting it allowed" : null);
    }
}
