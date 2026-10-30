using Tabletop.Content;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The 3.6 rules task F6b's engine work makes playable, one game per mechanism, grouped army:3.6:&lt;book uid&gt;: the morale test's range and
/// opponent (Hive Bond, Hold the Line).
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army) Hdf36 = ("z65fgu0l29i4lnlu", "Human Defense Force");

        public static IReadOnlyList<TestGame> Army36G { get; } =
        [
            // Both sides hold in their zones, over 9" apart: every morale test the shooting forces is "shot from over 9" away".
            In36(Morale(Hives36, "alien-hives-hive-bond", "Hive Bond", [R(1, "Hive Bond")], 1, ShotFromOverNine), Hives36),
            In36(Morale(Hdf36, "human-defense-force-hold-the-line", "Hold the Line", [R(1, "Hold the Line")], 1, ShotFromOverNine), Hdf36),
        ];

        const string ShotFromOverNine = "\"they get +1 to Morale Test rolls when shot/charged from over 9\"/6\" away\"";
    }
}
