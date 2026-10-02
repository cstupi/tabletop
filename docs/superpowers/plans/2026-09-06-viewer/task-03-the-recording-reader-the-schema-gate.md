# Task 3: The recording reader — the schema gate and the structural checks

**Files:**
- Create: `viewer/src/lib/recording/read.ts`, `viewer/src/lib/recording/source.ts`
- Test: `viewer/tests/read.test.ts`

**Interfaces:**
- Consumes: `Recording`, `RecordedFrame`, `SCHEMA_VERSION` from `../protocol/recording.generated` (Task 2).
- Produces: `class RecordingError extends Error`; `function readRecording(value: unknown): Recording`; `function parseRecording(text: string): Recording`; `function teamOrdinals(r: Recording): ReadonlyMap<string, number>`; `function teamNames(r: Recording): ReadonlyMap<string, string>`. In `source.ts`: `type RecordingSource = { readonly label: string; readonly recording: Recording }`; `async function fromFile(file: File): Promise<RecordingSource>`; `async function fromUrl(url: string): Promise<RecordingSource>`; `function recordingUrlFrom(search: string): string | null`.
- The reader validates and refuses; it never repairs. A recording is derived and disposable (spec → *The recording is the artifact*), so the answer to a malformed file is to re-run `sim/`, not to guess.

Spec: spec → *Two feeds* — "A recording, loaded from a file. The primary case, and the only one that works with no server running." Simulation plan Task 5 — "`schemaVersion` is RecordingJson.SchemaVersion; a reader that does not know it should refuse the file."

- [ ] **Step 1: Write the failing test**

```ts
// viewer/tests/read.test.ts
import { describe, expect, it } from 'vitest';
import { RecordingError, parseRecording, readRecording, teamOrdinals } from '../src/lib/recording/read';
import { SCHEMA_VERSION } from '../src/lib/protocol/recording.generated';
import { readFileSync } from 'node:fs';

const minimal = () => ({
  schemaVersion: SCHEMA_VERSION,
  meta: {
    scenario: 'terrain', seed: 1, driver: 'in-process', gameRules: 'grimdark-future',
    propertiesHash: 'sha256:x', schedulerShape: 'alternating_activation', enforcement: 'strict',
    teams: [{ id: 't1', ordinal: 1, name: 'Team 1', participants: ['p1'] }],
    outcome: 'round_limit', rounds: 1, lastSeq: 1, startedAt: '1970-01-01T00:00:00+00:00', durationMs: 1,
  },
  board: {
    width: 72, length: 48, texture: 'grass', terrain: [], markers: [], zones: [],
    models: [{ id: 'm1', unitId: 'u1', unitName: 'Rifles', name: 'Rifleman', ordinal: 1, teamId: 't1', teamOrdinal: 1, baseShape: 'circle', baseA: 1, baseB: 1, height: 1.2 }],
  },
  frames: [{
    index: 0, seq: 1, kind: 'phase_started', round: 0, phase: 'deployment', line: 'Phase: deployment',
    models: [{ id: 'm1', where: 'shelf', wounds: 0, spellTokens: 0, conditions: [] }],
    units: [{ id: 'u1', actions: [], conditions: [], grants: [], woundsThisActivation: 0 }],
    markers: [], openAsks: [], rolls: [],
  }],
});

describe('readRecording', () => {
  it('accepts a well-formed recording and hands back exactly what it was given', () => {
    const r = readRecording(minimal());
    expect(r.frames).toHaveLength(1);
    expect(r.board.models[0]!.baseShape).toBe('circle');
    expect(teamOrdinals(r).get('t1')).toBe(1);
  });

  it('refuses a schemaVersion it does not know rather than mis-drawing it', () => {
    expect(() => readRecording({ ...minimal(), schemaVersion: 2 }))
      .toThrow(new RecordingError('schemaVersion 2 is not supported; this viewer reads 1. Re-run sim/ to get a current recording.'));
    expect(() => readRecording({ ...minimal(), schemaVersion: undefined })).toThrow(RecordingError);
  });

  it('refuses anything that is not an object with meta, board and frames', () => {
    for (const bad of [null, 42, 'x', [], {}, { schemaVersion: 1 }]) expect(() => readRecording(bad)).toThrow(RecordingError);
    const noBoard: Record<string, unknown> = minimal();
    delete noBoard.board;
    expect(() => readRecording(noBoard)).toThrow(/board/);
  });

  it('refuses frames whose index is not its position', () => {
    const r = minimal();
    r.frames = [{ ...r.frames[0]!, index: 3 }];
    expect(() => readRecording(r)).toThrow(/frame 0 carries index 3/);
  });

  it('refuses a frame that names a model the board does not define', () => {
    const r = minimal();
    r.frames[0]!.models = [{ id: 'ghost', where: 'shelf', wounds: 0, spellTokens: 0, conditions: [] }];
    expect(() => readRecording(r)).toThrow(/ghost/);
  });

  it('accepts a recording with no frames — a run that emitted nothing is still a run', () => {
    expect(readRecording({ ...minimal(), frames: [] }).frames).toEqual([]);
  });

  it('parses text and reports the JSON error as a RecordingError', () => {
    expect(parseRecording(JSON.stringify(minimal())).meta.scenario).toBe('terrain');
    expect(() => parseRecording('{ not json')).toThrow(RecordingError);
  });
});
```

