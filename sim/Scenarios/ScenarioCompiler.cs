using System.Collections.Concurrent;
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

public sealed record CompiledScenario(
    Scenario Scenario, GameInfo Info, Board Board, PropertySet Properties,
    IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> ParticipantIds,
    IReadOnlyDictionary<Guid, Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)
{
    /// <summary>Side Battle's legs under the game's rule set: what the server sends in Welcome.</summary>
    public ZoneLegs Zones => GameRulesRegistry.Resolve(Info.GameRules).Zones;

    public Guid ParticipantFor(Guid teamId) => TeamOfParticipant.First(kv => kv.Value == teamId).Key;

    /// <summary>The participants a driver connects: every team's, or under self-play the one account's first row, whose connection plays every team.</summary>
    public IReadOnlyList<Guid> Connected => Scenario.SelfPlay ? [ParticipantIds[0]] : ParticipantIds;

    /// <summary>The participant whose connection sends for participantId: itself, or under self-play the one connection.</summary>
    public Guid ConnectionFor(Guid participantId) => Scenario.SelfPlay ? ParticipantIds[0] : participantId;

    /// <summary>The credential index of participant i: its own, or under self-play the first's for every team's row.</summary>
    public int CredentialOf(int participant) => Scenario.SelfPlay && Info.Participants[participant].TeamId is not null ? 0 : participant;

    /// <summary>The unit condition a player sets from the menu to set a unit aside before deployment (the ruleset's name); RunLoop sends it for Scenario's set-aside units.</summary>
    public string SetAsideCondition { get; init; } = GfNames.SetAside;
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
    /// <summary>The embedded core documents, by rules id.</summary>
    public static IReadOnlyDictionary<string, PropertySetDocument> CoreDocuments { get; } =
        PropertiesSeeder.EmbeddedDocuments().ToDictionary(d => d.GameRules);

    static readonly ConcurrentDictionary<string, PropertySet> Cache = new();

    /// <summary>An edition's shipped set, through the production path: the game server's composed embedded document, every army book included.</summary>
    public static PropertySet CorePropertiesOf(string rulesId) =>
        Cache.GetOrAdd(rulesId, id => PropertiesJson.ToSet(CoreDocuments[id], Guid.CreateVersion7));

    /// <summary>The Grimdark Future 3.5.1 set; every existing game stays on it.</summary>
    public static PropertySet CoreProperties { get; } = CorePropertiesOf(RuleSets.Gf351);

    public static CompiledScenario Compile(Scenario s)
    {
        // A team game (rulebook p.50) seats several armies on one team: one participant per army, on the team its Team names.
        var ordinals = s.Armies.Select((a, i) => a.Team ?? i + 1).ToList();
        var teams = Enumerable.Range(1, ordinals.Max()).Select(o => new Team(Guid.CreateVersion7(), o)).ToList();
        // Self-play: every team's row belongs to one account, the first's; the rows are still one per team, each with its army.
        // The server binds a self-play socket to the account's lowest participant id (`order by id`); v7 ids made in the same
        // millisecond are random past the timestamp, so they are sorted in uuid order to make the first army's row that one.
        var account = Guid.CreateVersion7();
        var selfPlayIds = s.Armies.Select(_ => Guid.CreateVersion7()).OrderBy(g => g.ToString(), StringComparer.Ordinal).ToList();
        var participants = s.Armies.Select((a, i) => s.SelfPlay
            ? new Participant(selfPlayIds[i], account, "Sim 1", teams[ordinals[i] - 1].Id, null, a.Name)
            : new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Sim {i + 1}", teams[ordinals[i] - 1].Id, null, a.Name)).ToList();
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

        var armies = s.Armies.Select((a, i) => new ArmyInPlay(participants[i].Id, BuildArmy(a, RuleSets.EditionOf(s.Rules)))).ToList();

        // The properties the engine is handed and the hash the game row names must be one set (hydration loads by hash).
        var props = CorePropertiesOf(s.Rules);

        // RngSeed is positional and last among the scalars (contracts plan → GameInfo), so a game cannot
        // be built without a seed. This is the one value a scenario's seed fixes.
        var info = new GameInfo(
            Guid.CreateVersion7(), s.Name, GameStatus.Playing, participants[0].AccountId,
            s.Rules, props.ContentHash, s.Shape, s.Enforcement,
            WaitsBetweenSteps: s.WaitsBetweenSteps, unchecked((long)s.Seed), teams, participants) { OptionalRules = s.OptionalRules, RuleSet = props.Name };

        return new CompiledScenario(s, info, new Board(snapshot, armies), props,
            teams.Select(t => t.Id).ToList(), participants.Where(p => p.TeamId is not null).Select(p => p.Id).ToList(),
            participants.Where(p => p.TeamId is not null).ToDictionary(p => p.Id, p => p.TeamId!.Value),
            snapshot, recorder.Id);
    }

    private static ArmySnapshot BuildArmy(ScenarioArmy a, string edition)
    {
        var units = new List<Unit>();
        for (var u = 1; u <= a.Units; u++)
        {
            var modelRules = a.ModelRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList();
            // Weapon ids are per model: the engine addresses weapons by id across the unit.
            var unit = u;
            var models = Enumerable.Range(1, a.SizeOf(u)).Select(m =>
            {
                // A model's own entries: its rules added to the unit's, its stats as the slot's, its weapons in place of the army's.
                var own = a.Models.Where(x => x.Unit == unit && x.Model == m).ToList();
                var rules = modelRules.Concat(own.SelectMany(x => x.Rules ?? []).Select(r => new PropertyInstance(r.Name, r.Parameter))).ToList();
                var stats = own.SelectMany(x => x.Stats ?? new Dictionary<string, string>()).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value);
                var weapons = own.LastOrDefault(x => x.Weapons is not null)?.Weapons ?? a.Weapons;
                return new ModelSlot(
                    Guid.CreateVersion7(), m, $"{a.Name} {unit}.{m}", stats, rules, Base.Circle(1.26), 1.5, null,
                    weapons.Select(w => new Weapon(Guid.CreateVersion7(), w.Name,
                        w.Stats, w.Rules.Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList())).ToList(), a.ArmyBook);
            }).ToList();
            units.Add(new Unit(Guid.CreateVersion7(), u, $"{a.Name} unit {u}", a.UnitStats,
                a.UnitRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList(), models));
        }
        foreach (var r in a.Reserves)
            units[r.Unit - 1] = units[r.Unit - 1] with
            {
                Reserve = new UnitReserve(r.Property, r.OfModel is { } m ? units[r.OfUnit - 1].Models[m - 1].Id : units[r.OfUnit - 1].Id),
            };
        return new ArmySnapshot(Guid.CreateVersion7(), a.Name, RuleSets.Gf351, units) { Edition = edition };
    }
}
