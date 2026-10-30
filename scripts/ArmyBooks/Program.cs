using Tabletop.ArmyBooks;
using Tabletop.Content;
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
        var indexJson = await http.GetStringAsync("/api/army-books?filters=official&gameSystemSlug=grimdark-future");
        var index = ArmyForgeJson.ReadIndex(indexJson);
        var edition = ArmyForgeJson.EditionOf(index);
        var dir = Path.Combine(dump, edition);
        if (File.Exists(Path.Combine(dir, "index.json")))
        {
            var held = ArmyForgeJson.EditionOf(ArmyForgeJson.ReadIndex(File.ReadAllText(Path.Combine(dir, "index.json"))));
            if (held != edition) { Console.Error.WriteLine($"refusing: {dir} holds edition {held}, Army Forge serves {edition}"); return 1; }
        }
        Directory.CreateDirectory(Path.Combine(dir, "books"));
        await File.WriteAllTextAsync(Path.Combine(dir, "index.json"), indexJson);
        foreach (var e in index)
            await File.WriteAllTextAsync(Path.Combine(dir, "books", e.Uid + ".json"), await http.GetStringAsync($"/api/army-books/{e.Uid}?gameSystem=2"));
        Console.WriteLine($"fetched {index.Count} books into {dir}");
        return 0;
    }
    case "generate":
    {
        var edition = Arg("--edition", "3.5");
        var rulesId = RuleSets.RulesIdOf(edition);
        var folder = edition == "3.5" ? "grimdark-future-3.5.1" : "grimdark-future-3.6";
        var dir = Path.Combine(dump, edition);
        List<AfBook> books;
        List<(string Uid, string Name, string Json)> raw;
        try
        {
            var index = ArmyForgeJson.ReadIndex(File.ReadAllText(Path.Combine(dir, "index.json")));
            raw = index.Select(e => (e.Uid, e.Name, File.ReadAllText(Path.Combine(dir, "books", e.Uid + ".json")))).ToList();
            books = index.Zip(raw).Select(p => ArmyForgeJson.ReadBook(p.Second.Json) with { Name = p.First.Name, VersionString = p.First.VersionString }).ToList();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"dump file missing under {dir}: {ex.Message}");
            return 2;
        }
        var problems = books.SelectMany(b => b.Spells.SelectMany(s =>
        {
            try { return TextCheck.Problems(s, SpellTranslator.Translate(b, s)).Select(p => $"{b.Name}: {p}"); }
            catch (GenerationException ex) { return ex.Problems; }
        })).ToList();
        IReadOnlyList<BookDocument> built = [];
        var content = Path.Combine(repo.FullName, "server", "game", "Content", folder);
        var core = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "core.properties.json")));
        var hand = PropertiesJson.Parse(File.ReadAllText(Path.Combine(content, "army-rules.part.json")));
        var (rules, report, ruleProblems) = RuleWriter.Build(books, core, hand, rulesId);
        problems.AddRange(ruleProblems);
        var (reserveUnits, reserveProblems) = ReserveUnits.Build(raw);
        problems.AddRange(reserveProblems);
        if (problems.Count == 0)
        {
            built = BookWriter.Build(books, rulesId);
            problems.AddRange(BookWriter.Check(built, core, hand, rules));
        }
        if (problems.Count > 0) { foreach (var p in problems) Console.Error.WriteLine(p); return 1; }
        BookWriter.Write(built, Arg("--out", Path.Combine(content, "army-books")));
        var rulesPath = Arg("--rules", Path.Combine(content, "army-book-rules.part.json"));
        File.WriteAllText(rulesPath, RuleWriter.Serialize(rules!));
        var unitsPath = Arg("--units", Path.Combine(repo.FullName, "server", "management", "Content", "Armies", ReserveUnits.FileName));
        File.WriteAllText(unitsPath, reserveUnits);
        Console.WriteLine($"wrote {unitsPath}");
        Console.WriteLine($"wrote {built.Count} books, {built.SelectMany(b => b.Spells).Select(s => s.Name).Distinct().Count()} spells");
        Console.WriteLine($"wrote {rulesPath}: {report!.Generated} generated, {rules!.Entries.Count - report.Generated} text-only");
        foreach (var (group, count) in report.Groups) Console.WriteLine($"  {group}: {count}");
        if (report.NoDescription.Count > 0) Console.WriteLine($"  no description: {string.Join(", ", report.NoDescription)}");
        return 0;
    }
    default:
        Console.Error.WriteLine("usage: army-books fetch [--to DIR] | generate [--edition 3.5|3.6] [--from DIR] [--out DIR] [--rules FILE] [--units FILE]");
        return 2;
}
