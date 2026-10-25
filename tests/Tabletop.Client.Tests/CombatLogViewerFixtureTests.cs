using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>
/// Renders Godot's combat log over three recorded logs into viewer/tests/fixtures/combatlog.json, which the viewer's Vitest renders
/// through its TypeScript port and compares line for line (browser play plan D3). `skirmish` and `terrain` are the viewer's whole sim
/// games with their welcome and board; `charge` is the owner's game 01a126db with no projection. Written only with
/// TABLETOP_WRITE_FIXTURES=1; a plain run fails when the checked-in file differs.
/// </summary>
public class CombatLogViewerFixtureTests
{
    sealed record Row(int Depth, string Kind, long Seq, bool Undone, string Text, string? Title = null, string? Result = null,
        string? Outcome = null, string? Chain = null, IReadOnlyList<string>? Details = null);

    sealed record Undo(long ToSeq, string Tooltip, IReadOnlyList<string> LostRolls);

    static readonly JsonSerializerOptions RowOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) return dir.FullName;
        throw new InvalidOperationException("Tabletop.sln not found above " + AppContext.BaseDirectory);
    }

    static string FixturePath => Path.Combine(RepoRoot(), "viewer", "tests", "fixtures", "combatlog.json");

    static IEnumerable<Row> Rows(IEnumerable<LogEntry> entries, int depth = 0)
    {
        foreach (var e in entries)
            switch (e)
            {
                case ActivationBlock b:
                    yield return new Row(depth, "block", b.Seq, b.Undone, b.Header, Chain: b.Chain);
                    foreach (var r in Rows(b.Entries, depth + 1)) yield return r;
                    break;
                case RollEntry r:
                    yield return new Row(depth, "roll", r.Seq, r.Undone, r.Collapsed(CombatLog.Pip), r.Title, r.Result, r.Outcome, r.Chain, [.. r.Details]);
                    break;
                case LineEntry l:
                    yield return new Row(depth, "line", l.Seq, l.Undone, l.Text);
                    break;
            }
    }

    static JsonObject CaseOf(string name, GameInfo game, Board? board, IReadOnlyList<GameEvent> events)
    {
        Projection? projection = null;
        if (board is not null)
        {
            var applier = new EventApplier(board, game);
            applier.ApplyAll(events);
            projection = applier.Projection;
        }
        var names = new Names(game, projection);
        var entries = CombatLog.Build(events, names);
        var latest = CombatLog.Latest(entries);
        var plan = UndoPlan.Of(events, names, entries);
        return new JsonObject
        {
            ["name"] = name,
            ["game"] = JsonNode.Parse(WireJson.Serialize(game)),
            ["board"] = board is null ? null : JsonNode.Parse(WireJson.Serialize(board)),
            ["events"] = JsonNode.Parse(JsonSerializer.Serialize(events.ToList(), WireJson.Options)),
            ["rows"] = JsonSerializer.SerializeToNode(Rows(entries).ToList(), RowOptions),
            ["latest"] = latest?.Collapsed(CombatLog.Pip),
            ["undo"] = plan is null ? null : JsonSerializer.SerializeToNode(new Undo(plan.ToSeq, plan.Tooltip, plan.LostRolls), RowOptions),
        };
    }

    static JsonObject Recorded(string name)
    {
        var dir = Path.Combine(RepoRoot(), "viewer", "tests", "fixtures");
        var welcome = (Welcome)WireJson.Deserialize(File.ReadAllText(Path.Combine(dir, name + ".welcome.json")));
        var board = (BoardMessage)WireJson.Deserialize(File.ReadAllText(Path.Combine(dir, name + ".board.json")));
        var events = (EventsMessage)WireJson.Deserialize(File.ReadAllText(Path.Combine(dir, name + ".events.json")));
        return CaseOf(name, welcome.Game, board.Board, events.Events);
    }

    static JsonArray Cases() => [Recorded("skirmish"), Recorded("terrain"), CaseOf("charge", CombatLogFixtureTests.Game with { Id = Guid.Parse("01a126db-0000-7000-8000-000000000001"), CreatedBy = Guid.Parse("01a126db-1bb1-77b2-9c9e-cef42380f3c4") }, null, CombatLogFixtureTests.Log())];

    [Fact]
    public void Combatlog_fixture_is_current()
    {
        var cases = Cases();
        var text = cases.ToJsonString() + "\n";
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1") File.WriteAllText(FixturePath, text, new UTF8Encoding(false));
        Assert.True(File.Exists(FixturePath), "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CombatLogViewerFixture");
        var onDisk = File.ReadAllText(FixturePath);
        if (onDisk == text) return;
        var stored = JsonNode.Parse(onDisk)!.AsArray();
        var stale = cases.Select((c, i) => (Name: (string)c!["name"]!, Same: i < stored.Count && stored[i]!.ToJsonString() == c!.ToJsonString())).FirstOrDefault(x => !x.Same).Name;
        Assert.Fail($"viewer/tests/fixtures/combatlog.json is stale ({stale ?? "the case list"}): run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CombatLogViewerFixture");
    }

    [Fact]
    public void Every_case_renders_rows()
    {
        foreach (var c in Cases()) Assert.NotEmpty(c["rows"]!.AsArray());
    }
}
