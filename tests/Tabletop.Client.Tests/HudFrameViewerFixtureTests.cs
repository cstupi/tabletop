using System.Text;
using System.Text.Json.Nodes;
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>
/// Writes viewer/tests/fixtures/hudFrame.json: HudFrame over the window sizes and states the spec lists (log open and closed, measured parts,
/// replay, the browser's play page without the corner, setup and the lobby). viewer/tests/hudFrame.agreement.test.ts computes the same
/// through hudFrame.ts. Written only with TABLETOP_WRITE_FIXTURES=1 (--filter HudFrameViewerFixture); a plain run fails when the
/// checked-in file differs.
/// </summary>
public class HudFrameViewerFixtureTests
{
    static string FixturePath => Path.Combine(CallCases.RepoRoot(), "viewer", "tests", "fixtures", "hudFrame.json");

    static readonly (int W, int H)[] Sizes = [(1280, 720), (1366, 768), (1920, 1080), (2560, 1440)];

    static readonly HudPart[] Play = [HudPart.Corner, HudPart.Header, HudPart.LogButton, HudPart.UndoButton, HudPart.Roster, HudPart.UnitCard, HudPart.Decision, HudPart.Dice];

    static readonly (string Name, string Rows, HudPart[] Shown, Dictionary<HudPart, (float W, float H)> Measured)[] States =
    [
        ("log closed", "play", Play, []),
        ("log open", "play", [.. Play, HudPart.LogPanel], []),
        ("measured", "play", [.. Play, HudPart.Loading], new() { [HudPart.Roster] = (420, 120), [HudPart.UnitCard] = (420, 260), [HudPart.Decision] = (480, 220),
            [HudPart.Dice] = (480, 80), [HudPart.Header] = (0, 118.4f), [HudPart.LogButton] = (44, 44), [HudPart.UndoButton] = (44, 44) }),
        ("measured, log open", "play", [.. Play, HudPart.LogPanel], new() { [HudPart.Decision] = (480, 180), [HudPart.Header] = (0, 120) }),
        ("an ask, no unit", "play", [HudPart.Corner, HudPart.Header, HudPart.LogButton, HudPart.UndoButton, HudPart.Roster, HudPart.Decision], []),
        ("replay", "play", [HudPart.Corner, HudPart.Header, HudPart.LogButton, HudPart.UndoButton, HudPart.Roster, HudPart.UnitCard, HudPart.Replay, HudPart.Dice],
            new() { [HudPart.Replay] = (420, 131) }),
        ("replay, log open", "play", [HudPart.Corner, HudPart.Header, HudPart.LogButton, HudPart.UndoButton, HudPart.Roster, HudPart.Replay, HudPart.LogPanel, HudPart.Dice], []),
        ("browser", "play", [HudPart.Header, HudPart.LogButton, HudPart.UndoButton, HudPart.Roster, HudPart.UnitCard, HudPart.Decision, HudPart.Dice],
            new() { [HudPart.Header] = (0, 64), [HudPart.Roster] = (420, 300), [HudPart.LogButton] = (40, 40), [HudPart.UndoButton] = (40, 40) }),
        ("setup", "setup", [HudPart.TopBar, HudPart.Corner, HudPart.Rail, HudPart.ToolPanel, HudPart.Hints, HudPart.TerrainCard],
            new() { [HudPart.TopBar] = (0, 60.5f), [HudPart.Hints] = (0, 34), [HudPart.Corner] = (360, 30), [HudPart.TerrainCard] = (420, 300) }),
        ("setup, tool shut", "setup", [HudPart.TopBar, HudPart.Corner, HudPart.Rail, HudPart.Hints, HudPart.Loading], []),
        ("lobby", "setup", [HudPart.TopBar, HudPart.Corner], []),
    ];

    static string Name(HudPart p) => char.ToLowerInvariant(p.ToString()[0]) + p.ToString()[1..];
    static JsonArray Rect(HudRect r) => new(r.X, r.Y, r.W, r.H);
    static JsonObject Rects(IReadOnlyDictionary<HudPart, HudRect> rects) => new(rects.OrderBy(kv => kv.Key).Select(kv => KeyValuePair.Create(Name(kv.Key), (JsonNode?)Rect(kv.Value))));

    public static JsonObject Fixture()
    {
        var cases = new JsonArray();
        foreach (var (w, h) in Sizes)
            foreach (var (name, rows, shown, measured) in States)
            {
                var f = HudFrame.Of(w, h, shown.ToHashSet(), (part, _) => measured.TryGetValue(part, out var m) ? m : null, rows == "play" ? HudFrame.Play : HudFrame.Setup);
                cases.Add(new JsonObject
                {
                    ["name"] = name, ["w"] = w, ["h"] = h, ["rows"] = rows,
                    ["shown"] = new JsonArray(shown.Select(p => (JsonNode?)Name(p)).ToArray()),
                    ["measured"] = new JsonObject(measured.Select(kv => KeyValuePair.Create(Name(kv.Key), (JsonNode?)new JsonArray(kv.Value.W, kv.Value.H)))),
                    ["slots"] = Rects(f.Slots), ["rects"] = Rects(f.Rects),
                    ["order"] = new JsonArray(f.Order.Select(p => (JsonNode?)Name(p)).ToArray()),
                    ["obstacles"] = new JsonArray(f.Obstacles.Select(o => (JsonNode?)Rect(o)).ToArray()),
                    ["covered"] = new JsonArray(Enum.GetValues<HudPart>().Where(f.Covered).Select(p => (JsonNode?)Name(p)).ToArray()),
                });
            }
        return new JsonObject { ["cases"] = cases };
    }

    [Fact]
    public void Hud_frame_fixture_is_current()
    {
        var text = Fixture().ToJsonString() + "\n";
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1") File.WriteAllText(FixturePath, text, new UTF8Encoding(false));
        Assert.True(File.Exists(FixturePath), "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter HudFrameViewerFixture");
        Assert.Equal(File.ReadAllText(FixturePath), text);
    }
}
