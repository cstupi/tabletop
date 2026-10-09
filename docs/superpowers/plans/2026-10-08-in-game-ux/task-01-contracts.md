# Task 1: Contracts

**Wave:** 1 · **Depends on:** — · **Model:** sonnet

**Files:**
- Modify: `libs/Tabletop.Protocol/Wire.cs` (`SightQuery`, `SightAnswer`, `SightLine`, `ModelFacts` and their three `[JsonDerivedType]` rows)
- Modify: `libs/Tabletop.Protocol/Events.cs` (`SequenceStarted` gains `Outline`; new `OutlineStep`)
- Modify: `libs/Tabletop.Protocol/Projection.cs` (`Frame` gains `Outline`)
- Modify: `libs/Tabletop.Protocol/EventApplier.cs` (passes the outline into the frame)
- Modify: `client/src/Core/ManagementDtos.cs` (`RuleTextDto`)
- Modify: `client/src/Core/ManagementClient.cs` (`GetRuleTexts`)
- Create: `client/src/Core/RuleTexts.cs`
- Regenerate: `viewer/src/lib/protocol/wire.generated.ts` (`SequenceStarted.outline?`, `OutlineStep`)
- Test: `tests/Tabletop.Protocol.Tests/Samples.cs`, `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`,
  `tests/Tabletop.Protocol.Tests/WireJsonTests.cs`, `tests/Tabletop.Client.Tests/ManagementClientTests.cs`,
  `tests/Tabletop.Client.Tests/RuleTextsTests.cs` (new)

**Interfaces:**
- Consumes: nothing new.
- Produces (README *Fixed interfaces → Protocol* and *Client core → RuleTexts*):

```csharp
public sealed record SightQuery(Guid QueryId, Guid ObserverUnitId, Guid TargetUnitId) : SocketMessage;
public sealed record SightAnswer(Guid QueryId, IReadOnlyList<SightLine> Lines, IReadOnlyList<Guid> CanShoot) : SocketMessage;
public sealed record SightLine(Guid FromModelId, Guid ToModelId, bool Clear, bool Screened, IReadOnlyList<Guid> BlockedBy);
public sealed record ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds) : SocketMessage;
public sealed record SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context, IReadOnlyList<OutlineStep>? Outline = null) : EventPayload;
public sealed record OutlineStep(string Step, string Label);
// Frame
public IReadOnlyList<OutlineStep> Outline { get; }
// client/src/Core
public sealed record RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText);
public Task<IReadOnlyList<RuleTextDto>> GetRuleTexts(string propertiesHash);   // ManagementClient
public sealed class RuleTexts { public static RuleTexts From(IReadOnlyList<RuleTextDto> rows); public static RuleTexts Empty { get; }
    public string? TextOf(string name); public bool IsAutomated(string name); }
```

Wire kinds: `sight_query`, `sight_answer`, `model_facts`. Every change is additive; `ProtocolVersion.Current` stays `1`.
`Outline` is omitted from the JSON when null (`ContentJson`'s `WhenWritingNull`), so a log written before this task
reads back with `Outline == null` and the frame's `Outline` empty.

The viewer: `viewer/src/lib/live/apply.ts` does not port the frame stack (its comment at `case 'sequence_started'`),
and `RecordedFrame` carries no stack, so the `EventApplier` change needs no port and `viewer/tests/fixtures/` need no
regeneration. The three new socket messages are not reachable from the generator's roots (`GeneratedTypesTests`:
`Connect`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `Refused`, `Rejected`), so only
`SequenceStarted` and the new `OutlineStep` change in `wire.generated.ts`.

`RuleTexts` matches names ordinally, as the catalog writes them (`"Tough"`, `"Ambush"`); stripping a parameter from a
snapshot label (`Tough(3)` → `Tough`) is the caller's (Task 10).

- [ ] **Step 1: Write the failing protocol tests**

In `tests/Tabletop.Protocol.Tests/Samples.cs`, give the outline to the sampled `SequenceStarted` in `Events` (the
fifth entry) and add the three messages to the end of `Messages`, so `Messages_round_trip`, `Events_round_trip` and
`Every_concrete_kind_has_a_sample` cover them:

```csharp
        new PhaseStarted("deployment"), new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Unit, [Unit], [], "Advance"),
            [new OutlineStep("move", "Move"), new OutlineStep("shoot", "Shoot")]),
```

