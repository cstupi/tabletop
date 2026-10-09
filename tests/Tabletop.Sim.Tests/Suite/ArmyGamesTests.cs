using Tabletop.GameServer.Properties;
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
    static readonly Dictionary<string, string> Defects = new()
    {
        ["soul-snatcher-cults-slayers"] = "no defender in this interaction",
        ["havoc-brothers-assault"] = "5 dice [3 2 2 4 5] for 3 wounds",
        ["jackals-scrap"] = "re-rolled dice [1] of [1 5], expected []",
    };

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
        Assert.All(Army, g => Assert.All(g.Scenario.Armies[0].Rules(), r => Assert.Contains(r, g.Covers)));
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
            Judge("saurian-starhost-primal-hunt", "Primal", start, Step(frame, "roll-to-hit", ["Primal"], null, [Pool(), .. added]));

        Assert.Null(With(Roll("hit", 4, [], (3, false), (4, true))));
        Assert.Null(With(Roll("hit", 4, [], (6, true), (4, true))));      // "This rule doesn't apply to newly generated attacks": the added 6 adds none
        Assert.Contains("1 added attacks", With(Roll("hit", 4, [], (3, false))));
        Assert.Contains("3 hit rolls", With(Roll("hit", 4, [], (6, true), (4, true)), Roll("hit", 4, [], (1, false))));
        Assert.Equal("no occasion in the log", Judge("saurian-starhost-primal-hunt", "Primal", start));
    }

    [Fact]
    public void A_modifier_must_have_its_size_on_its_roll_and_be_applied_to_the_dice()
    {
        var (start, frame) = Frame("attack-sequence");
        string? With(RollRecord roll) => Judge("saurian-starhost-primal-hunt", "Evasive", start, Step(frame, "roll-to-hit", ["Evasive"], null, roll));

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
        Assert.Null(Judge("dark-elf-raiders-harass", "Melee Evasion", melee, attack, Step(attackFrame, "roll-to-hit", [], null, roll)));
        Assert.Contains("outside where the rule applies", Judge("dark-elf-raiders-harass", "Melee Evasion", attack, Step(attackFrame, "roll-to-hit", [], null, roll)));
    }

    [Fact]
    public void Rerolls_must_be_exactly_the_dice_the_rule_names()
    {
        var (start, frame) = Frame("resolve-hits");
        var defense = Roll("defense", 4, [], (6, true), (5, true), (5, false), (2, false));      // the third die: a 5 a modifier made fail
        DiceRerolled Of(params int[] indexes) => new(frame, defense.Id, [.. indexes.Select(i => new DieReplacement(i, defense.Dice[i], new Die(1, 1, false)))]);
        string? With(string entry, IReadOnlyList<string> applied, params DiceRerolled[] rerolls) =>
            Judge("goblin-reclaimers-mischief", entry, [start, Step(frame, "roll-to-block", applied, null, defense), .. rerolls]);

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
