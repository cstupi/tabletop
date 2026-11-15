using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>One "-1" over a model's head: Head is the top of the model (its placement raised by its slot's height), Index its place among
/// the model's numbers in the batch, so several wounds rise one after another.</summary>
public readonly record struct WoundNumber(Guid ModelId, Point3 Head, int Index)
{
    public double Delay => Index * WoundNumbers.Stagger;
}

/// <summary>
/// The damage numbers: each wound a live batch deals to a model that is still standing after it floats a "-1" over the model's head,
/// rising and fading over Seconds, a model's numbers Stagger apart. A model taken off the table, or at its MaxWounds, shows nothing; a
/// total that fell (healing) shows nothing. Wounds on the wire are totals, so this keeps each model's last, as SoundCues does. A batch
/// while the session catches up, an empty one (a replay's jump) or one with an undo floats nothing and re-reads the totals.
/// </summary>
public sealed class WoundNumbers
{
    public const double Seconds = 2.5, Stagger = 0.35;
    public const string Text = "-1";
    /// <summary>The share of Seconds a number stays fully opaque before it fades.</summary>
    const double Hold = 0.6;
    readonly Dictionary<Guid, int> _wounds = new();

    public void Seed(Projection p) { _wounds.Clear(); foreach (var m in p.Models.Values) _wounds[m.Id] = m.Wounds; }
    public void OnBoard() => _wounds.Clear();

    /// <summary>Numbers by event seq, shown when the Animator plays that event.</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<WoundNumber>> OnBatch(IReadOnlyList<GameEvent> batch, Projection p, bool catchingUp, IReadOnlyDictionary<Guid, int>? maxWounds = null)
    {
        if (catchingUp || batch.Count == 0 || batch.Any(e => e.Payload is Undone)) { Seed(p); return new Dictionary<long, IReadOnlyList<WoundNumber>>(); }
        var taken = new List<(long Seq, Guid Model, int Count)>();
        foreach (var e in batch)
            foreach (var w in ChangesOf(e.Payload).OfType<WoundsSet>())
            {
                var k = w.Wounds - _wounds.GetValueOrDefault(w.ModelId);
                _wounds[w.ModelId] = w.Wounds;
                if (k > 0) taken.Add((e.Seq, w.ModelId, k));
            }
        var shown = new Dictionary<long, IReadOnlyList<WoundNumber>>(); var next = new Dictionary<Guid, int>();
        foreach (var (seq, model, count) in taken)
        {
            if (!p.Models.TryGetValue(model, out var m) || m.Location is not OnTable on) continue;
            if (maxWounds is not null && maxWounds.TryGetValue(model, out var max) && m.Wounds >= max) continue;
            var head = on.Placement.Position with { Z = on.Placement.Position.Z + m.Slot.Height };
            var list = shown.TryGetValue(seq, out var l) ? (List<WoundNumber>)l : new List<WoundNumber>();
            for (var i = 0; i < count; i++) { var at = next.GetValueOrDefault(model); next[model] = at + 1; list.Add(new WoundNumber(model, head, at)); }
            shown[seq] = list;
        }
        return shown;
    }

    /// <summary>A number <paramref name="age"/> seconds after it appeared: Rise, 0 to 1 of its climb (fast, then slowing), and Alpha, opaque
    /// for the first part of its life, then fading to nothing at Seconds.</summary>
    public static (double Rise, double Alpha) Look(double age)
    {
        var t = Math.Clamp(age / Seconds, 0, 1);
        return (1 - (1 - t) * (1 - t), t <= Hold ? 1 : 1 - (t - Hold) / (1 - Hold));
    }

    static IReadOnlyList<StateChange> ChangesOf(EventPayload payload) => payload switch
    {
        StepCompleted s => s.Changes, RoundStarted r => r.Changes, RoundEnded r => r.Changes, TurnEnded t => t.Changes, StateChange c => [c], _ => [],
    };
}
