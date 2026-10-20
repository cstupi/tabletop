using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A deploy activation the server is waiting on: the unit picked at a deploy Ask, and the models of it still on the shelf.</summary>
public sealed record MidDeploy(Guid UnitId, string Question, IReadOnlyList<Guid> ShelvedModelIds);

/// <summary>Deploying from the shelf. The deploy Asks pick a unit, and the activation ends only when every model of it is placed (scheduler → Deployment).</summary>
public static class DeployView
{
    static readonly HashSet<string> Questions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher, AskQuestions.DeployReinforcement];
    const int PerRow = 5, MaxRings = 64; const double Gap = 0.2;

    /// <summary>
    /// The unit my team picked at the latest Ask, when that Ask is a deploy question that has closed with a unit and some of its
    /// models are still shelved. Any later Ask — the next deploy, an activation — or a turn's end ends it, because the server moved on (a pick
    /// withdrawn when its unit was set aside ends the turn when the team has nothing left to deploy). Either player of
    /// the team places it (rulebook p.50 Team Games).
    /// </summary>
    public static MidDeploy? MidDeploy(Projection p, IReadOnlyList<GameEvent> log, Guid? myTeam)
    {
        if (myTeam is not { } team) return null;
        Ask? last = null;
        var ended = false;
        var live = EventApplier.EffectiveLog(log);                                // an undo takes back the Asks, closes and turn ends after its point
        foreach (var e in live)
        {
            if (e.Payload is AskOpened o) { last = o.Ask; ended = false; }
            else if (e.Payload is TurnEnded) ended = true;
        }
        if (last is null || ended || !Questions.Contains(last.Question) || !last.Teams.Contains(team) || p.OpenAsks.ContainsKey(last.Id)) return null;
        var answer = live.Select(e => e.Payload).OfType<AskClosed>().LastOrDefault(c => c.AskId == last.Id)?.Answers.FirstOrDefault(a => a.TeamId == team)?.Answer;
        if (answer is not SelectedPieces { PieceIds: [var unit] }) return null;
        var shelved = p.Models.Values.Where(m => m.UnitId == unit && m.Location is OnShelf).OrderBy(m => m.Slot.Ordinal).Select(m => m.Id).ToList();
        return shelved.Count == 0 ? null : new MidDeploy(unit, last.Question, shelved);
    }

    /// <summary>
    /// One relocation per model to a tight grid at yaw 0 — rows of five, spaced by the widest base plus a small gap — centred on the point
    /// and pushed in so the whole grid is on the table. The player drags the models into position afterwards.
    /// </summary>
    public static IReadOnlyList<ModelRelocation> Layout(IReadOnlyList<ModelInPlay> models, Point3 at, TableSize size)
    {
        if (models.Count == 0) return [];
        var pitch = models.Max(m => Math.Max(m.Slot.Base.A, m.Slot.Base.B)) + Gap;
        var cols = Math.Min(models.Count, PerRow); var rows = (models.Count + cols - 1) / cols;
        var cx = Fit(at.X, cols * pitch / 2, size.Width); var cy = Fit(at.Y, rows * pitch / 2, size.Length);
        return models.Select((m, i) => new ModelRelocation(m.Id, m.Location,
            new OnTable(new Placement(new Point3(cx + (i % cols - (cols - 1) / 2.0) * pitch, cy - (i / cols - (rows - 1) / 2.0) * pitch, at.Z), new Yaw(0))))).ToList();
    }

    /// <summary>
    /// Shelf models placed relative to a model on the table (a Spawn or Split reserve, placed "fully within 6\"" of its anchor): rings about the anchor's
    /// centre, the first clear of its base by the separation, each as many models as fit a pitch apart, yaw 0; a spot whose base would leave the table is skipped. A row of
    /// <see cref="Layout"/> would reach past 6" for five wide bases; a ring keeps every model as close as it can go.
    /// </summary>
    public static IReadOnlyList<ModelRelocation> Around(IReadOnlyList<ModelInPlay> models, Placement anchor, Base anchorBase, TableSize size, double? clearance = null)
    {
        if (models.Count == 0) return [];
        var widest = models.Max(m => Math.Max(m.Slot.Base.A, m.Slot.Base.B));
        var pitch = widest + Gap;
        // The anchor's farthest edge from its centre: a rectangle's corner, else its long half.
        var reach = anchorBase.Shape == BaseShape.Rect ? Math.Sqrt(anchorBase.A * anchorBase.A + anchorBase.B * anchorBase.B) / 2 : Math.Max(anchorBase.A, anchorBase.B) / 2;
        var radius = reach + Math.Max(Gap, clearance ?? 0) + widest / 2;   // clear of the anchor by the Ask's separation
        var placed = new List<ModelRelocation>(models.Count);
        var next = 0;
        // Ring by ring outward: a spot whose base would leave the table is skipped (pulling it back on would bring it toward the anchor).
        for (var rings = 0; next < models.Count && rings < MaxRings; rings++, radius += pitch)
        {
            var room = Math.Max(1, (int)Math.Floor(2 * Math.PI * radius / pitch));
            for (var i = 0; i < room && next < models.Count; i++)
            {
                var angle = 2 * Math.PI * i / room;
                var m = models[next]; var half = Math.Max(m.Slot.Base.A, m.Slot.Base.B) / 2;
                var x = anchor.Position.X + radius * Math.Cos(angle); var y = anchor.Position.Y + radius * Math.Sin(angle);
                if (x < half || x > size.Width - half || y < half || y > size.Length - half) continue;
                placed.Add(new ModelRelocation(m.Id, m.Location, new OnTable(new Placement(new Point3(x, y, anchor.Position.Z), new Yaw(0)))));
                next++;
            }
        }
        return placed;
    }

    static double Fit(double centre, double half, double extent) => extent <= 2 * half ? extent / 2 : Math.Clamp(centre, half, extent - half);

    /// <summary>A pick's eligible ids, less, at a deploy question, a unit with no model on the shelf: one put inside a Transport since the Ask
    /// opened deploys with the Transport (passengers spec), and naming it is the server's deploy_inside breach.</summary>
    public static IReadOnlyList<Guid> Pickable(Projection p, Ask ask) => ask.Legal is not PieceSelection sel ? [] : !Questions.Contains(ask.Question) ? sel.Eligible
        : sel.Eligible.Where(id => !p.Units.ContainsKey(id) || p.Models.Values.Any(m => m.UnitId == id && m.Location is OnShelf)).ToList();

    /// <summary>The eligible ids the table draws no node for: a unit none of whose models is on the table, or a model that is not. A shelved unit can only be picked from the panel.</summary>
    public static IReadOnlyList<Guid> OffTable(Projection p, IEnumerable<Guid> eligible) =>
        eligible.Where(id => p.Units.ContainsKey(id) ? !p.Models.Values.Any(m => m.UnitId == id && m.Location is OnTable)
            : p.Models.GetValueOrDefault(id) is { } model && model.Location is not OnTable).ToList();
}
