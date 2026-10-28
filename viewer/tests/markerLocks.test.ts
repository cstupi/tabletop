import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import { MarkerLocks, terrainMayDrag } from '../src/lib/play/markerLocks';
import type { GameStatus, Marker, TableState } from '../src/lib/protocol/wire.generated';
import { P1, boardOf, emptyTable, gameOf, markerOf, newId, sample } from './play';

/** Two setup objectives, the second moved in play. */
function rig() {
  const setupObjective: Marker = { ...markerOf(undefined), id: newId() };
  const playObjective = markerOf('Obj');
  const table: TableState = { ...emptyTable(), markers: [setupObjective, playObjective] };
  const s = sample();
  const applier = new Applier(boardOf(s.board.armies.map((a) => ({ participantId: a.participantId, units: [...a.snapshot.units] })), table), gameOf());
  applier.apply({ seq: 1, actor: P1, payload: { kind: 'marker_moved', markerId: playObjective.id, position: { x: 6, y: 6, z: 0 }, yaw: { degrees: 0 } } });
  return { p: applier.projection, setup: setupObjective.id, play: playObjective.id };
}

describe('MarkerLocks', () => {
  it.each<GameStatus>(['playing', 'ended'])('every objective is locked from play on (%s)', (status) => {
    const { p, setup, play } = rig();
    const locks = new MarkerLocks();
    expect(locks.isLocked(status, p, setup)).toBe(true);
    expect(locks.isLocked(status, p, play)).toBe(true);
    expect(locks.mayDrag(status, p, play)).toBe(false);
  });

  it.each<GameStatus>(['lobby', 'setup'])('setup moves markers freely (%s)', (status) => {
    const { p, setup } = rig();
    const locks = new MarkerLocks();
    expect(locks.isLocked(status, p, setup)).toBe(false);
    expect(locks.mayDrag(status, null, setup)).toBe(true);
  });

  it('unknown ids are free', () => {
    const { p, play } = rig();
    const locks = new MarkerLocks();
    expect(locks.mayDrag('playing', p, 'nobody')).toBe(true);
    expect(locks.itemFor('playing', p, 'nobody')).toBeNull();
    expect(locks.itemFor('setup', p, play)).toBeNull();
  });

  it('unlock frees one objective until it is locked again', () => {
    const { p, setup, play } = rig();
    const locks = new MarkerLocks();
    let changed = 0;
    locks.subscribe(() => changed++);
    const unlock = locks.itemFor('playing', p, play)!;
    expect(unlock.label).toBe('Unlock');
    unlock.apply();
    expect(changed).toBe(1);
    expect(locks.mayDrag('playing', p, play)).toBe(true);
    expect(locks.isLocked('playing', p, setup)).toBe(true);
    const lock = locks.itemFor('playing', p, play)!;
    expect(lock.label).toBe('Lock');
    lock.apply();
    expect(locks.mayDrag('playing', p, play)).toBe(false);
    expect(changed).toBe(2);
  });

  it('terrain drags only in setup', () => {
    expect(terrainMayDrag('setup')).toBe(true);
    for (const s of ['playing', 'ended', 'lobby', null] as const) expect(terrainMayDrag(s)).toBe(false);
  });
});
