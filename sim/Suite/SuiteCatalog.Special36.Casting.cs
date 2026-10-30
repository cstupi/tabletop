using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's 3.6.0 casting games (3.6.0 pp.13–15).</summary>
static partial class SpecialGames
{
    // ---- special-cast-mid-move@3.6 -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Caster, Fast, Slow. Red unit 1 (two Fast models from (2, 22), the first a Caster(4), so tokens are left for more spells after the first) Advances first: its first move Ask is answered with
    /// a stop before moving, the window with a cast of Shock Speed on Red unit 2, the next move Ask with 3" along the table and every later
    /// window with a pass. Red unit 2 (two models from (5, 14)) stands 7.3" from the caster behind a wall (y 17–18, x 0–12, 6" tall, sight
    /// blocking) and holds; Red unit 3 (two Slow models from (20, 22)) Advances. Blue unit 1 holds and has no gun. The casting roll is fixed to 5.
    /// </summary>
    static TestGame CastMidMove36()
    {
        const string name = "special-cast-mid-move";
        var asked = new Dictionary<Guid, int>();
        var script = Plays((ask, _) => AskPicks.Activate(ask, "Red unit 1", "Advance") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Activate(ask, "Red unit 3", "Advance")
                ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"),
            ("react", (ask, p) => ask.Prompt != "Before attacking" ? null
                : ShooterOf(ask, p) == "Red unit 1" && ask.FrameId is { } f && asked.GetValueOrDefault(f) == 1 && ask.Legal is Options o && o.Choices.Any(c => c.Id == "cast") ? new Chosen("cast") : new Passed()),
            ("cast", (ask, p) => AskPicks.Spell(ask, p, "Shock Speed", "Red unit 1")),
            ("spell_targets", (ask, p) => AskPicks.Pieces(ask, p, "Red unit 2")),
            (AskQuestions.SpendTokens, (_, _) => new TokensSpent(new Dictionary<Guid, int>())),
            ("move", (ask, p) =>
            {
                if (ask.Legal is not MoveRegion { Containment: null, ContactWith: null } region || Scripts36.UnitOf(p, region) != "Red unit 1" || ask.FrameId is not { } frame) return null;
                var n = asked[frame] = asked.GetValueOrDefault(frame) + 1;
                return n switch
                {
                    1 => new Destinations([], Continues: true),
                    2 => new Destinations([.. region.ModelIds.Select(id => Scripts36.MoveBy(p, id, 3, 0))]),
                    _ => null,
                };
            }));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(1).Table(36, 24).Scripted(script)
                .Army("Red", 3, 2, modelRules: [new(1, "Fast"), new(3, "Slow")], weapons: [Gun("Rifle", 24, 1)], unitStats: Stats(4, 4), armyBook: "z65fgu0l29i4lnlu",
                    models: [new ScenarioModel(1, 1, [new(0, "Caster", 4)])], anchors: new Dictionary<int, Point2> { [1] = new(2, 22), [2] = new(5, 14), [3] = new(20, 22) })
                .Army("Blue", 1, 3, weapons: [Blade("Bayonet", 1)], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(30, 2) })
                .Terrain("wall", new Point3(6, 17.5, 0), volumes:
                    [new Volume(VolumeKind.Solid, Footprint.Rect(12, 1), new Point2(0, 0), new Yaw(0), 0, 6, TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight, null)])
                .Build(),
            ["Caster", "Fast", "Slow"],
            [
                Exact("3.6.0 p.13 Caster(X): \"Once per activation, before attacking, spend as many tokens as the spell's value to try casting one or more spells\" (a stop, a cast, a move: no second window)", ["Caster"], run =>
                {
                    var log = LogQueries.Of(run);
                    var caster = log.UnitNamed("Red unit 1");
                    return LogQueries.Each(log.FramesOf("advance").Where(f => f.Context.ActorUnitId == caster).ToList(), "an Advance by the caster", f =>
                    {
                        var casts = log.FramesOf("casting").Where(c => c.Ancestors.Contains(f)).ToList();
                        if (casts.Count == 0) return "it cast nothing";
                        var moved = log.Steps.FirstOrDefault(s => s.Frame == f && s.Name == "move" && s.Seq > casts[0].Seq && s.Step.Changes.OfType<ModelsMoved>().Any());
                        if (moved is null) return "it never moved after its cast";
                        var reopened = log.AsksOf("react").FirstOrDefault(a => a.Seq > moved.Seq && a.Frame is { } at && (at == f || at.Ancestors.Contains(f)) && a.Ask.Legal is Options o && o.Choices.Any(c => c.Id == "cast"));
                        return reopened is null ? null : $"a cast was offered again at seq {reopened.Seq}, after the move";
                    });
                }),
                Exact("3.6.0 p.13 Caster(X): \"Pick a target and roll one die, and on 4+ resolve the spell's effect\" (no line of sight: Shock Speed on a unit behind a wall)", ["Caster"], run =>
                {
                    var log = LogQueries.Of(run);
                    var hidden = log.UnitNamed("Red unit 2");
                    return LogQueries.Each(log.AsksOf("spell_targets").Where(a => a.Frame?.Context.ActorUnitId == log.UnitNamed("Red unit 1")).ToList(), "a spell target pick by Red unit 1", a =>
                        a.Ask.Legal is PieceSelection s && s.Eligible.Contains(hidden) && a.Answer is SelectedPieces { PieceIds: [var picked] } && picked == hidden ? null : "the unit out of sight was not offered and picked");
                }),
                Exact("3.6.0 p.14 Fast: \"Models with this special rule move +2” when using Advance and +4” when using Rush/Charge\"", ["Fast"], run =>
                    Each(Moves(run).Where(m => m.Own && m.Unit == "Red unit 1" && m.Action is "Advance" or "Rush" or "Charge"), "a Fast move", m =>
                        m.Region.Allowance == (m.Action == "Advance" ? 8 : 16) ? null : $"{m.Action}: allowance {m.Region.Allowance}")),
                Exact("3.6.0 p.15 Slow: \"Models with this special rule move -2” when using Advance, and -4” when using Rush/Charge\"", ["Slow"], run =>
                    Each(Moves(run).Where(m => m.Own && m.Unit == "Red unit 3" && m.Action is "Advance" or "Rush" or "Charge"), "a Slow move", m =>
                        m.Region.Allowance == (m.Action == "Advance" ? 4 : 8) ? null : $"{m.Action}: allowance {m.Region.Allowance}")),
            ],
            dice: [new("casting", 0, [5], new RollKey(Actor: "Red unit 1"))]);
    }
}
