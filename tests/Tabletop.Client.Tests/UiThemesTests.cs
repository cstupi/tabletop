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
        Assert.Equal((72, 65), (UiThemes.Fantasy.Paths.Count(), UiThemes.SciFi.Paths.Count()));   // 2 fonts, 2 nine-patches, 6 faces, 62 icons; 3 fonts, 62 icons
    }
    [Fact] public void Every_theme_sets_fonts_six_teams_and_the_icons() => Assert.All(UiThemes.All, t =>
    {
        Assert.All(new[] { t.Display, t.Body, t.Numerals }, f => Assert.True(f.Size >= 12 && f.Path.EndsWith(".ttf"), f.Path));
        Assert.True(t.Teams.Count >= 6); Assert.All(new[] { "activated", "objective", "shaken", "fatigued", "settings", "team", "player", "army", "watch", "warning", "ready", "editing", "charge", "shoot", "replay", "menu", "search", "sign-out", "refresh" }.Concat(TerrainEffects.All.Select(e => e.Icon)).Append(TerrainEffects.Open.Icon), n => Assert.NotNull(t.Icon(n)));
    });
    [Fact] public void Teams_cycle_icons_ignore_case_and_faces_are_one_to_six()
    {
        var t = UiThemes.Fantasy;
        Assert.Equal(t.Teams[0], t.Team(1)); Assert.Equal(t.Teams[0], t.Team(0)); Assert.Equal(t.Teams[1], t.Team(t.Teams.Count + 2));
        Assert.Equal(t.Icon("shaken"), t.Icon("Shaken")); Assert.Null(t.Icon("pinned")); Assert.Same(t, UiThemes.Resolve("Fantasy"));
        Assert.Equal("res://content/themes/fantasy/dice/6.svg", t.Dice.FaceTexture(6)); Assert.Null(t.Dice.FaceTexture(7)); Assert.Null(UiThemes.SciFi.Dice.FaceTexture(3));
        Assert.Equal(new Rgba(1, 0, 0), Rgba.Hex("#FF0000")); Assert.Equal(0.85f, Rgba.Hex("#0D1620D9").A, 2);
    }

    static double Hue(Rgba c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B)), d = max - min;
        var h = max == c.R ? (c.G - c.B) / d % 6 : max == c.G ? (c.B - c.R) / d + 2 : (c.R - c.G) / d + 4;
        return (h * 60 + 360) % 360;
    }

    [Fact]
    public void No_team_reads_as_the_activated_band_green() => Assert.All(UiThemes.All, t => Assert.All(t.Teams, team =>
    {
        var apart = Math.Abs(Hue(team) - Hue(t.Success)); apart = Math.Min(apart, 360 - apart);
        Assert.True(apart >= 40, $"{t.Id}: team {team} is {apart:0}° from the success green {t.Success}");
    }));

    static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    static double Unlin(double c) { c = Math.Clamp(c, 0, 1); return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055; }
    static double Luminance(Rgba c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    static double Contrast(Rgba a, Rgba b) { var (x, y) = (Luminance(a), Luminance(b)); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }

    /// <summary>Machado, Oliveira and Fernandes (2009) at full severity, on linear RGB.</summary>
    static readonly Dictionary<string, double[,]> Deficiency = new()
    {
        ["deuteranopia"] = new[,] { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } },
        ["protanopia"] = new[,] { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } },
        ["tritanopia"] = new[,] { { 1.255528, -0.076749, -0.178779 }, { -0.078411, 0.930809, 0.147602 }, { 0.004733, 0.691367, 0.303900 } },
    };

    static Rgba Simulate(Rgba c, double[,] m)
    {
        double[] l = [Lin(c.R), Lin(c.G), Lin(c.B)];
        double Row(int i) => Unlin(m[i, 0] * l[0] + m[i, 1] * l[1] + m[i, 2] * l[2]);
        return new((float)Row(0), (float)Row(1), (float)Row(2));
    }

    /// <summary>CIE76 ΔE between two sRGB colours.</summary>
    static double DeltaE(Rgba a, Rgba b)
    {
        static (double, double, double) Lab(Rgba c)
        {
            double r = Lin(c.R), g = Lin(c.G), bl = Lin(c.B);
            double x = (0.4124 * r + 0.3576 * g + 0.1805 * bl) / 0.95047, y = 0.2126 * r + 0.7152 * g + 0.0722 * bl, z = (0.0193 * r + 0.1192 * g + 0.9505 * bl) / 1.08883;
            static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
            return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
        }
        var (l1, a1, b1) = Lab(a); var (l2, a2, b2) = Lab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    [Fact]
    public void The_colour_blind_teams_stay_apart_under_each_deficiency_and_read_on_both_panels()
    {
        var teams = UiThemes.ColourBlindTeams;
        Assert.Equal(6, teams.Count);
        foreach (var (name, m) in Deficiency.Prepend(new("normal", new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } })))
            for (var i = 0; i < teams.Count; i++)
                for (var j = i + 1; j < teams.Count; j++)
                    Assert.True(DeltaE(Simulate(teams[i], m), Simulate(teams[j], m)) >= 15, $"{name}: teams {i + 1} and {j + 1}");
        foreach (var t in UiThemes.All)
            foreach (var team in teams)
            {
                Assert.True(Contrast(team, t.Panel with { A = 1 }) >= 3, $"{t.Id}: {team} on the panel");
                var apart = Math.Abs(Hue(team) - Hue(t.Success)); apart = Math.Min(apart, 360 - apart);
                Assert.True(apart >= 40, $"{t.Id}: {team} is {apart:0}° from the success green");
            }
    }

    [Fact]
    public void Adjusted_scales_every_font_and_swaps_the_teams()
    {
        var t = UiThemes.Adjusted(UiThemes.SciFi, 130, colourBlind: true);
        Assert.Equal(((int)Math.Round(UiThemes.SciFi.Body.Size * 1.3), (int)Math.Round(UiThemes.SciFi.Display.Size * 1.3), (int)Math.Round(UiThemes.SciFi.Numerals.Size * 1.3)), (t.Body.Size, t.Display.Size, t.Numerals.Size));
        Assert.Equal(UiThemes.ColourBlindTeams, t.Teams);
        Assert.Equal(UiThemes.SciFi.Id, t.Id);
        Assert.Same(UiThemes.Fantasy, UiThemes.Adjusted(UiThemes.Fantasy, 100, colourBlind: false));
        Assert.Equal(26, UiThemes.Scaled(20, 130));
    }
}
