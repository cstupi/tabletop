namespace Tabletop.Sim;

/// <summary>
/// SplitMix64 over a counter. Deterministic for a seed and independent of the clock, the thread and
/// the machine. It chooses; it never judges — nothing here knows what a good choice is.
/// </summary>
public sealed class Chooser(ulong seed)
{
    private ulong _state = seed;
    public long Drawn { get; private set; }

    private ulong NextBits()
    {
        Drawn++;
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Next(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound), bound, "a choice needs at least one option");
        return bound == 1 ? 0 : (int)(NextBits() % (ulong)bound);
    }

    public double Double() => (NextBits() >> 11) * (1.0 / (1UL << 53));
    public bool Chance(double p) => p > 0 && Double() < p;
    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> items)
    {
        var a = items.ToArray();
        for (var i = a.Length - 1; i > 0; i--) { var j = Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }

    public IReadOnlyList<T> Subset<T>(IReadOnlyList<T> items, int count) => Shuffle(items).Take(count).ToList();
}
