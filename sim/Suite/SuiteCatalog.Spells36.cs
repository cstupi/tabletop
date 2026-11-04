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
            // Defense 2+ keeps the targets standing, and each target its casters (1-3 at target unit 1, 4-5 at 2, 6 at 3), so many blocks are rolled both over
            // half (units 2 and 3, and unit 1 before its fourth wound) and at or under it (unit 1 after).
            .Append(Repeated36(set, "jlray7cwf8mvw5sn", "Rapid Putrefaction", DecimateName36, DecimateCheck36, RapidPutrefaction, [1, 1],
                new SpellSetup(TargetModels: 1, TargetTough: 8, Anchored: true, TargetDefense: 2, Targets: new Dictionary<string, Func<string, IReadOnlyList<string>>>
                    { ["Rapid Putrefaction"] = caster => [SpellGames.UnitName(SpellGames.TargetArmy, int.Parse(caster[(caster.LastIndexOf(' ') + 1)..]) switch { <= 3 => 1, <= 5 => 2, _ => 3 })] })))
            .Append(Repeated36(set, "nyh41t82jugcdq8m", "Light Fragments", DestructiveName36, DestructiveCheck36, LightFragments, [1, 1, 1, 1, 1, 1],
                new SpellSetup(TargetModels: 1, TargetTough: 8, Anchored: true)));
    }

    public const string DecimateName36 = "spells-plague-disciples-decimate", DestructiveName36 = "spells-elven-jesters-destructive";

    /// <summary>The 3.6 spell games that are no book's game: each casts one spell from every unit (SpellGamesTests leaves them out of the per-book checks).</summary>
    public static readonly IReadOnlyList<string> RepeatedSpellGames36 = [DecimateName36, DestructiveName36];

    /// <summary>
    /// A book's spell game whose six units all cast one spell, at single-model Tough(8) targets of the setup (a single-model unit's size is its Tough, so a wounded
    /// target crosses half), every unit deployed on its lane (Anchored). Units 1 and 2 stand within the spell's 12" of target unit 1, unit 3 too: in round 1 units 1 and 2 pass their casts and
    /// their first blocks there are set to the faces given (failures, each a wound), and unit 3 passes its cast at it, so target unit 1 is at or under
    /// half when unit 3's hits land; the other targets are hit fresh. Covers nothing: its check reports against the spell.
    /// </summary>
    static TestGame Repeated36(Tabletop.GameServer.Properties.PropertySet set, string uid, string spell, string name, string check, Func<LogQueries, string?> failure, int[] faces,
        SpellSetup setup)
    {
        var book = set.Books[uid] with { Spells = [.. Enumerable.Repeat(spell, 6)] };
        string Unit(int k) => SpellGames.UnitName(book.Name, k);
        IReadOnlyList<DiceFix> dice = [.. Enumerable.Range(1, 3).Select(k => new DiceFix("casting", 0, [6], new RollKey(Actor: Unit(k)))),
            .. Enumerable.Range(1, faces.Length == 2 ? 2 : 1).Select(k => new DiceFix("defense", 0, faces, new RollKey(Actor: Unit(k))))];
        var game = SpellGames.Game(book, seed: 1, set, setup, dice, [Exact(check, [spell], failure)]);
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
            ["1wj1ysgxpuuz9bc7"] = new(First: ["Death Bolt"], TargetRules: [new(0, "Sturdy"), new(0, "Enhanced Sturdy")]),
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
            ["31xjrm9ivdimkjxp"] = SpellMorale,
            ["zz3kp5ry7ks6mxcx"] = SpellMorale,
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
            ["1wj1ysgxpuuz9bc7"] = [Exact(PierceCheck36, ["Death Bolt"], DeathBolt)],
            ["78qp9l5alslt6yj8"] = [Exact(SmashCheck36, ["Cerebral Trauma"], CerebralTrauma)],
            ["fk1mkbp8apvltu0z"] = [Exact(ScourgeCheck36, ["Smiting Rune"], SmitingRune)],
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
