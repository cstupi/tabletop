using System.Text;
using System.Text.Json.Nodes;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>
/// Writes viewer/tests/fixtures/unitActions.json: Godot's UnitActionsView over recorded logs cut at their first activation Asks, for each
/// player and each unit selected, and once watching. viewer/tests/unitActions.agreement.test.ts computes the same through the browser's port.
/// Written only with TABLETOP_WRITE_FIXTURES=1 (--filter UnitActionsViewerFixture); a plain run fails when the checked-in file differs.
/// </summary>
public class UnitActionsViewerFixtureTests
{
    static readonly string[] Logs = ["skirmish", "calls/selfplay", "calls/casting"];
    const int AsksPerLog = 4, OtherAsksPerLog = 8;

    static string FixturePath => Path.Combine(CallCases.RepoRoot(), "viewer", "tests", "fixtures", "unitActions.json");

    static JsonNode? Of(UnitActions? v) => v is null ? null : new JsonObject
    {
        ["askId"] = v.AskId.ToString(), ["unitId"] = v.UnitId.ToString(), ["anchor"] = new JsonArray(v.Anchor.X, v.Anchor.Y, v.Anchor.Z),
        ["options"] = new JsonArray(v.Options.Select(o => (JsonNode?)new JsonObject
        {
            ["optionId"] = o.OptionId, ["key"] = o.Key, ["word"] = o.Word, ["icon"] = o.Icon, ["tip"] = UnitActions.Tip(o),
        }).ToArray()),
        ["foot"] = new JsonArray(v.Actions.Select(a => (JsonNode?)new JsonObject
        {
            ["id"] = a.Id, ["word"] = a.Word, ["icon"] = a.Icon, ["primary"] = a.Primary, ["key"] = a.Key, ["tip"] = a.Tip,
        }).ToArray()),
        ["targets"] = new JsonArray(v.Targets.Select(t => (JsonNode?)new JsonObject
        {
            ["unitId"] = t.UnitId.ToString(), ["anchor"] = new JsonArray(t.Anchor.X, t.Anchor.Y, t.Anchor.Z),
        }).ToArray()),
    };

    public static JsonObject Fixture()
    {
        var cases = new JsonArray();
        foreach (var name in Logs)
        {
            var log = CallCases.Log(name);
            // The first activation Asks, then the first of every other step of an activation (moves, shooting, casualties, confirms).
            var seqs = log.Events.Where(e => e.Payload is AskOpened { Ask: var a } && DecisionCard.Activations.Contains(a.Question)).Select(e => e.Seq).Take(AsksPerLog)
                .Concat(log.Events.Where(e => e.Payload is AskOpened { Ask: var a } && !DecisionCard.Activations.Contains(a.Question) && !a.Question.StartsWith("deploy", StringComparison.Ordinal))
                    .Select(e => e.Seq).Take(OtherAsksPerLog)).ToList();
            var players = log.Game.Participants.Where(x => x.TeamId is not null).Select(x => x.Id).Distinct().Order().ToList();
            foreach (var seq in seqs)
                foreach (var (player, watching) in players.Select(x => (x, false)).Append((players[0], seq == seqs[0])).Distinct())
                {
                    var s = CallState.Playing(log, seq, player);
                    foreach (var unit in s.P!.Units.Keys.Order())
                    {
                        if (watching && unit != s.P.Units.Keys.Order().First()) continue;
                        cases.Add(new JsonObject
                        {
                            ["log"] = name, ["upTo"] = seq, ["participantId"] = player.ToString(), ["selected"] = unit.ToString(), ["watching"] = watching,
                            ["actions"] = Of(UnitActionsView.Of(s.P, s.Game, s.Acting, unit, watching)),
                        });
                    }
                }
        }
        return new JsonObject { ["cases"] = cases };
    }

    [Fact]
    public void Unit_actions_fixture_is_current()
    {
        var text = Fixture().ToJsonString() + "\n";
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1") File.WriteAllText(FixturePath, text, new UTF8Encoding(false));
        Assert.True(File.Exists(FixturePath), "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter UnitActionsViewerFixture");
        Assert.True(File.ReadAllText(FixturePath) == text, "viewer/tests/fixtures/unitActions.json is stale: run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter UnitActionsViewerFixture");
    }

    [Fact]
    public void The_fixture_shows_buttons_and_hides_them()
    {
        var cases = Fixture()["cases"]!.AsArray();
        Assert.Contains(cases, c => c!["actions"]?["foot"]!.AsArray().Count > 0);
        Assert.Contains(cases, c => c!["actions"]?["targets"]!.AsArray().Count > 0);
        Assert.Contains(cases, c => c!["actions"] is not null);
        Assert.Contains(cases, c => c!["actions"] is null && !(bool)c["watching"]!);
        Assert.Contains(cases, c => (bool)c!["watching"]!);
        Assert.All(cases.Where(c => (bool)c!["watching"]!), c => Assert.Null(c!["actions"]));
    }
}
