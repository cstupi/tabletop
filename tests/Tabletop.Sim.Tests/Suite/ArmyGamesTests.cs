using System.Net.WebSockets;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Sim.Drivers;
using Tabletop.Protocol;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>The army group (Task 6): what it covers, its exact checks over hand-built logs, and every game played in process.</summary>
public class ArmyGamesTests
{
    static IReadOnlyList<TestGame> Army => [.. SuiteCatalog.All.Where(g => g.Group.StartsWith(SuiteCatalog.ArmyPrefix))];

    /// <summary>Games that fail on a defect the suite found, with the failure each keeps until the defect is fixed (the game's comment names it).</summary>
    public static readonly Dictionary<string, string> Defects = new()
    {
        ["soul-snatcher-cults-slayers-charging"] = "no defender in this interaction",
        ["blessed-sisters-devout-edited-dice"] = "1 extra hits for edited dice [6 6 6 3 2 4 1 5 2 5 2 5], expected 3",
        ["havoc-brothers-resistance"] = "4 dice [3 2 1 1] for 3 wounds",
        ["robot-legions-self-repair"] = "4 dice [3 2 1 1] for 3 wounds",
        ["robot-legions-self-repair-boost"] = "4 dice [3 2 1 1] for 3 wounds",
        ["plague-disciples-plaguebound"] = "4 dice [3 2 1 1] for 3 wounds",
        ["plague-disciples-plaguebound-boost"] = "4 dice [3 2 1 1] for 3 wounds",
        ["jackals-scrapper-boost"] = "re-rolled dice [0,6,7] of [6 4 1 4 3 2 6 5], expected [0,6]",
    };

    /// <summary>
    /// Games that fail on a defect over the socket only. The socket server reads the property set back from Postgres, which does not keep a
    /// row's condition order, so Slayer's most_models opponent condition is judged first in a lone interaction (activation-terrain) and throws.
    /// </summary>
    public static readonly Dictionary<string, string> SocketDefects = new()
    {
        ["soul-snatcher-cults-slayers"] = "no defender in this interaction",
    };

