using Tabletop.TerrainMeshes;

// Writes the built-in terrain meshes: dotnet run --project scripts/TerrainMeshes -- [output directory [mesh names]].
var dir = args.Length > 0 ? args[0] : "client/content/builtin/terrain";
var only = args.Skip(1).ToHashSet();
Directory.CreateDirectory(dir);
foreach (var (name, build) in Catalog.All.Where(m => only.Count == 0 || only.Contains(m.Name)))
{
    var model = build();
    var bytes = Glb.Of(model);
    File.WriteAllBytes(Path.Combine(dir, $"{name}.glb"), bytes);
    Console.WriteLine($"{name,-18} {model.Triangles,6} triangles {bytes.Length / 1024,5} KiB");
}
