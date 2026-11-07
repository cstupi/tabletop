using Tabletop.Sounds;

// Writes the table's sound effects: dotnet run --project scripts/Sounds -- [output directory].
// A full run into the default directory also writes the stamp the tests check (Stamp).
// `measure [directory]` prints each WAV's measures (Measure) instead.
const string Shipped = "client/content/sounds";
if (args is ["measure", ..])
{
    foreach (var path in Directory.GetFiles(args.Length > 1 ? args[1] : Shipped, "*.wav").Order())
        Console.WriteLine($"{Path.GetFileNameWithoutExtension(path),-14} {Measure.Of(Measure.Read(File.ReadAllBytes(path)))}");
    return;
}
var dir = args.Length > 0 ? args[0] : Shipped;
Directory.CreateDirectory(dir);
var written = new List<(string, byte[])>();
foreach (var (name, _, _) in Cues.All)
{
    var bytes = Wav.Of(Cues.Build(name));
    File.WriteAllBytes(Path.Combine(dir, $"{name}.wav"), bytes);
    written.Add((name, bytes));
    Console.WriteLine($"{name,-14} {bytes.Length / 1024,4} KiB");
}
if (Path.GetFullPath(dir) == Path.GetFullPath(Shipped))
    File.WriteAllText(Path.Combine("scripts/Sounds", Stamp.FileName), Stamp.Of("scripts/Sounds", written));
