namespace Tabletop.Management.Http;

/// <summary>limit + after. The id is the cursor because UUIDv7 is time-ordered.</summary>
public readonly record struct Keyset(int Limit, Guid? After)
{
    public const int Default = 50, Max = 200;
    public static Keyset Parse(int? limit, Guid? after)
    {
        var l = limit ?? Default;
        if (l <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "limit must be positive");
        return new Keyset(Math.Min(l, Max), after);
    }
}

public sealed record Page<T>(IReadOnlyList<T> Items, Guid? Next)
{
    public static Page<T> Of(IReadOnlyList<T> items, int limit, Func<T, Guid> id) =>
        new(items, items.Count >= limit && items.Count > 0 ? id(items[^1]) : null);
}
