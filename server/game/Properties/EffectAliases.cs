namespace Tabletop.GameServer.Properties;

/// <summary>An effect row of another set: its entry, its position path (the index among its siblings at each level, top-level effect first)
/// and its kind.</summary>
public sealed record ForeignEffect(Guid Id, string Entry, IReadOnlyList<int> Path, EffectKind Kind);

/// <summary>
/// Grants name effect ids, and ids are per set. When a game moves to its game rules' current set (games-follow-the-current-rules spec),
/// an old set's row stands for the current set's row with the same entry, the same position path and the same kind; anything else has no alias.
/// </summary>
public static class EffectAliases
{
    public static IReadOnlyDictionary<Guid, Effect> Match(PropertySet set, IEnumerable<ForeignEffect> foreign)
    {
        var mine = Rows(set).ToDictionary(r => Key(r.Row.Entry, r.Row.Path), r => r.Effect);
        var own = new HashSet<Guid>(mine.Values.Select(e => e.Id));
        var aliases = new Dictionary<Guid, Effect>();
        foreach (var f in foreign)
            if (!own.Contains(f.Id) && mine.TryGetValue(Key(f.Entry, f.Path), out var e) && e.Kind == f.Kind) aliases.TryAdd(f.Id, e);
        return aliases;
    }

    /// <summary>Every effect row of a set as a foreign row: what the in-memory store hands another set of the same game rules.</summary>
    public static IEnumerable<ForeignEffect> Of(PropertySet set) => Rows(set).Select(r => r.Row);

    static IEnumerable<(ForeignEffect Row, Effect Effect)> Rows(PropertySet set)
    {
        IEnumerable<(ForeignEffect, Effect)> Walk(string entry, IReadOnlyList<Effect> effects, IReadOnlyList<int> parent) =>
            effects.SelectMany((e, i) =>
            {
                int[] path = [.. parent, i];
                return new[] { (new ForeignEffect(e.Id, entry, path, e.Kind), e) }.Concat(Walk(entry, e.Children, path));
            });
        return set.Entries.SelectMany(entry => Walk(entry.Name, entry.Effects, []));
    }

    static string Key(string entry, IReadOnlyList<int> path) => entry + "\u0000" + string.Join('.', path);
}
