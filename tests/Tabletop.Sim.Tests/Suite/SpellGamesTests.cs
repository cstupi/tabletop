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

    public static TheoryData<string> GameNames() => [.. Spells.Select(g => g.Name)];

    /// <summary>The games with exact checks: the ones whose dice fixes and log readings the socket run must reproduce.</summary>
    public static TheoryData<string> ExactGameNames() => [.. Spells.Where(g => g.Checks.Any(c => c.Entries is not null)).Select(g => g.Name)];

    [Fact]
    public void Every_book_with_spells_has_one_game_that_covers_its_spells_in_the_books_order()
    {
        var books = Set.Books.Values.Where(b => b.Spells.Count > 0).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList();
        Assert.Equal(books.Select(b => SuiteCatalog.SpellsPrefix + b.Uid), Spells.Select(g => g.Group));
        Assert.Equal(books.Select(b => b.Spells), Spells.Select(g => g.Covers));
        Assert.Equal(Spells.Count, Spells.Select(g => g.Name).Distinct().Count());
        Assert.All(Spells, g => Assert.StartsWith(SpellGames.NamePrefix, g.Name));
    }

    [Fact]
    public void Every_spell_of_the_set_is_covered()
    {
        var spells = Set.Entries.Where(e => e.Kind == CatalogKind.Spell).Select(e => e.Name).Order().ToList();
        Assert.Equal(spells, Spells.SelectMany(g => g.Covers).Distinct().Order().ToList());
    }

    [Fact]
    public void Every_model_of_the_books_army_is_a_caster_on_that_book()
    {
        Assert.All(Spells, g =>
        {
            var army = g.Scenario.Armies[0];
            Assert.Equal(g.Group[SuiteCatalog.SpellsPrefix.Length..], army.ArmyBook);
            Assert.Equal(Enumerable.Range(1, army.Units), army.ModelRules.Where(r => r.Name == "Caster").Select(r => r.Unit));
            Assert.Null(g.Scenario.Armies[1].ArmyBook);
        });
    }

    [Fact]
    public void Every_effect_kind_the_spells_use_has_an_exact_check_on_a_spell_that_uses_it()
    {
        static IEnumerable<Effect> All(IEnumerable<Effect> effects) => effects.SelectMany(e => All(e.Children).Prepend(e));
        var spells = Set.Entries.Where(e => e.Kind == CatalogKind.Spell).ToList();
        var used = spells.SelectMany(s => All(s.Effects)).Select(e => e.Kind).Distinct().Order().ToList();
        var checkedKinds = Spells.SelectMany(g => g.Checks).SelectMany(c => c.Entries ?? []).Select(Set.Entry).OfType<CatalogEntry>().Where(e => e.Kind == CatalogKind.Spell)
            .SelectMany(s => All(s.Effects)).Select(e => e.Kind).Distinct().Order().ToList();
        Assert.Equal(used, checkedKinds);
    }

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
        // Roller.Judge succeeds on modified >= threshold alone: the casting roll fixed to an unmodified 1 at +21 passes (p.5: "a roll of 1 always counts as a fail").
        ["spells-human-defense-force"] = [SuiteCatalog.CastingRollCheck],
    };

    public static void AssertOnlyKnown(SuiteGameResult result)
    {
        var expected = Known.GetValueOrDefault(result.Name) ?? [];
        var unexpected = result.Failures.Where(f => !expected.Any(c => f.StartsWith(c + ": ", StringComparison.Ordinal))).ToList();
        var fixedChecks = expected.Where(c => !result.Failures.Any(f => f.StartsWith(c + ": ", StringComparison.Ordinal))).ToList();
        Assert.True(unexpected.Count == 0 && fixedChecks.Count == 0,
            $"{result.Name} seed {result.Seed}\nunexpected: {string.Join("\n", unexpected)}\nno longer failing: {string.Join("\n", fixedChecks)}");
    }
}

/// <summary>The spell games over the socket: the in-memory game server built from this tree, the Docker Postgres, the suite's own rows.</summary>
[Collection("Postgres")]
public class SpellGamesSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = new();

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
            PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId), Ct);
        var socket = new SuiteSocket(pg.Db, (c, minted, _) =>
        {
            foreach (var p in c.Info.Participants) minted.Add(p.AccountId);
            return Task.FromResult(new SocketCredentials(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList()));
        }, () => new FactoryConnector(_factory));
        var game = SuiteCatalog.All.Single(g => g.Name == name);
        var report = await new SuiteRunner([game], socket).RunAsync(new SuiteOptions([name], DriverKind.Socket, false, TempDir()), null, Ct);
        SpellDefects.AssertOnlyKnown(report.Games.Single());
    }
}
