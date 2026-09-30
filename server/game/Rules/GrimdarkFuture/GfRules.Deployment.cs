using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed partial class GrimdarkFutureRules
{
    /// <summary>Deployment: the shelf minus set-aside units. Scouts: the set-aside Scouts, after every other unit (page 14). One unit per Ask; no pass.</summary>
    private static TurnAsk? DeployAsk(GameState state, PropertySet props, Guid teamId, string phase)
    {
        var units = TeamUnits.Of(state, teamId).Where(u => TeamUnits.AllShelved(state, u));
        units = phase == Deployment
            ? units.Where(u => !u.Conditions.Contains(SetAside))
            : units.Where(u => u.Conditions.Contains(SetAside) && Lookup.UnitHas(state, props, u.Id, Scout));
        var ids = units.Select(u => u.Id).ToList();
        if (ids.Count == 0) return null;
        return phase == Deployment
            ? new TurnAsk(AskQuestions.Deploy, "Deploy one unit", new PieceSelection(ids, 1, 1), AllowPass: false)
            : new TurnAsk(AskQuestions.DeployScouts, "Deploy a Scout", new PieceSelection(ids, 1, 1), AllowPass: false);
    }

    public static IReadOnlyList<Guid> Ambushers(GameState state, PropertySet props, Guid teamId) =>
        TeamUnits.Of(state, teamId).Where(u => u.Conditions.Contains(SetAside) && Lookup.UnitHas(state, props, u.Id, Ambush) && TeamUnits.AllShelved(state, u)).Select(u => u.Id).ToList();

    /// <summary>
    /// Page 13: from round two, Ambushers deploy "starting with the player that activates next", alternating, until every team has passed
    /// or has none left. Read from the deploy_ambusher Asks since round_started; a pass stands for this boundary; nothing is written to the record.
    /// </summary>
    public HookResult RoundBoundaryStep(GameState state, PropertySet props, int round, IReadOnlyList<Guid> order)
    {
        if (round < 2) return new HookDone();
        var log = SchedulerLog.Of(state);
        var since = log.SinceRound.Select(e => e.Payload).ToList();
        var asks = since.OfType<AskOpened>().Where(o => o.Ask.Question == AskQuestions.DeployAmbusher).ToList();
        var passed = new HashSet<Guid>();
        foreach (var a in asks)
        {
            if (state.Projection.OpenAsks.ContainsKey(a.Ask.Id)) return new HookWaiting();
            var answer = since.OfType<AskClosed>().First(c => c.AskId == a.Ask.Id).Answers.FirstOrDefault()?.Answer;
            if (answer is SelectedPieces { PieceIds: [var unit] } && state.Projection.Units.TryGetValue(unit, out var u) && TeamUnits.AllShelved(state, u))
                return new HookWaiting();                                            // models still coming off the shelf
            if (answer is not SelectedPieces) passed.Add(a.Ask.Teams[0]);
        }
        var eligible = order.Where(t => !passed.Contains(t) && Ambushers(state, props, t).Count > 0).ToList();
        if (eligible.Count == 0) return new HookDone();
        var next = eligible[0];
        if (asks.Count > 0)
        {
            var lastIndex = TeamUnits.IndexOf(order, asks[^1].Ask.Teams[0]);
            next = eligible.FirstOrDefault(t => TeamUnits.IndexOf(order, t) > lastIndex, eligible[0]);
        }
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployAmbusher, "Deploy an Ambusher or pass", [next],
            new PieceSelection(Ambushers(state, props, next), 1, 1), AllowPass: true, Sealed: false, WindowKind: null, FrameId: null);
        return new HookEmit([new AskOpened(ask)]);
    }
}
