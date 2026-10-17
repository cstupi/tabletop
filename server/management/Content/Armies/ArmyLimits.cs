using System.Globalization;
using Tabletop.Geometry;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Armies;

/// <summary>An import or patch outside the army limits; the message is the problem detail.</summary>
public sealed class InvalidArmyException(string reason) : Exception(reason);

/// <summary>The bounds every stored army stays inside, whether it came from Army Forge or a patch.</summary>
public static class ArmyLimits
{
    public const int MinTarget = 2, MaxTarget = 6;
    public const int MaxEntrySize = 50, MaxUnits = 100, MaxModels = 500;
    public const int MinAttacks = 1, MaxAttacks = 100;
    public const int MaxRating = 50;
    /// <summary>A weapon entry's count is at most this many per model of its entry.</summary>
    public const int WeaponsPerModel = 4;
    public const int MaxRange = 120;
    public const double MinBase = 0.4, MaxBase = 12, MinHeight = 0.1, MaxHeight = 24;

    /// <summary>Null when the base is a defined shape with both dimensions in range (inches), else the reason.</summary>
    public static string? Base(Base b)
    {
        if (!Enum.IsDefined(b.Shape)) return $"base shape {(int)b.Shape} is not a shape.";
        return InRange(b.A, MinBase, MaxBase) && InRange(b.B, MinBase, MaxBase) ? null : $"base {Inches(b.A)}x{Inches(b.B)} is outside {MinBase}–{MaxBase} inches.";
    }

    public static string? Height(double h) => InRange(h, MinHeight, MaxHeight) ? null : $"height {Inches(h)} is outside {MinHeight}–{MaxHeight} inches.";

    /// <summary>Written so NaN is out of range.</summary>
    public static bool InRange(double v, double min, double max) => v >= min && v <= max;

    private static string Inches(double v) => v.ToString("G4", CultureInfo.InvariantCulture);

    /// <summary>Null when the patch's name, bases and heights are acceptable, else the problem detail. Asset references are
    /// checked by the endpoint against the caller's assets.</summary>
    public static string? Patch(PatchArmyRequest p)
    {
        if (p.Name is not null && Limits.Text(p.Name, "name", Limits.Name) is { } bad) return bad;
        if (p.Units?.Count > MaxUnits) return $"A patch names at most {MaxUnits} units.";
        if (p.Slots?.Count > MaxModels) return $"A patch names at most {MaxModels} slots.";
        foreach (var s in p.Slots ?? [])
        {
            if (s.Base is { } b && Base(b) is { } badBase) return $"Slot {s.SlotId}: {badBase}";
            if (s.Height is { } h && Height(h) is { } badHeight) return $"Slot {s.SlotId}: {badHeight}";
        }
        return null;
    }
}
