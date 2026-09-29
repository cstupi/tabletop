using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>An Ask about interrupting. One Ask per eligible team, asked in reaction order, each chosen reaction pushed before the next team is asked.</summary>
public static class Windows
{
    /// <summary>
    /// Who reacts first (scheduler → Windows): GameRules.OrderReactions when it answers; else turn order starting after the actor's team
    /// (the actor's own team last), teams the order does not list following in Info.Teams order, only the reacting teams, passed teams dropped.
    /// </summary>
    public static IReadOnlyList<Guid> ReactionOrder(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams)
    {
        if (state.Rules.OrderReactions(state, actorTeam, reactingTeams) is { } replaced) return replaced;
        var s = state.Projection.Scheduler;
        var order = s.Order.Count > 0 ? s.Order.ToList() : state.Info.Teams.Select(t => t.Id).ToList();
        var start = actorTeam is { } a && order.Contains(a) ? order.IndexOf(a) + 1 : 0;
        var rotated = order.Skip(start).Concat(order.Take(start)).Concat(state.Info.Teams.Select(t => t.Id).Where(t => !order.Contains(t))).ToList();
        return rotated.Where(t => reactingTeams.Contains(t) && !s.Passed.Contains(t)).ToList();
    }

    public static StepOutcome? Run(StepContext ctx, string point)
    {
        var reactions = ctx.Rules.ReactionsAt(ctx.State, ctx.Properties, point, ctx.Frame);
        if (reactions.Count == 0) return null;
        var asks = ctx.History.Asks.Where(a => a.Ask.WindowKind == point && a.Ask.Question == AskQuestions.React).ToList();
        var pushed = 0;
        foreach (var team in ReactionOrder(ctx.State, ctx.ActorTeam, reactions.Select(r => r.TeamId).Distinct().ToList()))
        {
            var mine = reactions.Where(r => r.TeamId == team).ToList();
            if (mine.Count == 0) continue;
            var ask = asks.FirstOrDefault(a => a.Ask.Teams[0] == team);
            if (ask is null)
                return new OpenAsks([ctx.Ask(AskQuestions.React, $"React at {point}?", team, new Options(mine.Select(r => new AskOption(r.OptionId, r.Label, [r.Context.ActorUnitId ?? Guid.Empty])).ToList()), allowPass: true, windowKind: point)]);
            if (!ask.Closed) return new OpenAsks([]);   // unreachable: Advance returns before running a waiting step
            if (ask.Single is not Chosen chosen) continue;   // passed, or an out-of-set answer accepted under warn/silent
            var reaction = mine.FirstOrDefault(r => r.OptionId == chosen.OptionId) ?? mine[0];
            if (ctx.History.Children.Count <= pushed) return new Push(reaction.Sequence, reaction.Context);
            pushed++;   // its child exists and, since this frame is on top, has finished or was abandoned
        }
        return null;
    }

    /// <summary>
    /// p.13's spend: one sealed Ask to every team holding an eligible model, whose legal set is a cap per model — the tokens that
    /// model holds — for every addressed team's models in one map. There is no total to carry, so nothing here reads a team's own
    /// maximum: it is the sum of that team's entries.
    /// </summary>
    public static StepOutcome? SimultaneousSpend(StepContext ctx, string windowKind, IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> capsByTeam, string prompt)
    {
        var teams = capsByTeam.Where(kv => kv.Value.Any(c => c.Value > 0)).Select(kv => kv.Key).ToList();
        if (teams.Count == 0) return null;
        var existing = ctx.History.Asks.FirstOrDefault(a => a.Ask.WindowKind == windowKind && a.Ask.Question == AskQuestions.SpendTokens);
        if (existing is null)
        {
            var caps = teams.SelectMany(t => capsByTeam[t]).Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value);
            return new OpenAsks([ctx.SealedAsk(AskQuestions.SpendTokens, prompt, teams, new TokenSpend(caps), windowKind)]);
        }
        return existing.Closed ? null : new OpenAsks([]);
    }

    /// <summary>
    /// Every team's revealed spend, per model. Two checks are the step's rather than LegalSetMembership's, which has no state to
    /// answer them with (the MoveRegion precedent): a key the answering team does not own is dropped, and a count is cut to the
    /// cap the Ask recorded for that key. The caps come off the Ask, which the log holds, so a replay never recomputes them.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> Spends(StepContext ctx, string windowKind, IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> capsByTeam)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<Guid, int>>();
        var ask = ctx.History.Asks.LastOrDefault(a => a.Ask.WindowKind == windowKind && a.Ask.Question == AskQuestions.SpendTokens && a.Closed);
        if (ask is null) return result;
        var caps = ((TokenSpend)ask.Ask.Legal).MaxByModel;
        foreach (var team in ask.Ask.Teams)
        {
            var owned = capsByTeam.GetValueOrDefault(team) ?? new Dictionary<Guid, int>();
            var counts = new Dictionary<Guid, int>();
            if (ask.Answers!.GetValueOrDefault(team) is TokensSpent spent)
                foreach (var (model, asked) in spent.Counts.Where(c => owned.ContainsKey(c.Key)))
                {
                    var take = Math.Clamp(asked, 0, caps.GetValueOrDefault(model));
                    if (take > 0) counts[model] = take;
                }
            result[team] = counts;
        }
        return result;
    }
}
