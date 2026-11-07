# Call-log fixture: the `confirm` case

## Defect

`CallsViewerFixtureTests.Every_required_case_kind_is_present` fails with `["confirm"]` missing once the viewer fixtures are regenerated.
A `confirm` case is an answer to a `confirm` Ask ("Continue: {step}"), which `SequenceInterpreter` opens only when `GameInfo.WaitsBetweenSteps`
is on. Since the roll-off result spec the roll-off no longer gates without the flag, so the only `confirm` Asks left in the logs were the
roll-off gates in the old `skirmish` and `terrain` recordings; no call log's game set the flag. `TABLETOP_WRITE_FIXTURES=1 dotnet test
tests/Tabletop.Viewer.Tests` (`AgreementFixtureTests`) rewrites `skirmish`/`terrain` without that gate, and the kind disappears.

## Fix

`tests/Tabletop.Viewer.Tests/CallLogFixtureTests.cs`:

1. Test: `Holds("gameover")` also requires a `confirm` Ask with a `Confirmation` legal set (red against the checked-in log).
2. Generator: the `gameover` call log plays `final-round-warn` with `WaitsBetweenSteps = true`, so its log carries the roll-off gate
   (every team) and the step gates (the actor's team). `CallCases` takes two `confirm` cases from it (`calls/gameover@3`, `@32`).

## Validation

```bash
TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests --filter CallLogFixture
TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CallsViewerFixture
git diff --stat viewer/tests/fixtures
dotnet test tests/Tabletop.Viewer.Tests && dotnet test tests/Tabletop.Client.Tests
cd viewer && npm test
```

Also: `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests --filter AgreementFixture` (fresh `skirmish`/`terrain`, no roll-off
gate), then `Every_required_case_kind_is_present`.

## Results

- Before the fix, with fresh `skirmish`/`terrain`: `Every_required_case_kind_is_present` fails, missing `confirm`.
- After: with fresh `skirmish`/`terrain`, the three case checks pass; `confirm calls/gameover@3` and `@32` are in `calls.json`. The
  committed `skirmish`/`terrain` are left as they were (regenerating them restales `combatlog.json`).
- `git diff --stat viewer/tests/fixtures`: the 21 call-log files and `calls.json`. Every call log changes on each regeneration (new ids,
  and the in-process games are not identical run to run); `gameover.events.json` grows from 161 to 266 events (35 `confirm` Asks).
- `Tabletop.Viewer.Tests` 16 passed; `Tabletop.Client.Tests` 1036 passed; `viewer` `npm test` 1801 passed, 6 skipped (`calls.agreement` included).
