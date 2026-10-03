using Tabletop.ArmyBooks;
using Tabletop.GameServer.Properties;

var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Tabletop.sln"))) repo = repo.Parent;
if (repo is null) { Console.Error.WriteLine("run from inside the repository"); return 2; }
string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
var dump = Path.GetFullPath(Arg(args.FirstOrDefault() == "fetch" ? "--to" : "--from", Path.Combine(repo.FullName, ".superpowers", "armyforge")));

switch (args.FirstOrDefault())
{
    case "fetch":
    {
        // The only code path that calls Army Forge. Never run by a test.
        using var http = new HttpClient { BaseAddress = new Uri("https://army-forge.onepagerules.com") };
        var index = await http.GetStringAsync("/api/army-books?filters=official&gameSystemSlug=grimdark-future");
        Directory.CreateDirectory(Path.Combine(dump, "books"));
        await File.WriteAllTextAsync(Path.Combine(dump, "index.json"), index);
        foreach (var e in ArmyForgeJson.ReadIndex(index))
            await File.WriteAllTextAsync(Path.Combine(dump, "books", e.Uid + ".json"), await http.GetStringAsync($"/api/army-books/{e.Uid}?gameSystem=2"));
        Console.WriteLine($"fetched {ArmyForgeJson.ReadIndex(index).Count} books into {dump}");
        return 0;
    }
    case "generate":
    {
        var index = ArmyForgeJson.ReadIndex(File.ReadAllText(Path.Combine(dump, "index.json")));
        var books = index.Select(e => ArmyForgeJson.ReadBook(File.ReadAllText(Path.Combine(dump, "books", e.Uid + ".json"))) with { Name = e.Name, VersionString = e.VersionString }).ToList();
        var problems = books.SelectMany(b => b.Spells.SelectMany(s =>
        {
            try { return TextCheck.Problems(s, SpellTranslator.Translate(b, s)).Select(p => $"{b.Name}: {p}"); }
            catch (GenerationException ex) { return ex.Problems; }
        })).ToList();
        IReadOnlyList<BookDocument> built = [];
        if (problems.Count == 0)
        {
            built = BookWriter.Build(books);
            var content = Path.Combine(repo.FullName, "server", "game", "Content");
            problems.AddRange(BookWriter.Check(built,
                PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-core.properties.json"))),
                PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-army-rules.part.json")))));
        }
        if (problems.Count > 0) { foreach (var p in problems) Console.Error.WriteLine(p); return 1; }
        BookWriter.Write(built, Arg("--out", Path.Combine(repo.FullName, "server", "game", "Content", "army-books")));
        Console.WriteLine($"wrote {built.Count} books, {built.SelectMany(b => b.Spells).Select(s => s.Name).Distinct().Count()} spells");
        return 0;
    }
    default:
        Console.Error.WriteLine("usage: army-books fetch [--to DIR] | generate [--from DIR] [--out DIR]");
        return 2;
}
