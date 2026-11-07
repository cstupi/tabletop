using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The activate Ask says why a unit's actions are limited: every option of a limited unit carries the rule's note.</summary>
public class GfActivationNoteTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static IReadOnlyList<AskOption> Offered(GameState state, Guid teamId)
    {
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new PhaseStarted(GfNames.Rounds), null));
        return Assert.IsType<Options>(GrimdarkFutureRules.Instance.LegalTurn(state, Core, teamId)!.Legal).Choices;
    }

    static string? NoteOf(IReadOnlyList<AskOption> options, Guid unitId)
    {
        var mine = options.Where(o => o.Pieces.Contains(unitId)).ToList();
        Assert.NotEmpty(mine);
        Assert.Single(mine.Select(o => o.Note).Distinct());                                         // one note per unit, on each of its options
        return mine[0].Note;
    }

    [Fact]
    public void Each_limitation_carries_its_rule_and_page()
    {
        var plain = Unit("Plain", "4+", "4+", [], 1, []);
        var aircraft = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var artillery = Unit("Gun", "4+", "4+", [P(GfNames.Artillery)], 1, []);
        var immobile = Unit("Bunker", "4+", "4+", [P(GfNames.Immobile)], 1, []);
        var shaken = Unit("Shaken", "4+", "4+", [], 1, []);
        var state = State([plain, aircraft, artillery, immobile, shaken], [Unit("Enemy", "4+", "4+", [], 1, [])], x1: -12, x2: 12);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ConditionSet(TestGames.UnitSubject(shaken.Id), GfNames.Shaken, true), null));
        var options = Offered(state, Tabletop.Protocol.Tests.Samples.Team1);
        Assert.Equal("No Charge: no enemy unit within charge distance", NoteOf(options, plain.Id));
        Assert.Equal("Aircraft: may only use Advance actions, even if Shaken", NoteOf(options, aircraft.Id));
        Assert.Equal("Artillery: may only use Hold actions", NoteOf(options, artillery.Id));
        Assert.Equal("Immobile: may only use Hold actions", NoteOf(options, immobile.Id));
        Assert.Equal("Shaken: must spend this activation being idle, which stops it being Shaken at the end of the activation", NoteOf(options, shaken.Id));
    }

    [Fact]
    public void An_unlimited_unit_carries_no_note()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var state = State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -4, x2: 4);              // within charge distance
        var options = Offered(state, Tabletop.Protocol.Tests.Samples.Team1);
        Assert.Equal(4, options.Count);
        Assert.Null(NoteOf(options, a.Id));
    }
}
