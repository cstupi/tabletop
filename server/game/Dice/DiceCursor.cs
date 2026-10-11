using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

public static class DiceCursor
{
    /// <summary>Every die ever drawn, including the dice of undone events: the dice in each roll record, plus each forced or player reroll, over the whole append-only log. An undone roll stays spent, so the next roll after an undo draws fresh dice. Edits draw nothing.</summary>
    public static long DrawnSoFar(GameState state) => state.Applier.Log.Sum(e => e.Payload switch
    {
        StepCompleted s => s.Rolls.Sum(r => (long)r.Dice.Count),
        DiceRerolled d => d.Replaced.Count,
        _ => 0L,
    });
}
