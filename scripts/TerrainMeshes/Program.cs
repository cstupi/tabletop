using Tabletop.TerrainMeshes;

// Writes the built-in terrain meshes: dotnet run --project scripts/TerrainMeshes -- [output directory].
var dir = args.Length > 0 ? args[0] : "client/content/builtin/terrain";
Directory.CreateDirectory(dir);
foreach (var (name, build) in Catalog.All)
{
    var model = build();
    var bytes = Glb.Of(model);
    File.WriteAllBytes(Path.Combine(dir, $"{name}.glb"), bytes);
    Console.WriteLine($"{name,-10} {model.Triangles,6} triangles {bytes.Length / 1024,5} KiB");
}
