namespace Tabletop.Client.Core;

/// <summary>What the create-game dialog opens with: the default rule set selected, each set shown by its name.</summary>
public static class CreateDefaults
{
    public static int DefaultIndex(IReadOnlyList<PropertiesSummary> packs) => Math.Max(0, packs.ToList().FindIndex(p => p.Default));
    public static string PackLabel(PropertiesSummary p) => p.Name;

    /// <summary>What each enforcement setting does, one line under its picker (the viewer's ENFORCEMENT_HINT).</summary>
    public static string EnforcementHint(Tabletop.Protocol.Enforcement e) => e switch
    {
        Tabletop.Protocol.Enforcement.Silent => "Moves are never checked.",
        Tabletop.Protocol.Enforcement.Warn => "A move that breaks a rule is shown and asked about first.",
        _ => "A move that breaks a rule is refused.",
    };

    /// <summary>The name's problem, shown once the player has typed or tried to create: never before the first keystroke.</summary>
    public static string? NameShown(string name, bool touched) => touched ? TextLimits.NameProblem(name, "A game name") : null;
}
