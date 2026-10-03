using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

public sealed record CompiledScenario(
    Scenario Scenario, GameInfo Info, Board Board, PropertySet Properties,
    IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> ParticipantIds,
    IReadOnlyDictionary<Guid, Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)
{
    public Guid ParticipantFor(Guid teamId) => TeamOfParticipant.First(kv => kv.Value == teamId).Key;
}

/// <summary>
/// A scenario becomes exactly what Hydrator would have read: a GameInfo at playing, a Board over the
/// setup snapshot and one ArmySnapshot per participant. Ids are ordinary v7 GUIDs and differ between
/// runs; what a seed fixes is GameInfo.RngSeed, which is the value the game.rng_seed column holds and
/// therefore the value GameState.RngSeed takes.
/// </summary>
public static class ScenarioCompiler
{
    // Declared first: static initializers run in textual order and CoreProperties reads it.
    static PropertySetDocument CoreDocument { get; } =
        PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId);

    /// <summary>The shipped Grimdark Future set, through the production path: the game server's composed embedded document, every army book included.</summary>
    public static PropertySet CoreProperties { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

    public static CompiledScenario Compile(Scenario s)
    {
        var teams = s.Armies.Select((_, i) => new Team(Guid.CreateVersion7(), i + 1)).ToList();
        var participants = teams.Select((t, i) =>
            new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Sim {i + 1}", t.Id, null)).ToList();
        // The recorder: a participant with no team, which is what a spectator is (whole-design review ruling 5).
        // It is never Asked, and its filtered event stream is what the driver's projection and the recording are built from.
        var recorder = new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), "Recorder", null, null);
        participants.Add(recorder);

        var snapshot = new TableState(
            Guid.CreateVersion7(), s.Name, s.Table, VisualRef.OfBuiltin("grass"),
            s.Terrain.Select((t, i) => new TerrainPlacement(
                Guid.CreateVersion7(), VisualRef.OfBuiltin(t.Mesh), t.Position, new Yaw(t.Yaw), t.Scale,
                t.Volumes.Select((v, j) => new TerrainVolume(Guid.CreateVersion7(), v)).ToList())).ToList(),
            s.Markers.Select((m, i) => new Marker(
                Guid.CreateVersion7(), m.Kind, m.Position, new Yaw(0), m.Diameter, m.Label, VisualRef.OfBuiltin("objective"))).ToList(),
            s.ZoneShapes.Select((shape, i) => new DeploymentZone(Guid.CreateVersion7(), shape, i + 1)).ToList());

        var armies = s.Armies.Select((a, i) => new ArmyInPlay(participants[i].Id, BuildArmy(i + 1, a))).ToList();

        // The properties the engine is handed and the hash the game row names must be one set (hydration loads by hash).
        var props = CoreProperties;

        // RngSeed is positional and last among the scalars (contracts plan → GameInfo), so a game cannot
        // be built without a seed. This is the one value a scenario's seed fixes.
        var info = new GameInfo(
            Guid.CreateVersion7(), s.Name, GameStatus.Playing, participants[0].AccountId,
            GrimdarkFutureRules.RulesId, props.ContentHash, s.Shape, s.Enforcement,
            WaitsBetweenSteps: false, unchecked((long)s.Seed), teams, participants);

        return new CompiledScenario(s, info, new Board(snapshot, armies), props,
            teams.Select(t => t.Id).ToList(), participants.Where(p => p.TeamId is not null).Select(p => p.Id).ToList(),
            participants.Where(p => p.TeamId is not null).ToDictionary(p => p.Id, p => p.TeamId!.Value),
            snapshot, recorder.Id);
    }

    private static ArmySnapshot BuildArmy(int team, ScenarioArmy a)
    {
        var units = new List<Unit>();
        for (var u = 1; u <= a.Units; u++)
        {
            var modelRules = a.ModelRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList();
            // Weapon ids are per model: the engine addresses weapons by id across the unit.
            var models = Enumerable.Range(1, a.ModelsPerUnit).Select(m => new ModelSlot(
                Guid.CreateVersion7(), m, $"{a.Name} {u}.{m}",
                new Dictionary<string, string>(), modelRules, Base.Circle(1.26), 1.5, null,
                a.Weapons.Select(w => new Weapon(Guid.CreateVersion7(), w.Name,
                    w.Stats, w.Rules.Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList())).ToList(), a.ArmyBook)).ToList();
            units.Add(new Unit(Guid.CreateVersion7(), u, $"{a.Name} unit {u}", a.UnitStats,
                a.UnitRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList(), models));
        }
        return new ArmySnapshot(Guid.CreateVersion7(), a.Name, "grimdark-future", units);
    }
}
