using System.Text;
using System.Text.Json.Nodes;

namespace Tabletop.Client.Tests;

/// <summary>
/// Writes viewer/tests/fixtures/calls.json: for each case, what Godot's Core builders send for a player's choice in a state cut from a recorded
/// log, and the decisions behind it. viewer/tests/calls.agreement.test.ts replays the cases through the browser's modules. Written only with
/// TABLETOP_WRITE_FIXTURES=1; a plain run fails when the checked-in file differs. Regenerate after the call logs
/// (TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests --filter CallLogFixtureTests).
/// </summary>
public class CallsViewerFixtureTests
{
    static string FixturePath => Path.Combine(CallCases.RepoRoot(), "viewer", "tests", "fixtures", "calls.json");

    [Fact]
    public void Calls_fixture_is_current()
    {
        var fixture = CallCases.Fixture();
        var text = fixture.ToJsonString() + "\n";
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1") File.WriteAllText(FixturePath, text, new UTF8Encoding(false));
        Assert.True(File.Exists(FixturePath), "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CallsViewerFixture");
        var onDisk = File.ReadAllText(FixturePath);
        if (onDisk == text) return;
        var stored = JsonNode.Parse(onDisk)!["cases"]!.AsArray();
        var cases = fixture["cases"]!.AsArray();
        var stale = cases.Select((c, i) => (Name: (string)c!["name"]!, Same: i < stored.Count && stored[i]!.ToJsonString() == c!.ToJsonString())).FirstOrDefault(x => !x.Same).Name;
        Assert.Fail($"viewer/tests/fixtures/calls.json is stale ({stale ?? "the case list"}): run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CallsViewerFixture");
    }

    [Fact]
    public void Every_required_case_kind_is_present()
    {
        var names = CallCases.All().Select(c => c.Name.Split(' ')[0]).ToHashSet();
        Assert.Empty(CallCases.Required.Except(names));
    }

    [Fact]
    public void Every_ask_case_rebuilds_the_logged_answer()
    {
        // The built AnswerAsk's answer equals the logged one, but a disembarking model's facing (Godot answers the exit facing).
        foreach (var c in CallCases.Answers()) Assert.True(c.RebuildsLogged, c.Name);
    }

    [Fact]
    public void Case_names_are_unique() =>
        Assert.Empty(CallCases.All().GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key));
}
