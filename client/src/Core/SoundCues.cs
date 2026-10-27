using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The table's sounds (spec 2026-10-24-sound-effects → Cues). Shoot has three variants; the rest one each.</summary>
public enum Cue { Shoot, Impact, Miss, Melee, Whiff, Block, Wound, CastFriendly, CastEnemy, Fizzle, Move }

/// <summary>A cue to play Delay seconds after its batch arrives. Variant picks among a cue's files; Pitch scales its playback rate and
/// VolumeDb its level. Volley: one die's sound of a volley, which plays on the volley voices.</summary>
public sealed record CueAt(Cue Cue, double Delay, int Variant = 0, double Pitch = 1, double VolumeDb = 0, bool Volley = false);

/// <summary>
/// Spec 2026-10-24-sound-effects → Cues: the cues a batch of live events plays, in log order. A `hit` roll under `shooting` is a volley:
/// one shot per die in the weapon's variant (by its name), staggered and semi-overlapping (Volley), then Impact or Miss by its dice. A
/// `hit` roll under `melee` is a clash: per die in die order a strike (Melee) for a hit and a Whiff for a miss, staggered the same way.
/// A `defense` roll blocks every hit or lets a wound through; a `casting` roll that fails fizzles, and a casting frame's `targets`
/// answer casts at the caster's team or another; a ModelsMoved landing a model on the table is a move; WoundsSet totals that rose, or a
/// model leaving the table for the shelf, are a wound. Every cue but a volley's plays at most once between volleys, so each weapon
/// group sounds its own result and a step's many wounds one. Each starts after the one before by that one's Gap (a volley's next group
/// by GroupGap). Wounds on the wire are totals, so this keeps each model's last, as FloatingResults does. A silent batch (catching up), an empty
/// one (a replay's jump redraws, then raises one) or one with an undo plays nothing and re-reads the totals and the open frames from the
/// projection, so a replay's forward step plays from where it stands.
/// </summary>
public sealed class SoundCues
{
    const string Shooting = "shooting", MeleeSeq = "melee", Casting = "casting";
    readonly Dictionary<Guid, int> _wounds = new();
    readonly List<(Guid Id, string Sequence, SequenceContext Context)> _open = [];

    /// <summary>The owner's cap: a volley's last die sound starts at most this long after its first.</summary>
    public const double MaxVolleySeconds = 5;
    /// <summary>The natural spacing of a volley's dice, kept while the volley fits MaxVolleySeconds.</summary>
    public const double VolleyInterval = 0.095;
    /// <summary>The tightest spacing: past it a volley plays MaxVolleySounds of its dice, spread evenly, instead of crowding closer.</summary>
    public const double MinVolleyInterval = 0.03;
    public static int MaxVolleySounds => (int)Math.Floor(MaxVolleySeconds / MinVolleyInterval + 1e-9) + 1;
    /// <summary>Each die sound moves up to this much off its slot (a quarter of the spacing at most), so the dice keep their order.</summary>
    public const double VolleyJitter = 0.012;
    /// <summary>From a volley's last die sound to the next weapon group's first.</summary>
    public const double GroupGap = 0.15;
    /// <summary>Each die sound's playback rate is within PitchSpread of 1, and its level within VolumeSpreadDb below full.</summary>
    public const double PitchSpread = 0.06, VolumeSpreadDb = 3;

    /// <summary>The spacing of a volley of n die sounds (n already capped at MaxVolleySounds).</summary>
    public static double Interval(int n) => n <= 1 ? 0 : Math.Max(MinVolleyInterval, Math.Min(VolleyInterval, MaxVolleySeconds / (n - 1)));

    /// <summary>How long after a cue starts the next one in its batch starts: a volley's last die or a result waits out the dice tray's 0.4 s tumble.</summary>
    public static double Gap(Cue cue) => cue switch
    {
        Cue.Shoot or Cue.CastFriendly or Cue.CastEnemy => 0.5,
        Cue.Impact or Cue.Miss or Cue.Melee or Cue.Whiff => 0.45,
        _ => 0.4,
    };

    public static int Variants(Cue cue) => cue == Cue.Shoot ? 3 : 1;

