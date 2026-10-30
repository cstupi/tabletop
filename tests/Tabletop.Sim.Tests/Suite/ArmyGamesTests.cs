using Tabletop.Content;
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
    public static readonly Dictionary<string, string> Defects = new();

    /// <summary>Core rules a game gives a side only to stage the play (p.14 Immobile: "may only use Hold actions"; p.14 Slow, which Swift ignores; p.13 Caster(X), a target "with Caster"; p.15 Tough(X), a target "with Tough"; p.13 Artillery, a model "with Artillery"; p.13 Flying, a model that may move through units; p.14 Regeneration, a wound ignored; p.13 Hero; p.14 Indirect, a weapon Quick Readjustment reads); they are not the army group's.</summary>
    static readonly string[] Staging = ["Immobile", "Slow", "Caster", "Tough", "Artillery", "Flying", "Regeneration", "Hero", "Indirect", "Ambush"];

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Tabletop.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("no Tabletop.sln above the test binaries");
    }

    [Fact]
    public void The_army_games_cover_every_army_book_rule_once_each_under_a_shipped_book()
    {
        var part = PropertiesJson.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "server/game/Content/grimdark-future-3.5.1/army-rules.part.json")));
        var army351 = Army.Where(g => g.Edition == RuleSets.Gf351).ToList();   // the 3.5.1 hand part's games; the 3.6 games are its own set's
        var covered = army351.SelectMany(g => g.Covers).ToList();
        var generated = PropertiesSeeder.EmbeddedPart(GfEdition.V351.ContentFolder, PropertiesSeeder.GeneratedPartName)!.Entries.Where(e => e.Effects.Count > 0).Select(e => e.Name).ToHashSet();
        Assert.Equal(part.Entries.Select(e => e.Name).Order(), covered.Where(c => !generated.Contains(c)).Order());
        Assert.All(covered.Where(generated.Contains), c => Assert.Contains(army351, g => g.Pattern is not null && g.Covers.Contains(c)));   // a wrapper only by its pattern's game
        var books = Directory.GetFiles(Path.Combine(RepoRoot(), "server/game/Content/grimdark-future-3.5.1/army-books"), "*.book.json")
            .Select(f => Path.GetFileName(f)[..^".book.json".Length]).ToHashSet();
        Assert.All(army351, g => Assert.Contains(SuiteCatalog.BookOf(g.Group), books));
        Assert.All(army351, g => Assert.All(g.Covers, c => Assert.Contains(c, g.Scenario.Armies[0].Rules())));          // a game covers only what its first army carries
        Assert.All(army351, g => Assert.All(g.Scenario.Armies[0].Rules().Except(Staging), r =>                           // and every rule that army carries is its group's
            Assert.Contains(r, army351.Where(x => x.Group == g.Group).SelectMany(x => x.Covers))));
    }

    /// <summary>
    /// 3.6 hand-part entries that are played but have no 3.6 game yet: their 3.6 text changed (their 3.6 games cover one per new
    /// mechanism, not one per entry) or they are new 3.6 names, so a 3.5.1 game's checks do not quote them. A game covering one removes it here.
    /// </summary>
    static readonly string[] Uncovered36 =
    [
    ];

    /// <summary>
    /// The 3.6 hand part's played entries each have a 3.6 game but those in Uncovered36, and each generated 3.6 wrapper a 3.6 game of its pattern.
    /// </summary>
    [Fact]
    public void The_3_6_army_games_cover_every_played_3_6_army_book_rule_under_a_3_6_book()
    {
        var army36 = Army.Where(g => g.Edition == RuleSets.Gf360).ToList();
        var covered = army36.SelectMany(g => g.Covers).ToHashSet();
        var part = PropertiesJson.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "server/game/Content/grimdark-future-3.6/army-rules.part.json")));
        var books = Directory.GetFiles(Path.Combine(RepoRoot(), "server/game/Content/grimdark-future-3.6/army-books"), "*.book.json")
            .Select(f => Path.GetFileName(f)[..^".book.json".Length]).ToHashSet();
        Assert.All(army36, g => Assert.Contains(SuiteCatalog.BookOf(g.Group), books));
        Assert.All(army36, g => Assert.Equal(SuiteCatalog.BookOf(g.Group), g.Scenario.Armies[0].ArmyBook));
        Assert.All(army36, g => Assert.All(g.Covers, c => Assert.Contains(c, g.Scenario.Armies[0].Rules())));          // a game covers only what its first army carries
        var wrappers = PropertiesSeeder.EmbeddedPart(GfEdition.V360.ContentFolder, PropertiesSeeder.GeneratedPartName)!.Entries
            .Where(e => e.Effects.Count > 0 && e.Played != false).Select(e => e.Name[(e.Name.LastIndexOf(' ') + 1)..]).Distinct();
        Assert.All(wrappers, pattern => Assert.Contains(army36, g => g.Pattern == pattern));
        Assert.Equal(Uncovered36.Order(StringComparer.Ordinal), part.Entries.Where(e => e.Played != false && !covered.Contains(e.Name)).Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_defect_tables_are_the_games_the_catalog_pins()
    {
        Assert.Equal(Defects.Keys.Order(), Army.Where(g => g.KnownOn(DriverKind.InProcess).Count > 0).Select(g => g.Name).Order());
        Assert.Equal(Defects.Keys.Order(), Army.Where(g => g.KnownOn(DriverKind.Socket).Count > 0).Select(g => g.Name).Order());
    }

    public static TheoryData<string> Playable() => [.. Army.Where(g => !Defects.ContainsKey(g.Name)).Select(g => g.Name)];

    // Whole games in process: E2E for the unit suite's time, as `dotnet run --project sim -- suite` plays them all.
    [Trait("Category", "E2E")]
    [Theory]
    [MemberData(nameof(Playable))]
    public async Task Each_army_game_passes_in_process(string name)
    {
        var report = await new SuiteRunner(Army).RunAsync(new SuiteOptions([name], DriverKind.InProcess, false, TempDir()), null, Ct);
        Assert.True(report.Passed, string.Join("\n", report.Games.SelectMany(g => g.Failures)));
    }

    // Whole games in process: E2E for the unit suite's time, as `dotnet run --project sim -- suite` plays them all.
    [Trait("Category", "E2E")]
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

    static string? Judge(string game, string entry, params EventPayload[] log) => new Board(game).Judge(entry, log);

    /// <summary>A game's scenario compiled once, so a hand-built log can name its units by the ids its projection holds.</summary>
    sealed class Board(string game)
    {
        readonly TestGame _game = Army.Single(g => g.Name == game);
        readonly CompiledScenario _compiled = ScenarioCompiler.Compile(Army.Single(g => g.Name == game).Scenario);

        public Guid Unit(string name) => _compiled.Board.Armies.SelectMany(a => a.Snapshot.Units).Single(u => u.Name == name).Id;
        public Guid Weapon(string unit, string weapon) =>
            _compiled.Board.Armies.SelectMany(a => a.Snapshot.Units).Single(u => u.Name == unit).Models[0].Weapons.Single(w => w.Name == weapon).Id;

        SuiteGameRun Run(EventPayload[] log) => new(_game, new RunOutcome.RoundLimit(4), [.. log.Select((p, i) => new GameEvent(i + 1, null, p, null))],
            new EventApplier(_compiled.Board, _compiled.Info).Projection, []);
        public string? Judge(string entry, params EventPayload[] log) => Check(_game.Name, entry).Failure(Run(log));
        public IReadOnlyList<string> Reasons(string entry, params EventPayload[] log) => Check(_game.Name, entry).FailuresOf(Run(log));
    }

    /// <summary>
    /// One attack of a hand-built log: a shooting frame when shooting, the attack-sequence, its roll-to-hit step, and one resolve-hits frame
    /// with its roll-to-block step when a defense roll is given; every frame finishes.
    /// </summary>
    static EventPayload[] Attack(Guid attacker, Guid target, IReadOnlyList<Guid> weapons, bool shooting, StepCompleted? hit, Func<Guid, EventPayload[]>? block = null)
    {
        var log = new List<EventPayload>();
        var shoot = Guid.CreateVersion7();
        var attack = Guid.CreateVersion7();
        var resolve = Guid.CreateVersion7();
        if (shooting) log.Add(new SequenceStarted(shoot, "shooting", new SequenceContext(attacker, [target], [], "Hold")));
        log.Add(new SequenceStarted(attack, "attack-sequence", new SequenceContext(attacker, [target], weapons, shooting ? "Hold" : null)));
        if (hit is not null) log.Add(hit with { FrameId = attack });
        if (block is not null)
        {
            log.Add(new SequenceStarted(resolve, "resolve-hits", new SequenceContext(attacker, [target], [], null)));
            log.AddRange(block(resolve));
            log.Add(new SequenceFinished(resolve));
        }
        log.Add(new SequenceFinished(attack));
        if (shooting) log.Add(new SequenceFinished(shoot));
        return [.. log];
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
        var board = new Board("saurian-starhost-primal");
        var (holder, target) = (board.Unit("Saurian Starhost unit 1"), board.Unit("Opponents unit 1"));
        RollRecord Pool() => Roll("hit", 4, [], (6, true), (2, false), (6, true));
        string? With(params RollRecord[] added) =>
            board.Judge("Primal", Attack(holder, target, [], shooting: true, Step(Guid.Empty, "roll-to-hit", ["Primal"], null, [Pool(), .. added])));

        Assert.Null(With(Roll("hit", 4, [], (3, false), (4, true))));
        Assert.Null(With(Roll("hit", 4, [], (6, true), (4, true))));      // "This rule doesn't apply to newly generated attacks": the added 6 adds none
        Assert.Contains("1 added attacks", With(Roll("hit", 4, [], (3, false))));
        Assert.Contains("3 hit rolls", With(Roll("hit", 4, [], (6, true), (4, true)), Roll("hit", 4, [], (1, false))));
        Assert.Equal("no occasion in the log", board.Judge("Primal", Attack(holder, target, [], shooting: true, hit: null)));
        Assert.Equal("no occasion in the log", board.Judge("Primal",                           // the opponents' attacks are not the rule's
            Attack(target, holder, [], shooting: true, Step(Guid.Empty, "roll-to-hit", [], null, Pool()))));
    }

    /// <summary>
    /// The occasions are the holder's attacks, read from the log and the army definitions, not the steps whose Applied lists the rule: an
    /// attack the engine left the rule off fails, whatever Applied says.
    /// </summary>
    [Fact]
    public void A_rule_that_acts_on_one_of_its_two_occasions_fails()
    {
        var board = new Board("saurian-starhost-primal");
        var (holder, target) = (board.Unit("Saurian Starhost unit 1"), board.Unit("Opponents unit 1"));
        var applied = Attack(holder, target, [], shooting: true,
            Step(Guid.Empty, "roll-to-hit", ["Primal"], null, Roll("hit", 4, [], (6, true), (2, false)), Roll("hit", 4, [], (3, false))));
        var skipped = Attack(holder, target, [], shooting: true, Step(Guid.Empty, "roll-to-hit", [], null, Roll("hit", 4, [], (6, true), (2, false))));

        Assert.Null(board.Judge("Primal", applied));
        Assert.Equal(["1 of 2 occasions: 0 added attacks from [6 2], expected 1"], board.Reasons("Primal", [.. applied, .. skipped]));
    }

    /// <summary>Each distinct failure is its own reason, none dropped, so a pin on one never covers another.</summary>
    [Fact]
    public void An_exact_check_reports_every_distinct_failure_on_its_own_line()
    {
        var board = new Board("saurian-starhost-primal");
        var (holder, target) = (board.Unit("Saurian Starhost unit 1"), board.Unit("Opponents unit 1"));
        EventPayload[] Sixes(int count) => Attack(holder, target, [], shooting: true,
            Step(Guid.Empty, "roll-to-hit", [], null, Roll("hit", 4, [], [.. Enumerable.Repeat((6, true), count)])));
        var reasons = board.Reasons("Primal", [.. Sixes(1), .. Sixes(2), .. Sixes(3), .. Sixes(4), .. Sixes(4)]);
        Assert.Equal(
            ["1 of 5 occasions: 0 added attacks from [6], expected 1", "1 of 5 occasions: 0 added attacks from [6 6], expected 2",
             "1 of 5 occasions: 0 added attacks from [6 6 6], expected 3", "2 of 5 occasions: 0 added attacks from [6 6 6 6], expected 4"], reasons);
    }

    /// <summary>A unit that holds the Boost is the Boost's check's: the base rule's check skips its attacks; a weapon rule's occasions are the attacks with that weapon.</summary>
    [Fact]
    public void The_base_check_skips_a_Boost_holders_attacks()
    {
        var board = new Board("havoc-brothers-havocbound");
        var (plain, boosted, target) = (board.Unit("Havoc Brothers unit 1"), board.Unit("Havoc Brothers unit 2"), board.Unit("Opponents unit 1"));
        EventPayload[] Block(Guid frame, int ap) => [Step(frame, "roll-to-block", [], null, Roll("defense", 4, [new RollModifier("AP", ap)], (5, true)))];
        Assert.Equal("no occasion in the log", board.Judge("Havocbound", Attack(boosted, target, [], shooting: true, null, f => Block(f, -1))));
        Assert.Null(board.Judge("Havocbound", Attack(plain, target, [], shooting: true, null, f => Block(f, -1))));
        Assert.Contains("defense under AP [0]", board.Judge("Havocbound", [.. Attack(plain, target, [], shooting: true, null, f => Block(f, -1)),
            .. Attack(plain, target, [], shooting: true, null, f => Block(f, 0))]));
        Assert.Equal("no occasion in the log", board.Judge("Havocbound", Attack(plain, target, [], shooting: false, null, f => Block(f, -1))));   // a melee strike is not a shot
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
    public void A_weapon_rules_occasions_are_the_attacks_made_with_that_weapon()
    {
        var board = new Board("dwarf-guilds-shred");
        var (holder, target) = (board.Unit("Dwarf Guilds unit 1"), board.Unit("Opponents unit 1"));
        EventPayload[] With(string weapon, int extra) => Attack(holder, target, [board.Weapon("Dwarf Guilds unit 1", weapon)], shooting: weapon == "Rifle", null,
            f => [Step(f, "roll-to-block", [], new() { ["extra_wounds"] = extra }, Roll("defense", 4, [], (1, false), (5, true)))]);
        Assert.Null(board.Judge("Shred", With("Rifle", 1)));
        Assert.Contains("0 extra wounds", board.Judge("Shred", [.. With("Rifle", 1), .. With("Rifle", 0)]));
        Assert.Equal("no occasion in the log", board.Judge("Shred", With("Blade", 0)));          // the Blade carries no Shred
    }

    [Fact]
    public void Extra_wounds_count_only_failed_dice_showing_the_faces()
    {
        var board = new Board("infected-colonies-infected-boost");
        var defense = Roll("defense", 4, [new RollModifier("cover", 3)], (1, false), (2, true), (2, false), (5, true));    // the first 2 made a success
        string? With(int extra) => board.Judge("Infected Boost", Attack(board.Unit("Infected Colonies unit 1"), board.Unit("Opponents unit 1"), [], shooting: true, null,
            f => [Step(f, "roll-to-block", ["Infected Boost"], new() { ["extra_wounds"] = extra }, defense)]));
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
        string? With(int extra) => Judge("blessed-sisters-devout-edited-dice", "Devout", start, Step(frame, "roll-to-hit", [], null, hit), edit,
            Step(frame, "score-hits", [], new() { ["extra_hits"] = extra }));
        Assert.Null(With(1));
        Assert.Contains("0 extra hits for edited dice [6 3], expected 1", With(0));
    }

    [Fact]
    public void Rerolls_must_be_exactly_the_dice_the_rule_names()
    {
        var defense = Roll("defense", 4, [], (6, true), (5, true), (5, false), (2, false));      // the third die: a 5 a modifier made fail
        string? With(string entry, params int[] rerolled)
        {
            var board = new Board(entry == "Mischievous" ? "goblin-reclaimers-mischief" : "goblin-reclaimers-mischief-boost");
            return board.Judge(entry, Attack(board.Unit("Goblin Reclaimers unit 1"), board.Unit("Opponents unit 1"), [], shooting: true, null, f =>
                [Step(f, "roll-to-block", [], null, defense),
                 new DiceRerolled(f, defense.Id, [.. rerolled.Select(i => new DieReplacement(i, defense.Dice[i], new Die(1, 1, false)))])]));
        }

        Assert.Null(With("Mischievous", 0));
        Assert.Contains("re-rolled dice [0,1]", With("Mischievous", 0, 1));
        Assert.Null(With("Mischievous Boost", 0, 1));
        Assert.Contains("expected [0,1]", With("Mischievous Boost", 0));
        Assert.Contains("expected [0,1]", With("Mischievous Boost", 0, 1, 2));     // only "successful" 5s
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
        army.ModelRules.Select(r => r.Name).Concat(army.Models.SelectMany(m => m.Rules ?? []).Select(r => r.Name))
            .Concat(army.Weapons.SelectMany(w => w.Rules).Select(r => r.Name)).Distinct();
}

/// <summary>Every army game over the socket, against the in-process game server over the Docker Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "E2E")]
public class ArmyGamesSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = GameServerFactory.OverPostgres(pg.Db);
    public void Dispose() => _factory.Dispose();

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    [Fact]
    public async Task Every_army_game_gives_its_in_process_verdict_over_the_socket()
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(ScenarioCompiler.CoreDocuments[RuleSets.Gf351], Ct);
        var socket = new TestPool(pg).Socket(() => new FactoryConnector(_factory));
        var army = SuiteCatalog.All.Where(g => g.Group.StartsWith(SuiteCatalog.ArmyPrefix)).ToList();
        var report = await new SuiteRunner(army, socket).RunAsync(new SuiteOptions([.. army.Select(g => g.Name)], DriverKind.Socket, false, TempDir()), null, Ct);
        Assert.All(report.Games, g =>
        {
            if (ArmyGamesTests.Defects.TryGetValue(g.Name, out var defect))
                Assert.Contains(g.Failures, f => f.Contains(defect));
            else Assert.True(g.Passed, $"{g.Name}: {string.Join("; ", g.Failures)}");
        });
    }
}
