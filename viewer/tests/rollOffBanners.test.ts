import { describe, expect, it } from 'vitest';
import { Names } from '../src/lib/play/names';
import { rollOffBannerOf } from '../src/lib/play/rollOffBanners';
import type { EventPayload } from '../src/lib/protocol/wire.generated';
import { FRAME, ev, sample } from './play';

const s = sample();
const N = new Names(s.game, null);
const batch = (...p: EventPayload[]) => p.map((x, i) => ev(i + 1, x));
const ranked = (values: Record<string, number>): EventPayload => ({ kind: 'step_completed', frameId: FRAME, step: 'order', rolls: [], changes: [], values });

describe('RollOffBanners', () => {
  it('the ranking step names the first team and each team\'s roll best first', () => {
    const b = batch({ kind: 'step_completed', frameId: FRAME, step: 'roll-off', rolls: [], changes: [] },
      ranked({ winner_ordinal: 2, place_1: 2, place_2: 1, rolled_1_1: 3, rolled_2_1: 5 }));
    expect(rollOffBannerOf(b, N, false)).toEqual({ text: 'Team 2 goes first', seconds: 4, detail: 'Team 2 rolled 5 · Team 1 rolled 3' });
  });

  it('a tie and its re-roll show as the roll-off resolved them', () => {
    const b = batch(ranked({ place_1: 1, place_2: 2, rolled_1_1: 4, rolled_1_2: 6, rolled_2_1: 4, rolled_2_2: 2 }));
    expect(rollOffBannerOf(b, N, false)!.detail).toBe('Team 1 rolled 4, 6 · Team 2 rolled 4, 2');
  });

  it('nothing while catching up, after an undo or without a ranking', () => {
    const r = ranked({ place_1: 1, place_2: 2, rolled_1_1: 6, rolled_2_1: 1 });
    expect(rollOffBannerOf(batch(r), N, true)).toBeNull();
    expect(rollOffBannerOf(batch(r, { kind: 'undone', toSeq: 0 } as EventPayload), N, false)).toBeNull();
    expect(rollOffBannerOf(batch(ranked({ x: 1 })), N, false)).toBeNull();
  });

  it('a ranking from before the faces were named shows the first team alone', () => {
    expect(rollOffBannerOf(batch(ranked({ place_1: 1, place_2: 2 })), N, false)).toEqual({ text: 'Team 1 goes first', seconds: 4, detail: null });
  });
});
