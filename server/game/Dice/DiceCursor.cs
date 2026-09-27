using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

public static class DiceCursor
{
    /// <summary>Every die ever drawn in the live log: the dice in each roll record, plus each forced or player reroll. Edits draw nothing.</summary>
    public static long DrawnSoFar(GameState state) => EventApplier.EffectiveLog(state.Applier.Log).Sum(e => e.Payload switch
    {
        StepCompleted s => s.Rolls.Sum(r => (long)r.Dice.Count),
        DiceRerolled d => d.Replaced.Count,
        _ => 0L,
    });
}
