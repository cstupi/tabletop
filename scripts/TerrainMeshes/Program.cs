using Tabletop.TerrainMeshes;

// Writes the built-in terrain meshes: dotnet run --project scripts/TerrainMeshes -- [output directory [mesh names]].
// A full run into the default directory also writes the stamp the tests check (Stamp).
const string Shipped = "client/content/builtin/terrain";
var dir = args.Length > 0 ? args[0] : Shipped;
var only = args.Skip(1).ToHashSet();
Directory.CreateDirectory(dir);
var written = new List<(string, byte[])>();
foreach (var (name, build) in Catalog.All.Where(m => only.Count == 0 || only.Contains(m.Name)))
{
    var model = build();
    var bytes = Glb.Of(model);
    File.WriteAllBytes(Path.Combine(dir, $"{name}.glb"), bytes);
    written.Add((name, bytes));
    Console.WriteLine($"{name,-18} {model.Triangles,6} triangles {bytes.Length / 1024,5} KiB");
}
if (only.Count == 0 && Path.GetFullPath(dir) == Path.GetFullPath(Shipped))
    File.WriteAllText(Path.Combine("scripts/TerrainMeshes", Stamp.FileName), Stamp.Of("scripts/TerrainMeshes", written));
