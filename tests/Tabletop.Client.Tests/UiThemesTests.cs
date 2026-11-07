using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class UiThemesTests
{
    static readonly string ClientDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client"));
    [Fact] public void The_built_ins_are_fantasy_then_scifi() => Assert.Equal(new[] { "fantasy", "scifi" }, UiThemes.All.Select(t => t.Id));
    [Theory, InlineData(null), InlineData(""), InlineData("steampunk")] public void Unknown_theme_ids_resolve_to_scifi(string? id) => Assert.Same(UiThemes.SciFi, UiThemes.Resolve(id));
    [Fact] public void Every_theme_path_is_a_file_under_its_own_folder()
    {
        foreach (var t in UiThemes.All) foreach (var p in t.Paths)
        {
            Assert.StartsWith($"res://content/themes/{t.Id}/", p);
            Assert.True(File.Exists(Path.Combine(ClientDir, p["res://".Length..])), $"{t.Id}: {p} is missing");
        }
        Assert.Equal((32, 25), (UiThemes.Fantasy.Paths.Count(), UiThemes.SciFi.Paths.Count()));   // 2 fonts, 2 nine-patches, 6 faces, 22 icons; 3 fonts, 22 icons
    }
    [Fact] public void Every_theme_sets_fonts_six_teams_and_the_icons() => Assert.All(UiThemes.All, t =>
    {
        Assert.All(new[] { t.Display, t.Body, t.Numerals }, f => Assert.True(f.Size >= 12 && f.Path.EndsWith(".ttf"), f.Path));
        Assert.True(t.Teams.Count >= 6); Assert.All(new[] { "activated", "objective", "shaken", "fatigued", "settings", "team", "player", "army", "watch", "warning", "ready", "editing", "charge", "shoot", "activation", "hold", "advance", "rush", "melee", "cast", "wound", "key" }, n => Assert.NotNull(t.Icon(n)));
    });
    [Fact] public void Teams_cycle_icons_ignore_case_and_faces_are_one_to_six()
    {
        var t = UiThemes.Fantasy;
        Assert.Equal(t.Teams[0], t.Team(1)); Assert.Equal(t.Teams[0], t.Team(0)); Assert.Equal(t.Teams[1], t.Team(t.Teams.Count + 2));
        Assert.Equal(t.Icon("shaken"), t.Icon("Shaken")); Assert.Null(t.Icon("pinned")); Assert.Same(t, UiThemes.Resolve("Fantasy"));
        Assert.Equal("res://content/themes/fantasy/dice/6.svg", t.Dice.FaceTexture(6)); Assert.Null(t.Dice.FaceTexture(7)); Assert.Null(UiThemes.SciFi.Dice.FaceTexture(3));
        Assert.Equal(new Rgba(1, 0, 0), Rgba.Hex("#FF0000")); Assert.Equal(0.85f, Rgba.Hex("#0D1620D9").A, 2);
    }
}
