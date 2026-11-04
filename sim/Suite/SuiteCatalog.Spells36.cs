using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The 3.6.0 spells group: a game per 3.6 book with a played spell (spells:3.6:&lt;uid&gt;), generated from the 3.6 set's books by SpellGames
/// as the 3.5.1 group is. A text-only spell (played false) is not cast and not covered. The tables are written from the 3.6 books: one
/// exact check per effect kind the 3.6 spells use, on a spell whose 3.6 text states it, each quoting that text.
/// </summary>
public static partial class SuiteCatalog
{
    private static IEnumerable<TestGame> Spells36()
    {
        var set = ScenarioCompiler.CorePropertiesOf(RuleSets.Gf360);
        return SpellGames.Books(set).Select(book => SpellGames.Game(book, seed: 1, set, SpellTables36.Setups.GetValueOrDefault(book.Uid),
            SpellTables36.Dice.GetValueOrDefault(book.Uid), SpellTables36.Checks.GetValueOrDefault(book.Uid)))
            .Concat(Repeated36Games(set));
    }

    public const string DecimateName36 = "spells-plague-disciples-decimate", DestructiveName36 = "spells-elven-jesters-destructive",
        SmashName36 = "spells-battle-brothers-smash", PierceName36 = "spells-orc-marauders-pierce", ScourgeName36 = "spells-dwarf-guilds-scourge";

    /// <summary>The 3.6 spell games that are no book's game: each casts one spell from every unit (SpellGamesTests leaves them out of the per-book checks).</summary>
    public static readonly IReadOnlyList<string> RepeatedSpellGames36 = [DecimateName36, DestructiveName36, SmashName36, PierceName36, ScourgeName36];

    /// <summary>Each caster's one target, by its unit number: "&lt;army&gt; unit k" casts at Targets unit targets[k - 1].</summary>
    static IReadOnlyDictionary<string, Func<string, IReadOnlyList<string>>> Aimed(string spell, params int[] targets) =>
        new Dictionary<string, Func<string, IReadOnlyList<string>>> { [spell] = caster => [SpellGames.UnitName(SpellGames.TargetArmy, targets[int.Parse(caster[(caster.LastIndexOf(' ') + 1)..]) - 1])] };

    /// <summary>Unit k of the book's army passes its first cast.</summary>
    static DiceFix Passes(string army, int k) => new("casting", 0, [6], new RollKey(Actor: SpellGames.UnitName(army, k)));

    /// <summary>Unit k's first block roll (its first cast's target's) set to the faces.</summary>
    static DiceFix Blocks(string army, int k, params int[] faces) => new("defense", 0, faces, new RollKey(Actor: SpellGames.UnitName(army, k)));

