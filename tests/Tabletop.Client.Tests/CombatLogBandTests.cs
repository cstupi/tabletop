using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class CombatLogBandTests
{
    [Fact]
    public void A_block_takes_its_actor_s_team_band_and_any_other_entry_none()
    {
        var p = TableUx.After(TableUx.Deploy);
        var n = new Names(TableUx.Game, p);
        var e = new GameEvent(1, TableUx.P1, new RoundStarted(1, [], []), null);
        ActivationBlock Block(Guid? actor) => new(1, e, false, "R1", actor, [], [], []);
        Assert.Equal(2, CombatLogRows.Band(Block(TableUx.Orcs.Id), n, TableUx.Game));
        Assert.Equal(1, CombatLogRows.Band(Block(TableUx.Squad.Id), n, TableUx.Game));
        Assert.Equal(0, CombatLogRows.Band(Block(null), n, TableUx.Game));
        Assert.Equal(0, CombatLogRows.Band(new LineEntry(1, e, false, "Round 1"), n, TableUx.Game));
    }
}
