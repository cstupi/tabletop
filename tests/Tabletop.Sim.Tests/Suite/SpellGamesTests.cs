using Tabletop.Content;
using System.Net.WebSockets;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>The spells group: generated from the pinned set's books, every spell covered, an exact check per effect kind, every game passing in process.</summary>
public class SpellGamesTests
{
    static PropertySet Set => ScenarioCompiler.CoreProperties;
    static IReadOnlyList<TestGame> Spells => SuiteCatalog.All.Where(g => g.Group.StartsWith(SuiteCatalog.SpellsPrefix, StringComparison.Ordinal)).ToList();

    /// <summary>The per-book games: the group less its edited-casting-die game (SuiteCatalog.EditedCastingDieName) and the 3.6 games that cast one spell
    /// from every unit (SuiteCatalog.RepeatedSpellGames36).</summary>
    static IReadOnlyList<TestGame> BookGames => Spells.Where(g => g.Name != SuiteCatalog.EditedCastingDieName
        && !SuiteCatalog.RepeatedSpellGames36.Any(n => g.Name == n + "@3.6")).ToList();

    public static TheoryData<string> GameNames() => [.. Spells.Select(g => g.Name)];

    /// <summary>The games with exact checks: the ones whose dice fixes and log readings the socket run must reproduce.</summary>
    public static TheoryData<string> ExactGameNames() => [.. Spells.Where(g => g.Checks.Any(c => c.Entries is not null)).Select(g => g.Name)];

    public static TheoryData<string> Editions() => [.. RuleSets.All];

    static PropertySet SetOf(string rules) => ScenarioCompiler.CorePropertiesOf(rules);
    static IReadOnlyList<TestGame> BookGamesOf(string rules) => BookGames.Where(g => g.Edition == rules).ToList();

    [Theory]
    [MemberData(nameof(Editions))]
    public void Every_book_with_a_played_spell_has_one_game_that_covers_its_played_spells_in_the_books_order(string rules)
    {
        var set = SetOf(rules);
        var books = set.Books.Values.Where(b => set.SpellsOf(b.Uid).Count > 0).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList();
        var games = BookGamesOf(rules);
        Assert.Equal(books.Select(b => SuiteCatalog.SpellsGroup(rules, b.Uid)), games.Select(g => g.Group));
        Assert.Equal(books.Select(b => set.SpellsOf(b.Uid).Select(e => e.Name)), games.Select(g => g.Covers));
        Assert.Equal(games.Count, games.Select(g => g.Name).Distinct().Count());
        Assert.All(games, g => Assert.StartsWith(SpellGames.NamePrefix, g.Name));
    }

    [Theory]
    [MemberData(nameof(Editions))]
    public void Every_played_spell_of_the_set_is_covered_and_no_text_only_one_is(string rules)
    {
        var set = SetOf(rules);
        var spells = set.Entries.Where(e => e.Kind == CatalogKind.Spell && e.Played).Select(e => e.Name).Order().ToList();
        Assert.Equal(spells, Spells.Where(g => g.Edition == rules).SelectMany(g => g.Covers).Distinct().Order().ToList());
    }

    /// <summary>No spell is text-only (SpellsPlayedTests): Shifting Form, translated to remove_spell_tokens, is played and cast in its book's game.</summary>
    [Fact]
    public void Shifting_Form_is_played_and_cast_in_its_books_3_6_game()
    {
        var set = SetOf(RuleSets.Gf360);
        var book = set.Books["04z57ua0bwth37zh"];
        Assert.True(set.Entry("Shifting Form")!.Played);
        Assert.Contains("Shifting Form", book.Spells);
        var game = BookGamesOf(RuleSets.Gf360).Single(g => g.Group == SuiteCatalog.SpellsGroup(RuleSets.Gf360, book.Uid));
        Assert.Contains("Shifting Form", game.Covers);
        Assert.Equal(game.Covers.Count, game.Scenario.Armies[0].Units);
        Assert.Equal($"{SpellGames.NamePrefix}daemons-of-change@3.6", game.Name);
    }

    [Fact]
    public void The_books_army_is_one_caster_on_that_book_per_spell_and_every_game_checks_its_spells_resolve_on_legal_casts()
    {
        Assert.All(BookGames, g =>
        {
            var army = g.Scenario.Armies[0];
            Assert.Equal(SuiteCatalog.BookOf(g.Group), army.ArmyBook);
            Assert.Equal((g.Covers.Count, 1), (army.Units, army.ModelsPerUnit));
            Assert.Equal(Enumerable.Range(1, army.Units), army.ModelRules.Where(r => r.Name == "Caster").Select(r => r.Unit));
            Assert.Null(g.Scenario.Armies[1].ArmyBook);
            Assert.Contains(g.Checks, c => c.Name == SpellGames.ResolvedCheckOf(g.Edition));
        });
    }