```csharp
        new Refused(Guid.CreateVersion7(), RefusalReason.OutsideLegalSet, "unit already activated"), new Rejected(RejectReason.UpgradeRequired),
        new SightQuery(Guid.CreateVersion7(), Unit, Guid.CreateVersion7()),
        new SightAnswer(Guid.CreateVersion7(), [new SightLine(Model, Guid.CreateVersion7(), false, true, [Guid.CreateVersion7()])], [Model]),
        new ModelFacts(new Dictionary<Guid, int> { [Model] = 3 }),
    ];
```

Append to `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`, inside the class:

```csharp
    [Fact]
    public void SequenceStarted_without_outline_reads_as_empty()
    {
        var a = Fresh();
        a.Apply(Ev(1, new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))));
        Assert.Empty(a.Projection.Stack.Single().Outline);

        // A log row written before Outline existed: no "outline" key at all.
        var json = $"{{\"kind\":\"sequence_started\",\"frameId\":\"{Frame}\",\"sequence\":\"hold\",\"context\":{{\"actorUnitId\":\"{Samples.Unit}\",\"targetUnitIds\":[],\"weaponIds\":[]}}}}";
        var old = Assert.IsType<SequenceStarted>(WireJson.Deserialize<EventPayload>(json));
        Assert.Null(old.Outline);
        var b = Fresh();
        b.Apply(Ev(1, old));
        Assert.Empty(b.Projection.Stack.Single().Outline);
    }

    [Fact]
    public void A_frame_carries_its_sequences_outline_in_order()
    {
        var a = Fresh();
        a.Apply(Ev(1, new SequenceStarted(Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance"),
            [new OutlineStep("move", "Move"), new OutlineStep("shoot", "Shoot")])));
        Assert.Equal(["move", "shoot"], a.Projection.Stack.Single().Outline.Select(o => o.Step));
        Assert.Equal(["Move", "Shoot"], a.Projection.Stack.Single().Outline.Select(o => o.Label));
    }
```

Append to `tests/Tabletop.Protocol.Tests/WireJsonTests.cs`, inside the class:

```csharp
    [Fact]
    public void Sight_and_facts_travel_under_their_kinds()
    {
        Assert.StartsWith("{\"kind\":\"sight_query\",", WireJson.Serialize(new SightQuery(Guid.Empty, Guid.Empty, Guid.Empty)));
        Assert.StartsWith("{\"kind\":\"sight_answer\",", WireJson.Serialize(new SightAnswer(Guid.Empty, [], [])));
        var facts = WireJson.Serialize(new ModelFacts(new Dictionary<Guid, int> { [Guid.Empty] = 3 }));
        Assert.Equal("{\"kind\":\"model_facts\",\"maxWounds\":{\"00000000-0000-0000-0000-000000000000\":3}}", facts);
    }

    [Fact]
    public void A_sequence_without_an_outline_writes_no_outline_key()
    {
        var json = WireJson.Serialize<EventPayload>(new SequenceStarted(Guid.Empty, "hold", new SequenceContext(null, [], [], null)));
        Assert.DoesNotContain("outline", json);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Protocol.Tests`
Expected: compile errors: `OutlineStep`, `SightQuery`, `SightAnswer`, `SightLine`, `ModelFacts` do not exist;
`SequenceStarted` takes no fourth argument; `Frame` has no `Outline`.

- [ ] **Step 3: Implement the protocol**

In `libs/Tabletop.Protocol/Wire.cs`, add after `[JsonDerivedType(typeof(Rejected), "rejected")]`:

```csharp
[JsonDerivedType(typeof(SightQuery), "sight_query")]
[JsonDerivedType(typeof(SightAnswer), "sight_answer")]
[JsonDerivedType(typeof(ModelFacts), "model_facts")]
```

After `public sealed record EditMessage(...)` (the client → server block):

```csharp
/// <summary>Which models of the observer can see which of the target, and which may shoot it. Answered outside the log, to the asker alone.</summary>
public sealed record SightQuery(Guid QueryId, Guid ObserverUnitId, Guid TargetUnitId) : SocketMessage;
```

After `public sealed record Rejected(...)` (the server → client block):

