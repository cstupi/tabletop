using Tabletop.TerrainMeshes;

// Writes the flat-topped hill meshes the built-in terrain draws: dotnet run --project scripts/TerrainMeshes -- [output directory].
var dir = args.Length > 0 ? args[0] : "client/content/builtin/terrain";
float[] colour = [0.4f, 0.5f, 0.3f, 1];
Directory.CreateDirectory(dir);
File.WriteAllBytes(Path.Combine(dir, "hill.glb"), Glb.Of("hill", Plateau.Round(), colour));
File.WriteAllBytes(Path.Combine(dir, "hill-rect.glb"), Glb.Of("hill-rect", Plateau.Square(), colour));
