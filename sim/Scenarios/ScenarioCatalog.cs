using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

/// <summary>The named scenarios the console runs. Regression scripts are added here beside the soak ones.</summary>
public static class ScenarioCatalog
{
    /// <summary>One test spell, the shape of a damage spell: an enemy unit in range takes hits. Authored as JSON and parsed
    /// through the production path, so the sim declares a spell exactly as a content document would. Never shipped.</summary>
    static readonly EntryDocument Bolt = PropertiesJson.Parse(
        """
        { "name": "Sim spells", "gameRules": "grimdark-future", "stats": [], "actions": [], "rolls": [], "entries": [
          { "name": "Bolt", "kind": "spell", "cost": 2, "faction": "Sim Book",
            "ruleText": "Target enemy unit within 24\" takes 3 hits with AP(1).",
            "targeting": { "side": "enemy", "grain": "unit", "count": 1, "range": 24 },
            "effects": [ { "subject": "self", "ordinal": 10, "kind": "add_hit", "amount": 3, "conditions": [],
              "children": [ { "subject": "self", "ordinal": 20, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [] } ] } ] } ] }
        """).Entries.Single();

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
        // other five fields a caster, so nothing else opens casting's two player decisions in a whole-game run — the token
        // spend map (p.13), and the caster spending its own leftover tokens on its own roll.
        ["casting"] = () => new ScenarioBuilder("casting", seed: 1)
            .Spells(Bolt)
            .Army("Red", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)])
            .Army("Blue", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)])
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
