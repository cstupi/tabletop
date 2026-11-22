using Tabletop.ThemeTextures;

// Writes the theme images at 1× and 2× into client/content/themes/<id>/ and web/static/themes/<id>/:
// dotnet run --project scripts/ThemeTextures -- [names, or theme/name].
var only = args.ToHashSet();
foreach (var sheet in Themes.All.Where(s => only.Count == 0 || only.Contains(s.Name) || only.Contains($"{s.Theme}/{s.Name}")))
    foreach (var scale in Themes.Scales)
    {
        var png = sheet.Png(scale);
        foreach (var root in new[] { Themes.Client, Themes.Web })
        {
            var dir = Path.Combine(root, sheet.Theme); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, sheet.File(scale)), png);
        }
        Console.WriteLine($"{sheet.Theme}/{sheet.File(scale),-24} {sheet.Width * scale,5}×{sheet.Height * scale,-5} {png.Length / 1024,5} KiB");
    }
