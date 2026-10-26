using Tabletop.Sounds;

// Writes the table's sound effects: dotnet run --project scripts/Sounds -- [output directory].
// A full run into the default directory also writes the stamp the tests check (Stamp).
const string Shipped = "client/content/sounds";
var dir = args.Length > 0 ? args[0] : Shipped;
Directory.CreateDirectory(dir);
var written = new List<(string, byte[])>();
foreach (var (name, build) in Cues.All)
{
    var bytes = Wav.Of(build());
    File.WriteAllBytes(Path.Combine(dir, $"{name}.wav"), bytes);
    written.Add((name, bytes));
    Console.WriteLine($"{name,-14} {bytes.Length / 1024,4} KiB");
}
if (Path.GetFullPath(dir) == Path.GetFullPath(Shipped))
    File.WriteAllText(Path.Combine("scripts/Sounds", Stamp.FileName), Stamp.Of("scripts/Sounds", written));
