import { describe, expect, it } from 'vitest';
import { inchesToMm, mmToInches } from '../src/lib/units/units';
import { BASE_PRESETS, baseLabel, circle, oval, presetFor } from '../src/lib/units/bases';
import { DECLARATIONS, defaultVolume, formatDeclarations, parseDeclarations } from '../src/lib/units/volumes';

describe('units', () => {
  it('converts at the edge and nowhere else', () => {
    expect(mmToInches(25.4)).toBe(1);
    expect(inchesToMm(1.26)).toBeCloseTo(32.004, 3);
  });
});

describe('bases', () => {
  it('names the familiar sizes and stores inches', () => {
    expect(circle(32)).toEqual({ shape: 'circle', a: 32 / 25.4, b: 32 / 25.4 });
    expect(oval(60, 35)).toEqual({ shape: 'oval', a: 60 / 25.4, b: 35 / 25.4 });
    expect(presetFor(circle(32))?.label).toBe('32 mm round');
    expect(presetFor({ shape: 'circle', a: 1.26, b: 1.26 })?.label).toBe('32 mm round');   // the contracts plan's own example value
    expect(presetFor(circle(33))).toBeUndefined();
    expect(baseLabel(circle(33))).toBe('33 mm round (custom)');
    expect(baseLabel({ shape: 'oval', a: 2, b: 1 })).toBe('50.8 × 25.4 mm oval (custom)');
    expect(BASE_PRESETS.map((p) => p.base.shape)).toContain('rect');
  });
});

describe('declarations', () => {
  it('round-trips the .NET flags wire format', () => {
    expect(parseDeclarations('none')).toEqual(new Set());
    expect(parseDeclarations('blocks_line_of_sight, cover')).toEqual(new Set(['blocks_line_of_sight', 'cover']));
    expect(formatDeclarations(new Set())).toBe('none');
    expect(formatDeclarations(new Set(['cover', 'impassable']))).toBe('impassable, cover');   // flag order, not insertion order
    expect(DECLARATIONS).toHaveLength(6);
  });
  it('builds the bounding-box default', () => {
    expect(defaultVolume(6, 4, 3)).toEqual({ kind: 'solid', footprint: { kind: 'rect', a: 6, b: 4 }, offset: { x: 0, y: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 3, declarations: 'none' });
  });
});