    [Fact]
    public void The_casting_order_puts_the_setups_first_spells_ahead_of_the_books_order()
    {
        var book = Set.Books["04z57ua0bwth37zh"];
        var order = SpellGames.CastingOrder(book, new SpellSetup(First: ["Mutating Inferno"]));
        Assert.Equal(["Mutating Inferno", .. book.Spells.Where(s => s != "Mutating Inferno")], order);
        Assert.Equal(book.Spells, SpellGames.CastingOrder(book, null));
    }

    [Theory]
    [MemberData(nameof(Editions))]
    public void Every_effect_kind_the_spells_use_has_an_exact_check_on_a_spell_that_uses_it(string rules)
    {
        var set = SetOf(rules);
        static IEnumerable<Effect> All(IEnumerable<Effect> effects) => effects.SelectMany(e => All(e.Children).Prepend(e));
        var spells = set.Entries.Where(e => e.Kind == CatalogKind.Spell && e.Played).ToList();
        var used = spells.SelectMany(s => All(s.Effects)).Select(e => e.Kind).Distinct().Order().ToList();
        var checkedKinds = Spells.Where(g => g.Edition == rules).SelectMany(g => g.Checks).SelectMany(c => c.Entries ?? []).Select(set.Entry).OfType<CatalogEntry>().Where(e => e.Kind == CatalogKind.Spell)
            .SelectMany(s => All(s.Effects)).Select(e => e.Kind).Distinct().Order().ToList();
        Assert.Equal(used, checkedKinds);
    }

    // Whole games in process: E2E for the unit suite's time, as `dotnet run --project sim -- suite` plays them all.
    [Trait("Category", "E2E")]
    [Theory]
    [MemberData(nameof(GameNames))]
    public async Task The_game_passes_in_process(string name)
    {
        var game = Spells.Single(g => g.Name == name);
        var report = await new SuiteRunner([game]).RunAsync(new SuiteOptions([name], DriverKind.InProcess, false, TempDir()), null, Ct);
        SpellDefects.AssertOnlyKnown(report.Games.Single());
    }
}

/// <summary>
/// The spell games' checks that fail on an engine defect (rules suite plan: the game keeps its failing check and the task reports the
/// defect). A game's failures must be exactly these, so any other failure, or a defect fixed, fails its test.
/// </summary>
static class SpellDefects
{
    static readonly Dictionary<string, string[]> Known = new(StringComparer.Ordinal)
    {
    };

    public static void AssertOnlyKnown(SuiteGameResult result)
    {
        var expected = Known.GetValueOrDefault(result.Name) ?? [];
        var failures = result.Failures.Select(f => f.StartsWith("known defect ", StringComparison.Ordinal) ? f[(f.IndexOf(": ", StringComparison.Ordinal) + 2)..] : f).ToList();
        var unexpected = failures.Where(f => !expected.Any(c => f.StartsWith(c + ": ", StringComparison.Ordinal))).ToList();
        var fixedChecks = expected.Where(c => !failures.Any(f => f.StartsWith(c + ": ", StringComparison.Ordinal))).ToList();
        Assert.Equal(expected.Length > 0, result.Known is not null);
        Assert.True(unexpected.Count == 0 && fixedChecks.Count == 0,
            $"{result.Name} seed {result.Seed}\nunexpected: {string.Join("\n", unexpected)}\nno longer failing: {string.Join("\n", fixedChecks)}");
    }
}

/// <summary>The spell games over the socket: the in-process game server built from this tree over the Docker Postgres, the suite's own rows.</summary>
[Collection("Postgres")]
[Trait("Category", "E2E")]
public class SpellGamesSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = GameServerFactory.OverPostgres(pg.Db);

    public void Dispose() => _factory.Dispose();

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    [Theory]
    [MemberData(nameof(SpellGamesTests.ExactGameNames), MemberType = typeof(SpellGamesTests))]
    public async Task The_game_passes_over_the_socket(string name)
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(
            ScenarioCompiler.CoreDocuments[RuleSets.Gf351], Ct);
        var socket = new TestPool(pg).Socket(() => new FactoryConnector(_factory));
        var game = SuiteCatalog.All.Single(g => g.Name == name);
        var report = await new SuiteRunner([game], socket).RunAsync(new SuiteOptions([name], DriverKind.Socket, false, TempDir()), null, Ct);
        SpellDefects.AssertOnlyKnown(report.Games.Single());
    }
}