    /// <summary>Core rules a game gives a side only to stage the play (p.14 Immobile: "may only use Hold actions"); they are not the army group's.</summary>
    static readonly string[] Staging = ["Immobile"];

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Tabletop.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("no Tabletop.sln above the test binaries");
    }

    [Fact]
    public void The_army_games_cover_every_army_book_rule_once_each_under_a_shipped_book()
    {
        var part = PropertiesJson.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "server/game/Content/grimdark-future-army-rules.part.json")));
        var covered = Army.SelectMany(g => g.Covers).ToList();
        Assert.Equal(part.Entries.Select(e => e.Name).Order(), covered.Order());
        var books = Directory.GetFiles(Path.Combine(RepoRoot(), "server/game/Content/army-books"), "*.book.json")
            .Select(f => Path.GetFileName(f)[..^".book.json".Length]).ToHashSet();
        Assert.All(Army, g => Assert.Contains(g.Group[SuiteCatalog.ArmyPrefix.Length..], books));
        Assert.All(Army, g => Assert.All(g.Covers, c => Assert.Contains(c, g.Scenario.Armies[0].Rules())));          // a game covers only what its first army carries
        Assert.All(Army, g => Assert.All(g.Scenario.Armies[0].Rules().Except(Staging), r =>                           // and every rule that army carries is its group's
            Assert.Contains(r, Army.Where(x => x.Group == g.Group).SelectMany(x => x.Covers))));
    }

    [Fact]
    public void The_defect_tables_are_the_games_the_catalog_pins()
    {
        Assert.Equal(Defects.Keys.Order(), Army.Where(g => g.KnownOn(DriverKind.InProcess).Count > 0).Select(g => g.Name).Order());
        Assert.Equal(Defects.Keys.Concat(SocketDefects.Keys).Order(), Army.Where(g => g.KnownOn(DriverKind.Socket).Count > 0).Select(g => g.Name).Order());
    }

    public static TheoryData<string> Playable() => [.. Army.Where(g => !Defects.ContainsKey(g.Name)).Select(g => g.Name)];

    [Theory]
    [MemberData(nameof(Playable))]
    public async Task Each_army_game_passes_in_process(string name)
    {
        var report = await new SuiteRunner(Army).RunAsync(new SuiteOptions([name], DriverKind.InProcess, false, TempDir()), null, Ct);
        Assert.True(report.Passed, string.Join("\n", report.Games.SelectMany(g => g.Failures)));
    }

    [Fact]
    public async Task A_game_that_found_a_defect_still_fails_with_it()
    {
        foreach (var (name, defect) in Defects)
        {
            var report = await new SuiteRunner(Army).RunAsync(new SuiteOptions([name], DriverKind.InProcess, false, TempDir()), null, Ct);
            Assert.Contains(report.Games.Single().Failures, f => f.Contains(defect));
        }
    }

    // ---- The exact checks over hand-built logs -----------------------------------------------------------------------------------------

    static readonly Guid Unit = Guid.CreateVersion7();

    static GameCheck Check(string game, string entry) => Army.Single(g => g.Name == game).Checks.Single(c => c.Name.StartsWith(entry + ": "));

    static string? Judge(string game, string entry, params EventPayload[] log)
    {
        var compiled = ScenarioCompiler.Compile(Army.Single(g => g.Name == game).Scenario);
        var events = log.Select((p, i) => new GameEvent(i + 1, null, p, null)).ToList();
        var run = new SuiteGameRun(Army.Single(g => g.Name == game), new RunOutcome.RoundLimit(4), events, new EventApplier(compiled.Board, compiled.Info).Projection, []);
        return Check(game, entry).Failure(run);
    }

    static (SequenceStarted Start, Guid Frame) Frame(string sequence, string? action = null, Guid? actor = null)
    {
        var id = Guid.CreateVersion7();
        return (new SequenceStarted(id, sequence, new SequenceContext(actor ?? Unit, [], [], action)), id);
    }

    static RollRecord Roll(string kind, int threshold, IReadOnlyList<RollModifier> modifiers, params (int Raw, bool Success)[] dice) =>
        new(Guid.CreateVersion7(), kind, threshold, modifiers, [.. dice.Select(d => new Die(d.Raw, d.Raw + modifiers.Sum(m => m.Amount), d.Success))]);

    static StepCompleted Step(Guid frame, string step, IReadOnlyList<string> applied, Dictionary<string, int>? values, params RollRecord[] rolls) =>
        new(frame, step, null, rolls, [], applied) { Values = values };

    [Fact]
    public void Extra_attacks_must_match_the_pools_sixes_and_never_chain()
    {
        var (start, frame) = Frame("attack-sequence");
        RollRecord Pool() => Roll("hit", 4, [], (6, true), (2, false), (6, true));
        string? With(params RollRecord[] added) =>
            Judge("saurian-starhost-primal", "Primal", start, Step(frame, "roll-to-hit", ["Primal"], null, [Pool(), .. added]));

        Assert.Null(With(Roll("hit", 4, [], (3, false), (4, true))));
        Assert.Null(With(Roll("hit", 4, [], (6, true), (4, true))));      // "This rule doesn't apply to newly generated attacks": the added 6 adds none
        Assert.Contains("1 added attacks", With(Roll("hit", 4, [], (3, false))));
        Assert.Contains("3 hit rolls", With(Roll("hit", 4, [], (6, true), (4, true)), Roll("hit", 4, [], (1, false))));
        Assert.Equal("no occasion in the log", Judge("saurian-starhost-primal", "Primal", start));
    }

    [Fact]
    public void A_modifier_must_have_its_size_on_its_roll_and_be_applied_to_the_dice()
    {
        var (start, frame) = Frame("attack-sequence");
        string? With(RollRecord roll) => Judge("saurian-starhost-evasive-teleport", "Evasive", start, Step(frame, "roll-to-hit", ["Evasive"], null, roll));

        Assert.Null(With(Roll("hit", 4, [new RollModifier("Evasive", -1)], (5, true))));
        Assert.Contains("-2 on hit", With(Roll("hit", 4, [new RollModifier("Evasive", -2)], (5, true))));
        Assert.Contains("on a defense roll", With(Roll("defense", 4, [new RollModifier("Evasive", -1)], (5, true))));
        Assert.Contains("die 5 became 5", With(new RollRecord(Guid.CreateVersion7(), "hit", 4, [new RollModifier("Evasive", -1)], [new Die(5, 5, true)])));
    }

    [Fact]
    public void Melee_evasion_outside_a_melee_fails()
    {
        var (melee, _) = Frame("melee");
        var (attack, attackFrame) = Frame("attack-sequence");
        var roll = Roll("hit", 4, [new RollModifier("Melee Evasion", -1)], (5, true));
        Assert.Null(Judge("dark-elf-raiders-melee-evasion", "Melee Evasion", melee, attack, Step(attackFrame, "roll-to-hit", [], null, roll)));
        Assert.Contains("outside where the rule applies", Judge("dark-elf-raiders-melee-evasion", "Melee Evasion", attack, Step(attackFrame, "roll-to-hit", [], null, roll)));
    }

    [Fact]
    public void Extra_wounds_count_only_failed_dice_showing_the_faces()
    {
        var (start, frame) = Frame("resolve-hits");
        var defense = Roll("defense", 4, [new RollModifier("cover", 3)], (1, false), (2, true), (2, false), (5, true));    // the first 2 made a success
        string? With(int extra) => Judge("infected-colonies-infected-boost", "Infected Boost", start,
            Step(frame, "roll-to-block", ["Infected Boost"], new() { ["extra_wounds"] = extra }, defense));
        Assert.Null(With(2));
        Assert.Contains("expected 2", With(3));         // "failed defense rolls of 1-2": the successful 2 adds none
    }

    [Fact]
    public void Ignore_rolls_are_one_die_per_wound_the_block_dealt_as_its_dice_now_stand()
    {
        var (resolve, resolveFrame) = Frame("resolve-hits");
        var (deal, dealFrame) = Frame("deal-wounds");
        var defense = Roll("defense", 4, [], (5, true), (5, true), (2, false));
        var edit = new DiceEdited(resolveFrame, defense.Id, [new DieReplacement(0, defense.Dice[0], new Die(1, 1, false))]);   // two wounds once edited
        StepCompleted Assign(int dealt, params RollRecord[] rolls) => Step(dealFrame, "assign", ["Resistance"], new() { ["wounds_dealt"] = dealt }, rolls);
        string? With(params EventPayload[] tail) => Judge("havoc-brothers-resistance", "Resistance", [resolve, Step(resolveFrame, "roll-to-block", [], new() { ["wounds"] = 1 }, defense), edit, deal, .. tail]);

        Assert.Null(With(Assign(2, Roll("Resistance", 6, [], (2, false), (3, false)))));
        Assert.Contains("3 dice [2 3 4] for 2 wounds", With(Assign(1, Roll("Resistance", 6, [], (2, false), (3, false))), Assign(2, Roll("Resistance", 6, [], (4, false)))));
        Assert.Contains("1 wounds dealt", With(Assign(1, Roll("Resistance", 6, [], (2, false), (3, false)))));
    }

    [Fact]
    public void Edited_extra_hits_follow_the_dice_as_they_now_stand()
    {
        var (start, frame) = Frame("attack-sequence");
        var hit = Roll("hit", 4, [], (2, false), (3, false));
        var edit = new DiceEdited(frame, hit.Id, [new DieReplacement(0, hit.Dice[0], new Die(6, 6, true))]);
        string? With(int extra) => Judge("blessed-sisters-devout-edited-dice", "Devout", start, Step(frame, "roll-to-hit", [], new() { ["extra_hits"] = extra }, hit), edit);
        Assert.Null(With(1));
        Assert.Contains("0 extra hits for edited dice [6 3], expected 1", With(0));
    }

    [Fact]
    public void Rerolls_must_be_exactly_the_dice_the_rule_names()
    {
        var (start, frame) = Frame("resolve-hits");
        var defense = Roll("defense", 4, [], (6, true), (5, true), (5, false), (2, false));      // the third die: a 5 a modifier made fail
        DiceRerolled Of(params int[] indexes) => new(frame, defense.Id, [.. indexes.Select(i => new DieReplacement(i, defense.Dice[i], new Die(1, 1, false)))]);
        string? With(string entry, IReadOnlyList<string> applied, params DiceRerolled[] rerolls) =>
            Judge(entry == "Mischievous" ? "goblin-reclaimers-mischief" : "goblin-reclaimers-mischief-boost", entry, [start, Step(frame, "roll-to-block", applied, null, defense), .. rerolls]);

        Assert.Null(With("Mischievous", ["Mischievous"], Of(0)));
        Assert.Contains("re-rolled dice [0,1]", With("Mischievous", ["Mischievous"], Of(0, 1)));
        Assert.Null(With("Mischievous Boost", ["Mischievous Boost"], Of(0, 1)));
        Assert.Contains("expected [0,1]", With("Mischievous Boost", ["Mischievous Boost"], Of(0)));
        Assert.Contains("expected [0,1]", With("Mischievous Boost", ["Mischievous Boost"], Of(0, 1, 2)));     // only "successful" 5s
        Assert.Equal("no occasion in the log", With("Mischievous", ["Mischievous", "Mischievous Boost"], Of(0, 1)));   // a Boost step is the Boost's
    }

    [Fact]
    public void A_move_bonus_is_read_off_the_first_move_ask_of_the_holders_action()
    {
        var compiled = ScenarioCompiler.Compile(Army.Single(g => g.Name == "ratmen-clans-scurry").Scenario);
        Guid UnitNamed(string name) => compiled.Board.Armies.SelectMany(a => a.Snapshot.Units).Single(u => u.Name == name).Id;
        var (advance, frame) = Frame("advance", "Advance", UnitNamed("Ratmen Clans unit 1"));
        AskOpened Move(double allowance) => new(new Ask(Guid.CreateVersion7(), "move", "Move", [], new MoveRegion([], allowance, [], [], null), false, false, null, frame));
        string? With(params EventPayload[] log)
        {
            var events = log.Select((p, i) => new GameEvent(i + 1, null, p, null)).ToList();
            var game = Army.Single(g => g.Name == "ratmen-clans-scurry");
            return Check(game.Name, "Scurry").Failure(new SuiteGameRun(game, new RunOutcome.RoundLimit(4), events, new EventApplier(compiled.Board, compiled.Info).Projection, []));
        }

        Assert.Null(With(advance, Move(8), Move(2)));                       // p.7 Advance 6" + 2"; a later move Ask in the frame is not the action's
        Assert.Contains("advance allowed 6\"", With(advance, Move(6)));
        var (boosted, boostedFrame) = Frame("advance", "Advance", UnitNamed("Ratmen Clans unit 2"));
        Assert.Equal("no occasion in the log", With(boosted, new AskOpened(new Ask(Guid.CreateVersion7(), "move", "Move", [], new MoveRegion([], 10, [], [], null), false, false, null, boostedFrame))));
    }
}

