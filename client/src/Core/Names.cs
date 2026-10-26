using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Ids to words. Falls back to the id's first eight characters rather than throwing: a name is presentation.</summary>
public sealed class Names(GameInfo game, Projection? projection)
{
    public string Participant(Guid? id) => id is null ? "the game" : game.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? Short(id.Value);
    /// <summary>The game's teams by ordinal.</summary>
    public IReadOnlyList<Team> Teams => [.. game.Teams.OrderBy(t => t.Ordinal)];
    public string Team(Guid id) => game.Teams.FirstOrDefault(t => t.Id == id) is { } t ? $"Team {t.Ordinal}" : Short(id);
    public string Unit(Guid id) => projection?.Units.GetValueOrDefault(id)?.Definition.Name ?? Short(id);
    public string Model(Guid id) => projection?.Models.GetValueOrDefault(id) is { } m ? $"{Unit(m.UnitId)} ({m.Slot.Name})" : Short(id);
    /// <summary>The team of the participant whose army the model belongs to.</summary>
    public Guid? TeamOfModel(Guid modelId) => projection?.Models.GetValueOrDefault(modelId) is { } m && projection.Units.GetValueOrDefault(m.UnitId) is { } u
        ? game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;
    /// <summary>A weapon by its snapshot name, from the slot that carries it.</summary>
    public string Weapon(Guid id) => projection?.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == id)?.Name ?? Short(id);
    /// <summary>Each weapon found, in the order asked, with the model whose slot carries it; an id no slot carries is left out.</summary>
    public IReadOnlyList<(Guid ModelId, Weapon Weapon)> WeaponsOf(IEnumerable<Guid> ids) => projection is null ? []
        : ids.Select(id => projection.Models.Values.Select(m => (m.Id, W: m.Slot.Weapons.FirstOrDefault(w => w.Id == id))).FirstOrDefault(x => x.W is not null))
            .Where(x => x.W is not null).Select(x => (x.Id, x.W!)).ToList();
    public Guid? TeamOfUnit(Guid unitId) => projection?.Units.GetValueOrDefault(unitId) is { } u ? game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;
    public Guid? UnitOfModel(Guid modelId) => projection?.Models.GetValueOrDefault(modelId)?.UnitId;
    public string Piece(Guid id) => projection is null ? Short(id)
        : projection.Units.ContainsKey(id) ? Unit(id) : projection.Models.ContainsKey(id) ? Model(id) : projection.Markers.ContainsKey(id) || projection.Board.Table.Markers.Any(m => m.Id == id) ? Marker(id) : Short(id);
    /// <summary>
    /// A marker by its label: placed in play, or else from setup. One with no label is its kind and its place among the markers of
    /// that kind, setup's order first ("Objective 2"), never an id.
    /// </summary>
    public string Marker(Guid id)
    {
        var marker = projection?.Markers.GetValueOrDefault(id) ?? projection?.Board.Table.Markers.FirstOrDefault(m => m.Id == id);
        if (marker is null) return Short(id);
        if (!string.IsNullOrWhiteSpace(marker.Label)) return marker.Label;
        var table = projection!.Board.Table.Markers;
        var kin = table.Concat(projection.Markers.Values.Where(m => table.All(t => t.Id != m.Id)).OrderBy(m => m.Id))
            .Where(m => m.Kind == marker.Kind).Select(m => m.Id).ToList();
        return $"{(marker.Kind == MarkerKind.Objective ? "Objective" : "Token")} {kin.IndexOf(id) + 1}";
    }
    public static string Short(Guid id) => id.ToString("N")[..8];
}
