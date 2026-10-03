import { describe, expect, it } from 'vitest';
import { OVAL_VERTICES, sceneOf } from '../src/lib/board/frame';
import type { RecordedBoard, RecordedFrame } from '../src/lib/protocol/recording.generated';

const board: RecordedBoard = {
  width: 72, length: 48, texture: 'grass',
  terrain: [{
    id: 'tp1', mesh: 'ruin', x: 20, y: 10, z: 0, yaw: 90, scale: 2,
    volumes: [
      { id: 'v1', kind: 'solid', footprint: 'rect', a: 3, b: 1, offsetX: 1, offsetY: 0, yaw: 0, bottom: 0, top: 4, declarations: ['impassable', 'blocks_line_of_sight'] },
      { id: 'v2', kind: 'area', footprint: 'circle', a: 2, b: 2, offsetX: 0, offsetY: 0, yaw: 0, bottom: 0, top: 6, declarations: ['cover', 'difficult'] },
    ],
  }],
  markers: [{ id: 'k1', kind: 'objective', diameter: 2, label: 'A' }],
  // frontline/1 as ZoneStyles.Resolve returns it: the corner frame, four vertices, twelve inches deep.
  zones: [{ id: 'z1', shape: 'frontline/1', teamOrdinal: 1,
    polygons: [[{ x: 0, y: 0 }, { x: 72, y: 0 }, { x: 72, y: 12 }, { x: 0, y: 12 }]] }],
  models: [
    { id: 'm1', unitId: 'u1', unitName: 'Rifles', name: 'Rifleman', ordinal: 1, teamId: 't1', teamOrdinal: 1, baseShape: 'circle', baseA: 1, baseB: 1, height: 1.2 },
    { id: 'm2', unitId: 'u1', unitName: 'Rifles', name: 'Sergeant', ordinal: 2, teamId: 't1', teamOrdinal: 1, baseShape: 'oval', baseA: 3, baseB: 2, height: 2 },
    { id: 'm3', unitId: 'u2', unitName: 'Walker', name: 'Walker', ordinal: 1, teamId: 't2', teamOrdinal: 2, baseShape: 'rect', baseA: 2, baseB: 2, height: 4 },
  ],
};

const frame: RecordedFrame = {
  index: 0, seq: 9, kind: 'models_moved', round: 1, phase: 'rounds', line: '3 model(s) moved',
  models: [
    { id: 'm1', where: 'table', x: 10, y: 20, z: 0, yaw: 0, wounds: 1, spellTokens: 0, conditions: ['shaken'] },
    { id: 'm2', where: 'table', x: 12, y: 20, z: 2, yaw: 90, wounds: 0, spellTokens: 2, conditions: [] },
    { id: 'm3', where: 'inside', containerId: 'm2', wounds: 0, spellTokens: 0, conditions: [] },
  ],
  units: [
    { id: 'u1', actions: ['Advance'], conditions: [], grants: [], woundsThisActivation: 1 },
    { id: 'u2', actions: [], conditions: ['shaken'], grants: ['Regeneration'], woundsThisActivation: 0 },
  ],
  // k1 is also in board.markers and agrees with it; k9 was dropped during play and is in no board block.
  markers: [
    { id: 'k1', kind: 'objective', diameter: 2, label: 'A', x: 36, y: 24, z: 0, yaw: 0 },
    { id: 'k9', kind: 'token', diameter: 1, x: 4, y: 4, z: 0, yaw: 0 },
  ],
  openAsks: [], rolls: [], scores: { t1: 3, t2: 0 }, passed: ['t2'],
};

