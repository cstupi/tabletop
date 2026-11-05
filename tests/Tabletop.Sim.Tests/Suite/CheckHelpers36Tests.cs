using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>
/// The 3.6 check helpers over hand-built logs (Parry, Brace, Scourge's counted morale, and the spell checks that read half a spell's range): each passes
/// the log its rule describes, fails a log that misreads the rule, and fails an empty log.
/// </summary>
public class CheckHelpers36Tests
{
    /// <summary>A game's scenario compiled once, so a hand-built log names its units and models by the ids its projection holds.</summary>
    sealed class Board(string game)
    {
        readonly TestGame _game = SuiteCatalog.All.Single(g => g.Name == game);
        readonly CompiledScenario _compiled = ScenarioCompiler.Compile(SuiteCatalog.All.Single(g => g.Name == game).Scenario);

        Unit Of(string name) => _compiled.Board.Armies.SelectMany(a => a.Snapshot.Units).Single(u => u.Name == name);
        public Guid Unit(string name) => Of(name).Id;
        public IReadOnlyList<Guid> Models(string unit) => [.. Of(unit).Models.Select(m => m.Id)];
        public Guid Weapon(string unit, string weapon) => Of(unit).Models[0].Weapons.First(w => w.Name == weapon).Id;

        SuiteGameRun Run(EventPayload[] log) => new(_game, new RunOutcome.RoundLimit(4), [.. log.Select((p, i) => new GameEvent(i + 1, null, p, null))],
            new EventApplier(_compiled.Board, _compiled.Info).Projection, []);
        GameCheck Check(string prefix) => _game.Checks.Single(c => c.Name.StartsWith(prefix, StringComparison.Ordinal));
        public string? Judge(string check, params EventPayload[] log) => Check(check).Failure(Run(log));
    }

    static readonly Guid Team = Guid.CreateVersion7();

    static ModelsMoved Placed(IEnumerable<Guid> models, double x = 10, double y = 10) =>
        new([.. models.Select((m, i) => new ModelRelocation(m, new OnShelf(), new OnTable(new Placement(new Point3(x + 3 * i, y, 0), new Yaw(0)))))]);

    static ModelsMoved Removed(IEnumerable<Guid> models) =>
        new([.. models.Select(m => new ModelRelocation(m, new OnTable(new Placement(new Point3(0, 0, 0), new Yaw(0))), new OnShelf()))]);

    static RollRecord Roll(string kind, int threshold, IReadOnlyList<RollModifier> modifiers, params int[] faces) =>
        new(Guid.CreateVersion7(), kind, threshold, modifiers, [.. faces.Select(f => new Die(f, f + modifiers.Sum(m => m.Amount), f + modifiers.Sum(m => m.Amount) >= threshold && f != 1 || f == 6))]);

    static StepCompleted Step(Guid frame, string step, Dictionary<string, int>? values = null, AskAnswer? answer = null, params RollRecord[] rolls) =>
        new(frame, step, answer, rolls, []) { Values = values };

    static SequenceStarted Start(Guid frame, string sequence, Guid? actor, Guid? target, IReadOnlyList<Guid>? weapons = null, string? action = null) =>
        new(frame, sequence, new SequenceContext(actor, target is { } t ? [t] : [], weapons ?? [], action));

    // ---- Parry ----------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A charge at the Parry(1) unit (three models on the table): an attack whose three hits meet a parry step and a roll-to-block step. 3.6.0 Parry:
    /// "roll as many dice as its X value"; the dice are the lesser of X per model and the hits, a 6+ ignores a hit, and the hits left are blocked.
    /// </summary>
    static EventPayload[] Parry(Board board, RollRecord parry, int blocked)
    {
        var (defender, charger) = (board.Unit("Daemons of Change unit 1"), board.Unit("Opponents unit 1"));
        var (melee, attack, resolve) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        return
        [
            Placed(board.Models("Daemons of Change unit 1")),
            Start(melee, "melee", charger, defender, action: "Charge"),
            Start(attack, "attack-sequence", charger, defender),
            Step(attack, "roll-to-hit", rolls: Roll("hit", 4, [], 6, 6, 6)),
            Start(resolve, "resolve-hits", charger, defender),
            Step(resolve, "parry", rolls: parry),
            Step(resolve, "roll-to-block", rolls: Roll("defense", 4, [], [.. Enumerable.Repeat(4, blocked)])),
            new SequenceFinished(resolve), new SequenceFinished(attack), new SequenceFinished(melee),
        ];
    }

