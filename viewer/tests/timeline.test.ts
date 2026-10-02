import { describe, expect, it } from 'vitest';
import { EMPTY, frameOf, indexOfSeq, reduce, type Timeline } from '../src/lib/timeline/timeline';

const of = (count: number, over: Partial<Timeline> = {}): Timeline => ({ ...EMPTY, count, index: 0, ...over });

describe('the timeline reducer', () => {
  it('starts empty, with no frame to show', () => {
    expect(EMPTY).toEqual({ count: 0, index: -1, playing: false, fps: 8, follow: false });
    expect(frameOf(['a'], EMPTY)).toBeNull();
  });

  it('takes a frame count and lands on the first frame', () => {
    expect(reduce(EMPTY, { type: 'frames', count: 5 })).toMatchObject({ count: 5, index: 0 });
  });

  it('follows the end of a growing feed until the user steps, then stops following', () => {
    let t = reduce({ ...EMPTY, follow: true }, { type: 'frames', count: 3 });
    expect(t.index).toBe(2);
    t = reduce(t, { type: 'frames', count: 4 });
    expect(t.index).toBe(3);
    t = reduce(t, { type: 'step', by: -1 });
    expect(t).toMatchObject({ index: 2, follow: false, playing: false });
    expect(reduce(t, { type: 'frames', count: 9 }).index).toBe(2);
  });

  it('steps and clamps at both ends', () => {
    expect(reduce(of(3), { type: 'step', by: -1 }).index).toBe(0);
    expect(reduce(of(3, { index: 2 }), { type: 'step', by: 1 }).index).toBe(2);
    expect(reduce(of(3), { type: 'step', by: 5 }).index).toBe(2);
    expect(reduce(of(3, { index: 1 }), { type: 'first' }).index).toBe(0);
    expect(reduce(of(3, { index: 1 }), { type: 'last' })).toMatchObject({ index: 2, follow: true });
  });

  it('plays, ticks, and stops itself at the last frame', () => {
    let t = reduce(of(3), { type: 'play' });
    expect(t.playing).toBe(true);
    t = reduce(t, { type: 'tick' });
    expect(t.index).toBe(1);
    t = reduce(t, { type: 'tick' });
    expect(t).toMatchObject({ index: 2, playing: false });
    expect(reduce(t, { type: 'tick' }).index).toBe(2);
    expect(reduce(of(3), { type: 'toggle' }).playing).toBe(true);
    expect(reduce(of(3, { playing: true }), { type: 'toggle' }).playing).toBe(false);
  });

  it('scrubs to a clamped index and pauses', () => {
    expect(reduce(of(5, { playing: true }), { type: 'scrub', index: 3 })).toMatchObject({ index: 3, playing: false, follow: false });
    expect(reduce(of(5), { type: 'scrub', index: 99 }).index).toBe(4);
    expect(reduce(of(5), { type: 'scrub', index: -3 }).index).toBe(0);
  });

  it('clamps the speed', () => {
    expect(reduce(of(2), { type: 'fps', fps: 0 }).fps).toBe(1);
    expect(reduce(of(2), { type: 'fps', fps: 500 }).fps).toBe(60);
  });

  it('jumps to a sequence, and to the frame that precedes one the log does not have', () => {
    const seqs = [1, 2, 5, 9];
    expect(indexOfSeq(seqs, 5)).toBe(2);
    expect(indexOfSeq(seqs, 7)).toBe(2);
    expect(indexOfSeq(seqs, 1)).toBe(0);
    expect(indexOfSeq(seqs, 0)).toBe(0);
    expect(indexOfSeq(seqs, 99)).toBe(3);
    expect(indexOfSeq([], 4)).toBe(-1);
    expect(reduce(of(4, { playing: true }), { type: 'jump', seq: 7, seqs })).toMatchObject({ index: 2, playing: false });
  });

  it('hands back the frame at the index, or null', () => {
    expect(frameOf(['a', 'b', 'c'], of(3, { index: 2 }))).toBe('c');
    expect(frameOf(['a'], of(1, { index: 4 }))).toBeNull();
  });

  it('shrinks with the feed rather than pointing past the end', () => {
    expect(reduce(of(9, { index: 8 }), { type: 'frames', count: 3 }).index).toBe(2);
    expect(reduce(of(9, { index: 8 }), { type: 'frames', count: 0 })).toMatchObject({ count: 0, index: -1 });
  });
});