describe('sceneOf', () => {
  it('draws a circle base as a circle of half the recorded diameter', () => {
    const model = sceneOf(board, frame).models.find((m) => m.id === 'm1')!;
    expect(model.shape).toEqual({ kind: 'circle', centre: { x: 10, y: 20 }, radius: 0.5 });
    expect(model.teamOrdinal).toBe(1);
    expect(model.wounds).toBe(1);
    expect(model.conditions).toEqual(['shaken']);
  });

  it('draws an oval as a 64-gon with its length along the facing axis', () => {
    const oval = sceneOf(board, frame).models.find((m) => m.id === 'm2')!;
    expect(oval.shape.kind).toBe('polygon');
    const points = (oval.shape as { points: readonly { x: number; y: number }[] }).points;
    expect(points).toHaveLength(OVAL_VERTICES);
    // yaw 90 turns the 3-inch length onto -x, so the extreme x is 1.5 from the centre.
    expect(Math.min(...points.map((p) => p.x))).toBeCloseTo(12 - 1.5);
    expect(Math.min(...points.map((p) => p.y))).toBeCloseTo(20 - 1);
  });

  it('gives every table model a facing tick half the base length long', () => {
    const model = sceneOf(board, frame).models.find((m) => m.id === 'm1')!;
    expect(model.facing).not.toBeNull();
    expect(model.facing![1]).toEqual({ x: 10, y: 20.5 });
    expect(sceneOf(board, frame).models.find((m) => m.id === 'm2')!.facing![1].x).toBeCloseTo(12 - 1.5);
  });

  it('leaves a model with no position off the table and says where it is', () => {
    const scene = sceneOf(board, frame);
    expect(scene.models.map((m) => m.id)).toEqual(['m1', 'm2']);
    expect(scene.offTable).toEqual([
      { id: 'm3', label: 'Walker', teamOrdinal: 2, where: 'inside', containerLabel: 'Sergeant' },
    ]);
  });

  it('marks a model standing above the slab as elevated', () => {
    const scene = sceneOf(board, frame);
    expect(scene.models.find((m) => m.id === 'm1')!.elevated).toBe(false);
    expect(scene.models.find((m) => m.id === 'm2')!.elevated).toBe(true);
  });

  it('places a terrain volume by the piece transform, exactly as WorldVolumes does', () => {
    const [solid, area] = sceneOf(board, frame).terrain;
    // centre = position + rotate(offset * scale, pieceYaw); yaw 90 sends +x to +y.
    expect(solid!.shape.kind).toBe('polygon');
    const corners = (solid!.shape as { points: readonly { x: number; y: number }[] }).points;
    // offset (1, 0) * scale 2, rotated by the piece's yaw 90, lands at (0, 2), so the centre is (20, 12).
    // The 3x1 footprint at scale 2 is 6 by 2 locally; yaw 90 turns (x, y) into (-y, x), so the local
    // corners (-3,-1), (3,-1), (3,1), (-3,1) become (1,-3), (1,3), (-1,3), (-1,-3) about the centre.
    const expected = [{ x: 21, y: 9 }, { x: 21, y: 15 }, { x: 19, y: 15 }, { x: 19, y: 9 }];
    corners.forEach((corner, i) => {
      expect(corner.x).toBeCloseTo(expected[i]!.x, 6);
      expect(corner.y).toBeCloseTo(expected[i]!.y, 6);
    });
    expect(solid!.declarations).toEqual(['impassable', 'blocks_line_of_sight']);
    expect(solid!.top).toBe(8);            // top * scale
    expect(area!.shape).toEqual({ kind: 'circle', centre: { x: 20, y: 10 }, radius: 4 });
    expect(area!.label).toBe('ruin');
  });

  it('wraps a zone ring as a polygon in table inches, and tolerates a zone with no rings', () => {
    expect(sceneOf(board, frame).zones).toEqual([{
      id: 'z1', shape: 'frontline/1', teamOrdinal: 1,
      polygons: [{ kind: 'polygon', points: [{ x: 0, y: 0 }, { x: 72, y: 0 }, { x: 72, y: 12 }, { x: 0, y: 12 }] }],
    }]);
    // ZoneStyles.Resolve answers [] for a shape id it does not know, and the live feed resolves nothing.
    const unresolved = { ...board, zones: [{ ...board.zones[0]!, shape: 'made-up/1', polygons: [] }] };
    expect(sceneOf(unresolved, frame).zones[0]!.polygons).toEqual([]);
  });

  it('draws every marker from its own kind, diameter and label, falling back to the id when unlabelled', () => {
    const markers = sceneOf(board, frame).markers;
    expect(markers[0]).toEqual({ id: 'k1', kind: 'objective', label: 'A', centre: { x: 36, y: 24 }, radius: 1 });
    // k9 is in no board block; the frame carries its whole definition, so nothing is guessed.
    expect(markers[1]).toEqual({ id: 'k9', kind: 'token', label: 'k9', centre: { x: 4, y: 4 }, radius: 0.5 });
  });
});

describe('a controlled objective', () => {
  it('takes its team ordinal from the board models, and a neutral one has none', () => {
    const held = { ...frame, markers: [{ id: 'k1', kind: 'objective', diameter: 2, label: 'A', x: 36, y: 24, z: 0, yaw: 0, controlledBy: 't2' }] } as RecordedFrame;
    expect(sceneOf(board, held).markers[0]!.teamOrdinal).toBe(2);
    expect(sceneOf(board, frame).markers[0]!.teamOrdinal).toBeUndefined();
  });
});