    [Fact]
    public void Parried_fails_a_misread_parry_and_an_empty_log()
    {
        var board = new Board("daemons-of-change-parry@3.6");
        RollRecord Dice(params int[] faces) => Roll("Parry", 6, [], faces);
        Assert.Null(board.Judge("Parry: ", Parry(board, Dice(6, 6, 1), 1)));
        Assert.Contains("1 Parry dice for 3 hits and 3 per model, expected 3", board.Judge("Parry: ", Parry(board, Dice(6), 2)));   // one die for the unit
        Assert.Contains("3 dice blocked for 3 hits less 2 ignored", board.Judge("Parry: ", Parry(board, Dice(6, 6, 1), 3)));      // the ignored hits still blocked
        Assert.Contains("Parry dice 5 6 1x: success is 6+", board.Judge("Parry: ", Parry(board,                                    // a 5 read as a success
            new RollRecord(Guid.CreateVersion7(), "Parry", 5, [], [new Die(5, 5, true), new Die(6, 6, true), new Die(1, 1, false)]), 1)));
        Assert.Contains("no hit ignored", board.Judge("Parry: ", Parry(board, Dice(1, 1, 1), 3)));
        Assert.NotNull(board.Judge("Parry: "));
    }

    // ---- Brace: RollCount over BraceDice --------------------------------------------------------------------------------------------------

    /// <summary>Three Impact(2) chargers on the table charge the Brace(1) unit and roll their impact dice; Fatigued sets the defender's condition first.</summary>
    static EventPayload[] Brace(Board board, string charger, int dice, bool fatigued = false)
    {
        var defender = board.Unit("Daemons of Lust unit 1");
        var melee = Guid.CreateVersion7();
        return
        [
            Placed(board.Models(charger)),
            .. fatigued ? new EventPayload[] { new ConditionSet(new Subject(SubjectKind.Unit, defender), "fatigued", true) } : [],
            Start(melee, "melee", board.Unit(charger), defender, action: "Charge"),
            Step(melee, "impact", rolls: Roll("Impact", 2, [], [.. Enumerable.Repeat(4, dice)])),
            new SequenceFinished(melee),
        ];
    }

    [Fact]
    public void Brace_dice_fail_a_charge_that_ignores_brace_or_fatigue_and_an_empty_log()
    {
        var plain = new Board("daemons-of-lust-brace-fatigued@3.6");
        Assert.Null(plain.Judge("Brace: ", [.. Brace(plain, "Opponents unit 1", 5), .. Brace(plain, "Opponents unit 2", 6, fatigued: true)]));
        Assert.Contains("6 Impact dice at a unit not Fatigued, expected 5", plain.Judge("Brace: ", Brace(plain, "Opponents unit 1", 6)));   // Brace not taken off
        Assert.Contains("5 Impact dice at a Fatigued unit, expected 6", plain.Judge("Brace: ", Brace(plain, "Opponents unit 2", 5, fatigued: true)));
        Assert.Contains("no Impact roll at a Fatigued unit", plain.Judge("Brace: ", Brace(plain, "Opponents unit 1", 5)));
        Assert.Contains("no Impact roll at a Fatigued unit", plain.Judge("Brace: "));
    }

    // ---- Scourge: CountedMorale -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The Scourge rifle shoots the target unit (its models on the table): one roll-to-block with those faces, the models removed, then the target's
    /// wounds morale test or none.
    /// </summary>
    static EventPayload[] Scourged(Board board, int[] blocks, int fall, bool test)
    {
        var (holder, target) = (board.Unit("Change Disciples unit 1"), board.Unit("Opponents unit 1"));
        var models = board.Models("Opponents unit 1");
        var (shoot, attack, resolve, morale) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        return
        [
            Placed(models),
            Start(shoot, "shooting", holder, target, action: "Hold"),
            Start(attack, "attack-sequence", holder, target, [board.Weapon("Change Disciples unit 1", "Rifle")], "Hold"),
            Start(resolve, "resolve-hits", holder, target),
            Step(resolve, "roll-to-block", rolls: Roll("defense", 4, [], blocks)),
            Removed(models.Take(fall)),
            new SequenceFinished(resolve), new SequenceFinished(attack), new SequenceFinished(shoot),
            .. test ? new EventPayload[] { Start(morale, "morale-test", target, null, action: "wounds"), new SequenceFinished(morale) } : [],
        ];
    }