- [ ] **Step 2: Run; expect failure**

Run: `cd viewer && npx vitest run tests/read.test.ts`
Expected: `Failed to resolve import "../src/lib/recording/read"`.

- [ ] **Step 3: Write `read.ts`**

```ts
// viewer/src/lib/recording/read.ts
import { SCHEMA_VERSION, type Recording } from '../protocol/recording.generated';

/** A file this viewer will not draw. The fix is to re-run sim/, because a recording is derived. */
export class RecordingError extends Error {}

function object(value: unknown, what: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new RecordingError(`${what} is not an object`);
  return value as Record<string, unknown>;
}

function array(value: unknown, what: string): readonly unknown[] {
  if (!Array.isArray(value)) throw new RecordingError(`${what} is not an array`);
  return value;
}

export function readRecording(value: unknown): Recording {
  const root = object(value, 'the recording');
  if (root.schemaVersion !== SCHEMA_VERSION)
    throw new RecordingError(
      `schemaVersion ${root.schemaVersion} is not supported; this viewer reads ${SCHEMA_VERSION}. ` +
        `Re-run sim/ to get a current recording.`,
    );

  const meta = object(root.meta, 'meta');
  array(meta.teams, 'meta.teams');
  const board = object(root.board, 'board');
  const models = array(board.models, 'board.models');
  array(board.terrain, 'board.terrain');
  array(board.markers, 'board.markers');
  array(board.zones, 'board.zones');
  if (typeof board.width !== 'number' || typeof board.length !== 'number')
    throw new RecordingError('board.width and board.length must be numbers');

  const known = new Set(models.map((m) => object(m, 'a board model').id as string));
  const frames = array(root.frames, 'frames');
  frames.forEach((raw, i) => {
    const frame = object(raw, `frame ${i}`);
    if (frame.index !== i) throw new RecordingError(`frame ${i} carries index ${frame.index}`);
    if (typeof frame.seq !== 'number') throw new RecordingError(`frame ${i} has no seq`);
    if (typeof frame.line !== 'string') throw new RecordingError(`frame ${i} has no line`);
    array(frame.units, `frame ${i}.units`);
    array(frame.markers, `frame ${i}.markers`);
    array(frame.openAsks, `frame ${i}.openAsks`);
    array(frame.rolls, `frame ${i}.rolls`);
    for (const m of array(frame.models, `frame ${i}.models`)) {
      const id = object(m, `a model in frame ${i}`).id as string;
      if (!known.has(id)) throw new RecordingError(`frame ${i} names model ${id}, which board.models does not define`);
    }
  });

  return value as Recording;
}

export function parseRecording(text: string): Recording {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (cause) {
    throw new RecordingError(`not JSON: ${(cause as Error).message}`);
  }
  return readRecording(parsed);
}

export function teamOrdinals(r: Recording): ReadonlyMap<string, number> {
  return new Map(r.meta.teams.map((t) => [t.id, t.ordinal]));
}

export function teamNames(r: Recording): ReadonlyMap<string, string> {
  return new Map(r.meta.teams.map((t) => [t.id, t.name]));
}
```

- [ ] **Step 4: Write `source.ts`**

```ts
// viewer/src/lib/recording/source.ts
import type { Recording } from '../protocol/recording.generated';
import { RecordingError, parseRecording } from './read';

export type RecordingSource = { readonly label: string; readonly recording: Recording };

export async function fromFile(file: File): Promise<RecordingSource> {
  return { label: file.name, recording: parseRecording(await file.text()) };
}

export async function fromUrl(url: string): Promise<RecordingSource> {
  const response = await fetch(url);
  if (!response.ok) throw new RecordingError(`${url} answered ${response.status} ${response.statusText}`);
  return { label: url, recording: parseRecording(await response.text()) };
}

/** `?recording=<url>` — the whole of the viewer's routing (Task 1: no router). */
export function recordingUrlFrom(search: string): string | null {
  return new URLSearchParams(search).get('recording');
}
```

- [ ] **Step 5: Run the tests; expect 7 passed**

Run: `cd viewer && npm test && npm run check`

- [ ] **Step 6: Commit**

```bash
git add viewer/src/lib/recording viewer/tests/read.test.ts
git commit -m "Read a recording, gate it on its schema version, and refuse what is malformed"
```
