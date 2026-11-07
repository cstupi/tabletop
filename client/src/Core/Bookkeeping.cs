namespace Tabletop.Client.Core;

/// <summary>Conditions the server keeps so a rule can later read what a unit did this activation or round (Entrenched's "hasn't moved during
/// the round", Indirect's "after moving"). The unit card lists them; the roster and the table leave them out (client UX spec → Army roster).</summary>
public static class Bookkeeping
{
    public static readonly IReadOnlySet<string> Conditions = new HashSet<string>(StringComparer.Ordinal)
        { "moved", "moved this round", "ambushed this round", "re-deployed", "instinctive" };

    public static bool Shown(string condition) => !Conditions.Contains(condition);

    /// <summary>The conditions the unit card lists: `moved this round` goes while `moved` says the same.</summary>
    public static IEnumerable<string> OnCard(IEnumerable<string> conditions)
    {
        var all = conditions.ToList();
        return all.Contains("moved") ? all.Where(c => c != "moved this round") : all;
    }
}
