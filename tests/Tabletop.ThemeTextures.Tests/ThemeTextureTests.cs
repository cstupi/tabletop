using Tabletop.Client.Core;
using Tabletop.TableTextures;

namespace Tabletop.ThemeTextures.Tests;

public class ThemeTextureTests
{
    static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    public static TheoryData<string, string, int> Files { get { var d = new TheoryData<string, string, int>(); foreach (var s in Themes.All) foreach (var k in Themes.Scales) d.Add(s.Theme, s.Name, k); return d; } }
    public static TheoryData<string> Ids => new(UiThemes.All.Select(t => t.Id));
    static Sheet Named(string theme, string name) => Themes.All.Single(s => s.Theme == theme && s.Name == name);

    [Fact]
    public void The_generator_draws_the_same_image_every_time()
    {
        foreach (var sheet in new[] { Named("fantasy", "button"), Named("scifi", "input") })
            Assert.Equal(sheet.Render(1), sheet.Render(1));
    }

    /// <summary>The client's and the web app's copies are the same file, within the size budget, and the generator's output (a step per
    /// channel allowed: MathF's last bits differ between CPUs).</summary>
    [Theory, MemberData(nameof(Files))]
    public void The_shipped_image_is_the_generators_output_and_small(string theme, string name, int scale)
    {
        var sheet = Named(theme, name);
        var client = File.ReadAllBytes(Path.Combine(Root, Themes.Client, theme, sheet.File(scale)));
        Assert.Equal(client, File.ReadAllBytes(Path.Combine(Root, Themes.Web, theme, sheet.File(scale))));
        Assert.True(client.Length < 512 * 1024, $"{theme}/{sheet.File(scale)} is {client.Length} bytes");
        var (w, h, shipped) = Png.Read(client);
        Assert.Equal((sheet.Width * scale, sheet.Height * scale), (w, h));
        var fresh = sheet.Render(scale); var off = 0;
        for (var i = 0; i < fresh.Length; i++) if (Math.Abs(fresh[i] - shipped[i]) > 1) off++;
        Assert.True(off < fresh.Length / 1000, $"{theme}/{sheet.File(scale)} is stale ({off} bytes differ): run scripts/theme-textures");
    }

    /// <summary>A nine-patch's repeated parts join: the column (row) at the end of the centre differs from the one at its start no more
    /// than neighbouring columns inside it do. A tile wraps the same way.</summary>
    [Theory, MemberData(nameof(Ids))]
    public void Repeated_parts_join_without_a_seam(string id)
    {
        foreach (var sheet in Themes.All.Where(s => s.Theme == id && s.Use != Use.Stretch))
        {
            var px = sheet.Render(1); int w = sheet.Width, h = sheet.Height, m = sheet.Margin;
            float Diff(Func<int, int> a, Func<int, int> b, int n, bool column, int from = 0)
            {
                var sum = 0f;
                for (var i = from; i < n; i++)
                {
                    var (ka, kb) = column ? ((i * w + a(i)) * 4, (i * w + b(i)) * 4) : ((a(i) * w + i) * 4, (b(i) * w + i) * 4);
                    for (var c = 0; c < 4; c++) sum += Math.Abs(px[ka + c] - px[kb + c]);
                }
                return sum / (n - from);
            }
            if (sheet.Use == Use.Tile)
            {
                // A tile wraps: its last column beside its first differs no more than two neighbours inside it.
                var seamX = Diff(_ => w - 1, _ => 0, h, true); var insideX = Diff(_ => w / 2 - 1, _ => w / 2, h, true);
                var seamY = Diff(_ => h - 1, _ => 0, w, false); var insideY = Diff(_ => h / 2 - 1, _ => h / 2, w, false);
                Assert.True(seamX <= insideX * 2.5f + 8, $"{id}/{sheet.Name}: across {seamX} vs {insideX}");
                Assert.True(seamY <= insideY * 2.5f + 8, $"{id}/{sheet.Name}: down {seamY} vs {insideY}");
                continue;
            }
            // A face stretched top to bottom (sci-fi's glass) repeats only across.
            var stretchedDown = sheet.Name.StartsWith("button") && UiThemes.All.Single(t => t.Id == id).Materials.ButtonStretch;
            // A nine-patch repeats its centre from column m to w − m: the column that starts the next repeat (the right edge's first, which
            // Godot and the browser draw after the last repeat) is the centre's first, and the same down. Deliberate joints (a plank end, a
            // plate seam) repeat with it.
            for (var k = 0; k < 2; k++)
            {
                Assert.True(Diff(_ => m + k, _ => w - m + k, h - m, true, m) <= 6, $"{id}/{sheet.Name}: column {m + k} against {w - m + k}");
                if (!stretchedDown) Assert.True(Diff(_ => m + k, _ => h - m + k, w - m, false, m) <= 6, $"{id}/{sheet.Name}: row {m + k} against {h - m + k}");
            }
        }
    }

