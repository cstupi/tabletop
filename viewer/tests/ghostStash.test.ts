import { describe, expect, it } from 'vitest';
import { GhostStash } from '../src/lib/play/ghostStash';

const keep = (s: GhostStash, key: string) =>
  s.keep(key, new Map([['m1', { x: 1, y: 2, z: 0 }], ['m2', { x: 3, y: 4, z: 0 }]]), new Map([['m1', { degrees: 90 }]]));

describe('GhostStash', () => {
  it('ghosts kept across a reconnect come back once for the same key with their facings', () => {
    const stash = new GhostStash();
    keep(stash, 'ask');
    expect(stash.take('another')).toBeNull();
    keep(stash, 'ask');
    const back = stash.take('ask')!;
    expect(back.get('m1')).toEqual({ position: { x: 1, y: 2, z: 0 }, yaw: { degrees: 90 } });
    expect(back.get('m2')!.yaw).toBeNull();
    expect(stash.take('ask')).toBeNull();
  });

  it('ghosts kept for one key are not given to another', () => {
    const stash = new GhostStash();
    keep(stash, 'ask');
    expect(stash.take('unit')).toBeNull();
    expect(stash.take('ask')).toBeNull();                 // taking spends the stash
  });
});
