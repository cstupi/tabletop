using System.Text.Json;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using Xunit.Abstractions;

namespace Tabletop.Client.Tests;

/// <summary>The owner's game 01a126db, seq 245 (its round_started) and 251–354: one charge, the strike-back and the morale test
/// (combat-log spec → Why). No projection: units read as short ids, and no team or `moved` is known for them.</summary>
public class CombatLogFixtureTests(ITestOutputHelper output)
{
    static readonly Guid Team1 = Guid.Parse("01a126db-2140-7d0e-a608-24364b5444d4"), Team2 = Guid.Parse("01a126db-22e7-7ef0-8aa1-0c74483c322c");
    static readonly Guid Charger = Guid.Parse("01a126db-1b48-70ac-ac26-3195d4181a43"), Target = Guid.Parse("01a126db-18ba-702b-b0e9-9d9843265edf");

    static readonly GameInfo Game = Samples.Game with
    {
        Teams = [new Team(Team1, 1), new Team(Team2, 2)],
        Participants =
        [
            new Participant(Guid.Parse("01a126db-1bb1-77b2-9c9e-cef42380f3c4"), Guid.Parse("56f6345d-edfd-8cde-8e23-36dfe37b6125"), "e2e-alpha", Team1, null),
            new Participant(Guid.Parse("01a126db-1be8-72bb-96a5-733a1d8ed4db"), Guid.Parse("3deb2ca5-9b07-80af-a898-16e0780f18b8"), "e2e-bravo", Team2, null),
        ],
    };

    static IReadOnlyList<GameEvent> Log()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "game-01a126db-charge.json")));
        return doc.RootElement.EnumerateArray().Select(e => WireJson.Deserialize<GameEvent>(e.GetRawText())).ToList();
    }

    static IEnumerable<LogEntry> Tree(IEnumerable<LogEntry> entries) =>
        entries.SelectMany(e => e is ActivationBlock b ? [e, .. Tree(b.Entries)] : new[] { e });

    static string Text(LogEntry e) => e switch
    {
        ActivationBlock b => b.Header, RollEntry r => r.Collapsed(CombatLog.Pip), LineEntry l => l.Text, _ => "",
    };

    [Fact]
    public void The_fixture_reads_back_as_events()
    {
        var log = Log();
        Assert.Equal(105, log.Count);
        Assert.Equal(2, Assert.IsType<RoundStarted>(log[0].Payload).Round);   // the header's R2
        Assert.Equal(354L, log[^1].Seq);
    }

    [Fact]
    public void The_charge_is_one_block_of_rolls_and_changes()
    {
        var entries = CombatLog.Build(Log(), new Names(Game, null));
        foreach (var e in Tree(entries)) output.WriteLine($"{e.Seq,4} {e.GetType().Name,-15} {Text(e)}");

        Assert.Equal(["Round 2"], entries.OfType<LineEntry>().Select(l => l.Text));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.Equal($"R2 · {Names.Short(Charger)} · Charge → {Names.Short(Target)}", block.Header);
        Assert.Equal(Charger, block.Actor);                                  // every id of this game reads 01a126db: compare by Guid
        Assert.Equal([Target], block.Targets);

        Assert.DoesNotContain(Tree(entries).OfType<LineEntry>(), l => l.Event.Payload is AskOpened or AskAnswered or AskClosed
            or SequenceStarted { Context.Action: null } or SequenceFinished or SequenceAbandoned or StepCompleted { Rolls.Count: 0, Changes.Count: 0 });

        var rolls = Tree(block.Entries).OfType<RollEntry>().Select(r => r.Collapsed(CombatLog.Pip)).ToList();
        string[] expected =
        [
            "Hit · 3+ · 8 dice: ⚁ ⚅ ⚀ ⚅ ⚅ ⚀ ⚄ ⚀ · 4 hits",
            "Block · 5+ (AP(1), AP(4)) → needs 6 · 3 dice: ⚅ ⚁ ⚀ · 1 blocked, 2 wounds",
            "Block · 5+ (AP(1)) → needs 6 · ⚀ · 0 blocked, 1 wound",
            "Regeneration · 5+ · 3 wounds: ⚀ ⚅ ⚀ · 1 ignored, 2 wounds stand",
        ];
        Assert.Equal(expected, rolls.Where(expected.Contains));
        Assert.Contains("Morale · 4+ · ⚂ · failed → Shaken", rolls);
        Assert.DoesNotContain(Tree(block.Entries).OfType<LineEntry>(), l => l.Text == "Shaken");

        var lines = Tree(block.Entries).Count();
        Assert.True(lines <= 30, $"{lines} lines");
        Assert.Equal(17, lines);
    }
}
