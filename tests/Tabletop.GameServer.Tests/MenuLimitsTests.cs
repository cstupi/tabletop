using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>Security review H4 (menu strings) and L4 (hand moves and markers): what the override path accepts.</summary>
public class MenuLimitsTests
{
    static readonly Actor Clay = new(Samples.P1, Samples.Team1);

    sealed class PropertiesEngine(PropertySet properties) : IRulesEngine
    {
        public Legality Judge(GameState state, Command command, Actor actor) => Legality.Legal;
        public EngineResult Execute(GameState state, Command command, Actor actor) => Emit.None;
        public Emit Advance(GameState state) => Emit.None;
        public PropertySet? Properties => properties;
    }

    static GameState State()
    {
        var info = Samples.Game with { Enforcement = Enforcement.Silent };
        var board = Samples.Board;
        return new GameState(info, board, new EventApplier(board, info), GrimdarkFutureRules.Instance);
    }

    static DispatchOutcome Dispatch(Command c) => new CommandDispatcher(new PropertiesEngine(TestProperties.Core)).Dispatch(State(), c, Clay);
    static void Refuses(Command c) => Assert.IsType<Refused>(Dispatch(c));
    static void Applies(Command c) { var o = Dispatch(c); Assert.True(o is Applied, (o as Refused)?.Detail); }

    static readonly CatalogEntry Spell = TestProperties.Core.Entries.First(e => e.Effects.Count > 0);

    [Fact]
    public void An_action_must_be_one_the_property_set_declares()
    {
        Applies(new RecordAction(Samples.Unit, GfNames.Advance));
        Refuses(new RecordAction(Samples.Unit, "Teleport"));
    }

    [Fact]
    public void A_grant_must_name_an_entry_and_one_of_its_effects()
    {
        var effect = TestProperties.Core.AllEffects().First(e => Spell.Effects.Contains(e));
        Applies(new AddGrant(Samples.Unit, new Grant(Guid.NewGuid(), effect.Id, Spell.Name, Samples.Unit, 1)));
        Refuses(new AddGrant(Samples.Unit, new Grant(Guid.NewGuid(), effect.Id, "No Such Rule", Samples.Unit, 1)));
        Refuses(new AddGrant(Samples.Unit, new Grant(Guid.NewGuid(), Guid.NewGuid(), Spell.Name, Samples.Unit, 1)));
        Refuses(new AddGrant(Samples.Unit, new Grant(Guid.NewGuid(), effect.Id, Spell.Name, Guid.NewGuid(), 1)));
        Refuses(new AddGrant(Samples.Unit, new Grant(Guid.NewGuid(), effect.Id, Spell.Name, Samples.Unit, -1)));
    }

    /// <summary>Conditions are free names by design (game flow: "anything else the unit currently is"; the menu's Add a condition is free
    /// text), so a condition is bounded, not looked up.</summary>
    [Fact]
    public void A_condition_is_a_short_name()
    {
        Applies(new SetCondition(new Subject(SubjectKind.Unit, Samples.Unit), "pinned down", true));
        Refuses(new SetCondition(new Subject(SubjectKind.Unit, Samples.Unit), new string('c', 65), true));
        Refuses(new SetCondition(new Subject(SubjectKind.Unit, Samples.Unit), " ", true));
    }

    [Fact]
    public void A_placed_marker_is_near_the_table_finite_and_bounded()
    {
        Applies(new PlaceMarker(Samples.Token with { Id = Guid.NewGuid() }));
        Refuses(new PlaceMarker(Samples.Token with { Id = Guid.NewGuid(), Label = new string('l', 65) }));
        Refuses(new PlaceMarker(Samples.Token with { Id = Guid.NewGuid(), Position = new Point3(double.NaN, 1, 0) }));
        Refuses(new PlaceMarker(Samples.Token with { Id = Guid.NewGuid(), Position = new Point3(500, 1, 0) }));
        Refuses(new PlaceMarker(Samples.Token with { Id = Guid.NewGuid(), Diameter = 100 }));
        Refuses(new PlaceMarker(Samples.Token with { Id = Guid.NewGuid(), Yaw = new Yaw(double.PositiveInfinity) }));
        var placed = Samples.Board.Table.Markers[0].Id;
        Refuses(new MoveMarker(placed, new Point3(3, double.NegativeInfinity, 0), new Yaw(0)));
        Refuses(new MoveMarker(placed, new Point3(-100, 3, 0), new Yaw(0)));
    }

    [Fact]
    public void A_hand_move_lands_finite_and_near_the_table()
    {
        var model = Samples.Model;
        MoveModels To(double x, double y, double yaw = 0) => new([new ModelRelocation(model, new OnShelf(), new OnTable(new Placement(new Point3(x, y, 0), new Yaw(yaw))))]);
        Applies(To(10, 10));
        Refuses(To(double.NaN, 10));
        Refuses(To(10, 10, double.NaN));
        Refuses(To(1e9, 10));
    }

    [Fact]
    public void Menu_numbers_are_bounded()
    {
        Refuses(new SetWounds(Samples.Model, 1_000_000));
        Refuses(new SetWounds(Samples.Model, -1));
        Refuses(new SetRound(int.MaxValue));
        Refuses(new SetScore(Samples.Team1, int.MinValue));
        Applies(new SetRound(3));
    }
}
