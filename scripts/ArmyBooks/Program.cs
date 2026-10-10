using Tabletop.ArmyBooks;
using Tabletop.GameServer.Properties;

var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Tabletop.sln"))) repo = repo.Parent;
if (repo is null) { Console.Error.WriteLine("run from inside the repository"); return 2; }
string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
// The dump lives in the main checkout's .superpowers, which a git worktree does not have: find it from the working directory, as DotEnv finds .env.
static string? FindDump()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, ".superpowers", "armyforge"))) return Path.Combine(d.FullName, ".superpowers", "armyforge");
    return null;
}
var isFetch = args.FirstOrDefault() == "fetch";
var dumpArg = Arg(isFetch ? "--to" : "--from", "");
var dumpDir = dumpArg != "" ? dumpArg : FindDump() ?? (isFetch ? Path.Combine(repo.FullName, ".superpowers", "armyforge") : null);
if (dumpDir is null) { Console.Error.WriteLine("no .superpowers/armyforge found above the working directory; run `army-books fetch` or pass --from DIR"); return 2; }
var dump = Path.GetFullPath(dumpDir);

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
        List<AfBook> books;
        try
        {
            var index = ArmyForgeJson.ReadIndex(File.ReadAllText(Path.Combine(dump, "index.json")));
            books = index.Select(e => ArmyForgeJson.ReadBook(File.ReadAllText(Path.Combine(dump, "books", e.Uid + ".json"))) with { Name = e.Name, VersionString = e.VersionString }).ToList();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"dump file missing under {dump}: {ex.Message}");
            return 2;
        }
        var problems = books.SelectMany(b => b.Spells.SelectMany(s =>
        {
            try { return TextCheck.Problems(s, SpellTranslator.Translate(b, s)).Select(p => $"{b.Name}: {p}"); }
            catch (GenerationException ex) { return ex.Problems; }
        })).ToList();
        IReadOnlyList<BookDocument> built = [];
        var content = Path.Combine(repo.FullName, "server", "game", "Content");
        var core = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-core.properties.json")));
        var hand = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "grimdark-future-army-rules.part.json")));
        var (rules, report, ruleProblems) = RuleWriter.Build(books, core, hand);
        problems.AddRange(ruleProblems);
        if (problems.Count == 0)
        {
            built = BookWriter.Build(books);
            problems.AddRange(BookWriter.Check(built, core, hand, rules));
        }
        if (problems.Count > 0) { foreach (var p in problems) Console.Error.WriteLine(p); return 1; }
        BookWriter.Write(built, Arg("--out", Path.Combine(content, "army-books")));
        var rulesPath = Arg("--rules", Path.Combine(content, PropertiesSeeder.GeneratedPart));
        File.WriteAllText(rulesPath, RuleWriter.Serialize(rules!));
        Console.WriteLine($"wrote {built.Count} books, {built.SelectMany(b => b.Spells).Select(s => s.Name).Distinct().Count()} spells");
        Console.WriteLine($"wrote {rulesPath}: {report!.Generated} generated, {rules!.Entries.Count - report.Generated} text-only");
        foreach (var (group, count) in report.Groups) Console.WriteLine($"  {group}: {count}");
        if (report.NoDescription.Count > 0) Console.WriteLine($"  no description: {string.Join(", ", report.NoDescription)}");
        return 0;
    }
    default:
        Console.Error.WriteLine("usage: army-books fetch [--to DIR] | generate [--from DIR] [--out DIR] [--rules FILE]");
        return 2;
}