static class ScenarioArmyRules
{
    public static IEnumerable<string> Rules(this ScenarioArmy army) =>
        army.ModelRules.Select(r => r.Name).Concat(army.Weapons.SelectMany(w => w.Rules).Select(r => r.Name)).Distinct();
}

/// <summary>Every army game over the socket, against the in-memory game server and the Docker Postgres, as SuiteSocketTests runs the sample.</summary>
[Collection("Postgres")]
public class ArmyGamesSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    [Fact]
    public async Task Every_army_game_gives_its_in_process_verdict_over_the_socket()
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId), Ct);
        var socket = new SuiteSocket(pg.Db, (c, minted, _) =>
        {
            foreach (var p in c.Info.Participants) minted.Add(p.AccountId);
            return Task.FromResult(new SocketCredentials(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList()));
        }, () => new FactoryConnector(_factory));
        var army = SuiteCatalog.All.Where(g => g.Group.StartsWith(SuiteCatalog.ArmyPrefix)).ToList();
        var report = await new SuiteRunner(army, socket).RunAsync(new SuiteOptions([.. army.Select(g => g.Name)], DriverKind.Socket, false, TempDir()), null, Ct);
        Assert.All(report.Games, g =>
        {
            if (ArmyGamesTests.Defects.TryGetValue(g.Name, out var defect) || ArmyGamesTests.SocketDefects.TryGetValue(g.Name, out defect))
                Assert.Contains(g.Failures, f => f.Contains(defect));
            else Assert.True(g.Passed, $"{g.Name}: {string.Join("; ", g.Failures)}");
        });
    }
}