    /// <summary>A weapon's shot variant, from its name: the same weapon sounds the same in every game.</summary>
    public static int VariantOf(string weapon) => (int)(Fnv(System.Text.Encoding.UTF8.GetBytes(weapon), 0) % (uint)Variants(Cue.Shoot));

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
        foreach (var f in p.Stack) _open.Add((f.Id, f.Sequence, f.Context));
    }

    public void OnBoard() { _wounds.Clear(); _open.Clear(); }

    /// <param name="names">A unit's team, to tell a friendly spell target from an enemy one; a weapon's name, for its shot.</param>
    public IReadOnlyList<CueAt> OnBatch(IReadOnlyList<GameEvent> batch, Projection p, bool silent, Names names)
    {
        if (silent || batch.Count == 0 || batch.Any(e => e.Payload is Undone)) { Seed(p); return []; }
        var cues = new List<CueAt>(); var segment = 0;
        double Next(bool volley) => cues.Count == 0 ? 0 : cues[^1].Delay + (volley && cues[^1].Volley ? GroupGap : Gap(cues[^1].Cue));
        void Add(Cue c)
        {
            if (cues.Skip(segment).Any(x => x.Cue == c && !x.Volley)) return;
            cues.Add(new CueAt(c, Next(false)));
        }
        void Volley(IReadOnlyList<Cue> dice, Guid frame, int variant)
        {
            var n = Math.Min(dice.Count, MaxVolleySounds); var interval = Interval(n); var start = Next(true); var id = frame.ToByteArray();
            for (var i = 0; i < n; i++)
            {
                var die = n == dice.Count ? i : (int)((long)i * dice.Count / n);
                double U(uint salt) => Fnv(id, (uint)i * 4 + salt) / (double)uint.MaxValue;
                var jitter = i == 0 ? 0 : (2 * U(1) - 1) * Math.Min(VolleyJitter, interval / 4);
                if (i == n - 1) jitter = -Math.Abs(jitter);   // the last never starts past the cap
                cues.Add(new CueAt(dice[die], start + i * interval + jitter, variant, 1 + (2 * U(2) - 1) * PitchSpread, -VolumeSpreadDb * U(3), Volley: true));
            }
            segment = cues.Count;
        }
        void Rolled(Guid frame, RollRecord roll)
        {
            var hits = roll.Dice.Count(d => d.Success);
            switch (roll.Roll)
            {
                case "hit" when roll.Dice.Count > 0 && Kind() is { } kind:
                    if (kind.Sequence == MeleeSeq) { Volley(roll.Dice.Select(d => d.Success ? Cue.Melee : Cue.Whiff).ToList(), frame, 0); break; }
                    var weapon = Frame(frame)?.Context.WeaponIds is [var first, ..] ? names.Weapon(first) : null;
                    Volley(Enumerable.Repeat(Cue.Shoot, roll.Dice.Count).ToList(), frame, VariantOf(weapon ?? frame.ToString()));
                    Add(hits > 0 ? Cue.Impact : Cue.Miss);
                    break;
                case "defense" when roll.Dice.Count > 0:
                    // A blocked die may still set off extra wounds (`+1 wound`): only a roll with neither failures nor those is all blocked.
                    var through = hits < roll.Dice.Count || roll.Dice.Any(d => d.Triggered?.Any(t => t.EndsWith(" wound") || t.EndsWith(" wounds")) == true);
                    Add(through ? Cue.Wound : Cue.Block);
                    break;
                case Casting when roll.Dice.Count > 0 && hits == 0: Add(Cue.Fizzle); break;
            }
        }
        foreach (var e in batch)
        {
            switch (e.Payload)
            {
                case SequenceStarted s: _open.Add((s.FrameId, s.Sequence, s.Context)); break;
                case SequenceFinished f: Close(f.FrameId); break;
                case SequenceAbandoned f: Close(f.FrameId); break;
                case StepCompleted step:
                    foreach (var roll in step.Rolls) Rolled(step.FrameId, roll);
                    if (step.Answer is SelectedPieces picked && step.Step == "targets" && Frame(step.FrameId) is { Sequence: Casting } cast
                        && cast.Context.ActorUnitId is { } caster && picked.PieceIds.Count > 0)
                    {
                        var target = p.Models.TryGetValue(picked.PieceIds[0], out var model) ? model.UnitId : picked.PieceIds[0];
                        Add(names.TeamOfUnit(target) is { } t && t == names.TeamOfUnit(caster) ? Cue.CastFriendly : Cue.CastEnemy);
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

    /// <summary>FNV-1a over a salt and the bytes: a stable hash (string.GetHashCode differs per process).</summary>
    static uint Fnv(byte[] bytes, uint salt)
    {
        var h = 2166136261u;
        foreach (var b in BitConverter.GetBytes(salt).Concat(bytes)) { h ^= b; h *= 16777619; }
        return h;
    }
}