    /// <summary>
    /// The games of one spell cast by six units, every unit deployed on its lane (Anchored): the book's units at x 3, 9 … 33 on y = 13, the targets at
    /// x 6, 18, 30 on y = 10.
    /// </summary>
    static IEnumerable<TestGame> Repeated36Games(Tabletop.GameServer.Properties.PropertySet set)
    {
        // Decimate: single-model Tough(8) targets at Defense 2+, casters 1-3 at target 1, 4-5 at 2, 6 at 3. Units 1 and 2's first blocks at target 1 are
        // two failures each (4 wounds: at half), and unit 3 passes its cast there, so blocks are rolled both over half and at or under it.
        const string plague = "Plague Disciples";
        yield return Repeated36(set, "jlray7cwf8mvw5sn", "Rapid Putrefaction", DecimateName36, [Exact(DecimateCheck36, ["Rapid Putrefaction"], RapidPutrefaction)],
            [Passes(plague, 1), Passes(plague, 2), Passes(plague, 3), Blocks(plague, 1, 1, 1), Blocks(plague, 2, 1, 1)],
            new SpellSetup(TargetModels: 1, TargetTough: 8, TargetDefense: 2, Targets: Aimed("Rapid Putrefaction", 1, 1, 1, 2, 2, 3)));
        // Destructive: unit 1's first block at target 1 is six failures (2 of 8 wounds left), and units 2 and 3 pass their casts there.
        const string jesters = "Elven Jesters";
        yield return Repeated36(set, "nyh41t82jugcdq8m", "Light Fragments", DestructiveName36, [Exact(DestructiveCheck36, ["Light Fragments"], LightFragments)],
            [Passes(jesters, 1), Passes(jesters, 2), Passes(jesters, 3), Blocks(jesters, 1, 1, 1, 1, 1, 1, 1)],
            new SpellSetup(TargetModels: 1, TargetTough: 8));
        // Smash (24", over 12"): casters 1 and 6 at target 2 (about 15" centre to centre), 2 at target 3 and 5 at target 1 (about 21"), so four casts a
        // round over half; 3 and 4 at target 2 (about 4"), at or under. Tough(40) at Defense 2+ lasts the game; units 1 and 3 pass their first casts.
        const string battle = "Battle Brothers";
        yield return Repeated36(set, "78qp9l5alslt6yj8", "Cerebral Trauma", SmashName36, [Exact(SmashCheck36, ["Cerebral Trauma"], CerebralTrauma)],
            [Passes(battle, 1), Passes(battle, 3)],
            new SpellSetup(TargetModels: 1, TargetTough: 40, TargetDefense: 2, Targets: Aimed("Cerebral Trauma", 2, 3, 2, 2, 1, 2)));
        // Pierce (12", over 6"): casters 2 at target 2 and 4 at target 3 (about 9.5"), over half; 1, 3, 5 and 6 at their nearest target (about 4"), at or
        // under. The targets' Sturdy with Enhanced Sturdy always gives +1 to Defense, a positive modifier for Pierce to ignore at any range.
        const string orcs = "Orc Marauders";
        yield return Repeated36(set, "1wj1ysgxpuuz9bc7", "Death Bolt", PierceName36, [Exact(PierceCheck36, ["Death Bolt"], DeathBolt)],
            [Passes(orcs, 1), Passes(orcs, 2)],
            new SpellSetup(TargetModels: 1, TargetTough: 40, TargetDefense: 2, TargetRules: [new(0, "Sturdy"), new(0, "Enhanced Sturdy")],
                Targets: Aimed("Death Bolt", 1, 2, 2, 3, 3, 3)));
        // Scourge counted as shooting's toward p.10's test: target 1 is five single-wound models; unit 1 passes its first cast there and its blocks are one 1
        // and three 6s: one wound leaves 4 of 5 (over half), the 1's 2 counted wounds leave 2 (half or less), so the unit tests. The others aim at 2 and 3.
        const string dwarfs = "Dwarf Guilds";
        yield return Repeated36(set, "fk1mkbp8apvltu0z", "Smiting Rune", ScourgeName36, [Exact(ScourgeCheck36, ["Smiting Rune"], SmitingRune)],
            [Passes(dwarfs, 1), Blocks(dwarfs, 1, 1, 6, 6, 6)],
            new SpellSetup(TargetModels: 5, TargetTough: 1, Targets: Aimed("Smiting Rune", 1, 2, 2, 2, 3, 3)));
    }

    /// <summary>A book's spell game whose six units all cast one spell, the dice and setup given. Covers nothing: its checks report against the spell.</summary>
    static TestGame Repeated36(Tabletop.GameServer.Properties.PropertySet set, string uid, string spell, string name, IReadOnlyList<GameCheck> checks,
        IReadOnlyList<DiceFix> dice, SpellSetup setup)
    {
        var book = set.Books[uid] with { Spells = [.. Enumerable.Repeat(spell, 6)] };
        var game = SpellGames.Game(book, seed: 1, set, setup, dice, checks, firstCastsPass: false);
        return game with { Name = name, Scenario = game.Scenario with { Name = name }, Covers = [], Checks = [.. game.Checks.Where(c => c.Name != Checks.FiredAll().Name)] };
    }

    public const string NaturalCastingDieCheck36 = "casting die (3.6.0 p.5: \"a roll of 6 always counts as a success and a roll of 1 always counts as a fail, regardless of how much it is being modified by\")";
    public const string AddHitCheck36 = "add_hit (Mutating Inferno: \"which take 3 hits each\")";
    public const string RolledHitsCheck36 = "add_hit rolled (Bio-Horror: \"which takes 6 hits with Surge. Roll as many dice as hits\"; 3.6.0 p.15 Surge: \"For each unmodified result of 6 to hit, this weapon deals 1 extra hit\")";
    public const string GrantCheck36 = "grant_property (Breath of Change: \"Pick up to two friendly units within 12\\\", which get Bane when Shooting once\")";
    public const string RollModifierCheck36 = "modify_roll (Tech-Sickness: \"which get -1 to Defense rolls once (next time the effect would apply)\")";
    public const string StatCheck36 = "modify_stat (Shock Speed: \"moves +2\\\" when using Advance actions and +4\\\" when using Rush/Charge actions once\"; 3.6.0 p.7: Advance \"up to 6”\")";
    public const string ParameterCheck36 = "modify_parameter (Corrode Weapons: \"which loses AP(1) on its weapons when shooting once\")";
    public const string TerrainCheck36 = "count_as_terrain (Cursed Stride: \"counts as being in Dangerous Terrain once\"; 3.6.0 p.12: \"or activates in it, then the entire unit must take a Dangerous Terrain Test. To take a Dangerous Terrain Test, roll as many dice as the number of wounds it would take to fully destroy the unit\")";
    public const string FatiguedCheck36 = "take_test, set_condition (Terrifying Fury: \"must take a morale test. If failed, it becomes Fatigued\"; 3.6.0 p.10: \"If the test is failed, the unit is Shaken\")";
    public const string MoveCheck36 = "take_test, move (Deep Hypnosis: \"must take a Morale Test. If failed you may move it by up to 6\\\" in a straight line\")";

