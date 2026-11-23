import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import type { MidDeploy } from '../src/lib/play/deployView';
import { shortWords } from '../src/lib/play/markLabels';
import { ZoneReachCache, withZoneReach, zoneReachBreaches, zoneReachOrdinal, zoneReachRingOf } from '../src/lib/play/zoneReachArea';
import type { TableState } from '../src/lib/protocol/wire.generated';
import { P1, at, boardOf, emptyTable, gameOf, newId, projectionOf, unitOf } from './play';
import { loadTestGeometry } from './geometry';

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

// Godot's ZoneReachAreaTests: 72 x 48, frontline; Team 1 holds y 36–48. The unit being placed is Team 1's, on the shelf.
const scout = unitOf('Scouts');
const ghost = scout.models[0]!;
const table = { ...emptyTable(), zones: [{ id: newId(), shape: 'frontline/1', teamOrdinal: 1 }, { id: newId(), shape: 'frontline/2', teamOrdinal: 2 }] } as TableState;
const game = gameOf();
const p = projectionOf(game, boardOf([{ participantId: P1, units: [scout] }], table)).projection;
const mid = (reach: number | null): MidDeploy => ({ unitId: scout.id, question: 'deploy_scouts', shelvedModelIds: [ghost.id], keepAway: null, zoneReach: reach });
const area = (segments: readonly { a: { x: number; y: number }; b: { x: number; y: number } }[]) => segments.reduce((s, x) => s + x.a.x * x.b.y - x.a.y * x.b.x, 0) / 2;
const half = ghost.base.a / 2;

describe('zoneReachArea through the WebAssembly', () => {
  it('the area is the team\'s zone grown by the reach, labelled with it', () => {
    const ring = zoneReachRingOf(g, p, game, mid(9), null)!;
    expect(ring.kind).toBe('reach');
    expect(ring.label).toBe('Scout 9"');
    expect(Math.min(...ring.segments.map((s) => Math.min(s.a.y, s.b.y)))).toBeCloseTo(27, 6);
    expect(area(ring.segments)).toBeCloseTo(72 * 21, 3);
    expect(zoneReachOrdinal(p, game, mid(9))).toBe(1);
  });

  it('a pick with no reach draws nothing', () => expect(zoneReachRingOf(g, p, game, mid(null), null)).toBeNull());

  it('the cache keeps the ring until the reach changes', () => {
    const cache = new ZoneReachCache();
    const first = cache.of(g, p, game, mid(9), null);
    expect(cache.of(g, p, game, mid(9), null)).toBe(first);
    expect(cache.of(g, p, game, mid(12), null)!.label).toBe('Scout 12"');
  });

  it.each([[9, 0.1, false], [9, -0.1, true], [12, 0.1, false], [12, -0.1, true]])('reach %d, near edge %d" inside: marked %s', (reach, inside, marked) => {
    const y = 36 - reach + inside + half;
    const found = zoneReachBreaches(g, p, game, mid(reach), null, new Map([[ghost.id, at(20, y)]]));
    expect(found.has(ghost.id)).toBe(marked);
    if (marked) {
      expect(found.get(ghost.id)).toBe(`placed beyond ${reach}" of its deployment zone`);
      expect(shortWords(found.get(ghost.id)!)).toBe(`Beyond ${reach}"`);
    }
  });

  it('zero reach marks a ghost outside the zone', () => {
    const found = zoneReachBreaches(g, p, game, mid(0), null, new Map([[ghost.id, at(20, 35.8)]]));
    expect(found.get(ghost.id)).toBe('placed outside its deployment zone');
    expect(shortWords(found.get(ghost.id)!)).toBe('Outside its zone');
  });

  it('earlier words come first', () => {
    const proposed = new Map([[ghost.id, at(20, 20)]]);
    expect(withZoneReach(new Map([[ghost.id, 'overlaps a base']]), g, p, game, mid(9), null, proposed).get(ghost.id)).toBe('overlaps a base');
    expect(withZoneReach(new Map(), g, p, game, mid(9), null, proposed).get(ghost.id)).toBe('placed beyond 9" of its deployment zone');
  });
});
