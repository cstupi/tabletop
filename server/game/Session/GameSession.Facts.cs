using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Answers outside the log (in-game UX spec → Sight question, Model facts): nothing here appends, and a sight answer produces no AtRest.</summary>
public sealed partial class GameSession
{
    /// <summary>The last ModelFacts sent; null before any. Facts change only through a batch, and every batch compares against this.</summary>
    private ModelFacts? _factsSent;

    /// <summary>One line per observer model and target model on the table, in unit order, who may shoot and each weapon's range against the target — to the asker alone.</summary>
    private async Task HandleSightAsync(ISessionClient from, SightQuery query)
    {
        IReadOnlyList<SightLine> lines = []; IReadOnlyList<Guid> canShoot = []; IReadOnlyList<ShotRange> ranges = [];
        if (Play is { } state && state.Projection.Units.TryGetValue(query.ObserverUnitId, out var observer) && state.Projection.Units.TryGetValue(query.TargetUnitId, out var target))
        {
            var (watching, watched) = (OnTable(state, observer), OnTable(state, target));
            if (watching.Count > 0 && watched.Count > 0)
            {
                var sight = Sight.From(state, observer.Id);
                lines = watching.SelectMany(f => watched.Select(t => sight.Line(f, t))).OfType<SightLine>().ToList();
                if (Engine?.Properties is { } properties)
                    (canShoot, ranges) = (state.Rules.Shooters(state, properties, observer.Id, target.Id), state.Rules.ShotRanges(state, properties, observer.Id, target.Id));
            }
        }
        await from.SendAsync(new SightAnswer(query.QueryId, lines, canShoot, ranges));
    }

    static List<Guid> OnTable(GameState state, UnitInPlay unit) =>
        unit.Definition.Models.Select(m => m.Id).Where(id => state.Projection.Models[id].Location is OnTable).ToList();

    ModelFacts Facts(GameState state) => new(state.Projection.Models.Keys.ToDictionary(id => id, id => state.Rules.MaxWounds(state, id)), state.Rules.DeployInside(state),
        Engine?.Properties is { } properties ? state.Rules.MaySetAside(state, properties) : [],
        Engine?.Properties is { } props ? state.Rules.Threats(state, props) : new Dictionary<Guid, UnitThreat>(), state.Rules.InCover(state));

    static bool Same(ModelFacts a, ModelFacts b) =>
        a.MaxWounds.Count == b.MaxWounds.Count && b.MaxWounds.All(kv => a.MaxWounds.TryGetValue(kv.Key, out var v) && v == kv.Value)
        && (a.DeployInside ?? new Dictionary<Guid, IReadOnlyList<Guid>>()) is var x && (b.DeployInside ?? new Dictionary<Guid, IReadOnlyList<Guid>>()) is var y
        && x.Count == y.Count && y.All(kv => x.TryGetValue(kv.Key, out var v) && v.SequenceEqual(kv.Value))
        && (a.MaySetAside ?? []).SequenceEqual(b.MaySetAside ?? []) && (a.InCover ?? []).SequenceEqual(b.InCover ?? [])
        && SameThreats(a.Threats ?? new Dictionary<Guid, UnitThreat>(), b.Threats ?? new Dictionary<Guid, UnitThreat>());

    static bool SameThreats(IReadOnlyDictionary<Guid, UnitThreat> a, IReadOnlyDictionary<Guid, UnitThreat> b) =>
        a.Count == b.Count && b.All(kv => a.TryGetValue(kv.Key, out var t) && t.Charge == kv.Value.Charge && t.ShootMove == kv.Value.ShootMove
            && t.Ranges.Count == kv.Value.Ranges.Count && kv.Value.Ranges.All(r => t.Ranges.TryGetValue(r.Key, out var v) && v == r.Value));

    /// <summary>On connect, once playing: every model's facts, after the log.</summary>
    private async Task SendFactsAsync(ISessionClient client)
    {
        if (Play is null) return;
        var facts = Facts(Play);
        _factsSent = facts;
        await client.SendAsync(facts);
    }

    /// <summary>After a batch: to everyone, when any value differs from the last sent.</summary>
    private async Task BroadcastFactsIfChangedAsync()
    {
        if (Play is null) return;
        var facts = Facts(Play);
        if (_factsSent is { } sent && Same(sent, facts)) return;
        _factsSent = facts;
        await BroadcastAsync(facts);
    }
}