    [Theory, MemberData(nameof(Ids))]
    public void The_client_reads_each_image_with_its_sheets_margin_and_inset(string id)
    {
        var m = UiThemes.All.Single(t => t.Id == id).Materials;
        foreach (var image in m.NinePatches)
        {
            var sheet = Named(id, Path.GetFileNameWithoutExtension(image.Path));
            Assert.Equal((Use.NinePatch, sheet.Margin, sheet.Inset), (sheet.Use, image.Margin, image.Inset));
        }
        Assert.Equal(Named(id, "backdrop-tile").Width, m.TileSize);
    }

    static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    static double Luminance(Rgba c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    /// <summary>The lightest and darkest luminance under text: where text sits (inside the frame and the padding), each pixel's 3×3
    /// mean (a stroke covers several pixels), the image over white for the lightest and over black for the darkest.</summary>
    static (double Light, double Dark) UnderText(Sheet sheet, int padX, int padY)
    {
        var px = sheet.Render(1); int w = sheet.Width;
        double At(int x, int y, double behind)
        {
            var k = (y * w + x) * 4; double a = px[k + 3] / 255.0;
            double Ch(int c) => Lin(px[k + c] / 255.0) * a + behind * (1 - a);
            return 0.2126 * Ch(0) + 0.7152 * Ch(1) + 0.0722 * Ch(2);
        }
        double light = 0, dark = 1;
        for (var y = sheet.Inset + padY + 1; y < sheet.Height - sheet.Inset - padY - 1; y++)
            for (var x = sheet.Inset + padX + 1; x < w - sheet.Inset - padX - 1; x++)
            {
                double l = 0, d = 0;
                for (var j = -1; j <= 1; j++) for (var i = -1; i <= 1; i++) { l += At(x + i, y + j, 1); d += At(x + i, y + j, 0); }
                light = Math.Max(light, l / 9); dark = Math.Min(dark, d / 9);
            }
        return (light, dark);
    }

    static double Worst(Rgba text, (double Light, double Dark) under)
    {
        var t = Luminance(text);
        return Math.Min((Math.Max(t, under.Light) + 0.05) / (Math.Min(t, under.Light) + 0.05), (Math.Max(t, under.Dark) + 0.05) / (Math.Min(t, under.Dark) + 0.05));
    }

    /// <summary>Spec 2026-11-21-theme-materials-design.md → Constraints: text on every panel, button face and field reads at 4.5:1 or
    /// better against the material's lightest (and darkest) area under it.</summary>
    [Theory, MemberData(nameof(Ids))]
    public void Text_reads_at_four_and_a_half_to_one_on_every_material(string id)
    {
        var t = UiThemes.All.Single(x => x.Id == id);
        var panel = UnderText(Named(id, "panel"), 8, 8);
        foreach (var (name, colour) in new[] { ("text", t.Text), ("dim text", t.DimText), ("accent", t.Accent) })
            Assert.True(Worst(colour, panel) >= 4.5, $"{id}: {name} on the panel is {Worst(colour, panel):0.00}:1");
        foreach (var face in new[] { "button", "button-hover", "button-pressed", "button-primary" })
        {
            var under = UnderText(Named(id, face), 16, 8);
            Assert.True(Worst(t.ButtonText, under) >= 4.5, $"{id}: button text on {face} is {Worst(t.ButtonText, under):0.00}:1");
        }
        var field = UnderText(Named(id, "input"), 8, 8);
        Assert.True(Worst(t.Materials.FieldText, field) >= 4.5, $"{id}: field text on the input is {Worst(t.Materials.FieldText, field):0.00}:1");
    }
}
