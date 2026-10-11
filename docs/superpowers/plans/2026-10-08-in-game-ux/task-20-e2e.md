# Task 20: End to end

**Wave:** 7 · **Depends on:** 2, 3, 4b, 5, 19 · **Model:** opus

**Files:**
- Modify: `.superpowers/e2e/e2e.csproj` (project references to the three libs in place of the copied DLLs, so the
  driver always speaks the current protocol)
- Modify: `.superpowers/e2e/Program.cs` (`E2E_SCENARIO=ingame`; `Player` keeps `ModelFacts`, `SightAnswer`s and an
  `at_rest` count; `Describe` names the new messages)
- Modify: `.superpowers/e2e/Driver.cs` (`Run(Func<bool>? until)` stops when the caller has what it needs)
- Create: `.superpowers/e2e/InGame.cs` (the in-game UX's server side, checked over HTTP and the socket)
- Documents: [Task 21](task-21-documents.md), which follows this one.

`.superpowers/` is git-ignored: the e2e files are local tooling and are not committed.

**Interfaces:**
- Consumes: `SightQuery`, `SightAnswer`, `SightLine`, `ModelFacts`, `SequenceStarted.Outline` (Task 1); the
  `hand_move` breach and its detail `"{unit} moved by hand outside a move step"`, `StrictRefuses(Illegal)` (Task 2);
  outlines on the Grimdark Future sequences (Task 3); `ModelFacts` on connect and after a changing batch, the sight
  answer to the asker only (Task 4b); `GET /v1/properties/{hash}/rules` (Task 5); the strict mission-length end
  (`GfRules.MissionLength`, detail `"{n} rounds have been played: the game ends after 4 (p.7)"`).
- Produces: nothing for later tasks.

Spec → *Testing*: "End to end over HTTP and the socket (`.superpowers/e2e/`): a game from deployment through an
activation with a hand move under each setting, a sight query, and the end message's data."

The scenario runs once per enforcement setting (`E2E_ENFORCEMENT=Silent|Warn|Strict`); `Program.cs` already creates the
game with that setting, sets up a table with six built-in pieces and frontline zones, and ends setup. `InGame` then:

| Check | When | Pass |
| --- | --- | --- |
| `rule-texts` | before play | `GET /v1/properties/{hash}/rules` is a non-empty array of `{name, kind, takesParameter, ruleText}`; an unknown hash is 404; no bearer is 401 |
| `deployment-placement` | round 0 done | every shelf-to-table `models_moved` was accepted with no warning and no refusal, under every setting (Review Focus 1) |
| `model-facts` | round 0 done | each player holds a `ModelFacts` naming every model with a value ≥ 1; a model whose slot carries `Tough(X)` reports X |
| `model-facts-on-connect` | round 0 done | a second socket for the same account receives `ModelFacts` after `Welcome` |
| `step-outline` | first action frame finished | its `sequence_started` carries a non-empty outline, and a step it names was completed in the frame |
| `sight-query` | the same point | the asker alone gets one `SightAnswer` with its query id; one line per observer × target model on the table; `Clear` ⇔ no `BlockedBy`; `Screened` ⇒ `Clear`; `CanShoot` ⊆ the observer's models; no event appended; no `at_rest` |
| `hand-move` | an activate Ask of mine open | silent: an event with no warning; warn: an event whose warning names the unit "moved by hand outside a move step"; strict: `Refused(Illegal)` with that detail and no event |
| `hand-move-undo` | silent and warn | `Undo(seq − 1)` lands `undone` and the model is back where it was |
| `end` | strict: the game ends; warn, silent: round 5 starts | strict: `game_ended` with the mission-length reason, winners among the teams, status `ended`; warn: `round_started` 5 carries the mission-length warning; silent: it carries none |

The exit code is 0 when every check passed.

- [ ] **Step 1: e2e.csproj — reference the libs**

Replace the `ItemGroup` of `.superpowers/e2e/e2e.csproj` with:

```xml
  <ItemGroup>
    <ProjectReference Include="../../libs/Tabletop.Protocol/Tabletop.Protocol.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Content/Tabletop.Content.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Geometry/Tabletop.Geometry.csproj" />
  </ItemGroup>
```

and `rm -r .superpowers/e2e/lib`. The copied DLLs predate Task 1 and cannot deserialize `sight_answer` or
`model_facts`.

- [ ] **Step 2: Program.cs — the scenario switch and the new messages**

In `.superpowers/e2e/Program.cs`, replace

```csharp
// Play
var driver = new Driver(players, size, log, maxRound);
```

with

```csharp
if ((Environment.GetEnvironmentVariable("E2E_SCENARIO") ?? "game") == "ingame")
{
    var failed = await new InGame(players, size, log, http, mgmt, wsUri, gameId, hash, enforcement).Run();
    log.Note($"IN-GAME {enforcement}: {(failed == 0 ? "all checks passed" : $"{failed} checks failed")}");
    log.Flush();
    foreach (var p in players) await p.Close();
    return failed == 0 ? 0 : 1;
}

// Play
var driver = new Driver(players, size, log, maxRound);
```

In `class Player`, after `public long AtRestSeq = -1;` add:

```csharp
    public int AtRests;                                   // every at_rest received: a sight answer must add none
    public ModelFacts? Facts;                             // the latest model_facts
    public readonly List<SightAnswer> Sights = new();
```

In `Player.Handle`, replace `case AtRest a: AtRestSeq = a.Seq; break;` with:

```csharp
            case AtRest a: AtRestSeq = a.Seq; AtRests++; break;
            case ModelFacts f: Facts = f; break;
            case SightAnswer s: Sights.Add(s); break;
```

In `Describe.Out`, before `_ => m.GetType().Name,` add:

```csharp
        SightQuery q => $"sight_query {q.QueryId.ToString()[^6..]} {q.ObserverUnitId.ToString()[^4..]}->{q.TargetUnitId.ToString()[^4..]}",
```

In `Describe.In`, before `_ => m.GetType().Name,` add:

```csharp
        ModelFacts f => $"model_facts n={f.MaxWounds.Count} over1={f.MaxWounds.Count(kv => kv.Value > 1)}",
        SightAnswer s => $"sight_answer {s.QueryId.ToString()[^6..]} lines={s.Lines.Count} clear={s.Lines.Count(l => l.Clear)} screened={s.Lines.Count(l => l.Screened)} canShoot={s.CanShoot.Count}",
```

and in `Describe.Event`, directly above the existing `SequenceStarted s =>` arm, add:

```csharp
            SequenceStarted { Outline: { Count: > 0 } o } s2 => $"{s2.Sequence} outline=[{string.Join(",", o.Select(x => $"{x.Step}:{x.Label}"))}]",
```

The first matching arm wins, so a frame with an outline prints it and one without keeps the old line.

- [ ] **Step 3: Driver.cs — stop on a condition**

In `.superpowers/e2e/Driver.cs`, replace `public async Task<string> Run()` with
`public async Task<string> Run(Func<bool>? until = null)`, and after the line
`if (players[0].Game?.Status is GameStatus.Ended) return "game ended";` add:

```csharp
            if (until?.Invoke() == true) return "until";
```

The driver keeps its answered-Ask and pending-deploy state across calls, so `InGame` runs one driver in stages.

- [ ] **Step 4: InGame.cs**

A second socket for a participant replaces the first (`GameSession`: "A newer socket for a participant replaces the
older"), so `model-facts-on-connect` swaps the new connection in as `players[0]`; the driver reads the same list.
`Tough` is named here, in developer tooling outside `client/`, only to check the server's number against the army.

Create `.superpowers/e2e/InGame.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

/// <summary>The in-game UX's server side over HTTP and the socket (in-game UX spec → Testing). One CHECK line per check; Run returns the failures.</summary>
sealed class InGame(List<Player> players, TableSize size, Log log, HttpClient http, Uri mgmt, Uri ws, Guid gameId, string hash, Enforcement enforcement)
{
    const string HandMoveDetail = "moved by hand outside a move step", MissionLengthDetail = "rounds have been played";
    int failures;
    Player Alpha => players[0];
    Player Bravo => players[1];
    Projection P => Alpha.Applier!.Projection;
    IReadOnlyList<GameEvent> Events => Alpha.Applier!.Log;

    void Check(string name, bool ok, string detail)
    {
        if (!ok) failures++;
        log.Note($"{(ok ? "CHECK" : "CHECK-FAIL")} {name} [{enforcement}]: {detail}");
    }

    public async Task<int> Run()
    {
        await RuleTexts();
        var driver = new Driver(players, size, log, maxRound: 4);
        var refused = players.Sum(p => p.Refusals.Count);
        var stage = await driver.Run(until: () => P.Scheduler.Round >= 1);
        Deployment(refused, stage);
        Facts();
        await FactsOnConnect();
        stage = await driver.Run(until: () => FinishedActionFrame() is not null);
        Outline(stage);
        await Sight();
        stage = await driver.Run(until: () => MyActivateAsk() is not null);
        if (MyActivateAsk() is not null) await HandMove(); else Check("hand-move", false, $"no activate Ask of {Alpha.Name} ({stage})");
        End(await driver.Run());
        return failures;
    }

    async Task RuleTexts()
    {
        var rows = (await Http.Get<JsonElement>(http, mgmt, $"/v1/properties/{hash}/rules", Alpha.Jwt, log)).EnumerateArray().ToList();
        var shaped = rows.All(r => r.TryGetProperty("name", out _) && r.TryGetProperty("kind", out _) && r.TryGetProperty("takesParameter", out _) && r.TryGetProperty("ruleText", out _));
        Check("rule-texts", rows.Count > 0 && shaped, $"{rows.Count} entries, first {(rows.Count > 0 ? rows[0].GetProperty("name").GetString() : "-")}");
        var unknown = await Status("/v1/properties/sha256:0000/rules", Alpha.Jwt);
        var anonymous = await Status($"/v1/properties/{hash}/rules", null);
        Check("rule-texts-status", unknown == HttpStatusCode.NotFound && anonymous == HttpStatusCode.Unauthorized, $"unknown hash {(int)unknown}, no bearer {(int)anonymous}");
    }

    void Deployment(int refusedBefore, string stage)
    {
        var placements = Events.Where(e => e.Payload is ModelsMoved m && m.Moves.All(x => x.From is OnShelf && x.To is OnTable)).ToList();
        var refused = players.Sum(p => p.Refusals.Count) - refusedBefore;
        Check("deployment-placement", placements.Count > 0 && placements.All(e => e.Warning is null) && refused == 0,
            $"{placements.Count} placements, {placements.Count(e => e.Warning is not null)} warned, {refused} refused; round {P.Scheduler.Round} ({stage})");
    }

    void Facts()
    {
        foreach (var p in players)
        {
            var proj = p.Applier!.Projection;
            var facts = p.Facts?.MaxWounds ?? new Dictionary<Guid, int>();
            var onTable = proj.Models.Values.Where(m => m.Location is OnTable).ToList();
            int? Tough(ModelInPlay m) => m.Slot.Properties.FirstOrDefault(x => x.Name == "Tough")?.Parameter
                ?? proj.Units[m.UnitId].Definition.Properties.FirstOrDefault(x => x.Name == "Tough")?.Parameter;
            var tough = onTable.Where(m => Tough(m) is not null).ToList();
            var wrong = tough.Count(m => facts.GetValueOrDefault(m.Id) != Tough(m));
            var missing = onTable.Count(m => !facts.ContainsKey(m.Id));
            Check($"model-facts {p.Name}", p.Facts is not null && missing == 0 && facts.Values.All(v => v >= 1) && wrong == 0,
                $"{facts.Count} facts, {onTable.Count} on the table, {missing} missing, {tough.Count} with Tough, {wrong} disagreeing");
        }
    }

    async Task FactsOnConnect()
    {
        var again = new Player(Alpha.Name, Alpha.Jwt, Alpha.AccountId, log) { TeamId = Alpha.TeamId, ArmyId = Alpha.ArmyId };
        try
        {
            await again.Open(ws, gameId);
            again.ParticipantId = (await again.Expect<Welcome>()).ParticipantId;
            var facts = await again.Expect<ModelFacts>();
            players[0] = again;                                   // the server closed the older socket
            await Drain();
            Check("model-facts-on-connect", facts.MaxWounds.Count > 0 && again.Applier is not null, $"{facts.MaxWounds.Count} facts after welcome, seq {again.Applier?.Projection.LastSeq}");
        }
        catch (Exception ex) { Check("model-facts-on-connect", false, ex.Message); }
    }

    GameEvent? FinishedActionFrame() => Events.FirstOrDefault(s => s.Payload is SequenceStarted { Context.Action: not null } started
        && Events.Any(e => e.Payload is SequenceFinished f && f.FrameId == started.FrameId));

    void Outline(string stage)
    {
        if (FinishedActionFrame()?.Payload is not SequenceStarted started) { Check("step-outline", false, $"no action frame finished ({stage})"); return; }
        var outline = started.Outline ?? [];
        var done = Events.Select(e => e.Payload).OfType<StepCompleted>().Where(c => c.FrameId == started.FrameId).Select(c => c.Step).ToHashSet();
        Check("step-outline", outline.Count > 0 && outline.Any(o => done.Contains(o.Step)),
            $"{started.Sequence} [{started.Context.Action}]: {string.Join(", ", outline.Select(o => $"{o.Step}:{o.Label}{(done.Contains(o.Step) ? " done" : "")}"))}");
    }

    async Task Sight()
    {
        Guid? UnitOf(Player who) => P.Units.Values.FirstOrDefault(u => TeamOf(u.ParticipantId) == who.TeamId && OnTable(u.Id).Count > 0)?.Id;
        if (UnitOf(Alpha) is not { } observer || UnitOf(Bravo) is not { } target) { Check("sight-query", false, "no unit of each side on the table"); return; }
        var (seq, rests, bravoSights) = (P.LastSeq, Alpha.AtRests, Bravo.Sights.Count);
        var id = Guid.CreateVersion7();
        await Alpha.Send(new SightQuery(id, observer, target));
        await Drain();
        var answers = Alpha.Sights.Where(x => x.QueryId == id).ToList();
        var (from, to) = (OnTable(observer), OnTable(target));
        var a = answers.FirstOrDefault();
        var shaped = a is not null && a.Lines.Count == from.Count * to.Count && a.CanShoot.All(from.Contains)
            && a.Lines.All(l => from.Contains(l.FromModelId) && to.Contains(l.ToModelId) && l.Clear == (l.BlockedBy.Count == 0) && (!l.Screened || l.Clear));
        Check("sight-query", answers.Count == 1 && shaped && Bravo.Sights.Count == bravoSights && P.LastSeq == seq && Alpha.AtRests == rests,
            $"{answers.Count} answers; {a?.Lines.Count ?? 0} lines for {from.Count}x{to.Count}, {a?.Lines.Count(l => l.Clear) ?? 0} clear, " +
            $"{a?.Lines.Count(l => l.Screened) ?? 0} screened, {a?.CanShoot.Count ?? 0} can shoot; bravo +{Bravo.Sights.Count - bravoSights}; seq {seq}->{P.LastSeq}; at_rest +{Alpha.AtRests - rests}");
    }

    Ask? MyActivateAsk() => P.Scheduler.Round >= 1
        ? P.OpenAsks.Values.Select(o => o.Ask).FirstOrDefault(a => a.Question == AskQuestions.Activate && a.Teams.Contains(Alpha.TeamId)) : null;

    async Task HandMove()
    {
        var model = P.Models.Values.First(m => m.Location is OnTable && TeamOf(P.Units[m.UnitId].ParticipantId) == Alpha.TeamId);
        var at = ((OnTable)model.Location).Placement;
        var to = at with { Position = at.Position with { X = at.Position.X + 0.5 } };
        var (before, refusals) = (P.LastSeq, Alpha.Refusals.Count);
        var id = await Alpha.Command(new MoveModels([new ModelRelocation(model.Id, model.Location, new OnTable(to))]));
        await Drain();
        var moved = Events.FirstOrDefault(e => e.Seq > before && e.Payload is ModelsMoved m && m.Moves.Any(x => x.ModelId == model.Id));
        var refusal = Alpha.Refusals.Skip(refusals).FirstOrDefault(r => r.MessageId == id);
        var (ok, what) = enforcement switch
        {
            Enforcement.Silent => (moved is { Warning: null } && refusal is null, $"event #{moved?.Seq} warning {moved?.Warning ?? "none"}"),
            Enforcement.Warn => (moved?.Warning?.Contains(HandMoveDetail) == true && refusal is null, $"event #{moved?.Seq} warning {moved?.Warning ?? "none"}"),
            _ => (moved is null && refusal is { Reason: RefusalReason.Illegal } && refusal.Detail.Contains(HandMoveDetail), $"refused {refusal?.Reason} {refusal?.Detail}"),
        };
        Check("hand-move", ok, $"{model.Slot.Name} +0.5\": {what}");
        if (moved is null) return;
        await Alpha.Command(new Undo(moved.Seq - 1));                  // what the client's Undo move button sends
        await Drain();
        var back = P.Models[model.Id].Location is OnTable now && now.Placement.Position == at.Position;
        var undone = Events.Any(e => e.Seq > moved.Seq && e.Payload is Undone u && u.ToSeq == moved.Seq - 1);
        Check("hand-move-undo", back && undone, $"undone {undone}, back where it was {back}");
    }

    void End(string stage)
    {
        var game = Alpha.Game!;
        string Team(Guid id) => $"T{game.Teams.FirstOrDefault(t => t.Id == id)?.Ordinal}";
        var scores = string.Join(" ", P.Scores.Select(kv => $"{Team(kv.Key)}={kv.Value}"));
        if (enforcement == Enforcement.Strict)
        {
            var ended = P.Ended;
            Check("end", ended is not null && ended.Reason.Contains(MissionLengthDetail) && ended.Winners.All(w => game.Teams.Any(t => t.Id == w)),
                $"{stage}; reason \"{ended?.Reason}\"; winners [{string.Join(",", ended?.Winners.Select(Team) ?? [])}]; scores {scores}; status {game.Status}");
            return;
        }
        var fifth = Events.Any(e => e.Payload is RoundStarted { Round: 5 });
        var warning = Events.FirstOrDefault(e => e.Warning?.Contains(MissionLengthDetail) == true)?.Warning;
        Check("end", fifth && (enforcement == Enforcement.Warn ? warning is not null : warning is null), $"{stage}; round 5 started {fifth}; mission-length warning {warning ?? "none"}; scores {scores}");
    }

    Guid? TeamOf(Guid participantId) => Alpha.Game!.Participants.FirstOrDefault(x => x.Id == participantId)?.TeamId;

    HashSet<Guid> OnTable(Guid unitId) => P.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).Select(m => m.Id).ToHashSet();

    async Task Drain()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var any = false;
            foreach (var p in players) while (await p.Pump(TimeSpan.FromMilliseconds(200))) any = true;
            if (!any) return;
        }
    }

    async Task<HttpStatusCode> Status(string path, string? jwt)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(mgmt, path));
        req.Headers.Add(ProtocolVersion.Header, ProtocolVersion.Current.ToString());
        if (jwt is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        using var res = await http.SendAsync(req);
        log.Line($"HTTP GET {path} -> {(int)res.StatusCode}");
        return res.StatusCode;
    }
}
```

- [ ] **Step 5: Run it under each setting, then the sim**

```bash
docker compose up -d
dotnet build .superpowers/e2e/e2e.csproj
for e in Silent Warn Strict; do
  (cd .superpowers/e2e && E2E_SCENARIO=ingame E2E_ENFORCEMENT=$e dotnet run --no-build; cp transcript.log transcript-ingame-$e.log)
done
grep -h "CHECK-FAIL\|IN-GAME" .superpowers/e2e/transcript-ingame-*.log
```

Expected: three `IN-GAME …: all checks passed` lines and no `CHECK-FAIL`. Each transcript also shows the
`model_facts`, `sight_answer` and outline lines. Read the game server's log (`docker compose logs game --since 30m`)
for exceptions.

Then the sim over the socket, which deploys by hand under `strict` (Review Focus 1):

```bash
for s in final-round final-round-warn final-round-silent; do dotnet run --project sim -- $s --driver socket --no-recording || echo "FAIL $s"; done
dotnet run --project sim -- skirmish --seeds 1..3 --driver socket --no-recording
```

Expected: exit 0 for every run and no `FAIL` line. `final-round` (strict) shows `Ended`; the warn and silent variants
play past round 4 by design and stop at their round cap.

Then the whole suite: `dotnet build && dotnet test` (stack up). Expected: 0 errors, every project green;
`./scripts/check-client` prints nothing.

- [ ] **Step 6: Record the result**

Nothing to commit: `.superpowers/` is git-ignored. Note in the task report the three `IN-GAME` lines, the sim
summary lines and the test counts per project; Task 21 writes the counts into `docs/handoff.md`.
