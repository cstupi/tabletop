namespace Tabletop.GameServer.Dice;

public interface IDiceSource { int Draw(ulong seed, long index, int sides); }

/// <summary>Counter-based: die i of a game is a pure function of (seed, i), so the cursor is the whole RNG state.</summary>
public sealed class DiceSource : IDiceSource
{
    public int Draw(ulong seed, long index, int sides)
    {
        var z = seed ^ ((ulong)index * 0x9E3779B97F4A7C15UL) + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (ulong)sides) + 1;
    }
}

/// <summary>Tests: the faces in order, cycling. The seed is ignored; the index is the cursor.</summary>
public sealed class ScriptedDice(params int[] faces) : IDiceSource
{
    public int Draw(ulong seed, long index, int sides) => faces[(int)(index % faces.Length)];
}