```csharp
/// <summary>The answer to a SightQuery, to the asker alone: one line per observer-model, target-model pair, and the observer's models that
/// may fire at the target (p.8, Who can shoot). Both lists are empty when either unit is off the table.</summary>
public sealed record SightAnswer(Guid QueryId, IReadOnlyList<SightLine> Lines, IReadOnlyList<Guid> CanShoot) : SocketMessage;
/// <summary>Clear: line of sight exists. Screened: clear but through cover. BlockedBy: the terrain pieces or models that obstruct it.</summary>
public sealed record SightLine(Guid FromModelId, Guid ToModelId, bool Clear, bool Screened, IReadOnlyList<Guid> BlockedBy);
/// <summary>The ruleset's per-model facts: wounds a model takes before it is killed. On connect once playing, then whenever a value changes.</summary>
public sealed record ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds) : SocketMessage;
```

In `libs/Tabletop.Protocol/Events.cs`, replace

```csharp
public sealed record SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context) : EventPayload;
```

with

```csharp
/// <summary>Outline: the player-facing steps of the sequence, in order; Step matches StepCompleted.Step. Null in logs written before it existed.</summary>
public sealed record SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context, IReadOnlyList<OutlineStep>? Outline = null) : EventPayload;
public sealed record OutlineStep(string Step, string Label);
```

In `libs/Tabletop.Protocol/Projection.cs`, replace the head of `Frame`:

```csharp
public sealed class Frame(Guid id, string sequence, SequenceContext context)
{
    internal readonly List<CompletedStep> StepsMut = new();
    internal readonly Dictionary<string, List<RollModifier>> PendingMut = new();
    public Guid Id { get; } = id;
    public string Sequence { get; } = sequence;
    public SequenceContext Context { get; } = context;
```

with

```csharp
public sealed class Frame(Guid id, string sequence, SequenceContext context, IReadOnlyList<OutlineStep>? outline = null)
{
    internal readonly List<CompletedStep> StepsMut = new();
    internal readonly Dictionary<string, List<RollModifier>> PendingMut = new();
    public Guid Id { get; } = id;
    public string Sequence { get; } = sequence;
    public SequenceContext Context { get; } = context;
    /// <summary>The sequence's labelled steps from SequenceStarted; empty when the event carried none.</summary>
    public IReadOnlyList<OutlineStep> Outline { get; } = outline ?? [];
```

In `libs/Tabletop.Protocol/EventApplier.cs`, replace

```csharp
            case SequenceStarted s: p.StackMut.Add(new Frame(s.FrameId, s.Sequence, s.Context)); break;
```

with

```csharp
            case SequenceStarted s: p.StackMut.Add(new Frame(s.FrameId, s.Sequence, s.Context, s.Outline)); break;
```

The optional constructor parameter keeps `GfRollsTests`' `new Frame(id, "x", context)` compiling.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Protocol.Tests`
Expected: PASS.

- [ ] **Step 5: Regenerate the viewer's types and check the viewer**

Run:

```bash
TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests
dotnet test tests/Tabletop.Viewer.Tests
cd viewer && npm run check && npm test
```

Expected: `viewer/src/lib/protocol/wire.generated.ts` gains `readonly outline?: readonly OutlineStep[];` on
`SequenceStarted` and an `OutlineStep` interface (`step`, `label`); the second run and the viewer's check and tests
pass with nothing else changed.

- [ ] **Step 6: Write the failing client tests**

Create `tests/Tabletop.Client.Tests/RuleTextsTests.cs`:

```csharp
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class RuleTextsTests
{
    static readonly RuleTextDto Tough = new("Tough", "unit", true, "Must take X wounds to be killed.");
    static readonly RuleTextDto Ambush = new("Ambush", "unit", false, "May be set aside before deployment.");

    [Fact]
    public void A_catalog_entry_gives_its_text_and_reads_as_automated()
    {
        var rules = RuleTexts.From([Tough, Ambush]);
        Assert.Equal("Must take X wounds to be killed.", rules.TextOf("Tough"));
        Assert.True(rules.IsAutomated("Ambush"));
    }

    [Fact]
    public void A_name_with_no_entry_has_no_text_and_is_not_automated()
    {
        var rules = RuleTexts.From([Tough]);
        Assert.Null(rules.TextOf("Furious"));
        Assert.False(rules.IsAutomated("Furious"));
    }

    [Fact]
    public void Names_match_as_the_catalog_writes_them()
    {
        var rules = RuleTexts.From([Tough]);
        Assert.Null(rules.TextOf("tough"));
        Assert.Null(rules.TextOf("Tough(3)"));
    }

    [Fact]
    public void Empty_knows_no_rule()
    {
        Assert.Null(RuleTexts.Empty.TextOf("Tough"));
        Assert.False(RuleTexts.Empty.IsAutomated("Tough"));
    }

    [Fact]
    public void A_repeated_name_keeps_the_first_row()
    {
        var rules = RuleTexts.From([Tough, Tough with { RuleText = "other" }]);
        Assert.Equal("Must take X wounds to be killed.", rules.TextOf("Tough"));
    }
}
```

