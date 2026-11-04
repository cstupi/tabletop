using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

static partial class SpecialGames
{
    const double MidMoveTableY = 30;

    /// <summary>
    /// p.13 Caster(X): "At any point before attacking" — Red's casters stop their moves (casting during a move spec), one unit Fast and one
    /// Slow, so the allowance a stop carries over is a modified one. The first move Ask of an
    /// activation is answered with a stop before moving and the window with a cast; the next with a 1" stop toward the middle and the window
    /// with a pass; the rest go to the policy. Red wins the roll-off (fixed) and every unit deploys on its anchor, 20" from the enemy, so each caster
    /// makes its move before anything can shoot it.
    /// </summary>
    static TestGame CastMidMove()
    {
        const string name = "special-cast-mid-move";
        var asked = new Dictionary<Guid, int>();
        var script = AnswerScript.Build();
        for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
        // Red's casters always move: unit 1 Advances (Slow), unit 2 Rushes (Fast).
        script.Match("activate", (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Advance") ?? AskPicks.Activate(ask, "Red unit 2", "Rush"));
        // Red unit 1 casts Shock Speed on its own unit at the stop before moving (its casting die fixed to 6): "moves +2" when using Advance actions …
        // once (next time the effect would apply)", and the next time is the rest of the Advance it is making. Red unit 2 (two tokens) casts what it can.
        script.Match("cast", (ask, p) => AskPicks.Spell(ask, p, "Shock Speed", "Red unit 1"));
        script.Match("spell_targets", (ask, p) => p.Stack.LastOrDefault(f => f.Sequence == "casting")?.Context.ActorUnitId is { } caster
            && AskPicks.UnitNameOf(p, caster) == "Red unit 1" ? AskPicks.Pieces(ask, p, "Red unit 1") : null);
        // After the stop before moving, cast; after the part-way stop, pass, so only the stop carries the move on.
        script.Match("react", (ask, _) => ask.Prompt != "Before attacking" || ask.FrameId is not { } frame ? null
            : asked.GetValueOrDefault(frame) == 1 ? (ask.Legal is Options o && o.Choices.Any(c => c.Id == "cast") ? new Chosen("cast") : null)
            : asked.GetValueOrDefault(frame) == 2 ? new Passed() : null);
        script.Match("move", (ask, p) =>
        {
            if (ask.Legal is not MoveRegion { Stoppable: true, Containment: null } region || ask.FrameId is not { } frame) return null;
            var n = asked[frame] = asked.GetValueOrDefault(frame) + 1;
            return n switch
            {
                1 => new Destinations([], Continues: true),
                2 => region.ModelIds.Where(id => p.Models[id].Location is OnTable).Select(id => Toward(p.Models[id], MidMoveTableY / 2)).ToList() is var moves
                    && Clear(p, moves) ? new Destinations(moves, Continues: true) : null,
                _ => null,
            };
        });
        return Game(name, new ScenarioBuilder(name, 216).RoundCap(3).Table(36, MidMoveTableY).Scripted(script.Build())
                .Army("Red", 2, 2, modelRules: [new(1, "Caster", 3), new(1, "Slow"), new(2, "Caster", 2), new(2, "Fast")], weapons: [Gun("Rifle", 24, 1), Blade("Bayonet", 1)], unitStats: Stats(4, 4),
                    armyBook: "z65fgu0l29i4lnlu", anchors: new Dictionary<int, Point2> { [1] = new(6, 25), [2] = new(24, 25) })
                .Army("Blue", 2, 3, weapons: [Gun("Rifle", 24, 1), Blade("Bayonet", 1)], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(6, 5), [2] = new(24, 5) })
                .Seeking(NearestEnemy)
                .Build(),
            ["Caster", "Fast", "Slow"],
            [
                Exact("p.13 Caster(X): \"At any point before attacking\": a stop before moving opens the window, moves nothing and keeps the whole move; Shock Speed cast there on the moving unit adds its \"moves +2\" when using Advance actions and +4\" when using Rush/Charge actions\" to the rest of it", ["Caster"], run =>
                {
                    var log = LogQueries.Of(run);
                    return LogQueries.Each(Stops(log).Where(s => s.Ask.Answer is Destinations { Moves.Count: 0 }), "a stop before moving", s =>
                    {
                        if (s.Window?.Ask.Prompt != "Before attacking") return $"no window titled Before attacking after the stop (got {s.Window?.Ask.Prompt ?? "none"})";
                        if (s.Step?.Step.Changes.OfType<ModelsMoved>().Any() == true) return "the stop moved models";
                        if (s.Step?.Step.Changes.OfType<ConditionSet>().Any(c => c.Condition == "moved" && c.Present) == true) return "the stop set moved";
                        if (s.Next is not { } next) return "the move never asked again";
                        var expected = Allowance(s.Ask) + ShockSpeedOn(log, s);
                        return Math.Abs(Allowance(next) - expected) < 0.05 ? null : $"allowance {Allowance(s.Ask)} then {Allowance(next)}, expected {expected}";
                    }) ?? (Stops(log).Any(s => s.Ask.Answer is Destinations { Moves.Count: 0 } && ShockSpeedOn(log, s) > 0) ? null : "no Shock Speed on the moving unit at a stop before moving");
                }),
                Exact("p.13 Caster(X): \"At any point before attacking\": a cast inside a stopped move, then the move goes on", ["Caster"], run =>
                {
                    var log = LogQueries.Of(run);
                    var casts = Stops(log).Where(s => s.Next is not null && log.FramesOf("casting").Any(c => c.Parent == s.Ask.Frame && c.Seq > s.Ask.Seq && c.Seq < s.Next!.Seq)).ToList();
                    return casts.Count > 0 ? null : "no cast ran between a stop and the rest of its move";
                }),
                Exact("p.13 Caster(X): \"At any point before attacking\": a stop part-way, then a pass, leaves what is left of the move", ["Caster"], run =>
                {
                    var log = LogQueries.Of(run);
                    return LogQueries.Each(Stops(log).Where(s => s.Ask.Answer is Destinations { Moves.Count: > 0 }), "a stop part-way", s =>
                    {
                        if (s.Step is not { } step || s.Next is not { } next) return s.Window?.Answer is Passed ? "a pass after the stop ended the move" : "the move never asked again";
                        var expected = Allowance(s.Ask) - (step.Value("distance_used_tenths") - Before(log, step)) / 10.0;
                        return Math.Abs(Allowance(next) - expected) < 0.05 ? null : $"allowance {Allowance(s.Ask)} with {expected} left, then {Allowance(next)}";
                    });
                }),
            ], dice: [new("roll-off", 0, [6, 1]), new("casting", 0, [6], new RollKey(Actor: "Red unit 1"))]);
    }

    /// <summary>The model moved 1" straight toward the line y = <paramref name="middle"/>, facing as it faces.</summary>
    static ModelRelocation Toward(ModelInPlay m, double middle)
    {
        var at = ((OnTable)m.Location).Placement;
        var dy = Math.Sign(middle - at.Position.Y);
        return new ModelRelocation(m.Id, m.Location, new OnTable(at with { Position = at.Position with { Y = at.Position.Y + dy } }));
    }

    /// <summary>The 1" stop lands on no base and over 1" from every other unit (p.7); where it would not, the policy moves instead.</summary>
    static bool Clear(Projection p, IReadOnlyList<ModelRelocation> moves)
    {
        static PlacedModel Body(ModelInPlay m, Placement at) => new(new ModelBody(m.Slot.Base, m.Slot.Height), at);
        var moved = moves.ToDictionary(m => m.ModelId, m => ((OnTable)m.To).Placement);
        var bodies = p.Models.Values.Where(m => m.Location is OnTable)
            .Select(m => (m.Id, m.UnitId, At: Body(m, moved.GetValueOrDefault(m.Id, ((OnTable)m.Location).Placement)))).ToList();
        return moves.All(mv => bodies.First(b => b.Id == mv.ModelId) is var me
            && bodies.All(o => o.Id == me.Id || (o.UnitId == me.UnitId ? !Overlap.Bases(me.At, o.At) : Distance.BodyToBody(me.At, o.At) >= 1)));
    }

    sealed record Stop(LoggedAsk Ask, LoggedStep? Step, LoggedAsk? Window, LoggedAsk? Next);

    /// <summary>Every move Ask answered as a stop, with the move step that applied it, the window after it and the next move Ask of its frame.</summary>
    static IEnumerable<Stop> Stops(LogQueries log) =>
        log.AsksOf("move").Where(a => a.Answer is Destinations { Continues: true } && a.Frame is not null).Select(a =>
        {
            var next = log.AsksOf("move").FirstOrDefault(b => b.Frame == a.Frame && b.Seq > a.Seq);
            var window = log.AsksOf("react").FirstOrDefault(r => r.Frame == a.Frame && r.Seq > a.Seq && (next is null || r.Seq < next.Seq));
            var step = log.Steps.FirstOrDefault(s => s.Step.Step == "move" && s.Asks.Any(x => x.Id == a.Ask.Id));
            return new Stop(a, step, window, next);
        });

    static double Allowance(LoggedAsk ask) => ((MoveRegion)ask.Ask.Legal).Allowance;

    /// <summary>Shock Speed's literal bonus for the move a stop interrupts, when a cast of it in the stop's window granted it to the moving unit: +2" on an
    /// Advance, +4" on a Rush or Charge; 0 otherwise.</summary>
    static double ShockSpeedOn(LogQueries log, Stop s)
    {
        if (s.Next is not { } next || s.Ask.Frame is not { } frame || frame.Context.ActorUnitId is not { } unit) return 0;
        var granted = SpellGames.Casts(log, "Shock Speed").Any(c => c.Passed && c.Frame.Parent == frame && c.Frame.Seq > s.Ask.Seq && c.Frame.Seq < next.Seq
            && c.Grants.Any(g => g.UnitId == unit));
        return !granted ? 0 : frame.Sequence == "advance" ? 2 : 4;
    }

    /// <summary>The move's distance used before this step: the last earlier move step of the frame that recorded it.</summary>
    static int Before(LogQueries log, LoggedStep step) =>
        log.Steps.LastOrDefault(s => s.Frame == step.Frame && s.Step.Step == "move" && s.Seq < step.Seq && s.Step.Values?.ContainsKey("distance_used_tenths") == true)?.Value("distance_used_tenths") ?? 0;
}
