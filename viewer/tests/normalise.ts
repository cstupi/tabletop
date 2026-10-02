// viewer/tests/normalise.ts
// C# Dictionary enumeration order and JavaScript Map insertion order can diverge once an entry is removed
// and another added (a marker taken off the table, an Ask closed), and C#'s .Order() is culture-sensitive
// where JavaScript's .sort() is by code unit. Neither is a drift in the applier, so both sides are
// normalised before comparison and everything else is compared exactly.
import type { RecordedBoard, RecordedFrame } from '../src/lib/protocol/recording.generated';

const byId = <T extends { id: string }>(items: readonly T[]): readonly T[] =>
  [...items].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
const sorted = (items: readonly string[]): readonly string[] => [...items].sort();

export function normaliseFrame(frame: RecordedFrame): unknown {
  return {
    ...frame,
    models: byId(frame.models).map((m) => ({ ...m, conditions: sorted(m.conditions) })),
    units: byId(frame.units).map((u) => ({ ...u, actions: sorted(u.actions), conditions: sorted(u.conditions) })),
    markers: byId(frame.markers),
    openAsks: byId(frame.openAsks).map((a) => ({ ...a, answered: sorted(a.answered) })),
    passed: sorted(frame.passed),
  };
}

// `polygons` is the one field of the recording the live feed cannot produce and is not asked to:
// ZoneStyles.Resolve is C#, deliberately not ported (Task 4), so recordedBoardOf writes []. Dropping
// it here keeps the comparison honest about what is actually ported. Everything else about a zone —
// its id, its shape id, its team ordinal — is compared exactly.
export function normaliseBoard(board: RecordedBoard): unknown {
  return {
    ...board,
    markers: byId(board.markers),
    zones: byId(board.zones).map(({ polygons: _polygons, ...rest }) => rest),
    models: byId(board.models),
  };
}
