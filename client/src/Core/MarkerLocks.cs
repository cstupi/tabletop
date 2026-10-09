using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// Objective markers are locked from play on (Playing or Ended): a drag on one does not start until the marker's menu
/// unlocks it, and it stays unlocked until locked again. Tokens and setup are free. Local to this client; nothing on the wire.
/// </summary>
public sealed class MarkerLocks
{
    readonly HashSet<Guid> _unlocked = new();
    (MenuItem Item, Guid Id, bool Unlock)? _issued;
    public event Action? Changed;

    /// <summary>True for an objective, from setup or placed in play, while the game is Playing or Ended and it has not been unlocked.</summary>
    public bool IsLocked(GameStatus? status, Projection? p, Guid id) =>
        status is GameStatus.Playing or GameStatus.Ended && IsObjective(p, id) && !_unlocked.Contains(id);

    public bool MayDrag(GameStatus? status, Projection? p, Guid id) => !IsLocked(status, p, id);

    /// <summary>"Unlock" on a locked objective, "Lock" on an unlocked one, null otherwise. A local item: Build sends nothing, <see cref="Chosen"/> applies it.</summary>
    public MenuItem? ItemFor(GameStatus? status, Projection? p, Guid id)
    {
        if (status is not (GameStatus.Playing or GameStatus.Ended) || !IsObjective(p, id)) return null;
        var unlock = !_unlocked.Contains(id);
        var item = new MenuItem(unlock ? "Unlock" : "Lock", MenuInput.None, _ => null);
        _issued = (item, id, unlock);
        return item;
    }

    /// <summary>The menu's chosen item: applied and true when it is the last lock item this issued.</summary>
    public bool Chosen(MenuItem item)
    {
        if (_issued is not { } issued || !ReferenceEquals(issued.Item, item)) return false;
        if (issued.Unlock) _unlocked.Add(issued.Id); else _unlocked.Remove(issued.Id);
        _issued = null; Changed?.Invoke();
        return true;
    }

    static bool IsObjective(Projection? p, Guid id) =>
        (p?.Markers.GetValueOrDefault(id) ?? p?.Board.Table.Markers.FirstOrDefault(m => m.Id == id))?.Kind == MarkerKind.Objective;
}
