using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class SightSummaryTests
{
    static readonly Guid PA = Guid.CreateVersion7(), T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7();
    static Weapon Gun(string name) => new(Guid.CreateVersion7(), name, new Dictionary<string, string>(), []);
    static ModelSlot Slot(int ordinal, Weapon w) => new(Guid.CreateVersion7(), ordinal, "Trooper", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, [w]);
    static readonly Weapon R1 = Gun("Rifle"), R2 = Gun("Rifle"), P1 = Gun("Plasma Gun");
    static readonly Unit Shooters = new(Guid.CreateVersion7(), 1, "Blue Warriors", new Dictionary<string, string>(), [], [Slot(1, R1), Slot(2, R2), Slot(3, P1)]);
    static readonly Board Board = new(TableStateTests.Sample(),
        [new ArmyInPlay(PA, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Blue", "grimdark-future", "test", [Shooters]), Guid.CreateVersion7()))]);
    static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, PA, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(Guid.CreateVersion7(), 1)], [new Participant(PA, Guid.CreateVersion7(), "Clay", null, null)]);
    static Projection P => new EventApplier(Board, Game).Projection;
    static Guid M(int i) => Shooters.Models[i].Id;
    static readonly AskOption Rifles = new("Rifle", "Rifle", [R1.Id, R2.Id]), Plasma = new("Plasma Gun", "Plasma Gun", [P1.Id]);

    static SightLine Line(Guid from, bool clear, bool screened) => new(from, Guid.CreateVersion7(), clear, screened, clear ? [] : [Guid.CreateVersion7()]);

    /// <summary>The client counts lines; whether the unit is in cover is the rules' (p.11's majority), so the summary never says so.</summary>
    [Fact]
    public void The_summary_counts_seeing_models_and_screened_clear_lines()
    {
        var answer = new SightAnswer(Guid.CreateVersion7(), [Line(M(0), true, true), Line(M(0), true, false), Line(M(1), true, true), Line(M(2), false, false)], [M(0)]);
        Assert.Equal("2 of 3 models can see Red Warriors · 2 of 3 clear lines screened", SightSummary.Text(answer, "Red Warriors", shooting: false));
        Assert.Equal("2 of 3 models can see Red Warriors · 2 of 3 clear lines screened · 1 can shoot", SightSummary.Text(answer, "Red Warriors", shooting: true));
        var open = new SightAnswer(Guid.CreateVersion7(), [Line(M(0), true, false), Line(M(1), false, false)], []);
        Assert.Equal("1 of 2 models can see Red Warriors · 0 of 1 clear lines screened", SightSummary.Text(open, "Red Warriors", shooting: false));
        var blind = new SightAnswer(Guid.CreateVersion7(), [Line(M(0), false, false)], []);
        Assert.Equal("0 of 1 models can see Red Warriors", SightSummary.Text(blind, "Red Warriors", shooting: false));
    }

    /// <summary>A model that sees the target may still be out of range: the summary names each range a rule changed against this target, and the rules.</summary>
    [Fact]
    public void Under_a_target_pick_the_summary_names_the_ranges_rules_changed_against_the_target()
    {
        var lines = new[] { Line(M(0), true, false), Line(M(1), true, false), Line(M(2), true, false) };
        var answer = new SightAnswer(Guid.CreateVersion7(), lines, [M(2)],
            [new ShotRange("Carbine", 18, 14, ["Shadowborn"]), new ShotRange("Plasma Rifle", 24, 20, ["Shadowborn"]), new ShotRange("Pistol", 12, 12, [])]);
        Assert.Equal("3 of 3 models can see Reapers · 0 of 3 clear lines screened · 1 can shoot · Carbine 18\" → 14\", Plasma Rifle 24\" → 20\" (Shadowborn)",
            SightSummary.Text(answer, "Reapers", shooting: true));
        Assert.Equal("3 of 3 models can see Reapers · 0 of 3 clear lines screened", SightSummary.Text(answer, "Reapers", shooting: false));
        var half = answer with { Ranges = [new ShotRange("Carbine", 18, 13.5, ["Shadowborn", "Long Shot"])] };
        Assert.EndsWith(" · 1 can shoot · Carbine 18\" → 13.5\" (Shadowborn, Long Shot)", SightSummary.Text(half, "Reapers", shooting: true));
        Assert.EndsWith(" · 1 can shoot", SightSummary.Text(answer with { Ranges = null }, "Reapers", shooting: true));
    }

    /// <summary>Spec → Shooting targets: models that cannot shoot the hovered or the assigned target are dimmed.</summary>
    [Fact]
    public void Dimmed_are_the_models_that_cannot_shoot_the_hovered_target_or_their_groups_assigned_one()
    {
        var hovered = new SightAnswer(Guid.CreateVersion7(), [Line(M(0), true, false), Line(M(1), true, false), Line(M(2), true, false)], [M(0), M(2)]);
        Assert.Equal(new HashSet<Guid> { M(1) }, SightSummary.Dimmed(P, hovered, [Rifles, Plasma], new Dictionary<string, Guid>(), new Dictionary<Guid, SightAnswer>()));

        var assigned = new Dictionary<string, Guid> { ["Rifle"] = T1, ["Plasma Gun"] = T2 };
        var answers = new Dictionary<Guid, SightAnswer>
        {
            [T1] = new(Guid.CreateVersion7(), [], [M(0), M(2)]),          // the plasma model could shoot T1, but its group is aimed at T2
            [T2] = new(Guid.CreateVersion7(), [], [M(0)]),
        };
        Assert.Equal(new HashSet<Guid> { M(1), M(2) }, SightSummary.Dimmed(P, null, [Rifles, Plasma], assigned, answers));
        Assert.Empty(SightSummary.Dimmed(P, null, [Rifles, Plasma], assigned, new Dictionary<Guid, SightAnswer>()));   // no answer yet: nothing dimmed
    }
}