Append to `tests/Tabletop.Client.Tests/ManagementClientTests.cs`, inside the class:

```csharp
    [Fact]
    public async Task Rule_texts_are_read_from_the_pinned_set()
    {
        var (c, f) = Make();
        f.Answer(HttpStatusCode.OK, "[{\"name\":\"Tough\",\"kind\":\"unit\",\"takesParameter\":true,\"ruleText\":\"Must take X wounds to be killed.\"}]");
        var rows = await c.GetRuleTexts("sha256:abc");
        Assert.Equal("http://localhost:5100/v1/properties/sha256:abc/rules", f.Requests.Single().RequestUri!.ToString());
        Assert.Equal(new RuleTextDto("Tough", "unit", true, "Must take X wounds to be killed."), rows.Single());
    }

    [Fact]
    public async Task An_unknown_set_is_a_404()
    {
        var (c, f) = Make();
        f.Answer(HttpStatusCode.NotFound, "{\"title\":\"Not found\",\"status\":404}", "application/problem+json");
        var ex = await Assert.ThrowsAsync<ManagementException>(() => c.GetRuleTexts("sha256:nope"));
        Assert.Equal(404, ex.Status);
    }
```

The hash goes into the path unescaped, as `ListProperties`' hashes are used elsewhere: a content hash is
`sha256:<hex>`, and `:` is legal in a path segment.

- [ ] **Step 7: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "RuleTextsTests|ManagementClientTests"`
Expected: compile errors: `RuleTextDto`, `RuleTexts` and `ManagementClient.GetRuleTexts` do not exist.

- [ ] **Step 8: Implement the client pieces**

In `client/src/Core/ManagementDtos.cs`, after `PropertiesSummary`:

```csharp
/// <summary>One catalog entry of a pinned property set (GET /v1/properties/{hash}/rules).</summary>
public sealed record RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText);
```

In `client/src/Core/ManagementClient.cs`, after `ListProperties`:

```csharp
    public Task<IReadOnlyList<RuleTextDto>> GetRuleTexts(string propertiesHash) => Get<IReadOnlyList<RuleTextDto>>($"/v1/properties/{propertiesHash}/rules");
```

Create `client/src/Core/RuleTexts.cs`:

```csharp
namespace Tabletop.Client.Core;

/// <summary>A pinned set's catalog by rule name: the hover text of every rule, and whether the rule is in the catalog at all.
/// A rule with no entry is not automated (in-game UX spec → Stat sheet).</summary>
public sealed class RuleTexts
{
    readonly IReadOnlyDictionary<string, string> _byName;
    RuleTexts(IReadOnlyDictionary<string, string> byName) => _byName = byName;

    public static RuleTexts Empty { get; } = new(new Dictionary<string, string>());

    public static RuleTexts From(IReadOnlyList<RuleTextDto> rows)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows) byName.TryAdd(row.Name, row.RuleText);
        return new RuleTexts(byName);
    }

    public string? TextOf(string name) => _byName.TryGetValue(name, out var text) ? text : null;
    public bool IsAutomated(string name) => _byName.ContainsKey(name);
}
```

- [ ] **Step 9: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests`
Expected: PASS.

- [ ] **Step 10: Build everything that consumes the protocol**

Run: `dotnet build && ./scripts/check-client`
Expected: build succeeds; `check-client` prints nothing.

- [ ] **Step 11: Commit**

```bash
git add libs/Tabletop.Protocol/Wire.cs libs/Tabletop.Protocol/Events.cs libs/Tabletop.Protocol/Projection.cs libs/Tabletop.Protocol/EventApplier.cs \
  viewer/src/lib/protocol/wire.generated.ts client/src/Core/ManagementDtos.cs client/src/Core/ManagementClient.cs client/src/Core/RuleTexts.cs \
  tests/Tabletop.Protocol.Tests/Samples.cs tests/Tabletop.Protocol.Tests/EventApplierTests.cs tests/Tabletop.Protocol.Tests/WireJsonTests.cs \
  tests/Tabletop.Client.Tests/ManagementClientTests.cs tests/Tabletop.Client.Tests/RuleTextsTests.cs
git commit -m "Add the sight question, model facts and step outline to the protocol, and the client's rule texts" \
  -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
