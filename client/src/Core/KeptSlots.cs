namespace Tabletop.Client.Core;

/// <summary>The bookkeeping of a box's kept children (Hud/Kept.cs): a render takes each child in order by its key, reusing the child made for
/// that key at or after the taking position and making one only when none stands there; the children it did not take are left over for the
/// box to hide, kept for a later render. Godot-free: the box applies what each Take reports (made, moved to an index).</summary>
public sealed class KeptSlots<TBase> where TBase : class
{
    readonly List<(string Key, TBase Item)> _items = [];
    int _at;

    /// <summary>Every child in its order: the taken ones first, then the left-over ones.</summary>
    public IReadOnlyList<TBase> Items => _items.Select(i => i.Item).ToList();

    /// <summary>How many the current render has taken.</summary>
    public int Taken => _at;

    public void Begin() => _at = 0;

    /// <summary>The next child: the one made for this key (and of type T), moved here if it stood later, else a new one from make, inserted here.
    /// MovedTo is the index the box must move it to, null when it stands there already.</summary>
    public (T Item, bool Made, int? MovedTo) Take<T>(string key, Func<T> make) where T : class, TBase
    {
        var found = -1;
        for (var k = _at; k < _items.Count; k++) if (_items[k].Key == key && _items[k].Item is T) { found = k; break; }
        var at = _at++;
        if (found < 0)
        {
            var made = make();
            _items.Insert(at, (key, made));
            return (made, true, at);
        }
        var item = _items[found].Item;
        if (found == at) return ((T)item, false, null);
        _items.RemoveAt(found); _items.Insert(at, (key, item));
        return ((T)item, false, at);
    }

    /// <summary>Ends a render: the children not taken, for the box to hide.</summary>
    public IReadOnlyList<TBase> End() => _items.Skip(_at).Select(i => i.Item).ToList();

    /// <summary>Forgets every child (the box frees them): the next render makes them again.</summary>
    public IReadOnlyList<TBase> Clear()
    {
        var gone = Items; _items.Clear(); _at = 0;
        return gone;
    }
}
