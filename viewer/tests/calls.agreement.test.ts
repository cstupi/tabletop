// The calls agreement: tests/fixtures/calls.json is written by tests/Tabletop.Client.Tests/CallsViewerFixtureTests.cs from Godot's Core
// builders (what each choice sends, and the decisions behind it). Every case is replayed through the browser's own modules and must send the
// same message after the same normalisation. A difference is fixed in the browser, never in the fixture, unless the case shows a Godot bug.
import { readFileSync } from 'node:fs';
import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import { canonical, knownIds, normaliseCall } from './callNormalise';
import { decisionsOf, readLog, replay, stateOf, type CallCase } from './callRig';
import { loadTestGeometry } from './geometry';

const fixture = JSON.parse(readFileSync('tests/fixtures/calls.json', 'utf8')) as { logs: string[]; cases: CallCase[] };

/** Tests that still disagree (a case's title): each runs as it.fails and is removed when its fix lands. */
const pinned: readonly string[] = [];

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe.each(fixture.cases)('calls agreement', (c) => {
  const log = readLog(c.log);
  const known = knownIds(log.welcome, log.board, log.events, JSON.stringify({ ...c, decisions: undefined, message: undefined }));

  const test = (title: string): typeof it => (pinned.includes(title) ? (it.fails as typeof it) : it);

  test(`${c.name}: the decisions agree`)(`${c.name}: the decisions agree`, () => {
    expect(decisionsOf(c, stateOf(c, log), log)).toEqual(c.decisions);
  });

  test(`${c.name}: the browser sends what Godot sends`)(`${c.name}: the browser sends what Godot sends`, async () => {
    const sent = await replay(c, stateOf(c, log), g, log);
    if (c.message === null) { expect(sent).toBeNull(); return; }      // sends nothing when Godot sends nothing
    expect(canonical(normaliseCall(sent, known))).toBe(canonical(c.message));
  });
});

it('the fixture covers every kind the writer requires', () => {
  const kinds = new Set(fixture.cases.map((c) => c.name.split(' ')[0]));
  for (const kind of ['deploy/ambusher', 'move/disembark', 'selfplay/answer', 'undo/undone', 'edit/volNotesClear']) expect(kinds).toContain(kind);
  expect(fixture.cases.length).toBeGreaterThan(100);
});
