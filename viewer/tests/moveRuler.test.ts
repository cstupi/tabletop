import { describe, expect, it } from 'vitest';
import type { Frame } from '../src/lib/live/apply';
import { gate, isDisembark, isShelfPlacement, read, readoutText, usedInFrame } from '../src/lib/play/moveRuler';
import type { MoveRegion, Location } from '../src/lib/protocol/wire.generated';
import { Applier } from '../src/lib/live/apply';
import { sample } from './play';

const O = { x: 0, y: 0 };
const region = (over: Partial<MoveRegion> = {}): MoveRegion => ({
  kind: 'move_region', modelIds: [], allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false, ignoresTerrain: false, ...over,
});

describe('MoveRuler', () => {
  it('gate allows a commit with every model in range', () => {
    expect(gate([read(O, { x: 3, y: 4 }, 6)], 'strict')).toEqual({ allowed: true, reason: null });
  });

  it('gate counts the models past their allowance and strict disables the commit', () => {
    const over = read(O, { x: 8, y: 0 }, 6), fine = read(O, { x: 2, y: 0 }, 6);
    expect(gate([over, fine, over], 'strict')).toEqual({ allowed: false, reason: '2 models past their allowance.' });
    expect(gate([over], 'strict')).toEqual({ allowed: false, reason: '1 model past its allowance.' });
  });

  it('gate under warn or silent shows the reason and allows the commit', () => {
    const over = read(O, { x: 8, y: 0 }, 6);
    expect(gate([over], 'warn')).toEqual({ allowed: true, reason: '1 model past its allowance.' });
    expect(gate([over], 'silent')).toEqual({ allowed: true, reason: '1 model past its allowance.' });
  });

  it('gate counts a model short of its minimum', () => {
    const short = read(O, { x: 20, y: 0 }, 36, 30), over = read(O, { x: 40, y: 0 }, 36, 30);
    expect(gate([over, short, short], 'strict')).toEqual({ allowed: false, reason: '1 model past its allowance · 2 models short of their minimum.' });
  });

  it('gate names each contact reason once with its count', () => {
    const marked = new Map([['a', 'within 1" of Orcs'], ['b', 'within 1" of Orcs'], ['c', 'path crosses a model']]);
    expect(gate([], 'strict', marked)).toEqual({ allowed: false, reason: 'within 1" of Orcs (2 models) · path crosses a model.' });
    expect(gate([], 'warn', marked).allowed).toBe(true);
    const over = read(O, { x: 8, y: 0 }, 6);
    expect(gate([over], 'strict', new Map([['c', 'path crosses a model']]))).toEqual({ allowed: false, reason: '1 model past its allowance · path crosses a model.' });
  });

  it('the ruler reads displacement and remaining allowance', () => {
    expect(read(O, { x: 6, y: 0 }, 7.5)).toMatchObject({ distance: 6, remaining: 1.5, overAllowance: false, outOfRange: false });
    expect(read(O, { x: 6, y: 0 }, null)).toMatchObject({ remaining: null, outOfRange: false });
    expect(read(O, { x: 9, y: 0 }, 7.5)).toMatchObject({ remaining: 0, overAllowance: true, outOfRange: true });
  });

  it('a minimum move reads short moves red', () => {
    expect(read(O, { x: 20, y: 0 }, 36, 30)).toMatchObject({ underMinimum: true, outOfRange: true });
    expect(read(O, { x: 30, y: 0 }, 36, 30).outOfRange).toBe(false);
  });

  it('readoutText is RulerNode\'s label', () => {
    expect(readoutText(read(O, { x: 6, y: 0 }, 7.5))).toBe('6.0"  (1.5" left)');
    expect(readoutText(read(O, { x: 6, y: 0 }, null))).toBe('6.0"');
  });

  it('used in frame sums the frame\'s table-to-table moves', () => {
    const at = (x: number, y: number): Location => ({ kind: 'table', placement: { position: { x, y, z: 0 }, yaw: { degrees: 0 } } });
    const step = (changes: Frame['steps'][number]['changes']) => ({ step: 's', answer: undefined, rolls: [], changes, values: undefined, applied: undefined });
    const frame = {
      id: 'f', sequence: 'activation', context: {} as never, outline: [],
      steps: [
        step([{ kind: 'models_moved', moves: [{ modelId: 'm', from: at(0, 0), to: at(3, 4) }, { modelId: 'other', from: at(0, 0), to: at(9, 0) }] }]),
        step([{ kind: 'models_moved', moves: [{ modelId: 'm', from: at(3, 4), to: at(3, 6) }, { modelId: 'm', from: { kind: 'shelf' }, to: at(1, 1) }] }]),
      ],
    } as Frame;
    expect(usedInFrame(frame, 'm')).toBe(7);
  });

  it('a region anchored on a model over shelf models is a placement, not a disembark', () => {
    const s = sample();
    const p = new Applier(s.board, s.game).projection;               // every model starts on the shelf
    const anchor = s.rival.id, mover = s.model.id;
    const place = region({ modelIds: [mover], allowance: 0, containment: { enterable: [], exitingId: anchor, exitWithin: 6 } });
    expect(isDisembark(place)).toBe(true);
    expect(isShelfPlacement(place, p)).toBe(true);
    p.models.get(mover)!.location = { kind: 'table', placement: { position: { x: 1, y: 1, z: 0 }, yaw: { degrees: 0 } } };
    expect(isShelfPlacement(place, p)).toBe(false);
    expect(isShelfPlacement(region({ modelIds: [mover] }), p)).toBe(false);
    expect(isDisembark(region())).toBe(false);
  });
});
