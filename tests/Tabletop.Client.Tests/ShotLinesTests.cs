using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class ShotLinesTests
{
    static readonly Guid PA = Guid.CreateVersion7(), PB = Guid.CreateVersion7();
    static Weapon Gun(string name) => new(Guid.CreateVersion7(), name, new Dictionary<string, string>(), []);
    static ModelSlot Slot(int ordinal, params Weapon[] w) => new(Guid.CreateVersion7(), ordinal, "Trooper", new Dictionary<string, string>(), [], Base.Circle(0.5), 2, null, w);
    static readonly Weapon R1 = Gun("Rifle"), R2 = Gun("Rifle"), P1 = Gun("Plasma Gun");
    static readonly Unit Shooters = new(Guid.CreateVersion7(), 1, "Blue Warriors", new Dictionary<string, string>(), [], [Slot(1, R1), Slot(2, R2), Slot(3, P1)]);
    static readonly Unit Targets = new(Guid.CreateVersion7(), 1, "Red Warriors", new Dictionary<string, string>(), [], [Slot(1), Slot(2)]);
    static readonly Board Board = new(TableStateTests.Sample(),
    [
        new ArmyInPlay(PA, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Blue", "grimdark-future", "test", [Shooters]), Guid.CreateVersion7())),
        new ArmyInPlay(PB, ArmySnapshot.Of(new Army(Guid.CreateVersion7(), "Red", "grimdark-future", "test", [Targets]), Guid.CreateVersion7())),
    ]);
    static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, PA, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 1, [new Team(Guid.CreateVersion7(), 1), new Team(Guid.CreateVersion7(), 2)],
        [new Participant(PA, Guid.CreateVersion7(), "A", null, null), new Participant(PB, Guid.CreateVersion7(), "B", null, null)]);

    long _seq;
    readonly EventApplier _applier = new(Board, Game);

    void Apply(EventPayload payload) => _applier.Apply(new GameEvent(++_seq, null, payload, null));

    void Place(Guid model, double x, double y) =>
        Apply(new ModelsMoved([new ModelRelocation(model, new OnShelf(), new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))]));

    void Start(string sequence, Guid actor, Guid target, params Guid[] weapons) =>
        Apply(new SequenceStarted(Guid.CreateVersion7(), sequence, new SequenceContext(actor, [target], weapons, "Hold")));

    public ShotLinesTests()
    {
        Place(Shooters.Models[0].Id, 10, 10); Place(Shooters.Models[1].Id, 12, 10); Place(Shooters.Models[2].Id, 14, 10);
        Place(Targets.Models[0].Id, 10, 30); Place(Targets.Models[1].Id, 20, 30);
    }

    [Fact]
    public void A_shooting_group_fires_from_each_model_carrying_its_weapons_to_the_nearest_target_model_at_half_height()
    {
        Start("shooting", Shooters.Id, Targets.Id, R1.Id, R2.Id, P1.Id);
        Start("attack-sequence", Shooters.Id, Targets.Id, R1.Id, R2.Id);
        var shots = ShotLines.Of(_applier.Projection);
        Assert.Equal([Shooters.Models[0].Id, Shooters.Models[1].Id], shots.Select(s => s.ShooterModelId).Order());
        var first = shots.Single(s => s.ShooterModelId == Shooters.Models[0].Id);
        Assert.Equal(Shooters.Id, first.ShooterUnitId);
        Assert.Equal(new Point3(10, 10, 1), first.From);
        Assert.Equal(new Point3(10, 30, 1), first.To);
    }

    [Fact]
    public void Melee_and_the_shooting_frame_itself_fire_nothing()
    {
        Start("shooting", Shooters.Id, Targets.Id, R1.Id);
        Assert.Empty(ShotLines.Of(_applier.Projection));
        Start("melee", Shooters.Id, Targets.Id, R1.Id);
        Start("attack-sequence", Shooters.Id, Targets.Id, R1.Id);
        Assert.Empty(ShotLines.Of(_applier.Projection));
    }

    [Fact]
    public void Models_off_the_table_neither_fire_nor_are_aimed_at()
    {
        Apply(new ModelsMoved([new ModelRelocation(Targets.Models[0].Id, new OnTable(new Placement(new Point3(10, 30, 0), new Yaw(0))), new OnShelf())]));
        Start("shooting", Shooters.Id, Targets.Id, R1.Id);
        Start("attack-sequence", Shooters.Id, Targets.Id, R1.Id);
        Assert.Equal(new Point3(20, 30, 1), Assert.Single(ShotLines.Of(_applier.Projection)).To);
    }
}
