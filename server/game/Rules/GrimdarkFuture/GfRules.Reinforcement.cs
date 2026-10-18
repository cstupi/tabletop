using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed partial class GrimdarkFutureRules
{
    /// <summary>
    /// Reinforcement (GfReserves) at the round boundary, after the Ambushers: teams alternate as Ambushers do (p.13, "starting with the player that
    /// activates next"), each picking one Reinforcement copy (`deploy_reinforcement`) or passing, until every team has passed or has none left. A pick
    /// removes a Shaken original "from the table as destroyed", then waits while the copy's models come off the shelf by hand. A pass stands for this
    /// boundary; read from the Asks since round_started.
    /// </summary>
    HookResult ReinforcementBoundary(GameState state, int round, IReadOnlyList<Guid> order)
    {
        if (round < 1 || state.Projection.Scheduler.Round < 2) return new HookDone();   // "the next round": none before round two
        var since = SchedulerLog.Of(state).SinceRound.Select(e => e.Payload).ToList();
        var asks = since.OfType<AskOpened>().Where(o => o.Ask.Question == AskQuestions.DeployReinforcement).ToList();
        var passed = new HashSet<Guid>();
        foreach (var a in asks)
        {
            if (state.Projection.OpenAsks.ContainsKey(a.Ask.Id)) return new HookWaiting();
            var answer = since.OfType<AskClosed>().FirstOrDefault(c => c.AskId == a.Ask.Id)?.Answers.FirstOrDefault()?.Answer;
            if (answer is SelectedPieces { PieceIds: [var copy] } && state.Projection.Units.TryGetValue(copy, out var u) && u.Definition.Reserve is { Property: GfReserves.Reinforcement })
            {
                if (GfReserves.RemoveOriginal(state, copy) is { Count: > 0 } removal) return new HookEmit([.. removal.Cast<EventPayload>()]);
                if (TeamUnits.ModelsOf(state, u).Any(m => m.Location is OnShelf)) return new HookWaiting();   // models still coming off the shelf
            }
            if (answer is not SelectedPieces) passed.Add(a.Ask.Teams[0]);
        }
        var eligible = order.Where(t => !passed.Contains(t) && GfReserves.Reinforcements(state, t).Count > 0).ToList();
        if (eligible.Count == 0) return new HookDone();
        var next = eligible[0];
        if (asks.Count > 0)
        {
            var lastIndex = TeamUnits.IndexOf(order, asks[^1].Ask.Teams[0]);
            next = eligible.FirstOrDefault(t => TeamUnits.IndexOf(order, t) > lastIndex, eligible[0]);
        }
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployReinforcement, "Reinforcement: deploy a new copy of a unit, or pass", [next],
            new PieceSelection(GfReserves.Reinforcements(state, next), 1, 1), AllowPass: true, Sealed: false, WindowKind: null, FrameId: null) { OwnUnitsOnly = true };
        return new HookEmit([new AskOpened(ask)]);
    }
}
