using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// p.7: "After 4 rounds have been played the game ends, and players determine who won by checking their mission objectives."
/// The breach is the act of starting the round after the final one, judged on the scheduler's proposed round_started, so it comes
/// after a player has ended round 4 and before any of round 5's work. A menu set_round is a command and never reaches a judge.
/// The mission's objectives are scored by the engine. p.6: the player that controls most markers wins (GfObjectives).
/// </summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>Fixed for the core game (manager ruling, 2026-10-02); p.27's and p.41's longer games are optional modules.</summary>
    public const int FinalRound = 4;
    /// <summary>The core book gives the rule no heading; p.59 calls the same count "Mission Length".</summary>
    public const string MissionLength = "mission_length";

    public static string MissionLengthDetail(int roundsPlayed) => $"{roundsPlayed} rounds have been played: the game ends after {FinalRound}";

    /// <summary>The winners and the standing are read after round 4's seizures: the round-end step runs before BeginRound proposes round 5 (objectives spec → Where the seize runs).</summary>
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) =>
        proposed.OfType<RoundStarted>().FirstOrDefault(r => r.Round > FinalRound) is { } next
            ? new GameEndingBreach(MissionLength, $"{MissionLengthDetail(next.Round - 1)}. {GfObjectives.Standing(state)}", GfObjectives.Winners(state))
            : null;
}
