import { describe, expect, it } from 'vitest';
import { bandColour, CLEAR_ALPHA, DANGEROUS_ALPHA, DIFFICULT_ALPHA, layers, striped } from '../src/lib/play/moveBandLook';
import { bandsOverlay } from '../src/lib/play/moveOverlays';

describe('move band look (MoveBandLook)', () => {
  it('the clear band is the team colour, the difficult band that colour muted and fainter, the dangerous band the terrain colour striped', () => {
    expect(bandColour('clear', '#4ea1ff')).toBe(`rgba(78, 161, 255, ${CLEAR_ALPHA})`);
    expect(bandColour('difficult', '#4ea1ff')).toBe(`rgba(103, 144, 191, ${DIFFICULT_ALPHA})`);
    expect(bandColour('dangerous', '#4ea1ff')).toBe(`rgba(240, 114, 46, ${DANGEROUS_ALPHA})`);
    expect(bandColour('dangerous', '#ff6b5c')).toBe(bandColour('dangerous', '#4ea1ff'));
    expect([striped('clear'), striped('difficult'), striped('dangerous')]).toEqual([false, false, true]);
  });

  it('layers draw clear, then difficult, then dangerous for every model', () => {
    const a = [{ x: 0, y: 0 }], b = [{ x: 1, y: 0 }];
    expect(layers([{ clear: a, difficult: b, dangerous: [] }, { clear: b, difficult: [], dangerous: a }]))
      .toEqual([{ band: 'clear', areas: [a, b] }, { band: 'difficult', areas: [b, []] }, { band: 'dangerous', areas: [[], a] }]);
  });

  it('the overlay fills each band once and strokes stripes only over the dangerous one', () => {
    const calls: string[] = [];
    const ctx = new Proxy({}, { get: (_t, k) => (typeof k === 'string' && ['fill', 'stroke', 'clip'].includes(k) ? () => calls.push(k) : () => {}), set: () => true }) as unknown as CanvasRenderingContext2D;
    const tri = [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }];
    bandsOverlay([{ clear: tri, difficult: [], dangerous: tri }], '#4ea1ff')(ctx, (p) => ({ x: p.x * 10, y: -p.y * 10 }), 10);
    expect(calls).toEqual(['fill', 'fill', 'clip', 'stroke']);
  });
});