    /// <summary>The 3.6 per-book tables, keyed by the 3.6 book uid, in a class of their own as SpellTables is.</summary>
    static class SpellTables36
    {
        public static readonly Dictionary<string, SpellSetup> Setups = new(StringComparer.Ordinal)
        {
            // Mutating Inferno first: no attack precedes it, so its first target's blocks are the game's defense roll 0. Breath of Change on
            // its caster's own unit every round, so a recast meets a unit still holding the first.
            ["04z57ua0bwth37zh"] = new(First: ["Mutating Inferno"],
                Targets: new Dictionary<string, Func<string, IReadOnlyList<string>>> { ["Breath of Change"] = caster => [caster] }),
            ["hk70l4d471plza00"] = new(First: ["Tech-Sickness"]),
            ["3j10zage1lddt6sr"] = new(Targets: new Dictionary<string, Func<string, IReadOnlyList<string>>> { ["Shock Speed"] = SpellTables.NextUnit }),
            // Corrode Weapons first, at target unit 1, which Advances and shoots its AP(1) rifles every round.
            ["7el7k3hgy5pb9o9i"] = new(First: ["Corrode Weapons"],
                Targets: new Dictionary<string, Func<string, IReadOnlyList<string>>> { ["Corrode Weapons"] = _ => [SpellGames.UnitName(SpellGames.TargetArmy, 1)] },
                BlueAction: j => j == 1 ? "Advance" : "Rush"),
            ["7o6om21wxlvvy3hq"] = new(First: ["Cursed Stride"]),
            // Psychic Terror ("Pick one enemy unit within 6"") first: unit 1's lane is the one nearest target unit 1's.
            ["78qp9l5alslt6yj8"] = new(First: ["Psychic Terror"]),
            ["rvvb3kdn2x2pqkki"] = new(First: ["Psychic Terror"]),
            ["xp5zwh73lg1uaym4"] = new(First: ["Psychic Terror"]),
            ["gk7me4sgn9s740kw"] = new(First: ["Psychic Terror"]),
            ["oqnnu0gk8q6hyyny"] = new(First: ["Psychic Terror"]),
            ["rl7ympklz4r0ls38"] = new(First: ["Psychic Terror"]),
            // Every spare token on the roll, so an unmodified 1 is lifted to 4+ and the natural casting die check has a case.
            ["z65fgu0l29i4lnlu"] = new(Bonus: 6),
            // Shifting Form first, at targets that are Caster(2): each target model holds 2 tokens from round 1.
            ["r6hr29338u4micfw"] = new(First: ["Shifting Form"], TargetRules: [new(0, "Caster", 2)]),
            // Death Bolt at targets whose Sturdy always gives +1 to Defense (Enhanced Sturdy), a positive modifier for Pierce to ignore.
            // Every unit of the book's army has Rapid Blink, which Shadow Dance's grant boosts.
            ["nyh41t82jugcdq8m"] = new(Rules: [new(0, "Rapid Blink")]),
            ["yxjboa8oma9bbdck"] = new(Rules: [new(0, "Wolfborn")]),
        };

        // Declared before Dice: static initializers run in textual order.
        static readonly IReadOnlyList<DiceFix> SpellMorale =
            [new DiceFix("morale", 0, [1], new RollKey(Action: "spell")), new DiceFix("morale", 1, [6], new RollKey(Action: "spell"))];

