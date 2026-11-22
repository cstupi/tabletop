using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The terrain card's two folds.</summary>
public enum SetupFold { PieceMesh, PieceVolumes }

/// <summary>The terrain card's Mesh and Volumes folds (spec 2026-11-21-setup-screen-layout-design.md → Selected piece): closed until opened. A fold
/// the player opens or closes stays so, per status, for as long as this instance lives (the game screen's).</summary>
public sealed class SetupFolds
{
    readonly Dictionary<(SetupFold, GameStatus), bool> _chosen = new();

    public bool IsOpen(SetupFold fold, GameStatus status) => _chosen.TryGetValue((fold, status), out var open) && open;

    public void Set(SetupFold fold, bool open, GameStatus status) => _chosen[(fold, status)] = open;

    public static string Title(SetupFold fold) => fold == SetupFold.PieceMesh ? "Mesh" : "Volumes";

    /// <summary>The terrain card's Volumes heading: "Volume" for one, "Volumes 3" for more.</summary>
    public static string PieceVolumesTitle(int count) => count == 1 ? "Volume" : $"Volumes {count}";
}

/// <summary>An objective's row in the Objectives section: its label and where it stands.</summary>
public sealed record ObjectiveRow(Guid Id, string Label, string Position);

public static class ObjectiveRows
{
    /// <summary>The draft's objectives in order; one without a label is "Objective n" by its place in the list.</summary>
    public static IReadOnlyList<ObjectiveRow> Of(TableState? draft) => draft is null ? [] : draft.Markers.Where(m => m.Kind == MarkerKind.Objective)
        .Select((m, i) => new ObjectiveRow(m.Id, string.IsNullOrWhiteSpace(m.Label) ? $"Objective {i + 1}" : m.Label!,
            $"({m.Position.X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}, {m.Position.Y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)})")).ToList();
}
