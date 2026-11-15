using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The attack a roll belongs to (combat-log spec → Attacks): the frame whose hits it resolves, who attacks whom, whether it is a
/// melee, the rule that dealt the hits when no weapon did (Impact, a spell, Deathstrike), and the weapons. Rolls of one attack share Frame.</summary>
public sealed record AttackTag(Guid Frame, string Actor, string Targets, bool Melee, string? Source, string Weapons);

public static partial class CombatLog
{
    /// <summary>
    /// Each frame's parent and sequence, read ahead of the build: a frame started while others are open is a child of the innermost one
    /// still open; a finish or abandonment closes it and any opened inside it, an undo closes those started after its seq. Tag names the
    /// attack of a roll in a frame: the parent of the innermost `resolve-hits` frame at or above it (a block, a roll per wound), else the
    /// frame itself (a hit roll, Impact's roll); a roll in a frame with no hits to resolve gets a tag no other roll shares.
    /// </summary>
    sealed class FrameTree
    {
        readonly Dictionary<Guid, Guid?> _parent = [];
        readonly Dictionary<Guid, SequenceStarted> _started = [];
        readonly Dictionary<Guid, string> _applied = [];   // the latest rule a roll-less step of the frame applied, so far in the build

        public static FrameTree Of(IReadOnlyList<GameEvent> log)
        {
            var t = new FrameTree();
            var open = new List<(Guid Frame, long Seq)>();
            void CloseFrom(int i) { if (i >= 0) open.RemoveRange(i, open.Count - i); }
            foreach (var e in log)
                switch (e.Payload)
                {
                    case SequenceStarted s:
                        if (!t._started.ContainsKey(s.FrameId)) { t._parent[s.FrameId] = open.Count > 0 ? open[^1].Frame : null; t._started[s.FrameId] = s; }
                        open.Add((s.FrameId, e.Seq));
                        break;
                    case SequenceFinished f: CloseFrom(open.FindLastIndex(x => x.Frame == f.FrameId)); break;
                    case SequenceAbandoned a: CloseFrom(open.FindLastIndex(x => x.Frame == a.FrameId)); break;
                    case Undone u: CloseFrom(open.FindIndex(x => x.Seq > u.ToSeq)); break;
                }
            return t;
        }

        /// <summary>A roll-less step's first applied rule: the rule that dealt the hits a later block of the frame resolves.</summary>
        public void See(StepCompleted step)
        {
            if (step.Rolls.Count == 0 && step.Applied is [var first, ..]) _applied[step.FrameId] = first;
        }

        IEnumerable<Guid> Up(Guid frame)
        {
            for (Guid? f = frame; f is { } id; f = _parent.GetValueOrDefault(id)) yield return id;
        }

        string? Sequence(Guid frame) => _started.GetValueOrDefault(frame)?.Sequence;

        public AttackTag Tag(StepCompleted step, RollRecord roll, SequenceContext rollContext, Names n)
        {
            var resolve = Up(step.FrameId).Cast<Guid?>().FirstOrDefault(f => Sequence(f!.Value) == "resolve-hits");
            var frame = resolve is { } r && _parent.GetValueOrDefault(r) is { } p ? p : step.FrameId;
            var c = _started.GetValueOrDefault(frame)?.Context is { ActorUnitId: not null } fc ? fc : rollContext;
            var melee = Up(frame).Any(f => Sequence(f) == "melee");
            var source = Sequence(frame) == "attack-sequence" ? null
                : step.FrameId == frame && roll.Roll is not ("hit" or "defense") && step.Applied is [var own, ..] ? own
                : _applied.GetValueOrDefault(frame);
            return new AttackTag(frame, c.ActorUnitId is { } a ? n.Unit(a) : "", string.Join(", ", c.TargetUnitIds.Select(n.Unit)), melee, source,
                string.Join(", ", c.WeaponIds.Select(n.Weapon).Distinct()));
        }
    }
}
