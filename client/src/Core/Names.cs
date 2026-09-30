using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Ids to words. Falls back to the id's first eight characters rather than throwing: a name is presentation.</summary>
public sealed class Names(GameInfo game, Projection? projection)
{
    public string Participant(Guid? id) => id is null ? "the game" : game.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? Short(id.Value);
    public string Team(Guid id) => game.Teams.FirstOrDefault(t => t.Id == id) is { } t ? $"Team {t.Ordinal}" : Short(id);
    public string Unit(Guid id) => projection?.Units.GetValueOrDefault(id)?.Definition.Name ?? Short(id);
    public string Model(Guid id) => projection?.Models.GetValueOrDefault(id) is { } m ? $"{Unit(m.UnitId)} ({m.Slot.Name})" : Short(id);
    /// <summary>The team of the participant whose army the model belongs to.</summary>
    public Guid? TeamOfModel(Guid modelId) => projection?.Models.GetValueOrDefault(modelId) is { } m && projection.Units.GetValueOrDefault(m.UnitId) is { } u
        ? game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;
    public Guid? UnitOfModel(Guid modelId) => projection?.Models.GetValueOrDefault(modelId)?.UnitId;
    public string Piece(Guid id) => projection is null ? Short(id)
        : projection.Units.ContainsKey(id) ? Unit(id) : projection.Models.ContainsKey(id) ? Model(id) : projection.Markers.GetValueOrDefault(id)?.Label ?? Short(id);
    public static string Short(Guid id) => id.ToString("N")[..8];
}
