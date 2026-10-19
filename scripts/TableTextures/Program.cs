using Tabletop.TableTextures;

// Writes the table textures: dotnet run --project scripts/TableTextures -- [boards directory [table directory [names]]].
var boards = args.Length > 0 ? args[0] : "client/content/builtin/textures";
var table = args.Length > 1 ? args[1] : "client/content/table";
var only = args.Skip(2).ToHashSet();
foreach (var (dir, list) in new[] { (boards, Boards.All), (table, Details.All) })
{
    Directory.CreateDirectory(dir);
    foreach (var t in list.Where(t => only.Count == 0 || only.Contains(t.Name)))
    {
        var png = t.Png();
        File.WriteAllBytes(Path.Combine(dir, $"{t.Name}.png"), png);
        Console.WriteLine($"{t.Name,-16} {t.Size,5}² {png.Length / 1024,6} KiB");
    }
}