    [Fact]
    public void Counted_morale_fails_a_test_the_counted_wounds_do_not_call_for_and_an_empty_log()
    {
        var tests = new Board("change-disciples-scourge-morale@3.6");                 // five models
        Assert.Null(tests.Judge("Scourge: ", Scourged(tests, [1], 1, test: true)));
        Assert.Contains("4 of 5 left, 1 ones: no test", tests.Judge("Scourge: ", Scourged(tests, [1], 1, test: false)));   // the counted wounds ignored
        Assert.NotNull(tests.Judge("Scourge: "));

        var failed = new Board("change-disciples-scourge-failed-block@3.6");           // nine models
        Assert.Null(failed.Judge("Scourge: ", Scourged(failed, [1, 2], 2, test: false)));
        Assert.Contains("7 of 9 left, 1 ones: a test", failed.Judge("Scourge: ", Scourged(failed, [1, 2], 2, test: true)));   // the 2 counted as well
        Assert.NotNull(failed.Judge("Scourge: "));
    }

    // ---- The spell checks that read half the range ----------------------------------------------------------------------------------------

    /// <summary>
    /// One resolved cast of the spell by the caster unit's model at (10, 10) on the target unit's model at (10 + distance, 10): its spell-hits frame
    /// (hit, score-hits, a resolve-hits with one roll-to-block, then attack-end), the block's later events, and what follows the cast.
    /// </summary>
    static EventPayload[] Cast(Board board, string spell, string caster, string target, double distance, RollRecord block,
        Func<Guid, Guid, EventPayload[]>? afterBlock = null, Dictionary<string, int>? blockValues = null, EventPayload[]? afterCast = null)
    {
        var (casterId, targetId) = (board.Unit(caster), board.Unit(target));
        var (casting, hits, resolve) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        return
        [
            Placed([board.Models(caster)[0]]), Placed(board.Models(target), 10 + distance),
            Start(casting, "casting", casterId, null),
            Step(casting, "pick-spell", answer: new Chosen(spell + "|0")),
            Step(casting, "roll", rolls: Roll("casting", 4, [], 6)),
            Step(casting, "targets", answer: new SelectedPieces([targetId])),
            Step(casting, "result", new() { ["passed"] = 1 }),
            Start(hits, "spell-hits", casterId, targetId),
            Step(hits, "hit"), Step(hits, "score-hits"),
            Start(resolve, "resolve-hits", casterId, targetId),
            Step(resolve, "roll-to-block", blockValues, rolls: block),
            .. afterBlock?.Invoke(resolve, block.Id) ?? [],
            new SequenceFinished(resolve),
            Step(hits, "attack-end"),
            new SequenceFinished(hits), new SequenceFinished(casting),
            .. afterCast ?? [],
        ];
    }

    static EventPayload[] Rerolled(Guid frame, RollRecord roll, params int[] indexes) =>
        [new DiceRerolled(frame, roll.Id, [.. indexes.Select(i => new DieReplacement(i, roll.Dice[i], new Die(3, 3, false)))])];

    [Fact]
    public void Smash_fails_a_reroll_read_at_the_wrong_range_and_an_empty_log()
    {
        var board = new Board("spells-battle-brothers-smash@3.6");                     // Cerebral Trauma, 24": over half is over 12"
        RollRecord Block() => Roll("defense", 2, [new RollModifier("AP", -1)], 6, 3, 6);
        EventPayload[] Far(bool reroll) { var b = Block(); return Cast(board, "Cerebral Trauma", "Battle Brothers unit 1", "Targets unit 1", 16, b, reroll ? (f, _) => Rerolled(f, b, 0, 2) : null); }
        EventPayload[] Near(bool reroll) { var b = Block(); return Cast(board, "Cerebral Trauma", "Battle Brothers unit 2", "Targets unit 2", 8, b, reroll ? (f, _) => Rerolled(f, b, 0, 2) : null); }
        const string smash = SuiteCatalog.SmashCheck36;

        Assert.Null(board.Judge(smash, [.. Far(true), .. Near(false)]));
        Assert.Contains("re-rolled [] of [6 3 6] from over half the range", board.Judge(smash, [.. Far(false), .. Near(false)]));
        Assert.Contains("re-rolled [0,2] of [6 3 6] from at or under half the range", board.Judge(smash, [.. Far(true), .. Near(true)]));   // re-rolled at any range
        Assert.Equal("no block from 12\" or under", board.Judge(smash, Far(true)));
        Assert.NotNull(board.Judge(smash));
    }

