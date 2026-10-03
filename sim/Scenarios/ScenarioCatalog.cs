using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

/// <summary>The named scenarios the console runs. Regression scripts are added here beside the soak ones.</summary>
public static class ScenarioCatalog
{
    private static readonly ScenarioWeapon Rifle = new("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, []);
    private static readonly ScenarioWeapon HandWeapon = new("Hand Weapon", new Dictionary<string, string> { ["Attacks"] = "1" }, []);

    private static readonly Dictionary<string, Func<Scenario>> Factories = new(StringComparer.Ordinal)
    {
        ["skirmish"] = () => new ScenarioBuilder("skirmish", seed: 1)
            .Army("Red", units: 3, modelsPerUnit: 3)
            .Army("Blue", units: 3, modelsPerUnit: 3)
            .Build(),

        ["terrain"] = () => new ScenarioBuilder("terrain", seed: 1)
            .Army("Red", 3, 3).Army("Blue", 3, 3)
            .Terrain("ruin", new Point3(36, 24, 0), volumes:
                [new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 3,
                    TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, null)])
            .Terrain("forest", new Point3(18, 30, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Circle(4), new Point2(0, 0), new Yaw(0), 0, 2,
                    TerrainDeclarations.Difficult | TerrainDeclarations.Cover, null)])
            .Build(),

        ["player-turn"] = () => new ScenarioBuilder("player-turn", seed: 1)
            .Shape(SchedulerShape.PlayerTurn).Army("Red", 3, 3).Army("Blue", 3, 3).Build(),

        ["free-for-all"] = () => new ScenarioBuilder("free-for-all", seed: 1)
            .Army("Red", 2, 2).Army("Blue", 2, 2).Army("Green", 2, 2)
            .Zones(ZoneStyles.FreeForAll).Build(),

        ["stalemate"] = () => new ScenarioBuilder("stalemate", seed: 1)
            .RoundCap(3).Army("Red", 1, 1).Army("Blue", 1, 1).Build(),

        // The round-4 end (GF p.7) under each setting. One unit a side keeps six rounds cheap; a wipe-out no longer ends a run (p.6),
        // so the outcome does not depend on the seed.
        ["final-round"] = () => new ScenarioBuilder("final-round", seed: 1)
            .RoundCap(6).Army("Red", 1, 1).Army("Blue", 1, 1)
            .Expect(new Expectation.Outcome("ended"), new Expectation.ReachedRound(4), new Expectation.EventOfKind("game_ended"))
            .Build(),

        ["final-round-warn"] = () => new ScenarioBuilder("final-round-warn", seed: 1)
            .Enforcing(Enforcement.Warn).RoundCap(6).Army("Red", 1, 1).Army("Blue", 1, 1)
            .Expect(new Expectation.Outcome("round_limit"), new Expectation.NoEventOfKind("game_ended"), new Expectation.Warnings("round_started", 2))
            .Build(),

        ["final-round-silent"] = () => new ScenarioBuilder("final-round-silent", seed: 1)
            .Enforcing(Enforcement.Silent).RoundCap(6).Army("Red", 1, 1).Army("Blue", 1, 1)
            .Expect(new Expectation.Outcome("round_limit"), new Expectation.NoEventOfKind("game_ended"), new Expectation.Warnings("round_started", 0))
            .Build(),

        // Casting is not a mode: a spell is cast inside an ordinary activation. This preset exists only because none of the
        // other scenarios fields a caster. Both armies are Human Defense Force (z65fgu0l29i4lnlu), whose six spells cover a unit-rule
        // grant, an "against" grant, a speed grant, unit and model damage, and one to three targets (spell library spec → Testing).
        ["casting"] = () => new ScenarioBuilder("casting", seed: 1)
            .Army("Red", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)], armyBook: "z65fgu0l29i4lnlu")
            .Army("Blue", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)], armyBook: "z65fgu0l29i4lnlu")
            .Expect(new Expectation.AskAsked("cast"))
            .Build(),

        // Every army-rule hook in one game (army-rule hooks spec -> Testing): moves after melee and shooting, a placement before attacking and at
        // activation, shooting after Rush, a die at the attack's start, extra attacks, and the near-terrain bonus, on the terrain board.
        ["army-rules"] = () => new ScenarioBuilder("army-rules", seed: 1)
            .Army("Red", 4, 3,
                modelRules: [new ScenarioRule(1, "Hit & Run Fighter"), new ScenarioRule(2, "Teleport"), new ScenarioRule(3, "Quick Shot"), new ScenarioRule(4, "Rapid Blink")],
                weapons: [Rifle, HandWeapon])
            .Army("Blue", 4, 3,
                modelRules: [new ScenarioRule(1, "Hit & Run Shooter"), new ScenarioRule(2, "Unpredictable Fighter"), new ScenarioRule(3, "Primal"), new ScenarioRule(4, "Grounded Reinforcement")],
                weapons: [Rifle, HandWeapon])
            .Terrain("ruin", new Point3(36, 24, 0), volumes:
                [new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, null)])
            .Expect(new Expectation.AskAsked("react"))
            .Build(),

        // The core mission scored (p.6): three markers across the middle, one deep in Red's frontline zone. Ends after round 4 under strict
        // with the team holding most markers named on game_ended.
        ["objectives"] = () => new ScenarioBuilder("objectives", seed: 1)
            .Army("Red", 3, 3).Army("Blue", 3, 3)
            .Marker(MarkerKind.Objective, new Point3(18, 24, 0), label: "West")
            .Marker(MarkerKind.Objective, new Point3(36, 24, 0), label: "Centre")
            .Marker(MarkerKind.Objective, new Point3(54, 24, 0), label: "East")
            .Marker(MarkerKind.Objective, new Point3(36, 4, 0), label: "Red home")
            .Expect(new Expectation.Outcome("ended"), new Expectation.ReachedRound(4), new Expectation.EventOfKind("round_ended"), new Expectation.Won(2))
            .Build(),
    };

    public static IReadOnlyList<string> Names { get; } = Factories.Keys.Order().ToList();
    public static Scenario Get(string name) =>
        Factories.TryGetValue(name, out var f) ? f() : throw new KeyNotFoundException($"no scenario '{name}'; known: {string.Join(", ", Names)}");
    public static bool TryGet(string name, out Scenario scenario)
    {
        if (Factories.TryGetValue(name, out var f)) { scenario = f(); return true; }
        scenario = null!; return false;
    }
}
