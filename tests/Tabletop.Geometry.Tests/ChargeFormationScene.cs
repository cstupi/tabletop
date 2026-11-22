using System.Text.Json;

namespace Tabletop.Geometry.Tests;

/// <summary>
/// A charge move Ask's formation search as the game server's ChargeFormation builds it: Fixtures/charge-formation-*.json are five Flayer Cavalry
/// (60 × 35 mm ovals, a 16" Fast charge) of the dev game "Testing Locally" at three charge move Asks, projected from its log, where the search
/// took seconds before the Ask opened.
/// </summary>
internal sealed record ChargeFormationScene(string Name, TableSize Table, List<WorldVolume> Volumes, List<Guid> Impassable, List<Guid> Difficult,
    double? DifficultCap, List<Guid> Steep, List<Guid> Landing, double Allowance, bool PassesModels, List<ChargeFormationScene.Mover> Unit,
    List<PlacedModel> Members, List<PlacedModel> Targets, List<PlacedModel> Standing, double ContactVertical)
{
    internal sealed record Mover(Guid Id, PlacedModel Start);

    public static readonly long[] Seqs = [679, 1109, 1261];

    public static ChargeFormationScene Load(long seq) => JsonSerializer.Deserialize<ChargeFormationScene>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"charge-formation-{seq}.json")), ReferenceScene.Json)!;

    public IReadOnlyList<(Guid Id, PlacedModel Start)> Movers => Unit.Select(m => (m.Id, m.Start)).ToList();

    List<WorldVolume> Of(List<Guid> ids) => Volumes.Where(v => ids.Contains(v.Id)).ToList();

    /// <summary>The terms ChargeFormation passes (GfNames' coherency 1", 3", 9"), with a contact reach of the caller's choosing.</summary>
    public FormationTerms Terms(double? contactVertical) => new(Allowance, Of(Impassable), Of(Difficult), DifficultCap, Of(Landing), Volumes, Table, Standing,
        PassesModels, 1, 3, 9, Steep.Count > 0 ? Of(Steep) : null, ContactVertical: contactVertical);
}
