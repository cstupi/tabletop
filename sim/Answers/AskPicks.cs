using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>
/// Picks for ScriptBuilder.Match: an answer named by option label, unit name or model name rather than by id. Each returns null when the
/// Ask offers nothing that matches, so the run falls through to the policy. Labels and names only: nothing here reads a rule.
/// </summary>
public static class AskPicks
{
    /// <summary>The first option whose label passes.</summary>
    public static AskAnswer? Option(Ask ask, Func<AskOption, bool> pass) =>
        ask.Legal is Options o && o.Choices.FirstOrDefault(pass) is { } c ? new Chosen(c.Id) : null;

    /// <summary>The option with exactly that label.</summary>
    public static AskAnswer? Labeled(Ask ask, string label) => Option(ask, c => c.Label == label);

    /// <summary>An activate option, labelled "&lt;unit&gt;: &lt;action&gt;".</summary>
    public static AskAnswer? Activate(Ask ask, string unit, string action) => Labeled(ask, $"{unit}: {action}");

    /// <summary>A cast option for that spell, labelled "&lt;spell&gt; (&lt;cost&gt;)", optionally cast by a model of the named unit.</summary>
    public static AskAnswer? Spell(Ask ask, Projection projection, string spell, string? casterUnit = null) =>
        Option(ask, c => c.Label.StartsWith($"{spell} (", StringComparison.Ordinal)
            && (casterUnit is null || c.Pieces.Any(p => UnitNameOf(projection, p) == casterUnit)));

    /// <summary>
    /// From a PieceSelection, the eligible pieces whose unit is named one of <paramref name="units"/>, in that order of names, up to the Ask's
    /// maximum; null when fewer than its minimum match. A piece is a unit or a model; a model counts by its unit's name or its own.
    /// </summary>
    public static AskAnswer? Pieces(Ask ask, Projection projection, params string[] units)
    {
        if (ask.Legal is not PieceSelection s) return null;
        var picked = units.SelectMany(name => s.Eligible.Where(id => UnitNameOf(projection, id) == name || ModelNameOf(projection, id) == name))
            .Distinct().Take(s.Max).ToList();
        return picked.Count >= Math.Max(1, s.Min) ? new SelectedPieces(picked) : null;
    }

    /// <summary>The name of a unit, or of the unit a model belongs to; null for neither.</summary>
    public static string? UnitNameOf(Projection projection, Guid id) =>
        projection.Units.TryGetValue(id, out var u) ? u.Definition.Name
        : projection.Models.TryGetValue(id, out var m) && projection.Units.TryGetValue(m.UnitId, out var owner) ? owner.Definition.Name : null;

    static string? ModelNameOf(Projection projection, Guid id) => projection.Models.TryGetValue(id, out var m) ? m.Slot.Name : null;
}
