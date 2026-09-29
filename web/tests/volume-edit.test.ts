import { describe, expect, it } from 'vitest';
import { fromDraft, screenToTable, toDraft, validate } from '../src/lib/library/volume-edit';
import type { Volume } from '../src/lib/api/types';

const v: Volume = { kind: 'area', footprint: { kind: 'circle', a: 3, b: 3 }, offset: { x: 1, y: -2 }, yaw: { degrees: 30 }, bottom: 2, top: 8, declarations: 'blocks_line_of_sight, see_in_and_out, cover', notes: 'forest' };

describe('volume drafts', () => {
  it('round-trips and keeps a circle square', () => {
    const d = toDraft(v);
    expect(d.declarations).toEqual(new Set(['blocks_line_of_sight', 'see_in_and_out', 'cover']));
    expect(fromDraft(d)).toEqual(v);
    expect(fromDraft({ ...d, footprintKind: 'circle', a: 4, b: 9 }).footprint).toEqual({ kind: 'circle', a: 4, b: 4 });
    expect(fromDraft({ ...d, notes: '  ' }).notes).toBeUndefined();
  });
  it('validates heights, dimensions and the see-in-and-out modifier', () => {
    expect(validate(toDraft(v))).toEqual([]);
    expect(validate({ ...toDraft(v), top: 1 })).toContain('top must be above bottom');
    expect(validate({ ...toDraft(v), a: 0 })).toContain('dimensions must be positive');
    expect(validate({ ...toDraft(v), declarations: new Set(['see_in_and_out']) })).toContain('see in and out needs blocks line of sight');
  });
  it('maps a click in the top-down view to table coordinates', () => {
    // a 400×200 viewport showing x ∈ [-4, 4], y ∈ [-2, 2]: the centre is the origin, +y is up on screen
    expect(screenToTable(200, 100, { width: 400, height: 200 }, { minX: -4, maxX: 4, minY: -2, maxY: 2 })).toEqual({ x: 0, y: 0 });
    expect(screenToTable(400, 0, { width: 400, height: 200 }, { minX: -4, maxX: 4, minY: -2, maxY: 2 })).toEqual({ x: 4, y: 2 });
  });
});
