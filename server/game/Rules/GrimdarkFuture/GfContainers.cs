using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// p.15 Transport(X): "When a transport is destroyed, units inside must take a dangerous terrain test, are Shaken, and must be placed
/// fully within 6" of the transport before it's removed." p.21 says the same of a solid building under BUILDING DESTRUCTION. One frame
/// per occupant unit, pushed by deal-wounds' disgorge step, which shelves the wreck once the last of them is out.
/// </summary>
public static class GfContainers
{
    public static Sequence TransportDestroyed() => new(GfNames.TransportDestroyedSeq, [new WreckTestStep(), new PlaceSurvivorsStep()]);

    /// <summary>
    /// Destroyed containers still on the table: `assign` leaves a container holding anybody in place at its Tough capacity, so this is the
    /// whole predicate. A container destroyed empty is an ordinary casualty and is already on the shelf.
    /// </summary>
    public static IReadOnlyList<Guid> Wrecks(GameState state) =>
        state.Projection.Models.Values.Where(m => m.Location is OnTable && GfMoves.IsContainer(state, m.Id)
            && GfAttack.AtCapacity(state, m.Id)).Select(m => m.Id).ToList();

    /// <summary>p.11's dangerous terrain test, invoked by a destroyed transport. Same shape as GfMoves.ActivationTerrainStep: settle the dice on this frame, then push, so the child reads a settled count.</summary>
    sealed class WreckTestStep : IStep
    {
        public string Name => "wreck-test";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0) return new Complete([]);                               // the test has been run
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var dice = GfMoves.DangerousDice(ctx.State, Lookup.AliveModels(ctx.State, unit));
            if (dice == 0) return new Complete([]);
            return ctx.Frame.Value("dangerous_dice") > 0
                ? new Push(GfNames.DangerousTerrain, ctx.Frame.Context)
                : new Complete([], new Dictionary<string, int> { ["dangerous_dice"] = dice }, Repeat: true);
        }
    }

    /// <summary>
    /// p.15: the survivors are Shaken and "placed fully within 6" of the transport". The Ask is a move_region whose containment names
    /// the container being exited, so the client draws the region a disembark move draws. A survivor left out of the answer, or answered
    /// where it is (inside the wreck), has nowhere legal to stand; the rulebook does not cover it (ruling 12), so it is destroyed. A placement
    /// outside the radius, off the table or in impassable terrain is refused under strict (GfMoves.PathBreach, move_exit); under warn and silent it is applied as nowhere
    /// to stand, and that survivor is destroyed too.
    /// That removal is a casualty and writes what `assign` writes for one: wounds at capacity, the model to the shelf, and the wounds on the
    /// unit's activation tally, so p.10's end-of-activation morale test counts it. `Values["destroyed"]` is how many went that way.
    /// </summary>
    sealed class PlaceSurvivorsStep : IStep
    {
        public string Name => "place";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Lookup.ContainerOf(ctx.State, unit) is not { } container) return new Complete([]);     // the test killed everyone
            if (ctx.History.LastClosed() is not { } closed)
                return new OpenAsks([ctx.Ask("disembark", $"Place the survivors fully within {GfNames.ExitWithin:0.#}\" of the wreck", ctx.TeamOf(unit)!.Value,
                    // Allowance is unbound on a disembark (Task 14): ExitWithin is passed so the number is the exit distance. The client reads Containment.
                    new MoveRegion(Lookup.AliveModels(ctx.State, unit), GfNames.ExitWithin,
                        Battlefield.Terrain(ctx.State).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList(),
                        [], null, new MoveContainment([], container, GfNames.ExitWithin)), allowPass: false)]);
            var proposed = GfAnswers.Moves(ctx.State, closed);
            var changes = new List<StateChange> { new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true) };
            var moves = new List<ModelRelocation>(); var destroyed = 0; var fatal = 0;
            foreach (var m in Lookup.AliveModels(ctx.State, unit))
            {
                var to = proposed.FirstOrDefault(r => r.ModelId == m)?.To as OnTable;
                if (to is not null && GfMoves.ExitPlacement(ctx.State, m, container, to.Placement, GfNames.ExitWithin) is { } p)
                { moves.Add(new ModelRelocation(m, new Inside(container), new OnTable(p))); continue; }
                var capacity = Lookup.ModelParameter(ctx.State, m, GfNames.Tough) ?? 1;
                destroyed++; fatal += capacity - ctx.Projection.Models[m].Wounds;
                changes.Add(new WoundsSet(m, capacity));
                moves.Add(new ModelRelocation(m, new Inside(container), new OnShelf()));
            }
            changes.Add(new ModelsMoved(moves));
            ctx.Applied.Add(GfNames.Transport);
            if (fatal > 0) changes.Add(new WoundsThisActivationSet(unit, ctx.Projection.Units[unit].WoundsThisActivation + fatal));
            return new Complete(changes, new Dictionary<string, int> { ["destroyed"] = destroyed });
        }
    }
}
