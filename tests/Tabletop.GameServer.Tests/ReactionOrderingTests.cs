using Interpreter = Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class ReactionOrderingTests
{
    static TestGames.Built Four() => TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1), TestArmies.Build("D", 1, 1)]);

    [Fact]
    public void Reactions_resolve_in_turn_order_starting_after_the_actor_and_passed_teams_drop_out()
    {
        var g = Four(); var t = g.Teams;
        TestGames.Apply(g, new RoundStarted(1, [t[2], t[0], t[3], t[1]], []));
        Assert.Equal([t[3], t[1], t[2]], Interpreter.Windows.ReactionOrder(g.State, t[0], [t[1], t[2], t[3]]));
        Assert.Equal([t[1], t[2]], Interpreter.Windows.ReactionOrder(g.State, t[0], [t[1], t[2]]));                 // only those who reacted
        Assert.Equal([t[3], t[1], t[2], t[0]], Interpreter.Windows.ReactionOrder(g.State, t[0], t));                // the actor's own team may react (OPR's casting window): last
        TestGames.Apply(g, new TurnStarted(t[3]), new TurnEnded(t[3], true, []));
        Assert.Equal([t[1], t[2]], Interpreter.Windows.ReactionOrder(g.State, t[0], [t[1], t[2], t[3]]));           // t3 passed
        Assert.Equal([t[0], t[1]], Interpreter.Windows.ReactionOrder(g.State, t[2], [t[0], t[1], t[3]]));           // after t2 comes t0, then t3 (passed, dropped), then t1
    }

    [Fact]
    public void A_non_active_actor_is_still_the_anchor_and_no_order_yet_means_team_order()
    {
        var g = Four(); var t = g.Teams;
        Assert.Equal([t[1], t[2]], Interpreter.Windows.ReactionOrder(g.State, t[0], [t[2], t[1]]));                 // round 0, no order yet: Info.Teams order after the actor
        TestGames.Apply(g, new RoundStarted(1, [t[0], t[1], t[2], t[3]], []), new TurnStarted(t[0]));
        Assert.Equal([t[3], t[0], t[1]], Interpreter.Windows.ReactionOrder(g.State, t[2], [t[0], t[1], t[3]]));     // t2 reacted inside t0's turn; the order still starts after t2
    }

    [Fact]
    public void Game_rules_may_replace_the_ordering()
    {
        var rules = new FakeRules { OrderReactionsHook = (_, actor, reacting) => reacting.Reverse().ToList() };
        var g = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)], rules: rules);
        TestGames.Apply(g, new RoundStarted(1, g.Teams, []));
        Assert.Equal([g.Teams[2], g.Teams[1]], Interpreter.Windows.ReactionOrder(g.State, g.Teams[0], [g.Teams[1], g.Teams[2]]));
    }
}
