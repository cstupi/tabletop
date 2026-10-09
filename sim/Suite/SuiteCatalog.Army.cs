using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The army-book rules of grimdark-future-army-rules.part.json, one game per book that prints them (group army:&lt;book uid&gt;; a rule several
/// books print is played once, under one of them). The first army carries the book's rules, a Boost on the unit that also holds its base;
/// the second is the opponent, with what a rule reads on its target (Tough, Defense, AP). Both armies close on each other (Closing), so
/// every game shoots and fights. Exact checks read the logged dice, values and Asks and compare them with the literals of the rule's own
/// text, which each check's name quotes (army books print no page); modifiers follow p.5's "add or subtract the value from the roll".
/// </summary>
public static partial class SuiteCatalog
{
    private static partial IEnumerable<TestGame> Army() =>
    [
        Book("saurian-starhost-primal-hunt", "U6hwlur14RpnInZr", "Saurian Starhost", 1,
            [R(1, "Primal"), R(1, "Evasive"), R(1, "Teleport"), R(2, "Primal"), R(2, "Primal Boost"), R(3, "Evasive")],
            checks:
            [
                ExtraAttacks("Primal", "Primal Boost", [6], "\"For each unmodified roll of 6 to hit ... may roll +1 attack with that weapon\""),
                ExtraAttacks("Primal Boost", null, [5, 6], "\"extra attacks on successful unmodified hit results of 5-6\""),
                Modifier("Evasive", "hit", -1, "\"Enemies get -1 to hit rolls\""),
                Teleport(),
            ]),
        Book("eternal-dynasty-clan-warriors", "vux1Y5vvULmaxZ8P", "Eternal Dynasty", 6,
            [R(1, "Clan Warrior"), R(1, "Counter-Attack"), R(2, "Clan Warrior"), R(2, "Clan Warrior Boost"), R(3, "Counter-Attack")],
            checks:
            [
                ExtraAttacks("Clan Warrior", "Clan Warrior Boost", [6], "\"For each unmodified roll of 6 to hit ... may roll +1 attack with that weapon\""),
                ExtraAttacks("Clan Warrior Boost", null, [5, 6], "\"extra attacks on successful unmodified hit results of 5-6\""),
            ]),
        Book("blessed-sisters-devout-fire", "7oi8zeiqfamiur21", "Blessed Sisters", 6,
            [R(1, "Devout"), R(2, "Devout"), R(2, "Devout Boost"), R(3, "Devout")],
            weapons: [Gun(2, rules: R(0, "Disintegrate")), Blade(2)], enemyStats: Stats(defense: 3),
            checks:
            [
                ExtraHits("Devout", "Devout Boost", [6], "\"unmodified results of 6 to hit deal 1 extra hit\""),
                ExtraHits("Devout Boost", null, [5, 6], "\"extra hits on successful unmodified hit results of 5-6\""),
                ApGrant("Disintegrate", 2, "\"against units where most models have Defense 2+ to Defense 3+, this weapon gets AP(+2)\""),
            ]),
        Book("orc-marauders-ferocious-raid", "1wj1ysgxpuuz9bc7", "Orc Marauders", 9,
            [R(1, "Ferocious"), R(1, "Hit & Run Fighter"), R(1, "Piercing Assault"), R(2, "Ferocious"), R(2, "Ferocious Boost"), R(3, "Hit & Run Fighter"), R(3, "Piercing Assault")],
            checks:
            [
                ExtraHits("Ferocious", "Ferocious Boost", [6], "\"unmodified results of 6 to hit deal 1 extra hit\""),
                ExtraHits("Ferocious Boost", null, [5, 6], "\"extra hits on successful unmodified hit results of 5-6\""),
                Reposition("Hit & Run Fighter", 3, "\"may move by up to 3\" after being in melee\""),
                ApGrant("Piercing Assault", 1, "\"This model gets AP(+1) when charging\""),
            ]),
        Book("soul-snatcher-cults-ruins", "zz3kp5ry7ks6mxcx", "Soul-Snatcher Cults", 13,
            [R(1, "Grounded Reinforcement"), R(2, "Quick Shot"), R(3, "Grounded Reinforcement")], board: Ruins,
            checks: [Modifier("Grounded Reinforcement", "defense", 1, "\"they get +1 to defense rolls\"")]),
        // Blocked by an engine defect: GfMelee.ChargeTargets settles a defender-less melee interaction, and Slayer's charge row reaches its
        // most_models opponent condition there, which throws "no defender in this interaction" when round 1 offers its first activation.
        Book("soul-snatcher-cults-slayers", "zz3kp5ry7ks6mxcx", "Soul-Snatcher Cults", 1,
            [R(1, "Slayer"), R(2, "Slayer")], enemyRules: [R(1, "Tough", 3), R(2, "Tough", 3)],
            checks: [ApGrant("Slayer", 2, "\"its weapons get AP(+2) if most models in the target have Tough(3) or higher\"")]),
        Book("custodian-brothers-guard", "ewbra8nv3nq3k27p", "Custodian Brothers", 3,
            [R(1, "Guardian"), R(2, "Guardian"), R(2, "Guardian Boost"), R(3, "Guardian")],
            weapons: [Gun(2, rules: R(0, "Tear")), Blade(2)],
            enemyRules: [R(1, "Tough", 9), R(2, "Tough", 9)], enemyModels: 1, enemyWeapons: [Gun(3, rules: R(0, "AP", 1)), Blade(3, R(0, "AP", 1))],
            checks:
            [
                ApGrant("Guardian", 0, "\"those hits count as having AP(-1), to a min. of AP(0)\" (against AP(1))"),
                ApGrant("Guardian Boost", 0, "\"enemy hits always count as having AP(-1)\" (against AP(1))"),
                ApGrant("Tear", 4, "\"Against units where most models have Tough(9) or higher, this weapon gets AP(+4)\""),
            ]),
        Book("dark-elf-raiders-harass", "q11la9v8h1heu9ja", "Dark Elf Raiders", 17,
            [R(1, "Harassing"), R(1, "Melee Evasion"), R(1, "Piercing Hunter"), R(2, "Harassing"), R(2, "Harassing Boost"), R(3, "Melee Evasion")],
            weapons: [Gun(2, rules: R(0, "Lacerate")), Blade(2)],
            checks:
            [
                Reposition("Harassing", 3, "\"may move by up to 3\" after shooting or being in melee\""),
                Reposition("Harassing Boost", 6, "\"they may move by up to 6\" from Harassing\""),
                Modifier("Melee Evasion", "hit", -1, "\"Enemies get -1 to hit rolls in melee\"", s => s.Nearest(MeleeSequence) is not null),
                ApGrant("Piercing Hunter", 1, "\"When this model shoots at enemies over 9\" away, its weapons get AP(+1)\""),
                Rerolls("Lacerate", null, [6], successOnly: false, "\"the target must re-roll unmodified Defense results of 6\""),
            ]),
        // Exposes an engine defect: a wound left over when its model dies is carried to the next model and its ignore roll is made again, so a
        // deal-wounds frame rolls more Resistance dice than the block dealt wounds ("roll one die for each").
        Book("havoc-brothers-assault", "7o6om21wxlvvy3hq", "Havoc Brothers", 4,
            [R(1, "Havocbound"), R(1, "Resistance"), R(2, "Havocbound"), R(2, "Havocbound Boost"), R(3, "Resistance")], enemyWeapons: [Gun(3), Blade(3)],
            checks:
            [
                ApGrant("Havocbound", 1, "\"its weapons get AP(+1)\""),
                ApGrant("Havocbound Boost", 1, "\"it always gets AP(+1) from Havocbound\""),
                IgnoreWounds("Resistance", 6, "\"roll one die for each. On a 6+ it is ignored\""),
            ]),
        Book("ratmen-clans-scurry", "hk70l4d471plza00", "Ratmen Clans", 1,
            [R(1, "Scurry"), R(2, "Scurry"), R(2, "Scurry Boost")],
            weapons: [Gun(2, rules: R(0, "Hazardous")), Blade(2)],
            checks:
            [
                Allowance("Scurry", "Scurry Boost", "\"Moves +2\" when using Advance, and +2\" when using Rush/Charge\"", ("advance", 8), ("rush", 14), ("charge", 14)),
                Allowance("Scurry Boost", null, "\"it moves +4\" on Advance and +4\" on Rush/Charge\"", ("advance", 10), ("rush", 16), ("charge", 16)),
                ApGrant("Hazardous", 4, "\"Gets AP(4)\""),
                SelfWounds("Hazardous", "\"this model's unit takes one wound on unmodified rolls of 1 to hit\""),
            ]),
        Book("high-elf-fleets-highborn", "7i7blhft75q9zfdc", "High Elf Fleets", 1,
            [R(1, "Highborn"), R(2, "Highborn"), R(2, "Highborn Boost")],
            weapons: [Gun(1), Blade(2, R(0, "Unwieldy"))],
            checks:
            [
                Allowance("Highborn", "Highborn Boost", "\"Moves +2\" when using Advance, and +2\" when using Rush/Charge\"", ("advance", 8), ("rush", 14), ("charge", 14)),
                Allowance("Highborn Boost", null, "\"it moves +4\" on Advance and +4\" on Rush/Charge\"", ("advance", 10), ("rush", 16), ("charge", 16)),
            ]),
        Book("alien-hives-hive-bond", "w7qor7b2kuifcyvk", "Alien Hives", 18,
            [R(1, "Hive Bond"), R(2, "Hive Bond"), R(2, "Hive Bond Boost"), R(3, "Hive Bond")], enemyWeapons: [Gun(3), Blade(3)],
            checks:
            [
                Modifier("Hive Bond", "morale", 1, "\"+1 to morale test rolls triggered by shooting/melee\""),
                Modifier("Hive Bond Boost", "morale", 2, "\"they get +2 to morale test rolls from Hive Bond\""),
            ]),
        Book("human-defense-force-line", "z65fgu0l29i4lnlu", "Human Defense Force", 25,
            [R(1, "Hold the Line"), R(1, "Rapid Advance"), R(2, "Hold the Line"), R(2, "Hold the Line Boost"), R(3, "Hold the Line")],
            weapons: [Gun(2, rules: R(0, "Crack")), Blade(2)], enemyWeapons: [Gun(3), Blade(3)],
            checks:
            [
                Modifier("Hold the Line", "morale", 1, "\"+1 to morale test rolls triggered by shooting/melee\""),
                Modifier("Hold the Line Boost", "morale", 2, "\"they get +2 to morale test rolls from Hold the Line\""),
                Allowance("Rapid Advance", null, "\"This model moves +4\" when using Advance actions\"", ("advance", 10)),
                PerDieAp("Crack", 2, "\"On unmodified results of 6 to hit, those hits get AP(+2)\""),
            ]),
        Book("infected-colonies-plague-fire", "dnthspt7c0klhmt8", "Infected Colonies", 6,
            [R(1, "Infected"), R(2, "Infected"), R(2, "Infected Boost"), R(3, "Infected")],
            checks:
            [
                ExtraWounds("Infected", "Infected Boost", [1], "\"take 1 extra wound for each unmodified result of 1 that they roll\""),
                ExtraWounds("Infected Boost", null, [1, 2], "\"take extra wounds on failed defense rolls of 1-2\""),
            ]),
        Book("machine-cults-fog", "7el7k3hgy5pb9o9i", "Machine Cults", 7,
            [R(1, "Machine-Fog"), R(1, "Hit & Run Shooter"), R(2, "Machine-Fog"), R(2, "Machine-Fog Boost"), R(3, "Machine-Fog"), R(3, "Hit & Run Shooter")],
            checks:
            [
                Modifier("Machine-Fog", "hit", -1, "\"enemy units get -1 to hit rolls\""),
                Modifier("Machine-Fog Boost", "hit", -1, "\"enemies attacking them always get -1 to hit rolls\""),
                Reposition("Hit & Run Shooter", 3, "\"may move by up to 3\" after shooting\""),
            ]),
        Book("goblin-reclaimers-mischief", "k5T0f2XIfpmPO28h", "Goblin Reclaimers", 1,
            [R(1, "Mischievous"), R(1, "Unpredictable Shooter"), R(2, "Mischievous"), R(2, "Mischievous Boost"), R(3, "Mischievous"), R(3, "Unpredictable Shooter")],
            checks:
            [
                Rerolls("Mischievous", "Mischievous Boost", [6], successOnly: false, "\"targets must re-roll unmodified Defense results of 6\""),
                Rerolls("Mischievous Boost", null, [5, 6], successOnly: true, "\"must re-roll successful unmodified defense results of 5-6\""),
                Unpredictable("Unpredictable Shooter"),
            ]),
        Book("elven-jesters-blink", "nyh41t82jugcdq8m", "Elven Jesters", 1,
            [R(1, "Rapid Blink"), R(1, "Unpredictable Fighter"), R(2, "Rapid Blink"), R(2, "Rapid Blink Boost"), R(3, "Unpredictable Fighter")],
            checks: [RapidBlink("Rapid Blink", 1), RapidBlink("Rapid Blink Boost", 2), Unpredictable("Unpredictable Fighter")]),
        // Exposes a content defect: Scrapper Boost's row re-rolls every unmodified 5 (roll_result eq 5 raw), so under Shatter's AP(+2) a 5 that
        // failed to block is re-rolled, where the rule re-rolls only "successful unmodified defense results of 5-6".
        Book("jackals-scrap", "BKi_hJaJflN8ZorH", "Jackals", 1,
            [R(1, "Scrapper"), R(2, "Scrapper"), R(2, "Scrapper Boost"), R(3, "Scrapper")],
            weapons: [Gun(2, rules: R(0, "Shatter")), Blade(2)], enemyRules: [R(1, "Tough", 3), R(2, "Tough", 3)],
            checks:
            [
                Rerolls("Scrapper", "Scrapper Boost", [6], successOnly: false, "\"targets must re-roll unmodified Defense results of 6\""),
                Rerolls("Scrapper Boost", null, [5, 6], successOnly: true, "\"must re-roll successful unmodified defense results of 5-6\""),
                ApGrant("Shatter", 2, "\"Against units where most models have Tough(3) or higher, this weapon gets AP(+2)\""),
            ]),
        Book("robot-legions-self-repair", "4k5amkxoybdiqotm", "Robot Legions", 14,
            [R(1, "Self-Repair"), R(2, "Self-Repair"), R(2, "Self-Repair Boost"), R(3, "Self-Repair")],
            weapons: [Gun(2, rules: R(0, "Destructive")), Blade(2)], enemyWeapons: [Gun(3), Blade(3)],
            checks:
            [
                IgnoreWounds("Self-Repair", 6, "\"roll one die for each. On a 6+ it is ignored\""),
                IgnoreWounds("Self-Repair Boost", 5, "\"they ignore wounds on rolls of 5-6\""),
                PerDieAp("Destructive", 4, "\"On unmodified results of 6 to hit, those hits get AP(+4)\""),
            ]),
        Book("dwarf-guilds-sturdy", "fk1mkbp8apvltu0z", "Dwarf Guilds", 14,
            [R(1, "Sturdy"), R(2, "Sturdy"), R(2, "Sturdy Boost"), R(3, "Sturdy")],
            weapons: [Gun(2, rules: R(0, "Shred")), Blade(2)],
            checks:
            [
                Modifier("Sturdy", "defense", 1, "\"they get +1 to defense rolls\""),
                Modifier("Sturdy Boost", "defense", 1, "\"they always get +1 to defense rolls from Sturdy\""),
                ExtraWounds("Shred", null, [1], "\"On unmodified results of 1 to block hits, this weapon deals 1 extra wound\""),
            ]),
        Book("dao-union-visors", "z8205ez2boggzs22", "DAO Union", 2,
            [R(1, "Targeting Visor"), R(1, "Shielded"), R(2, "Targeting Visor"), R(2, "Targeting Visor Boost"), R(3, "Targeting Visor"), R(3, "Shielded")],
            checks:
            [
                Modifier("Targeting Visor", "hit", 1, "\"it gets +1 to hit rolls\""),
                Modifier("Targeting Visor Boost", "hit", 1, "\"it always gets +1 to hit rolls when shooting\""),
                Modifier("Shielded", "defense", 1, "\"+1 to defense rolls against hits that are not from spells\""),
            ]),
        Book("change-disciples-shifting", "r6hr29338u4micfw", "Change Disciples", 14,
            [R(1, "Changebound"), R(1, "Bane when Shooting"), R(2, "Changebound"), R(2, "Changebound Boost"), R(3, "Changebound"), R(3, "Bane when Shooting")],
            checks:
            [
                Modifier("Changebound", "hit", -1, "\"enemy units get -1 to hit rolls\""),
                Modifier("Changebound Boost", "hit", -1, "\"enemies attacking them always get -1 to hit rolls\""),
                Rerolls("Bane when Shooting", null, [6], successOnly: false, "\"This model gets Bane when shooting\": p.13 Bane, \"re-roll unmodified Defense results of 6\""),
            ]),
        Book("lust-disciples-reach", "drqw1iswxmuugp3d", "Lust Disciples", 2,
            [R(1, "Lustbound"), R(2, "Lustbound"), R(2, "Lustbound Boost"), R(3, "Lustbound")],
            checks:
            [
                Allowance("Lustbound", "Lustbound Boost", "\"moves +2\" when using Charge actions\"", ("charge", 14)),
                Allowance("Lustbound Boost", null, "\"moves +4\" when using Charge actions\"", ("charge", 16)),
            ]),
        Book("plague-disciples-rot", "jlray7cwf8mvw5sn", "Plague Disciples", 31,
            [R(1, "Plaguebound"), R(1, "Rapid Rush"), R(2, "Plaguebound"), R(2, "Plaguebound Boost"), R(3, "Plaguebound")], enemyWeapons: [Gun(3), Blade(3)],
            checks:
            [
                IgnoreWounds("Plaguebound", 6, "\"roll one die for each. On a 6+ it is ignored\""),
                IgnoreWounds("Plaguebound Boost", 5, "\"they ignore wounds on rolls of 5-6\""),
                Allowance("Rapid Rush", null, "\"This model moves +6\" when using Rush actions\"", ("rush", 18)),
            ]),
        Book("war-disciples-bloodbound", "31xjrm9ivdimkjxp", "War Disciples", 6,
            [R(1, "Warbound"), R(2, "Warbound"), R(2, "Warbound Boost"), R(3, "Warbound")],
            checks:
            [
                ExtraWounds("Warbound", "Warbound Boost", [1], "\"take 1 extra wound for each unmodified result of 1 that they roll\""),
                ExtraWounds("Warbound Boost", null, [1, 2], "\"take extra wounds on failed defense rolls of 1-2\""),
            ]),
    ];

    static ScenarioWeapon Gun(int attacks = 1, int range = 24, params ScenarioRule[] rules) =>
        new("Rifle", new Dictionary<string, string> { ["Range"] = $"{range}\"", ["Attacks"] = $"{attacks}" }, rules);
    static ScenarioWeapon Blade(int attacks = 1, params ScenarioRule[] rules) =>
        new("Blade", new Dictionary<string, string> { ["Attacks"] = $"{attacks}" }, rules);
    static ScenarioRule R(int unit, string name, int? parameter = null) => new(unit, name, parameter);
    static Dictionary<string, string> Stats(int quality = 4, int defense = 4) => new() { ["Quality"] = $"{quality}+", ["Defense"] = $"{defense}+" };

    /// <summary>Every model's move ranks its legal destinations by the nearest enemy model on the table, so the armies meet.</summary>
    static Point2? Closing(Guid modelId, PolicyView view)
    {
        if (view.PlacedOf(modelId) is not { } placed || view.TeamOf(view.ModelOf(modelId).UnitId) is not { } team) return null;
        var at = placed.Placement.Position.Horizontal;
        return view.Projection.Models.Values
            .Where(m => m.Location is OnTable && view.TeamOf(m.UnitId) != team)
            .Select(m => (Point2?)((OnTable)m.Location).Placement.Position.Horizontal)
            .MinBy(p => p!.Value.DistanceTo(at));
    }

    /// <summary>Low ruins (a cover area) across the first army's zone, so its units start within 1" of terrain (Grounded Reinforcement).</summary>
    static ScenarioBuilder Ruins(ScenarioBuilder b) => b
        .Terrain("ruin", new Point3(18, 30, 0), volumes:
            [new Volume(VolumeKind.Area, Footprint.Rect(34, 10), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Cover, null)]);

    /// <summary>
    /// The first army, one unit of three models per unit index the rules name, carries the book's rules and weapons; the second, two units, is
    /// the opponent. Covers is every rule the first army carries, on its models or its weapons. Table 36" x 36", four rounds.
    /// </summary>
    static TestGame Book(string name, string book, string armyName, ulong seed, IReadOnlyList<ScenarioRule> rules,
        IReadOnlyList<ScenarioWeapon>? weapons = null, IReadOnlyList<ScenarioRule>? enemyRules = null, Dictionary<string, string>? enemyStats = null,
        int enemyModels = 3, IReadOnlyList<ScenarioWeapon>? enemyWeapons = null, Func<ScenarioBuilder, ScenarioBuilder>? board = null,
        IReadOnlyList<GameCheck>? checks = null)
    {
        weapons ??= [Gun(2), Blade(2)];
        var builder = new ScenarioBuilder(name, seed).Table(36, 36).RoundCap(4).Seeking(Closing)
            .Army(armyName, rules.Max(r => r.Unit), 3, modelRules: rules, weapons: weapons, armyBook: book)
            .Army("Opponents", 2, enemyModels, modelRules: enemyRules, weapons: enemyWeapons ?? [Gun(2), Blade(2)], unitStats: enemyStats);
        var covers = rules.Select(r => r.Name).Concat(weapons.SelectMany(w => w.Rules).Select(r => r.Name)).Distinct().ToList();
        return new TestGame(name, ArmyPrefix + book, (board ?? (x => x))(builder).Build(), covers, [], checks ?? []);
    }

    const string MeleeSequence = "melee";

    /// <summary>A completed step, the sequence frame it ran in with that frame's ancestors (nearest first), and the rerolls made of its rolls.</summary>
    sealed record LoggedStep(int Index, StepCompleted Step, SequenceStarted Frame, IReadOnlyList<SequenceStarted> Ancestors, List<DiceRerolled> Rerolls)
    {
        public bool Lists(string entry) => Step.Applied?.Contains(entry) == true;
        public int Value(string key) => Step.Values?.GetValueOrDefault(key) ?? 0;
        public IEnumerable<RollRecord> Rolls(string kind) => Step.Rolls.Where(r => r.Roll == kind);
        public SequenceStarted? Nearest(params string[] sequences) => Ancestors.Prepend(Frame).FirstOrDefault(f => sequences.Contains(f.Sequence));
    }

    sealed record LoggedAsk(Ask Ask, SequenceStarted? Frame, IReadOnlyList<SequenceStarted> Ancestors);

    /// <summary>The effective log's steps and Asks, each with its frame: sequences nest, so a frame's parent is the one open when it started.</summary>
    sealed record ArmyLog(IReadOnlyList<LoggedStep> Steps, IReadOnlyList<LoggedAsk> Asks)
    {
        public static ArmyLog Of(IReadOnlyList<GameEvent> log)
        {
            var frames = new Dictionary<Guid, SequenceStarted>();
            var parents = new Dictionary<Guid, Guid?>();
            var open = new List<Guid>();
            var steps = new List<LoggedStep>();
            var asks = new List<LoggedAsk>();
            IReadOnlyList<SequenceStarted> Ancestors(Guid frame)
            {
                var list = new List<SequenceStarted>();
                for (var p = parents.GetValueOrDefault(frame); p is { } id; p = parents.GetValueOrDefault(id)) list.Add(frames[id]);
                return list;
            }
            foreach (var e in EventApplier.EffectiveLog(log))
                switch (e.Payload)
                {
                    case SequenceStarted s:
                        frames[s.FrameId] = s;
                        parents[s.FrameId] = open.Count > 0 ? open[^1] : null;
                        open.Add(s.FrameId);
                        break;
                    case SequenceFinished f: open.Remove(f.FrameId); break;
                    case SequenceAbandoned a: open.Remove(a.FrameId); break;
                    case StepCompleted s when frames.TryGetValue(s.FrameId, out var frame):
                        steps.Add(new LoggedStep(steps.Count, s, frame, Ancestors(s.FrameId), []));
                        break;
                    case DiceRerolled r:
                        steps.LastOrDefault(s => s.Step.Rolls.Any(x => x.Id == r.RollId))?.Rerolls.Add(r);
                        break;
                    case AskOpened a:
                        var asked = a.Ask.FrameId is { } id && frames.TryGetValue(id, out var f2) ? f2 : null;
                        asks.Add(new LoggedAsk(a.Ask, asked, asked is null ? [] : Ancestors(asked.FrameId)));
                        break;
                }
            return new ArmyLog(steps, asks);
        }
    }

    /// <summary>
    /// An exact check over the occasions it finds: each yields null when the rule's literal held, else the reason. A check with no occasion
    /// fails, so a seed that never brings the rule's number into play cannot pass for it.
    /// </summary>
    static GameCheck Exact(string entry, string quote, Func<SuiteGameRun, ArmyLog, IEnumerable<string?>> occasions) =>
        new($"{entry}: {quote}", run =>
        {
            var verdicts = occasions(run, ArmyLog.Of(run.EffectiveLog)).ToList();
            if (verdicts.Count == 0) return "no occasion in the log";
            var failures = verdicts.OfType<string>().Distinct().ToList();
            return failures.Count == 0 ? null : $"{failures.Count} of {verdicts.Count} occasions: {string.Join("; ", failures.Take(3))}";
        }, [entry]);

    static string Faces(IEnumerable<Die> dice) => $"[{string.Join(" ", dice.Select(d => d.Raw))}]";
    static int Count(IEnumerable<RollRecord> rolls, IReadOnlyCollection<int> faces, bool successOnly = true) =>
        rolls.SelectMany(r => r.Dice).Count(d => faces.Contains(d.Raw) && (d.Success || !successOnly));

    /// <summary>Whether the unit's models carry the entry in the army definition (what the scenario gave them, not a rules reading).</summary>
    static bool Holds(SuiteGameRun run, Guid? unitId, string entry) =>
        unitId is { } id && run.Final.Units.TryGetValue(id, out var unit) && unit.Definition.Models.Any(m => m.Properties.Any(p => p.Name == entry));

    /// <summary>A modify_roll: every roll it modified is of that kind, carries it at the stated size, and its dice are raw plus the roll's modifiers (p.5).</summary>
    static GameCheck Modifier(string entry, string roll, int amount, string quote, Func<LoggedStep, bool>? scope = null) =>
        Exact(entry, $"{quote}: {roll} {amount:+0;-0}", (_, log) =>
            from s in log.Steps
            from r in s.Step.Rolls
            let mine = r.Modifiers.Where(m => m.Source == entry).ToList()
            where mine.Count > 0
            let total = r.Modifiers.Sum(m => m.Amount)
            select r.Roll != roll ? $"on a {r.Roll} roll"
                : mine.Any(m => m.Amount != amount) ? $"{string.Join(",", mine.Select(m => m.Amount))} on {r.Roll}"
                : r.Dice.FirstOrDefault(d => d.Modified != d.Raw + total) is { } d ? $"die {d.Raw} became {d.Modified} under {total:+0;-0}"
                : scope?.Invoke(s) == false ? $"in {s.Frame.Sequence}, outside where the rule applies"
                : null);

    /// <summary>add_hit (hits): the roll-to-hit's extra hits equal its successful dice showing the faces. Base checks skip the Boost's steps.</summary>
    static GameCheck ExtraHits(string entry, string? boost, IReadOnlyCollection<int> faces, string quote) =>
        Exact(entry, quote, (_, log) =>
            from s in log.Steps
            where s.Step.Step == "roll-to-hit" && s.Lists(entry) && (boost is null || !s.Lists(boost))
            let expected = Count(s.Rolls("hit"), faces)
            select s.Value("extra_hits") == expected ? null : $"{s.Value("extra_hits")} extra hits from {Faces(s.Rolls("hit").SelectMany(r => r.Dice))}, expected {expected}");

    /// <summary>add_hit (attacks): one added die per successful pool die showing the faces, rolled once in the same step, and the added dice add none.</summary>
    static GameCheck ExtraAttacks(string entry, string? boost, IReadOnlyCollection<int> faces, string quote) =>
        Exact(entry, quote, (_, log) =>
            from s in log.Steps
            where s.Step.Step == "roll-to-hit" && s.Lists(entry) && (boost is null || !s.Lists(boost))
            let hits = s.Rolls("hit").ToList()
            let expected = Count(hits.Take(1), faces)
            let added = hits.Skip(1).Sum(r => r.Dice.Count)
            select hits.Count > 2 ? $"{hits.Count} hit rolls: the added attacks generated more"
                : added == expected ? null : $"{added} added attacks from {Faces(hits[0].Dice)}, expected {expected}");

    /// <summary>add_hit (wounds) on the defender's dice: the block's extra wounds equal its defense dice showing the faces.</summary>
    static GameCheck ExtraWounds(string entry, string? boost, IReadOnlyCollection<int> faces, string quote) =>
        Exact(entry, quote, (_, log) =>
            from s in log.Steps
            where s.Step.Step == "roll-to-block" && s.Lists(entry) && (boost is null || !s.Lists(boost))
            let expected = Count(s.Rolls("defense"), faces, successOnly: false)
            select s.Value("extra_wounds") == expected ? null : $"{s.Value("extra_wounds")} extra wounds from {Faces(s.Rolls("defense").SelectMany(r => r.Dice))}, expected {expected}");

    /// <summary>Hazardous's wound: the roll-to-hit's self wounds equal its unmodified 1s.</summary>
    static GameCheck SelfWounds(string entry, string quote) =>
        Exact(entry, quote, (_, log) =>
            from s in log.Steps
            where s.Step.Step == "roll-to-hit" && s.Lists(entry)
            let expected = Count(s.Rolls("hit"), [1], successOnly: false)
            select s.Value("self_wounds") == expected ? null : $"{s.Value("self_wounds")} self wounds from {Faces(s.Rolls("hit").SelectMany(r => r.Dice))}, expected {expected}");

    /// <summary>A granted or changed AP: every block that lists the entry rolls defense under AP of the stated size (p.13 AP: "-X to Defense rolls"), and under no other.</summary>
    static GameCheck ApGrant(string entry, int ap, string quote) =>
        Exact(entry, $"{quote}: AP {-ap:+0;-0}", (_, log) =>
            from s in log.Steps
            where s.Step.Step == "roll-to-block" && s.Lists(entry)
            let aps = s.Rolls("defense").SelectMany(r => r.Modifiers).Where(m => m.Source == "AP").Select(m => m.Amount).ToList()
            select aps.Contains(-ap) && aps.All(a => a == -ap) ? null : $"defense under AP [{string.Join(",", aps)}]");

    /// <summary>A per-die AP grant on the hit's 6: the roll-to-hit grants it once per unmodified 6, and the block that follows rolls that many dice under it.</summary>
    static GameCheck PerDieAp(string entry, int ap, string quote) =>
        Exact(entry, quote, (_, log) =>
            from s in log.Steps
            where s.Step.Step == "roll-to-hit" && s.Lists(entry)
            let sixes = Count(s.Rolls("hit"), [6])
            let block = log.Steps.Skip(s.Index + 1).FirstOrDefault(b => b.Step.Step == "roll-to-block")
            let under = block?.Rolls("defense").Where(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -ap)).Sum(r => r.Dice.Count)
            select s.Value($"granted_AP_{ap}") != sixes ? $"AP({ap}) granted {s.Value($"granted_AP_{ap}")} times for {Faces(s.Rolls("hit").SelectMany(r => r.Dice))}"
                : under != sixes ? $"{under} defense dice under AP -{ap} for {sixes} sixes" : null);

    /// <summary>A forced re-roll of the defender's dice: exactly the dice showing the faces (successful ones only when the rule says so) are re-rolled.</summary>
    static GameCheck Rerolls(string entry, string? boost, IReadOnlyCollection<int> faces, bool successOnly, string quote) =>
        Exact(entry, quote, (_, log) =>
            from s in log.Steps
            where s.Step.Step == "roll-to-block" && s.Lists(entry) && (boost is null || !s.Lists(boost))
            from r in s.Rolls("defense")
            let expected = r.Dice.Select((d, i) => (d, i)).Where(x => faces.Contains(x.d.Raw) && (x.d.Success || !successOnly)).Select(x => x.i).ToList()
            let rerolled = s.Rerolls.Where(x => x.RollId == r.Id).SelectMany(x => x.Replaced).Select(x => x.Index).Order().ToList()
            select expected.SequenceEqual(rerolled) ? null : $"re-rolled dice [{string.Join(",", rerolled)}] of {Faces(r.Dice)}, expected [{string.Join(",", expected)}]");

    /// <summary>
    /// A roll to ignore each wound, per deal-wounds frame: one die for each wound the block dealt (its wounds and extra wounds), each ignored
    /// at the stated number, and the wounds dealt (with any left over when the unit is gone) are the dice that failed.
    /// </summary>
    static GameCheck IgnoreWounds(string entry, int threshold, string quote) =>
        Exact(entry, quote, (_, log) =>
            from frame in log.Steps.Where(s => s.Step.Step == "assign").GroupBy(s => s.Frame.FrameId)
            let rolls = frame.SelectMany(s => s.Rolls(entry)).ToList()
            where rolls.Count > 0
            let parent = frame.First().Ancestors.FirstOrDefault()?.FrameId
            let block = log.Steps.LastOrDefault(s => s.Frame.FrameId == parent && s.Step.Step == "roll-to-block")
            let incoming = block is null ? 0 : block.Value("wounds") + block.Value("extra_wounds")
            let dice = rolls.SelectMany(r => r.Dice).ToList()
            let dealt = frame.Last().Value("wounds_dealt") + frame.Last().Value("wounds_left")
            select rolls.Any(r => r.Threshold != threshold) ? $"threshold {string.Join(",", rolls.Select(r => r.Threshold))}"
                : dice.FirstOrDefault(d => d.Success != d.Raw >= threshold) is { } d ? $"{d.Raw} {(d.Success ? "ignored" : "not ignored")}"
                : dice.Count != incoming ? $"{dice.Count} dice {Faces(dice)} for {incoming} wounds"
                : dealt != dice.Count(d => !d.Success) ? $"{dealt} wounds dealt after {Faces(dice)}"
                : null);

    /// <summary>
    /// A move bonus: the first move Ask of each action of a unit holding the entry (and not the Boost that replaces it) allows the action's
    /// p.7 distance ("Advance - Moves 6", "Rush - Moves 12", "Charge - Moves 12") plus the bonus, as given per action.
    /// </summary>
    static GameCheck Allowance(string entry, string? boost, string quote, params (string Action, int Inches)[] allowed) =>
        Exact(entry, $"{quote}: {string.Join(", ", allowed.Select(a => $"{a.Action} {a.Inches}\""))}", (run, log) =>
            from a in log.Asks
            where a.Ask.Question == "move" && a.Frame is { } f && allowed.Any(x => x.Action == f.Sequence)
                && Holds(run, f.Context.ActorUnitId, entry) && (boost is null || !Holds(run, f.Context.ActorUnitId, boost))
            group a by a.Frame!.FrameId into frame
            let first = frame.First()
            let inches = allowed.Single(x => x.Action == first.Frame!.Sequence).Inches
            select first.Ask.Legal is MoveRegion m && m.Allowance == inches ? null
                : $"{first.Frame!.Sequence} allowed {(first.Ask.Legal as MoveRegion)?.Allowance}\"");

    /// <summary>A move after attacking: each of the entry's reposition moves allows the stated distance.</summary>
    static GameCheck Reposition(string entry, int inches, string quote) =>
        Exact(entry, quote, (_, log) =>
            from a in log.Asks
            where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
            select a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");

    /// <summary>Teleport: "fully within 3" of its position when using Advance/Charge actions, or fully within 6" ... when using Rush actions".</summary>
    static GameCheck Teleport() =>
        Exact("Teleport", "\"within 3\" ... when using Advance/Charge actions, or fully within 6\" ... when using Rush actions\"", (_, log) =>
            from a in log.Asks
            where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == "Teleport"
            let action = a.Ancestors.FirstOrDefault(x => x.Sequence is "advance" or "charge" or "rush")?.Sequence
            let inches = action == "rush" ? 6 : 3
            select action is null ? "no action above the placement"
                : a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"{action} allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");

    /// <summary>Rapid Blink: the placement allows the sum of the entry's dice, each "a D6 and halve the result, rounding up" (p.4).</summary>
    static GameCheck RapidBlink(string entry, int dice) =>
        Exact(entry, $"\"anywhere fully within {(dice == 1 ? "D3" : "2D3")}\"", (_, log) =>
            from a in log.Asks
            where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
            let roll = log.Steps.LastOrDefault(s => s.Frame.FrameId == a.Frame!.FrameId && s.Rolls(entry).Any())?.Rolls(entry).Single()
            let inches = roll?.Dice.Sum(d => (d.Raw + 1) / 2)
            select roll is null ? "no distance roll" : roll.Dice.Count != dice ? $"{roll.Dice.Count} dice"
                : a.Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\" after {Faces(roll.Dice)}");

    /// <summary>
    /// Unpredictable: one die at the attack's start for the unit; "on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead",
    /// read on that unit's hit rolls and blocks under the same shooting or melee.
    /// </summary>
    static GameCheck Unpredictable(string entry) =>
        Exact(entry, "\"on a 1-3 they get AP(+1), and on a 4-6 they get +1 to hit rolls instead\"", (_, log) =>
            from start in log.Steps
            where start.Step.Step is "attack-start" or "melee-start"
            from key in (start.Step.Values ?? new Dictionary<string, int>()).Keys
            where key.StartsWith("face_") && key.EndsWith($"_{entry}")
            let unit = Guid.ParseExact(key["face_".Length..key.IndexOf('_', "face_".Length)], "N")
            let face = start.Step.Values![key]
            let attacks = log.Steps.Where(s => s.Ancestors.Any(x => x.FrameId == start.Frame.FrameId) && s.Nearest("attack-sequence")?.Context.ActorUnitId == unit).ToList()
            // p.9: a fatigued unit "only hit[s] on unmodified rolls of 6 in melee", so no modifier reaches its hit roll.
            let hits = attacks.Where(s => s.Step.Step == "roll-to-hit").SelectMany(s => s.Rolls("hit")).Where(r => !(r.Threshold == 6 && r.Modifiers.Count == 0)).ToList()
            let blocks = attacks.Where(s => s.Step.Step == "roll-to-block").SelectMany(s => s.Rolls("defense")).ToList()
            where hits.Count > 0
            select face >= 4
                ? hits.All(r => r.Modifiers.Any(m => m.Source == entry && m.Amount == 1)) ? null : $"face {face}: a hit roll without +1"
                : hits.Any(r => r.Modifiers.Any(m => m.Source == entry)) ? $"face {face}: a hit roll with +1"
                : blocks.All(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -1)) ? null : $"face {face}: a block without AP -1");
}