        public static readonly Dictionary<string, IReadOnlyList<DiceFix>> Dice = new(StringComparer.Ordinal)
        {
            // Mutating Inferno's caster (unit 1) passes its first cast, so the game's first defense roll is its first target's blocks: two 1s and a
            // 6 against Defense 4+.
            ["04z57ua0bwth37zh"] = [new DiceFix("casting", 0, [6], new RollKey(Actor: "Daemons of Change unit 1")), new DiceFix("defense", 0, [1, 1, 6])],
            // A spell's morale tests (GfCasting.SpellTest: action "spell"): the first fails and the second passes, so both outcomes are checked.
            // Terrifying Fury's caster (unit 1) passes its second cast too, so round 2's test meets target unit 2, not the unit round 1's failure left Shaken.
            ["31xjrm9ivdimkjxp"] = [.. SpellMorale, new DiceFix("casting", 1, [6], new RollKey(Actor: "War Disciples unit 1"))],
            ["zz3kp5ry7ks6mxcx"] = SpellMorale,
            ["z65fgu0l29i4lnlu"] = SpellTables.NaturalCastingFixes,
            // Bio-Horror's caster (unit 6 of the book's order) rolls a natural 6 on its first hit die, which Surge turns into an extra hit.
            ["dnthspt7c0klhmt8"] = [new DiceFix("hit", 0, [6], new RollKey(Actor: "Infected Colonies unit 6"))],
        };

        public static readonly Dictionary<string, IReadOnlyList<GameCheck>> Checks = new(StringComparer.Ordinal)
        {
            ["z65fgu0l29i4lnlu"] = [Exact(NaturalCastingDieCheck36, ["Caster"], NaturalCastingDice)],
            ["04z57ua0bwth37zh"] = [Exact(AddHitCheck36, ["Mutating Inferno"], q => FixedHits(q, "Mutating Inferno", 3, 2)),
                Exact(GrantCheck36, ["Breath of Change"], BreathOfChange)],
            ["dnthspt7c0klhmt8"] = [Exact(RolledHitsCheck36, ["Bio-Horror"], q => RolledHits(q, "Bio-Horror", 6, 0))],
            ["hk70l4d471plza00"] = [Exact(RollModifierCheck36, ["Tech-Sickness"], TechSickness)],
            ["3j10zage1lddt6sr"] = [Exact(StatCheck36, ["Shock Speed"], ShockSpeed)],
            ["7el7k3hgy5pb9o9i"] = [Exact(ParameterCheck36, ["Corrode Weapons"], CorrodeWeapons)],
            ["7o6om21wxlvvy3hq"] = [Exact(TerrainCheck36, ["Cursed Stride"], CursedStride36)],
            ["31xjrm9ivdimkjxp"] = [Exact(FatiguedCheck36, ["Terrifying Fury"], TerrifyingFury)],
            ["zz3kp5ry7ks6mxcx"] = [Exact(MoveCheck36, ["Deep Hypnosis"], DeepHypnosis)],
            ["r6hr29338u4micfw"] = [Exact(RemoveTokensCheck36, ["Shifting Form"], ShiftingForm)],
            ["nyh41t82jugcdq8m"] = [Exact(RapidBlinkCheck36, ["Shadow Dance"], q => BoostedMove(q, "Shadow Dance", "Rapid Blink"))],
            ["yxjboa8oma9bbdck"] = [Exact(WolfbornCheck36, ["Righteous Fury"], q => BoostedMove(q, "Righteous Fury", "Wolfborn"))],
            ["4k5amkxoybdiqotm"] = [Exact(IndirectCheck36, ["Triangulation Bots"], q => GrantWritten(q, "Triangulation Bots"))],
        };
    }

    /// <summary>Cursed Stride under 3.6.0 p.12: the target's next activation takes one test for the unit, a die per wound left on its living Tough(6) models.</summary>
    static string? CursedStride36(LogQueries q)
    {
        if (FirstGrant(q, "Cursed Stride") is not var (at, grant)) return "Cursed Stride never granted";
        var activation = q.Frames.FirstOrDefault(f => f.Seq > at.Seq && RootActions.Contains(f.Sequence) && f.Context.ActorUnitId == grant.UnitId);
        if (activation is null) return "the target never activated after the grant";
        var tests = q.Descendants(activation).Where(f => f.Sequence == "dangerous-terrain-36").ToList();
        if (tests.Count != 1) return $"its next activation ({activation.Sequence}) took {tests.Count} unit dangerous terrain tests, not 1";
        var dice = tests[0].Steps.SelectMany(s => s.AllRolls).Sum(r => r.Dice.Count);
        var left = q.LocationsBefore(tests[0].Seq).Where(kv => kv.Value is not OnShelf && q.UnitOf(kv.Key) == grant.UnitId).Sum(kv => 6 - q.WoundsOn(kv.Key, tests[0].Seq));
        return dice == left ? null : $"{dice} dice for {left} wounds left on the unit";
    }
}
