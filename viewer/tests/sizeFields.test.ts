import { describe, expect, it } from 'vitest';
import { SizeFields } from '../src/lib/setup/sizeFields';

describe('SizeFields', () => {
  it('keeps both typed edges when the draft has not changed, so Set sends them', () => {
    const f = new SizeFields({ width: 72, length: 48 });
    f.width = 36;
    f.follow({ width: 72, length: 48 });               // a board frame for another edit, or leaving the field
    f.length = 30;
    f.follow({ width: 72, length: 48 });
    expect([f.width, f.length]).toEqual([36, 30]);
    expect(f.changed({ width: 72, length: 48 })).toBe(true);
  });

  it("takes another player's new size", () => {
    const f = new SizeFields({ width: 72, length: 48 });
    f.width = 36;
    f.follow({ width: 60, length: 44 });
    expect([f.width, f.length]).toEqual([60, 44]);
    expect(f.changed({ width: 60, length: 44 })).toBe(false);
  });
});
