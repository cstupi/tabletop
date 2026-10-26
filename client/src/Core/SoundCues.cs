using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The table's sounds (spec 2026-10-24-sound-effects → Cues). Shoot has three variants; the rest one each.</summary>
public enum Cue { Shoot, Impact, Miss, Melee, Whiff, Block, Wound, CastFriendly, CastEnemy, Fizzle, Move }

/// <summary>A cue to play Delay seconds after its batch arrives; Variant picks among a cue's files.</summary>
public sealed record CueAt(Cue Cue, double Delay, int Variant = 0);

/// <summary>
/// Spec 2026-10-24-sound-effects → Cues: the cues a batch of live events plays, in order of first appearance, each at most once, each
/// after the one before it by that one's Gap, so a shot reads fire, then the dice landing (hit or miss), then the block or the wound.
/// Reads the records the combat log reads: an `attack-sequence` starting under an open `shooting` frame fires (once per shooting frame,
/// the variant by that frame's id); a `hit` roll under `shooting` or `melee` hits or misses by its dice; a `defense` roll blocks every
/// hit or lets a wound through; a `casting` roll that fails fizzles, and a casting frame's `targets` answer casts at the caster's team
/// or another; a ModelsMoved landing a model on the table is a move; WoundsSet totals that rose, or a model leaving the table for the
/// shelf, are a wound. Wounds on the wire are totals, so this keeps each model's last, as FloatingResults does. A silent batch
/// (catching up, a replay) or one with an undo plays nothing and re-reads the totals and the open frames from the projection; a
/// shooting frame already open then has played its shot.
/// </summary>
public sealed class SoundCues
{
    const string Shooting = "shooting", MeleeSeq = "melee", AttackSequence = "attack-sequence", Casting = "casting";
    readonly Dictionary<Guid, int> _wounds = new();
    readonly List<(Guid Id, string Sequence, SequenceContext Context)> _open = [];
    readonly HashSet<Guid> _fired = [];

    /// <summary>How long after a cue starts the next one in its batch starts: a fire or a result waits out the dice tray's 0.4 s tumble.</summary>
    public static double Gap(Cue cue) => cue switch
    {
        Cue.Shoot or Cue.CastFriendly or Cue.CastEnemy => 0.5,
        Cue.Impact or Cue.Miss or Cue.Melee or Cue.Whiff => 0.45,
        _ => 0.4,
    };

    public static int Variants(Cue cue) => cue == Cue.Shoot ? 3 : 1;

    /// <summary>The file a cue's variant plays, in content/sounds without its .wav.</summary>
    public static string File(Cue cue, int variant) => cue switch
    {
        Cue.Shoot => $"shoot-{variant + 1}",
        Cue.CastFriendly => "cast-friendly",
        Cue.CastEnemy => "cast-enemy",
        _ => cue.ToString().ToLowerInvariant(),
    };

    public void Seed(Projection p)
    {
        OnBoard();
        foreach (var m in p.Models.Values) _wounds[m.Id] = m.Wounds;
        foreach (var f in p.Stack) { _open.Add((f.Id, f.Sequence, f.Context)); if (f.Sequence == Shooting) _fired.Add(f.Id); }
    }

    public void OnBoard() { _wounds.Clear(); _open.Clear(); _fired.Clear(); }

    /// <param name="teamOf">A unit's team, to tell a friendly spell target from an enemy one.</param>
    public IReadOnlyList<CueAt> OnBatch(IReadOnlyList<GameEvent> batch, Projection p, bool silent, Func<Guid, Guid?> teamOf)
    {
        if (silent || batch.Any(e => e.Payload is Undone)) { Seed(p); return []; }
        var cues = new List<CueAt>();
        void Add(Cue c, int variant = 0)
        {
            if (cues.Any(x => x.Cue == c)) return;
            cues.Add(new CueAt(c, cues.Count == 0 ? 0 : cues[^1].Delay + Gap(cues[^1].Cue), variant));
        }
        foreach (var e in batch)
        {
            switch (e.Payload)
            {
                case SequenceStarted s:
                    if (s.Sequence == AttackSequence && Kind() is { Sequence: Shooting } shooting && _fired.Add(shooting.Id))
                        Add(Cue.Shoot, Variant(shooting.Id));
                    _open.Add((s.FrameId, s.Sequence, s.Context));
                    break;
                case SequenceFinished f: Close(f.FrameId); break;
                case SequenceAbandoned f: Close(f.FrameId); break;
                case StepCompleted step:
                    foreach (var roll in step.Rolls) Rolled(roll);
                    if (step.Answer is SelectedPieces picked && step.Step == "targets" && Frame(step.FrameId) is { Sequence: Casting } cast
                        && cast.Context.ActorUnitId is { } caster && picked.PieceIds.Count > 0)
                    {
                        var target = p.Models.TryGetValue(picked.PieceIds[0], out var model) ? model.UnitId : picked.PieceIds[0];
                        Add(teamOf(target) is { } t && t == teamOf(caster) ? Cue.CastFriendly : Cue.CastEnemy);
                    }
                    break;
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

        void Rolled(RollRecord roll)
        {
            var hits = roll.Dice.Count(d => d.Success);
            switch (roll.Roll)
            {
                case "hit" when roll.Dice.Count > 0 && Kind() is { } kind:
                    var melee = kind.Sequence == MeleeSeq;
                    Add(hits > 0 ? melee ? Cue.Melee : Cue.Impact : melee ? Cue.Whiff : Cue.Miss);
                    break;
                case "defense" when roll.Dice.Count > 0:
                    // A blocked die may still set off extra wounds (`+1 wound`): only a roll with neither failures nor those is all blocked.
                    var through = hits < roll.Dice.Count || roll.Dice.Any(d => d.Triggered?.Any(t => t.EndsWith(" wound") || t.EndsWith(" wounds")) == true);
                    Add(through ? Cue.Wound : Cue.Block);
                    break;
                case Casting when roll.Dice.Count > 0 && hits == 0: Add(Cue.Fizzle); break;
            }
        }
    }

    /// <summary>The innermost open attack frame: shooting or melee, unless a casting or another sequence that deals hits sits inside it.</summary>
    (Guid Id, string Sequence, SequenceContext Context)? Kind()
    {
        for (var i = _open.Count - 1; i >= 0; i--)
            if (_open[i].Sequence is Shooting or MeleeSeq or Casting or "spell-hits" or "rule-hits" or "pick-rule")
                return _open[i].Sequence is Shooting or MeleeSeq ? _open[i] : null;
        return null;
    }

    (Guid Id, string Sequence, SequenceContext Context)? Frame(Guid id) => _open.FindIndex(f => f.Id == id) is var i and >= 0 ? _open[i] : null;

    /// <summary>Pops the frame and everything opened after it.</summary>
    void Close(Guid id) { if (_open.FindIndex(f => f.Id == id) is var i and >= 0) _open.RemoveRange(i, _open.Count - i); }

    /// <summary>The same shooting frame always picks the same variant: its id's last byte, random in a v7 id.</summary>
    static int Variant(Guid frame) => frame.ToByteArray()[15] % Variants(Cue.Shoot);
}