    [Fact]
    public void Pierce_fails_ap_read_at_the_wrong_range_and_an_empty_log()
    {
        var board = new Board("spells-orc-marauders-pierce@3.6");                      // Death Bolt, 12": over half is over 6"
        IReadOnlyList<IgnoredEffect> pierced = [new IgnoredEffect("Sturdy", "Pierce", 1)];
        RollRecord Block(int ap) => Roll("defense", 2, [new RollModifier("AP", ap)], 4, 5) with { Ignored = pierced };
        EventPayload[] At(string caster, string target, double distance, RollRecord block) => Cast(board, "Death Bolt", caster, target, distance, block);
        const string pierce = SuiteCatalog.PierceCheck36;

        Assert.Null(board.Judge(pierce, [.. At("Orc Marauders unit 1", "Targets unit 1", 9, Block(-2)), .. At("Orc Marauders unit 2", "Targets unit 2", 4, Block(-1))]));
        Assert.Contains("a block at AP(1) from over half the range", board.Judge(pierce,                                          // no AP(+1) from over 6"
            [.. At("Orc Marauders unit 1", "Targets unit 1", 9, Block(-1)), .. At("Orc Marauders unit 2", "Targets unit 2", 4, Block(-1))]));
        Assert.Contains("a block at AP(2) from at or under half the range", board.Judge(pierce,                                   // AP(+1) at any range
            [.. At("Orc Marauders unit 1", "Targets unit 1", 9, Block(-2)), .. At("Orc Marauders unit 2", "Targets unit 2", 4, Block(-2))]));
        Assert.Contains("a block under Sturdy +1", board.Judge(pierce,
            [.. At("Orc Marauders unit 1", "Targets unit 1", 9, Block(-2) with { Modifiers = [new RollModifier("AP", -2), new RollModifier("Sturdy", 1)] }),
             .. At("Orc Marauders unit 2", "Targets unit 2", 4, Block(-1))]));
        // 6.5" centre to centre is neither: base to base it may be 5.24" or 6.5", so the block is no case and the far one is missing.
        Assert.Equal("no Death Bolt block from over 6\"", board.Judge(pierce, [.. At("Orc Marauders unit 1", "Targets unit 1", 6.5, Block(-2)), .. At("Orc Marauders unit 2", "Targets unit 2", 4, Block(-1))]));
        Assert.NotNull(board.Judge(pierce));
    }

    [Fact]
    public void Scourge_spell_fails_a_count_or_test_that_misreads_the_ones_and_an_empty_log()
    {
        var board = new Board("spells-dwarf-guilds-scourge@3.6");                      // Smiting Rune at five one-wound models
        var target = board.Unit("Targets unit 1");
        // The block as the game's fix leaves it, one 1 and three 6s (edited, as the fix does), and one model removed for the 1.
        EventPayload[] Rune(Dictionary<string, int> counts, bool test)
        {
            var block = Roll("defense", 4, [], 1, 6, 6, 6);
            var morale = Guid.CreateVersion7();
            return Cast(board, "Smiting Rune", "Dwarf Guilds unit 1", "Targets unit 1", 8, block,
                (f, id) => [new DiceEdited(f, id, [new DieReplacement(0, block.Dice[0], block.Dice[0])]), Removed(board.Models("Targets unit 1").Take(1))],
                counts, test ? [Start(morale, "morale-test", target, null, action: "wounds"), new SequenceFinished(morale)] : []);
        }
        const string scourge = SuiteCatalog.ScourgeCheck36;

        Assert.Null(board.Judge(scourge, Rune(new() { ["counted_0_1_Scourge"] = 2, ["counted_wounds"] = 2 }, test: true)));
        Assert.Contains("counts [counted_0_1_Scourge=1]", board.Judge(scourge, Rune(new() { ["counted_0_1_Scourge"] = 1, ["counted_wounds"] = 1 }, test: true)));   // 1 per 1, not 2
        Assert.Contains("counts [counted_0_1_Scourge=2, counted_0_2_Scourge=2]", board.Judge(scourge,                                                            // every failed face counted
            Rune(new() { ["counted_0_1_Scourge"] = 2, ["counted_0_2_Scourge"] = 2, ["counted_wounds"] = 2 }, test: true)));
        Assert.Contains("counted 1 as rolled for 1 1s", board.Judge(scourge, Rune(new() { ["counted_0_1_Scourge"] = 2, ["counted_wounds"] = 1 }, test: true)));
        Assert.Equal("no Morale Test on the target before the next activation", board.Judge(scourge, Rune(new() { ["counted_0_1_Scourge"] = 2, ["counted_wounds"] = 2 }, test: false)));
        Assert.NotNull(board.Judge(scourge));
    }
}
