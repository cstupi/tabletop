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

    /// <summary>The Yes of a Yes/No Ask (the extra attacks an optional row offers).</summary>
    public static AskAnswer? Yes(Ask ask) => Labeled(ask, "Yes");

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

    /// <summary>The unit whose shooting a declare_targets Ask is for: the actor of the frame that asks.</summary>
    public static string? ShooterOf(Ask ask, Projection projection) =>
        ask.FrameId is { } frame && projection.Stack.FirstOrDefault(f => f.Id == frame)?.Context.ActorUnitId is { } actor ? UnitNameOf(projection, actor) : null;

    /// <summary>A declare_targets answer with every weapon group at the named unit, when every group may take it; else null.</summary>
    public static AskAnswer? AllAt(Ask ask, Projection projection, string unit) =>
        ask.Legal is Assignment a && a.Targets.FirstOrDefault(t => UnitNameOf(projection, t) == unit) is var t && t != Guid.Empty
            && a.Items.All(i => a.TargetsOf(i.Id).Contains(t)) ? new Assigned(a.Items.ToDictionary(i => i.Id, _ => t)) : null;

    /// <summary>Every weapon group of the shooter at the unit targetOf names for it (AllAt); null when it names none.</summary>
    public static AskAnswer? Aimed(Ask ask, Projection projection, Func<string, string?> targetOf) =>
        ShooterOf(ask, projection) is { } shooter && targetOf(shooter) is { } target ? AllAt(ask, projection, target) : null;
}
