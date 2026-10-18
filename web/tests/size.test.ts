import { describe, expect, it } from 'vitest';
import { describeSize, GLB_DEFAULTS, suggestUnit, toggleUpright } from '../src/lib/mesh/size';
import { UNIT_SCALES } from '../src/lib/units/units';
import { toWorld, worldExtent } from '../src/lib/mesh/guess';
import type { Bounds } from '../src/lib/mesh/types';

// A Y-up miniature: 25 wide (X), 32 tall (Y), 25 deep (Z), times k.
const yUp = (k: number): Bounds => ({ min: [-12.5 * k, 0, -12.5 * k], max: [12.5 * k, 32 * k, 12.5 * k] });

describe('GLB defaults', () => {
  it('reads glTF as metres, Y-up', () => {
    expect(GLB_DEFAULTS.scale).toBeCloseTo(39.3701, 4);
    expect(GLB_DEFAULTS.orientation).toEqual({ pitch: 90, yaw: 180, roll: 0 });
  });
  it("turns glTF's front (+Z) to the facing arrow (+Y)", () => {
    const [x, y, z] = toWorld([0, 0, 1], { scale: 1, orientation: GLB_DEFAULTS.orientation });
    expect(x).toBeCloseTo(0, 9); expect(y).toBeCloseTo(1, 9); expect(z).toBeCloseTo(0, 9);
  });
  it('stands a Y-up model upright: its Y becomes the table height', () => {
    const e = worldExtent(yUp(0.001), GLB_DEFAULTS);
    expect(e.height * 25.4).toBeCloseTo(32, 6);
    expect(e.width * 25.4).toBeCloseTo(25, 6);
    expect(e.length * 25.4).toBeCloseTo(25, 6);
  });
  it('has a scale for every unit button', () => {
    expect(UNIT_SCALES).toEqual({ mm: 1 / 25.4, cm: 10 / 25.4, in: 1, m: 1000 / 25.4 });
  });
});

describe('suggestUnit', () => {
  it('suggests nothing when metres give a miniature', () => {
    expect(suggestUnit(yUp(0.001), UNIT_SCALES.m)).toBeNull();
  });
  it('suggests millimetres for a model exported in mm', () => {
    expect(suggestUnit(yUp(1), UNIT_SCALES.m)).toBe('mm');      // 32 m as metres
  });
  it('suggests centimetres for a model exported in cm', () => {
    expect(suggestUnit(yUp(0.1), UNIT_SCALES.m)).toBe('cm');    // 3.2 m as metres; 3.2 mm or 32 mm
  });
  it('suggests nothing when no unit makes it plausible', () => {
    expect(suggestUnit(yUp(1000), UNIT_SCALES.m)).toBeNull();   // 32 km, 32 m, 320 m
    expect(suggestUnit(yUp(1e-6), UNIT_SCALES.m)).toBeNull();   // under 2 mm in every unit
  });
  it('flags a model shorter than 2 mm', () => {
    expect(suggestUnit({ min: [0, 0, 0], max: [1.5, 1.5, 1.5] }, UNIT_SCALES.mm)).toBe('cm');
  });
});

describe('describeSize', () => {
  it('gives height and footprint in mm with inches in parentheses', () => {
    expect(describeSize(yUp(0.001), GLB_DEFAULTS)).toBe('32 mm tall, 25 × 25 mm footprint (1.26 in tall, 0.98 × 0.98 in)');
  });
  it('follows the scale', () => {
    expect(describeSize(yUp(1), { scale: UNIT_SCALES.mm, orientation: GLB_DEFAULTS.orientation })).toBe('32 mm tall, 25 × 25 mm footprint (1.26 in tall, 0.98 × 0.98 in)');
    expect(describeSize(yUp(1), GLB_DEFAULTS)).toBe('32000 mm tall, 25000 × 25000 mm footprint (1259.8 in tall, 984.3 × 984.3 in)');
  });
  it('shows small sizes to a tenth of a millimetre', () => {
    expect(describeSize({ min: [0, 0, 0], max: [1.5, 1.5, 1.5] }, { scale: UNIT_SCALES.mm, orientation: { pitch: 0, yaw: 0, roll: 0 } })).toBe('1.5 mm tall, 1.5 × 1.5 mm footprint (0.06 in tall, 0.06 × 0.06 in)');
  });
});

describe('toggleUpright', () => {
  it('lays a standing model flat, keeping yaw', () => {
    expect(toggleUpright({ pitch: 90, yaw: 180, roll: 0 }, true)).toEqual({ pitch: 0, yaw: 180, roll: 0 });
  });
  it('stands a flat glTF up with the glTF yaw when it had none', () => {
    expect(toggleUpright({ pitch: 0, yaw: 0, roll: 0 }, true)).toEqual({ pitch: 90, yaw: 180, roll: 0 });
  });
  it('keeps a chosen yaw, and an STL never gets the glTF yaw', () => {
    expect(toggleUpright({ pitch: 0, yaw: 90, roll: 0 }, true).yaw).toBe(90);
    expect(toggleUpright({ pitch: 0, yaw: 0, roll: 0 }, false).yaw).toBe(0);
  });
});
