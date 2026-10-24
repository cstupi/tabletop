using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>
/// Roll-off result spec → The message: a step_completed whose values carry place_1..place_n (team ordinals, best first) and
/// rolled_&lt;ordinal&gt;_&lt;k&gt; (each team's faces in order) is the roll-off's ranking; it shows "Team k goes first" with each team's roll
/// under it, best first. Live batches only: nothing while catching up, in a batch with an undo, or with the banner off.
/// </summary>
public static class RollOffBanners
{
    public const double Seconds = 4.0;

    public static Banner? Of(IReadOnlyList<GameEvent> batch, Names n, bool enabled, bool catchingUp)
    {
        if (!enabled || catchingUp || batch.Any(e => e.Payload is Undone)) return null;
        if (batch.Select(e => e.Payload).OfType<StepCompleted>().LastOrDefault(s => s.Values?.ContainsKey(Place(1)) is true) is not { Values: { } values }) return null;
        var placed = new List<Team>();
        for (var k = 1; values.TryGetValue(Place(k), out var ordinal); k++)
            if (n.Teams.FirstOrDefault(t => t.Ordinal == ordinal) is { } team && !placed.Contains(team)) placed.Add(team);
        if (placed.Count == 0) return null;
        var rolls = placed.Select(t => (Team: t, Faces: Faces(values, t.Ordinal))).Where(r => r.Faces.Count > 0).ToList();
        var detail = rolls.Count == 0 ? null : string.Join(" · ", rolls.Select(r => $"{n.Team(r.Team.Id)} rolled {string.Join(", ", r.Faces)}"));
        return new Banner($"{n.Team(placed[0].Id)} goes first", Seconds, detail);
    }

    static string Place(int k) => $"place_{k}";

    static List<int> Faces(IReadOnlyDictionary<string, int> values, int ordinal)
    {
        var faces = new List<int>();
        for (var k = 1; values.TryGetValue($"rolled_{ordinal}_{k}", out var face); k++) faces.Add(face);
        return faces;
    }
}
