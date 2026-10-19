using System.Text.Json;

namespace Tabletop.TableTextures.Tests;

public class TableTextureTests
{
    static readonly string Client = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/content"));
    public static TheoryData<string> Names => new(Boards.All.Concat(Details.All).Select(t => t.Name));
    static Texture Named(string name) => Boards.All.Concat(Details.All).Single(t => t.Name == name);
    static string FileOf(string name) => Boards.All.Any(t => t.Name == name) ? Path.Combine(Client, "builtin", "textures", $"{name}.png") : Path.Combine(Client, "table", $"{name}.png");

    [Fact]
    public void A_png_reads_back_as_written()
    {
        var rgba = new byte[7 * 5 * 4];
        for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 37 % 251);
        var (w, h, back) = Png.Read(Png.Write(7, 5, rgba));
        Assert.Equal((7, 5), (w, h));
        Assert.Equal(rgba, back);
    }

    [Fact]
    public void The_noise_repeats_once_per_tile()
    {
        foreach (var (u, v) in new[] { (0.1f, 0.3f), (0.77f, 0.02f), (0.5f, 0.99f) })
        {
            Assert.Equal(Field.Fbm(u, v, 3, 4, 7), Field.Fbm(u + 1, v - 1, 3, 4, 7), 4);
            Assert.Equal(Field.Gradient(u, v, 5, 40, 2), Field.Gradient(u - 1, v + 2, 5, 40, 2), 4);
            Assert.Equal(Field.Cells(u, v, 9, 1).F1, Field.Cells(u + 1, v, 9, 1).F1, 4);
        }
    }

    /// <summary>A print tiles without a seam: its first and last columns (and rows) differ no more than neighbouring columns inside it.</summary>
    [Theory, MemberData(nameof(Names))]
    public void Tiles_without_a_seam(string name)
    {
        var t = Named(name); var n = t.Size;
        float Diff(Func<int, (float, float)> a, Func<int, (float, float)> b)
        {
            var sum = 0f;
            for (var i = 0; i < n; i += 2)
            {
                var (ca, ha) = t.At(a(i).Item1, a(i).Item2); var (cb, hb) = t.At(b(i).Item1, b(i).Item2);
                sum += (ca - cb).Length() + MathF.Abs(ha - hb);
            }
            return sum / (n / 2);
        }
        float P(int i) => (i + 0.5f) / n;
        var seamU = Diff(i => (P(n - 1), P(i)), i => (P(0), P(i)));
        var insideU = Diff(i => (P(n / 2 - 1), P(i)), i => (P(n / 2), P(i)));     // across a cell edge, as the seam is
        var seamV = Diff(i => (P(i), P(n - 1)), i => (P(i), P(0)));
        var insideV = Diff(i => (P(i), P(n / 2 - 1)), i => (P(i), P(n / 2)));
        Assert.True(seamU < insideU * 2 + 0.02f, $"{name}: u seam {seamU} vs {insideU}");
        Assert.True(seamV < insideV * 2 + 0.02f, $"{name}: v seam {seamV} vs {insideV}");
    }

    [Fact]
    public void The_catalog_lists_every_board()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Client, "builtin", "catalog.json")));
        Assert.Equal(Boards.All.Select(b => b.Name), json.RootElement.GetProperty("textures").EnumerateArray().Select(t => t.GetString()));
    }

    /// <summary>Within one step per channel of the generator's output (MathF's last bits differ between CPUs), and within the budget.</summary>
    [Theory, MemberData(nameof(Names))]
    public void The_shipped_print_is_the_generators_output(string name)
    {
        var bytes = File.ReadAllBytes(FileOf(name));
        Assert.True(bytes.Length <= 3 << 20, $"{name}.png is {bytes.Length} bytes");
        var (w, h, shipped) = Png.Read(bytes);
        var t = Named(name);
        Assert.Equal((t.Size, t.Size), (w, h));
        var fresh = t.Render();
        var off = 0;
        for (var i = 0; i < fresh.Length; i++) if (Math.Abs(fresh[i] - shipped[i]) > 1) off++;
        Assert.True(off < fresh.Length / 1000, $"{name}.png is stale ({off} bytes differ): run scripts/table-textures");
    }
}
