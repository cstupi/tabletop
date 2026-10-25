using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum PickChange { Added, Removed, Unchanged }

/// <summary>The decision box's pick of pieces (a pick, the deploy pick, the Ambusher pick): toggles in the order made, the Max cap, the answer.</summary>
public sealed class PiecePick(PieceSelection selection, IReadOnlyList<Guid> eligible)
{
    readonly List<Guid> _picked = [];
    public IReadOnlyList<Guid> Picked => _picked;

    /// <summary>An eligible id toggles; a new pick past Max, or an id not eligible, is Unchanged.</summary>
    public PickChange Toggle(Guid id)
    {
        if (!eligible.Contains(id)) return PickChange.Unchanged;
        if (_picked.Remove(id)) return PickChange.Removed;
        if (_picked.Count >= selection.Max) return PickChange.Unchanged;
        _picked.Add(id);
        return PickChange.Added;
    }

    /// <summary>Max is 1 and one is picked: the box answers without Done.</summary>
    public bool AnswersAtOnce => selection.Max == 1 && _picked.Count == 1;
    public bool Ready => _picked.Count >= selection.Min && _picked.Count <= selection.Max;
    public SelectedPieces? Answer() => Ready ? new SelectedPieces(_picked.ToList()) : null;
    public string Count(Names names) => AnswersAtOnce ? names.Piece(_picked[0]) : $"{_picked.Count} of {selection.Min}–{selection.Max}";
}
