# Phase 3 — A game picks its edition (design step 4)

Index: `index.md`. Spec: `2026-10-29-rule-set-versions-design.md` → "How a game picks its edition", "Client and viewer". Group **C**, after phase 2 merges. 3.1–3.2 and 3.5 are independent of 3.3–3.4 and may run in a second worktree.

Decisions this phase fixes:

- `PropertiesSummary.Default` is `GameRules == RuleSets.Default`, computed in management's `ListProperties` (no new column).
- Both clients show a set by its `Name` only; the Godot dialog's `"{Name} ({GameRules})"` becomes `Name`.
- A lobby `SetConfiguration` whose hash belongs to another id: the session resolves the new rules **before** the write (so `OptionProblems` is the new catalog's), writes, swaps `GameSession.Rules`, broadcasts the new catalog as `RulesOptionsMessage`, and clears every picked army whose `Edition != RuleSets.EditionOf(newId)` (the row write `PickArmy(null)` uses). Option keys the new catalog refuses are refused as today (`Unknown`), not dropped silently: the clients send the catalog's presets, so a refusal is a client bug, not a player path.
- The set's name reaches the clients as `GameInfo.RuleSet` (init property, `string? RuleSet`), filled by management's `Get` and the game server's `ReadGameAsync` from `management.properties.name`; `GameSummary.RuleSet` the same; `RecordingMeta.RuleSet` for replays (null in old recordings → the viewer shows `gameRules`).

---

### Task 3.1: `PropertiesSummary.Default` — **Sonnet**

**Files:**
- Modify: `server/management/Games/GameDtos.cs:8` (`PropertiesSummary(string ContentHash, string Name, string GameRules, bool Default)`), `server/management/Games/GameRepository.cs:27` (`Default = GameRules == RuleSets.Default` after the query), `tests/Tabletop.Management.Tests/Infrastructure/InMemoryGameRepository.cs:19`
- Test: `tests/Tabletop.Management.Tests/GameTests.cs:35` (extend: with two seeded sets, exactly the `grimdark-future-3.6` one has `default: true`), `Contracts/RepositoryContract.Games.cs:20`

- [ ] **Step 1: Failing test**: seed `(hashA, "Grimdark Future v3.5.1", "grimdark-future")` and `(hashB, "Grimdark Future v3.6.0", "grimdark-future-3.6")`; `GET /v1/properties` → the 3.6 entry `Default == true`, the other false.
- [ ] **Step 2:** `dotnet test tests/Tabletop.Management.Tests --filter "FullyQualifiedName~GameTests|FullyQualifiedName~RepositoryContract"` — FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit `git commit -am "Management: the default rule set is flagged"`.

---

### Task 3.2: Create dialogs preselect the default set and show names only — **Sonnet**

**Files:**
- Modify: `client/src/Core/ManagementDtos.cs:15` (`PropertiesSummary(..., bool Default = false)`), create `client/src/Core/CreateDefaults.cs` (`public static int DefaultIndex(IReadOnlyList<PropertiesSummary> packs) => Math.Max(0, packs.ToList().FindIndex(p => p.Default))`, `public static string PackLabel(PropertiesSummary p) => p.Name`), `client/src/Screens/HomeScreen.cs:249` (`pack.AddItem(CreateDefaults.PackLabel(p))`, then `pack.Select(CreateDefaults.DefaultIndex(packs))`)
- Modify: `viewer/src/lib/api/types.ts:15` (`default: boolean`), `viewer/src/lib/games/listModel.ts:19` (`createDefaults` picks `packs.find(p => p.default) ?? packs[0]`), `viewer/src/lib/games/CreateGameDialog.svelte:~62` (the select shows whenever `packs.length > 1`, already; option text `pack.name`, already)
- Test: `tests/Tabletop.Client.Tests/CreateDefaultsTests.cs` (new), `viewer/tests/listModel.test.ts`

- [ ] **Step 1: Failing tests**: `The_default_set_is_preselected_wherever_it_is_listed` (default at index 1 → 1; none flagged → 0); viewer: `createDefaults` returns the flagged pack's hash when it is second.
- [ ] **Step 2:** run both — FAIL. **Step 3:** implement. **Step 4:** `dotnet test tests/Tabletop.Client.Tests`, `./scripts/check-client`, `cd viewer && npm test` — PASS. Against the stack: the browser's New game dialog shows "Grimdark Future v3.6.0" selected with "Grimdark Future v3.5.1" available.
- [ ] **Step 5:** commit `git commit -am "Create dialogs preselect the default rule set"`.

---

### Task 3.3: The session re-resolves its rules when the lobby changes the set — **Opus**

**Files:**
- Modify: `libs/Tabletop.Protocol/Wire.cs:42-48` (add `public sealed record RulesOptionsMessage(RulesOptionCatalog Options) : SocketMessage;` discriminator `rules_options`, registered where `GameInfoMessage` is), `server/game/Persistence/IGameRows.cs:20` (add `Task<string?> PropertiesGameRulesAsync(string hash, CancellationToken ct)`; `PropertiesExistAsync` stays), `server/game/Persistence/PostgresGameRows.cs:52` (the query `select game_rules from management.properties where content_hash = $1`), `tests/Tabletop.GameServer.Tests/Infrastructure/InMemoryGameRows.cs:180`, `server/game/Session/GameSession.cs:41` (`public IGameRules Rules { get; private set; }`), `server/game/Session/GameSession.Lobby.cs:111-126`
- Test: `tests/Tabletop.GameServer.Tests/LobbyEditionTests.cs` (new; `GameServerFactory` over an `InMemorySeeder` that seeds both sets — add `InMemorySeeder.SeedPropertiesAsync(string gameRules, string name)`; `ScenarioCompiler` is sim, so build the two sets from `PropertiesSeeder.EmbeddedDocuments()` via `PropertiesJson.ToSet`)

**Interfaces:**
- Produces: `GameSession.Rules` swaps on a set change; `RulesOptionsMessage` to every client (watchers too) when the id changed; participants' `ArmyId` cleared when `army.Edition != RuleSets.EditionOf(newId)` (`GameInfo` broadcast shows it).

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public async Task Changing_the_set_to_another_edition_swaps_the_rules_and_resends_the_option_catalog()
{   // game on the 3.5.1 hash; creator sends SetConfiguration with the 3.6 hash
    // assert: GameInfoMessage.Game.GameRules == "grimdark-future-3.6"; a RulesOptionsMessage arrived whose Options equals GrimdarkFutureRules.V360.OptionCatalog;
    // the session's Rules.Id is "grimdark-future-3.6" (through the factory's SessionRegistry, or a second connect's Welcome.Options)
}
[Fact] public async Task Changing_the_edition_drops_armies_of_the_other_edition()
{   // creator picked an army with Edition "3.5"; switch to the 3.6 hash → GameInfo.Participants[creator].ArmyId == null; a "3.6" army on another seat stays
}
[Fact] public async Task A_set_change_within_the_same_edition_sends_no_option_catalog() // 3.5.1 hash A → 3.5.1 hash B (seed two 3.5.1 sets): GameInfo only
[Fact] public async Task Options_are_checked_against_the_new_editions_catalog() // a key the new catalog refuses → Refused(Unknown), nothing written
```

- [ ] **Step 2:** `dotnet test tests/Tabletop.GameServer.Tests --filter LobbyEdition` — FAIL.
- [ ] **Step 3: Implement** in `HandleLobbyEditAsync`: `var newId = await rows.PropertiesGameRulesAsync(c.PropertiesHash, ct)` (null → `Unknown` "no properties"); `var newRules = newId == Rules.Id ? Rules : GameRulesRegistry.Resolve(newId)`; `newRules.OptionProblems(...)`; write; `ReloadInfoAsync()`; if `newId != Rules.Id`: `Rules = newRules`, broadcast `RulesOptionsMessage(Rules.OptionCatalog)`, then for each participant with an army: `rows.ReadArmyAsync` → if `RuleSets.EditionOf(newId) != army.Edition` clear it (the same row write `PickArmy(null)` makes) and `ReloadInfoAsync()` once at the end.
- [ ] **Step 4:** PASS; `dotnet test tests/Tabletop.GameServer.Tests` whole — PASS. Regenerate `wire.generated.ts` (`TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`).
- [ ] **Step 5: AGENTS.md**: `server/game/AGENTS.md` → "How it works", the lobby-authority bullet: "A `SetConfiguration` naming another edition's hash re-resolves `Rules`, broadcasts `RulesOptionsMessage`, and clears armies of the other edition."
- [ ] **Step 6:** commit `git commit -am "Lobby: a set change re-resolves the rules and resends the options"`.

---

### Task 3.4: Rule-set picker in both lobbies; the clients take `RulesOptionsMessage` — **Sonnet**

**Files:**
- Modify: `client/src/Core/LobbyEdits.cs:14` (`Configure(GameInfo game, string name, SchedulerShape shape, Enforcement enforcement, RulesOptionCatalog catalog, IReadOnlyDictionary<string,string> chosen, string? propertiesHash = null)` → `propertiesHash ?? game.PropertiesHash`), `client/src/Panels/LobbyPanel.cs:60-70` (an `OptionButton` of `s.Management.ListProperties()` names, selected = the game's hash, creator only; its hash goes to `Configure`), the client's socket message switch (`client/src/Core/Session*.cs`: `RulesOptionsMessage` replaces `Session.Options`; find the `Welcome` handler that stores `Options`), `client/src/Core/ManagementDtos.cs`
- Modify: `viewer/src/lib/lobby/rulesOptions.ts:29` (`configurationOf(..., propertiesHash = game.propertiesHash)`), `viewer/src/lib/lobby/LobbyPage.svelte:29-65` (a `<select>` of `api.listProperties()` names, bound to the hash, creator only, sent on Apply), the viewer's live message reducer (`viewer/src/lib/live/*` where `welcome.options` is stored: handle `rules_options`)
- Test: `tests/Tabletop.Client.Tests/LobbyEditsTests.cs` (`Configure_sends_the_chosen_hash_or_the_games`), the session test that stores `Options` (`RulesOptionsMessage` replaces them), `viewer/tests/rulesOptions.test.ts`, `viewer/tests/lobbyPage.test.ts`, the live reducer test; `CallsViewerFixtureTests` gains one case: a configuration with another hash (client/AGENTS.md: every new outbound shape has a case) — regenerate `calls.json` per `tests/AGENTS.md`.

- [ ] **Step 1: Failing tests** (C# and TS): the configuration carries the picked hash; `RulesOptionsMessage` replaces the catalog the lobby's option controls read.
- [ ] **Step 2:** run — FAIL. **Step 3:** implement. **Step 4:** `dotnet test tests/Tabletop.Client.Tests`, `./scripts/check-client`, `dotnet build client/Tabletop.csproj`, `cd viewer && npm run check && npm test` (calls agreement included) — PASS. Against the stack in the browser: switch a lobby from v3.6.0 to v3.5.1; the option presets redraw; a 3.6 army picked before is cleared.
- [ ] **Step 5:** commit `git commit -am "Lobbies pick the rule set; option catalogs follow a set change"`.

---

### Task 3.5: The set's name on the games list, lobby, in-game header, replay — **Sonnet**

**Files:**
- Modify: `libs/Tabletop.Protocol/Game.cs:29-37` (`GameInfo` init property `public string? RuleSet { get; init; }`), `server/management/Games/GameDtos.cs:13` (`GameSummary(..., string? RuleSet = null)`), `server/management/Games/GameRepository.cs:77-92` (`List` and `Get` join `properties p on p.content_hash = g.properties_hash`, select `p.name`), `server/game/Persistence/PostgresGameRows.cs:11-31` (`ReadGameAsync` joins the same), `tests/.../InMemoryGameRepository.cs`, `tests/.../InMemoryGameRows.cs:260` (`RuleSet = set.Name`), `sim/Recording/Recording.cs:20` (`RecordingMeta` gains `string? RuleSet`; `FrameProjector`/the recorder fills it from `GameInfo.RuleSet`)
- Modify: `client/src/Core/ManagementDtos.cs:13` (`GameSummary(..., string? RuleSet = null)`), `client/src/Screens/HomeScreen.cs` list row text (append ` · {RuleSet}`), `client/src/Core/HeaderView.cs` (the lobby/in-game header line shows `game.RuleSet ?? game.GameRules`), `client/src/Panels/LobbyPanel.cs` (a label with the set name); `viewer/src/lib/live/games.ts:9` (`ruleSet?: string`), `viewer/src/lib/games/GamesPage.svelte:86,152` (a chip with `game.ruleSet`), `viewer/src/lib/games/present.ts` (`ruleSetLine`), `viewer/src/lib/game/header.ts` / `play/headerText.ts` (set name), `viewer/src/lib/components/PlaybackView.svelte:104` (`meta.ruleSet ?? meta.gameRules`), `viewer/src/lib/feed/liveFeed.ts:85` (`ruleSet: info.ruleSet`)
- Regenerate: `recording.generated.ts`, `wire.generated.ts`, viewer fixtures (`tests/AGENTS.md`).
- Test: `tests/Tabletop.Management.Tests/GameTests.cs` (`GET /v1/games` rows carry `ruleSet` = the set's name; `GET /v1/games/{id}` too), `tests/Tabletop.GameServer.Tests/GameRowsContract.cs` (`ReadGameAsync` fills `RuleSet`), `tests/Tabletop.Client.Tests/HeaderViewTests.cs`, `viewer/tests/present.test.ts`, `headerText.test.ts`, `replayView.test.ts` (old meta without `ruleSet` shows the id).

- [ ] **Step 1: Failing tests** as listed. **Step 2:** run — FAIL. **Step 3:** implement. **Step 4:** the five test commands — PASS; `dotnet run --project sim -- skirmish --seed 1` writes a recording whose `meta.ruleSet` is "Grimdark Future v3.5.1". **Step 5:** commit `git commit -am "Games show their rule set's name"`.
