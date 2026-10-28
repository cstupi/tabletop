// viewer/tests/agreement.test.ts
import { describe, expect, it } from 'vitest';
import { Applier, ProjectionDesyncError } from '../src/lib/live/apply';
import { FrameProjector, recordedBoardOf } from '../src/lib/live/project';
import type { GameEvent } from '../src/lib/protocol/wire.generated';
import { normaliseBoard, normaliseFrame } from './normalise';
import { FIXTURES, loadFixture } from './fixture';

describe.each(FIXTURES)('the TypeScript applier agrees with the C# one on %s', (name) => {
  const { game, board, events, recording } = loadFixture(name);

  it('reproduces the recorded board from the wire board', () => {
    expect(normaliseBoard(recordedBoardOf(board, game))).toEqual(normaliseBoard(recording.board));
    // normaliseBoard drops `polygons` because the browser has no ZoneStyles; check here that the C#
    // side really does resolve them, so the drop never quietly covers an empty field on both sides.
    for (const zone of recording.board.zones) {
      expect(zone.polygons.length, `${zone.shape} resolved to no rings`).toBeGreaterThan(0);
      for (const ring of zone.polygons) expect(ring.length).toBeGreaterThan(2);
    }
  });

  it('reproduces every recorded frame, field for field, from the raw event log', () => {
    const projector = new FrameProjector(board, game);
    const produced = events.map((event) => projector.add(event));
    expect(produced).toHaveLength(recording.frames.length);
    produced.forEach((frame, i) => {
      expect(normaliseFrame(frame), `frame ${i} (seq ${frame.seq}, ${frame.kind})`)
        .toEqual(normaliseFrame(recording.frames[i]!));
    });
  });

  it('replayed a real game, not a two-event stub', () => {
    expect(recording.frames.length).toBeGreaterThan(20);
    expect(new Set(recording.frames.map((f) => f.kind)).size).toBeGreaterThan(5);
    expect(recording.frames.some((f) => f.rolls.length > 0)).toBe(true);
    expect(recording.frames.some((f) => f.openAsks.length > 0)).toBe(true);
    expect(recording.frames.some((f) => f.models.some((m) => m.where === 'table'))).toBe(true);
  });
});

// The other half of the pair whose C# side is AgreementFixtureTests, "the C# applier faults on an id
// the projection does not hold". Agreement on 200 valid frames says nothing about the one input where
// the two implementations naturally diverge, so both faults are asserted on the same event.
describe('the two appliers fault the same way', () => {
  const { game, board } = loadFixture('skirmish');
  const UNKNOWN = '0198b7ac-0000-7000-8000-0000000000ff';

  it('throws ProjectionDesyncError where the C# throws ProjectionDesyncException', () => {
    const applier = new Applier(board, game);
    const moved = { seq: 1, actor: null, warning: null, payload: {
      kind: 'marker_moved', markerId: UNKNOWN, position: { x: 5, y: 5, z: 0 }, yaw: { degrees: 90 },
    } } as unknown as GameEvent;

    expect(() => applier.apply(moved)).toThrow(ProjectionDesyncError);
    // Not a typed throw against a silent success: nothing was written, so the projection did not
    // quietly diverge from the server's. `{ ...undefined, position }` would have left a marker here.
    expect(applier.projection.markers.size).toBe(0);
  });
});
