namespace Tabletop.Sim.Suite;

/// <summary>
/// The names of a socket run's games. Every game a run seeds carries the run id in its name: "suite-run &lt;run&gt; &lt;game&gt;" while the
/// run owns it and will delete it, "suite &lt;run&gt; &lt;game&gt;" when --keep keeps it. A run holds its lock (ISocketRows.HoldAsync) for its
/// whole length; the sweep deletes the suite-run games of every other run whose lock no one holds. Kept games are never swept.
/// </summary>
public static class SuiteRows
{
    private const string Transient = "suite-run", Kept = "suite";

    /// <summary>What every transient game's name starts with.</summary>
    public const string TransientPrefix = Transient + " ";

    public static string GameName(Guid runId, string game, bool keep) => $"{(keep ? Kept : Transient)} {runId:N} {game}";

    /// <summary>The run that owns a transient game, or null for any name the suite did not write as transient: not the suite's to delete.</summary>
    public static Guid? TransientRunOf(string gameName)
    {
        var parts = gameName.Split(' ', 3);
        return parts.Length == 3 && parts[0] == Transient && Guid.TryParseExact(parts[1], "N", out var run) ? run : null;
    }

    /// <summary>The account every kept game's spectator participant names, beside the pool its players share.</summary>
    public const string Spectator = "sim-spectator";
}
