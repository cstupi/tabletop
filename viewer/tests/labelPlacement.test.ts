import { describe, expect, it } from 'vitest';
import { LABEL_MARGIN, placeLabel, wrapText } from '../src/lib/board/labelPlacement';

const measure = (text: string): number => text.length * 7;   // 7 px a character

describe('label placement', () => {
  it('a label near either edge moves inside the canvas whole', () => {
    const text = 'Sappers can see Berserkers';                       // 182 px
    const [left] = placeLabel(text, { x: 10, y: 50 }, measure, 800, { lineHeight: 16, grow: 'up' });
    expect(left!.x - measure(left!.text) / 2).toBeCloseTo(LABEL_MARGIN);
    const [right] = placeLabel(text, { x: 795, y: 50 }, measure, 800, { lineHeight: 16, grow: 'up' });
    expect(right!.x + measure(right!.text) / 2).toBeCloseTo(800 - LABEL_MARGIN);
    const [middle] = placeLabel(text, { x: 400, y: 50 }, measure, 800, { lineHeight: 16, grow: 'up' });
    expect(middle).toEqual({ text, x: 400, y: 50 });
  });

  it('a long reason wraps to a readable width, every line inside the canvas', () => {
    const reason = 'answer to \'move\': the unit moved in or through difficult terrain, so no model may move more than 6" this activation';
    const lines = placeLabel(reason, { x: 20, y: 300 }, measure, 600, { lineHeight: 13, grow: 'up' });
    expect(lines.length).toBeGreaterThan(1);
    for (const l of lines) {
      expect(measure(l.text)).toBeLessThanOrEqual(360);
      expect(l.x - measure(l.text) / 2).toBeGreaterThanOrEqual(LABEL_MARGIN - 1e-9);
      expect(l.x + measure(l.text) / 2).toBeLessThanOrEqual(600 - LABEL_MARGIN + 1e-9);
    }
    expect(lines.at(-1)!.y).toBe(300);                                // grown upward from the anchor
    expect(lines[0]!.y).toBe(300 - 13 * (lines.length - 1));
    expect(lines.map((l) => l.text).join(' ')).toBe(reason);
  });

  it('down puts the first line on the anchor; a canvas narrower than the label centres it', () => {
    const lines = placeLabel('one two three four', { x: 5, y: 10 }, measure, 60, { lineHeight: 16, grow: 'down' });
    expect(lines.map((l) => l.y)).toEqual(lines.map((_, i) => 10 + 16 * i));
    for (const l of lines) expect(measure(l.text)).toBeLessThanOrEqual(60 - 2 * LABEL_MARGIN);
  });

  it('wrapping cuts a word too wide alone and ends past the last line with an ellipsis', () => {
    expect(wrapText('abcdefghij', measure, 42)).toEqual(['abcde…']);
    expect(wrapText('aa bb cc dd ee', measure, 14, 2)).toEqual(['aa', 'b…']);
    expect(wrapText('', measure, 100)).toEqual([]);
  });
});
