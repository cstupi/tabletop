using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The four sounds, named as the files in content/sounds.</summary>
public enum Cue { Shoot, Melee, Move, Wound }

/// <summary>
/// Spec 2026-10-24-sound-effects → Cues: the cues a batch of live events plays, in order of first appearance, each at most once.
/// Reads the kinds the combat log reads: an `attack-sequence` frame starting under an open `shooting` frame is a shot (once per
/// shooting frame), under a `melee` frame a clash (once per striking unit); a ModelsMoved landing a model on the table is a move;
/// WoundsSet totals that rose, or a model leaving the table for the shelf, are a wound. Wounds on the wire are totals, so this keeps
/// each model's last, as FloatingResults does. A silent batch (catching up, a replay) or one with an undo plays nothing and
/// re-reads the totals and the open frames from the projection; a frame already open then has played its cue.
/// </summary>
public sealed class SoundCues
{
    const string Shooting = "shooting", Melee = "melee", AttackSequence = "attack-sequence";
    readonly Dictionary<Guid, int> _wounds = new();
    readonly List<(Guid Id, string Sequence)> _open = [];
    readonly HashSet<(Guid Frame, Guid? Striker)> _played = [];

    public void Seed(Projection p)
    {
        OnBoard();
        foreach (var m in p.Models.Values) _wounds[m.Id] = m.Wounds;
        foreach (var f in p.Stack.Where(f => f.Sequence is Shooting or Melee)) { _open.Add((f.Id, f.Sequence)); _played.Add((f.Id, null)); }
    }

    public void OnBoard() { _wounds.Clear(); _open.Clear(); _played.Clear(); }

    public IReadOnlyList<Cue> OnBatch(IReadOnlyList<GameEvent> batch, Projection p, bool silent)
    {
        if (silent || batch.Any(e => e.Payload is Undone)) { Seed(p); return []; }
        var cues = new List<Cue>();
        void Add(Cue c) { if (!cues.Contains(c)) cues.Add(c); }
        foreach (var e in batch)
        {
            switch (e.Payload)
            {
                case SequenceStarted { Sequence: Shooting or Melee } s: _open.Add((s.FrameId, s.Sequence)); break;
                case SequenceStarted { Sequence: AttackSequence } s when _open.Count(f => f.Sequence is Shooting or Melee) > 0:
                    var parent = _open.Last(f => f.Sequence is Shooting or Melee);
                    var key = parent.Sequence == Shooting ? (parent.Id, (Guid?)null) : (parent.Id, s.Context.ActorUnitId);
                    if (!_played.Contains((parent.Id, null)) && _played.Add(key)) Add(parent.Sequence == Shooting ? Cue.Shoot : Cue.Melee);
                    break;
                case SequenceFinished f: _open.RemoveAll(o => o.Id == f.FrameId); break;
                case SequenceAbandoned f: _open.RemoveAll(o => o.Id == f.FrameId); break;
            }
            IReadOnlyList<StateChange> changes = e.Payload switch { StepCompleted s => s.Changes, StateChange c => [c], _ => [] };
            foreach (var change in changes)
            {
                if (change is ModelsMoved moved)
                {
                    if (moved.Moves.Any(m => m.To is OnTable)) Add(Cue.Move);
                    if (moved.Moves.Any(m => m.From is OnTable && m.To is OnShelf)) Add(Cue.Wound);
                }
                else if (change is WoundsSet w)
                {
                    if (w.Wounds > _wounds.GetValueOrDefault(w.ModelId)) Add(Cue.Wound);
                    _wounds[w.ModelId] = w.Wounds;
                }
            }
        }
        return cues;
    }
}
