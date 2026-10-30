using System.Text;
using System.Text.Json;

namespace Tabletop.Sim.Suite;

/// <summary>
/// suite-&lt;run id&gt;.json. Verdict: running (written after each game), passed, failed, or aborted (an exception or Ctrl+C ended the
/// run; Games holds the games played). Summary is the verdict line. Coverage is null until the run finishes, and stays null when it aborted.
/// </summary>
public sealed record SuiteRunDocument(Guid RunId, string Driver, IReadOnlyList<string> Only, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    string Verdict, string Summary, int Planned, IReadOnlyList<SuiteGameResult> Games, IReadOnlyList<CoverageEntry>? Coverage);

/// <summary>
/// A suite run's report on disk: suite-&lt;run id&gt;.json and suite-&lt;run id&gt;.md in the out directory, copied to suite-latest.json and
/// suite-latest.md. Rewritten after every game, so a run that dies keeps what it played; Finish or Abort writes the last version.
/// </summary>
public sealed class SuiteReportFiles
{
    public const string Latest = "suite-latest";

    private readonly string outDir;
    private readonly Guid runId;
    private readonly DriverKind driver;
    private readonly IReadOnlyList<string> only;
    private readonly int planned;
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private readonly List<SuiteGameResult> games = [];

    public SuiteReportFiles(string outDir, Guid runId, DriverKind driver, IReadOnlyList<string> only, int planned)
    {
        (this.outDir, this.runId, this.driver, this.only, this.planned) = (outDir, runId, driver, only, planned);
        JsonPath = Path.Combine(outDir, $"suite-{runId:N}.json");
        MarkdownPath = Path.Combine(outDir, $"suite-{runId:N}.md");
    }

    public string JsonPath { get; }
    public string MarkdownPath { get; }

    public void Game(SuiteGameResult result)
    {
        games.Add(result);
        Write(Document("running", $"RUNNING: {Played()}; run {runId:N}", null, ended: false));
    }

    public void Finish(SuiteReport report)
    {
        games.Clear();
        games.AddRange(report.Games);
        Write(Document(report.Passed ? "passed" : "failed", report.Verdict, report.Coverage, ended: true), report);
    }

    /// <summary>The run ended before its report: the games played so far, no coverage.</summary>
    public void Abort(string reason) =>
        Write(Document("aborted", $"ABORTED ({reason}): {Played()}; run {runId:N}", null, ended: true));

    private string Played() =>
        $"{games.Count} of {planned} games played, {games.Count(g => g.Passed)} passed, {games.Count(g => !g.Passed)} failed";

    private SuiteRunDocument Document(string verdict, string summary, IReadOnlyList<CoverageEntry>? coverage, bool ended) =>
        new(runId, driver == DriverKind.Socket ? SimOptions.Socket : SimOptions.InProcess, only, startedAt,
            ended ? DateTimeOffset.UtcNow : null, verdict, summary, planned, [.. games], coverage);

    private void Write(SuiteRunDocument document, SuiteReport? report = null)
    {
        Directory.CreateDirectory(outDir);
        Replace(JsonPath, JsonSerializer.Serialize(document, SuiteReport.Json));
        Replace(MarkdownPath, Markdown(document, report));
        File.Copy(JsonPath, Path.Combine(outDir, $"{Latest}.json"), overwrite: true);
        File.Copy(MarkdownPath, Path.Combine(outDir, $"{Latest}.md"), overwrite: true);
    }

    /// <summary>Through a temporary file, so a reader never sees half a report.</summary>
    private static void Replace(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private static string Markdown(SuiteRunDocument d, SuiteReport? report)
    {
        var md = new StringBuilder();
        md.AppendLine($"# Rules suite run {d.RunId:N}").AppendLine();
        md.AppendLine($"**{d.Summary}**").AppendLine();
        md.AppendLine($"- Driver: {d.Driver}");
        md.AppendLine($"- Selection: {(d.Only.Count == 0 ? "every game" : string.Join(", ", d.Only))}");
        md.AppendLine($"- Started: {d.StartedAt:u}{(d.EndedAt is { } end ? $", ended: {end:u} ({(end - d.StartedAt).TotalSeconds:0} s)" : "")}");
        md.AppendLine($"- Machine-readable: `suite-{d.RunId:N}.json`").AppendLine();

        var failed = d.Games.Where(g => !g.Passed).ToList();
        md.AppendLine($"## Failures ({failed.Count})").AppendLine();
        foreach (var g in failed)
        {
            md.AppendLine($"### {g.Verdict} {g.Name}").AppendLine();
            md.AppendLine($"Group `{g.Group}`, seed {g.Seed}.").AppendLine();
            md.AppendLine("```text");
            foreach (var line in g.Failures) md.AppendLine(line);
            md.AppendLine("```").AppendLine();
            md.AppendLine($"Replay: `{g.Replay}`").AppendLine();
        }

        var stale = d.Games.SelectMany(g => g.StaleLines.Select(line => $"- {g.Name}: {line}")).ToList();
        if (stale.Count > 0)
        {
            md.AppendLine($"## Stale pins ({stale.Count})").AppendLine();
            foreach (var line in stale) md.AppendLine(line);
            md.AppendLine();
        }

        md.AppendLine($"## Games ({d.Games.Count} of {d.Planned})").AppendLine();
        md.AppendLine("| Verdict | Game | Edition | Group | Seed | Seconds |").AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var g in d.Games)
            md.AppendLine($"| {g.Verdict} | {g.Name} | {SuiteCatalog.EditionLabel(g.Edition)} | {g.Group} | {g.Seed} | {g.DurationSeconds:0.0} |");
        md.AppendLine();

        md.AppendLine("## Coverage").AppendLine();
        if (report is null) md.AppendLine(d.Verdict == "running" ? "Written when the run finishes." : "Not computed: the run did not finish.");
        else
        {
            md.AppendLine("```text");
            foreach (var line in report.CoverageLines(problemsOnly: false)) md.AppendLine(line);
            md.AppendLine("```");
        }
        return md.ToString();
    }
}
