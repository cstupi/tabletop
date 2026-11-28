using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>
    /// p.9 CHARGE MOVES: "Charging models must move by up to 12” to get into base contact with an enemy model from the target unit, or as close as
    /// possible, whilst still maintaining unit coherency". The region's ContactWith is the target's models. Contact is base contact in plan with a
    /// target model whose base is within p.9's vertical strike reach (GfEdition.MeleeVertical) of the landing's settled rest (owner ruling: "The fight
    /// cant happen if no one can strike so the charge should be rejected if nothing would reach the 3" threshold."), here, in ChargeLanding and in
    /// ChargeFormation. The answer is read as the charge ends it
    /// (ChargeLanding, through GfAnswers.Moves), so a charging model drawn into contact there is in contact here. When the charging unit could have
    /// reached base contact in coherency (ChargeFormation: one model on a path to contact, Movement.ContactDistance as GfMelee.ChargeTargets reads
    /// "a clear path to reach it", and the rest formed up on it, every move judged as the other breaches judge it), an answer, a pass included, in
    /// which no charging model ends within ChargeContactGrace of a target model breaches it. When it could not (p.7: "if being within charge
    /// distance becomes impossible"), "as close as possible" is not judged. A stop (Destinations.Continues) is not the end of the move, and a
    /// disembark has no start on the table to measure from; neither is judged.
    /// </summary>
    public static AnswerBreachFound? ChargeContactBreach(GameState state, MoveRegion region, Destinations? answer)
    {
        if (region.ContactWith is not { Count: > 0 } contact || answer is { Continues: true } || region.Containment?.ExitingId is not null) return null;
        var p = state.Projection;
        var targets = Targets(state, contact);
        var chargers = region.ModelIds.Where(p.Models.ContainsKey).Select(id => (Id: id, Start: Battlefield.Placed(state, id))).Where(c => c.Start is not null)
            .Select(c => (c.Id, Start: c.Start!.Value)).ToList();
        if (targets.Count == 0 || chargers.Count == 0) return null;
        var landed = (answer is null ? [] : GfAnswers.Moves(state, region, answer)).Where(m => m.To is OnTable).DistinctBy(m => m.ModelId)
            .ToDictionary(m => m.ModelId, m => Battlefield.At(state, m.ModelId, Battlefield.Settle(state, m.ModelId, ((OnTable)m.To).Placement)));
        if (chargers.Any(c => Gap(state, landed.GetValueOrDefault(c.Id, c.Start), targets) <= GfNames.ChargeContactGrace + Touching)) return null;
        if (ChargeFormation(state, region, chargers, targets) is null) return null;
        var name = p.Units.GetValueOrDefault(p.Models[contact.First(p.Models.ContainsKey)].UnitId)?.Definition.Name ?? "the target";
        return new(GfNames.ChargeContactRule, $"no charging model reached base contact with {name}: charging models must move to get into base contact with an enemy model from the target unit");
    }

    /// <summary>
    /// A charge move's relocations as the charge ends them. Owner ruling (GfNames.ChargeContactGrace): a charging model whose base ends within the grace
    /// of a target model reaches it, and "if it had the movement to make the charge then it should be accepted". Each charging model that ends within
    /// the grace, nearest first, is drawn flush into base contact with the nearest target model (Collision.Abut: straight in by the gap); when none ends
    /// within it and none touches, the nearest charging model that can be is drawn in from wherever it ended. Only an answer that breaks none of
    /// these is drawn in, and a model only where it still breaks none: each path within the allowance round what it may not pass and the difficult
    /// cap (PathBreach, DifficultCapBreach), no base overlapped or model passed through (ContactBreach), the unit's coherency (CoherencyBreach). So base contact is
    /// true contact for the defender's close-in ("all models from the target unit that are not in base contact with a charging model must move").
    /// Each landing is read where the move settles it (Battlefield.Settle), and is drawn only against a target model within the vertical strike reach
    /// of that rest (Gap), and only where its settled flush spot still is.
    /// A stop (Continues) or a disembark is read as it is.
    /// </summary>
    internal static IReadOnlyList<ModelRelocation> ChargeLanding(GameState state, MoveRegion region, Destinations answer, IReadOnlyList<ModelRelocation> moves)
    {
        if (region.ContactWith is not { Count: > 0 } contact || answer.Continues || region.Containment?.ExitingId is not null) return moves;
        var targets = Targets(state, contact);
        if (targets.Count == 0) return moves;
        PlacedModel Body(ModelRelocation m) => Battlefield.At(state, m.ModelId, Battlefield.Settle(state, m.ModelId, ((OnTable)m.To).Placement));
        var gaps = moves.Where(m => m.To is OnTable && Battlefield.Placed(state, m.ModelId) is not null).Select(m => (Move: m, Gap: Gap(state, Body(m), targets))).ToList();
        if (gaps.Count == 0) return moves;
        var apart = gaps.Where(g => g.Gap > Touching && double.IsFinite(g.Gap)).OrderBy(g => g.Gap).ToList();
        var touching = gaps.Count(g => g.Gap <= Touching);
        var inGrace = apart.Where(g => g.Gap <= GfNames.ChargeContactGrace + Touching).ToList();
        var candidates = inGrace.Count > 0 ? inGrace : touching == 0 ? apart : [];
        // Judged without the draw-in (no ContactWith). An answer that already breaks one of these is read as it is.
        var plain = region with { ContactWith = null, ContactGrace = null };
        var bases = TargetUnits(state, region);
        if (candidates.Count == 0 || Breaks(state, plain, moves, bases)) return moves;
        var reading = moves.ToList();
        foreach (var (move, _) in candidates)
        {
            var at = ((OnTable)move.To).Placement;
            var body = Body(move);
            var flush = Collision.Abut(body, body.Placement.Position, new Abutment(InReach(state, body, targets), double.PositiveInfinity), [], [], []);
            var drawn = at with { Position = new Point3(flush.X, flush.Y, at.Position.Z) };
            if (Gap(state, Battlefield.At(state, move.ModelId, Battlefield.Settle(state, move.ModelId, drawn)), targets) > Touching + 1e-6) continue;
            var trial = reading.Select(m => m.ModelId == move.ModelId ? m with { To = new OnTable(drawn) } : m).ToList();
            if (Breaks(state, plain, trial, bases)) continue;
            reading = trial;
            if (inGrace.Count == 0) break;   // the nearest that can be drawn in reaches contact; the rest stay where the player put them
        }
        return reading;
    }

    /// <summary>The plain region's breaches, the charge's target units (`bases`) reached as their models' bases.</summary>
    static bool Breaks(GameState state, MoveRegion plain, IReadOnlyList<ModelRelocation> moves, IReadOnlyList<Guid>? bases)
    {
        var answer = new Destinations(moves);
        return (PathBreach(state, plain, answer) ?? DifficultCapBreach(state, false, plain, answer, bases) ?? ContactBreach(state, plain, answer, bases)
            ?? CoherencyBreach(state, plain, answer)) is not null;
    }

    /// <summary>Within this the bases touch: the defender's close-in reads the same (GfMelee: "not in base contact").</summary>
    const double Touching = 1e-6;

    static List<PlacedModel> Targets(GameState state, IReadOnlyList<Guid> contact) =>
        contact.Where(state.Projection.Models.ContainsKey).Select(id => Battlefield.Placed(state, id)).OfType<PlacedModel>().ToList();

    /// <summary>The plan gap from a landing to the nearest target model within p.9's vertical strike reach of it (GfEdition.MeleeVertical: 3.5.1
    /// "4” vertically", 3.6.0 "3” of an enemy model ... (horizontally or vertically)"); infinite when none is.</summary>
    static double Gap(GameState state, PlacedModel model, IReadOnlyList<PlacedModel> targets) =>
        InReach(state, model, targets).Select(t => Distance.EdgeToEdge(model, t)).DefaultIfEmpty(double.PositiveInfinity).Min();

    /// <summary>The target models whose base is within p.9's vertical strike reach of the model's.</summary>
    static IReadOnlyList<PlacedModel> InReach(GameState state, PlacedModel model, IReadOnlyList<PlacedModel> targets)
    {
        var reach = GfEdition.Of(state).MeleeVertical;
        return targets.Where(t => Distance.Vertical(model, t) <= reach + Touching).ToList();
    }

    /// <summary>Whether a living model of the unit on the table stands in one of these volumes, as a move's path is read for them (Movement.VolumesOnPath, ApplyMove's
    /// "in or through"): any move of that model is in difficult ground (3.5.1 p.11, 3.6.0 p.12). Read by GfMelee.ChargeTargets.</summary>
    public static bool StandsIn(GameState state, Guid unit, IReadOnlyList<WorldVolume> volumes) =>
        volumes.Count > 0 && Lookup.AliveModels(state, unit).Select(id => Battlefield.Placed(state, id)).OfType<PlacedModel>()
            .Any(m => Movement.VolumesOnPath(m, [m.Placement.Position.Horizontal, m.Placement.Position.Horizontal], volumes).Count > 0);

    /// <summary>
    /// The charging unit in base contact and in coherency (UnitFormation.InContact), its moves judged against the Ask's own parameters: the listed
    /// impassable and difficult volumes and the cap, the models standing (none to go round when the region PassesModels), the impassable landing
    /// (none for an Aircraft), the table and p.7's coherency with p.11's 3" at different elevation; the lead's landing rests nearest the target's level
    /// and is contact only within p.9's vertical strike reach (FormationTerms.ContactVertical). Null when none is found.
    /// </summary>
    static IReadOnlyDictionary<Guid, Point3>? ChargeFormation(GameState state, MoveRegion region, IReadOnlyList<(Guid Id, PlacedModel Start)> chargers,
        IReadOnlyList<PlacedModel> targets)
    {
        var p = state.Projection;
        var terrain = Battlefield.Terrain(state);
        var moving = region.ModelIds.ToHashSet();
        var standing = p.Models.Values.Where(x => !moving.Contains(x.Id) && x.Location is OnTable).Select(x => (x.UnitId, At: Battlefield.Placed(state, x.Id)!.Value)).ToList();
        var units = chargers.Select(c => p.Models[c.Id].UnitId).ToHashSet();
        var others = new Others([.. standing.Select(s => s.At)], [.. standing.Select(s => s.UnitId)]);
        var terms = new FormationTerms(region.Allowance, terrain.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList(),
            terrain.Where(v => region.DifficultVolumeIds.Contains(v.Id)).ToList(), region.DifficultCap,
            region.IgnoresTerrain ? [] : Declared(terrain, TerrainDeclarations.Impassable), terrain, state.Board.Table.Size, standing.Select(s => s.At).ToList(),
            region.PassesModels, GfNames.CoherencyChain, GfNames.CoherencyElevated, GfNames.CoherencySpread,
            region.SteepVolumeIds is { Count: > 0 } steep ? terrain.Where(v => steep.Contains(v.Id)).ToList() : null,
            ContactVertical: GfEdition.Of(state).MeleeVertical, Units: others.For(units.First(), region.Separation, TargetUnits(state, region)));
        return UnitFormation.InContact(chargers, standing.Where(s => units.Contains(s.UnitId)).Select(s => s.At).ToList(), targets, terms);
    }
}
