import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { guessUpAxis, propose, worldExtent } from '../src/lib/mesh/guess';
import { detectSupports } from '../src/lib/mesh/supports';
import { terrainVolume } from '../src/lib/mesh/bounds';
import { parseStl } from '../src/lib/mesh/stl';
import { bounds } from '../src/lib/mesh/types';
import { weld } from '../src/lib/mesh/weld';

const load = (name: string) => { const b = readFileSync(`tests/fixtures/${name}`); return weld(parseStl(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength))); };

describe('guesses', () => {
  it('picks the axis along which the model is taller than wide, Z when in doubt', () => {
    expect(guessUpAxis(bounds(load('cylinder-binary.stl').positions))).toBe('z');   // 20 wide, 40 tall along Z
    expect(guessUpAxis(bounds(load('dense-binary.stl').positions))).toBe('y');      // 16 wide, 40 tall along Y
    expect(guessUpAxis(bounds(load('cube-ascii.stl').positions))).toBe('z');        // a cube: neither
  });
  it('proposes millimetres and the rotation that stands the model up', () => {
    const z = propose(bounds(load('cylinder-binary.stl').positions));
    expect(z).toEqual({ scale: 1 / 25.4, orientation: { pitch: 0, yaw: 0, roll: 0 }, upAxis: 'z', unitsGuess: 'mm' });
    const y = propose(bounds(load('dense-binary.stl').positions));
    expect(y.orientation).toEqual({ pitch: 90, yaw: 0, roll: 0 });
    const ext = worldExtent(bounds(load('dense-binary.stl').positions), y);
    expect(ext.height).toBeCloseTo(40 / 25.4, 3);
    expect(ext.width).toBeCloseTo(16 / 25.4, 3);
  });
  it('takes the up-axis it is given rather than guessing again', () => {
    const b = bounds(load('cube-ascii.stl').positions);
    expect(propose(b, 'y').orientation).toEqual({ pitch: 90, yaw: 0, roll: 0 });
    expect(propose(b, 'y').upAxis).toBe('y');
    expect(propose(b).orientation).toEqual({ pitch: 0, yaw: 0, roll: 0 });
  });
  it('writes the bounding-box volume in inches, bottom 0, solid', () => {
    const b = bounds(load('cube-ascii.stl').positions);
    const s = 20 * (1 / 25.4);   // the asset row carries one scale; every consumer multiplies by it
    expect(terrainVolume(b, propose(b))).toEqual({ kind: 'solid', footprint: { kind: 'rect', a: s, b: s }, offset: { x: 0, y: 0 }, yaw: { degrees: 0 }, bottom: 0, top: s, declarations: 'none' });
  });
});

describe('detectSupports', () => {
  it('fires on the pillared fixture and on nothing else', () => {
    const r = detectSupports(load('supported-binary.stl'));
    expect(r.components).toBe(17);
    expect(r.supportLike).toBe(16);
    expect(r.suspected).toBe(true);
    for (const f of ['cube-ascii.stl', 'cylinder-binary.stl', 'dense-binary.stl']) expect(detectSupports(load(f)).suspected).toBe(false);
  });
  it('does not count zero-extent orphan vertices as supports', () => {
    const box = [0, 0, 0, 4, 0, 0, 4, 4, 0, 0, 4, 0, 0, 0, 40, 4, 0, 40, 4, 4, 40, 0, 4, 40];
    const orphans = [1, 1, 0, 1, 2, 0, 2, 1, 0, 2, 2, 0, 3, 1, 0, 3, 2, 0];
    const mesh = {
      positions: Float32Array.from([...box, ...orphans]),
      indices: Uint32Array.from([0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 3, 0, 4, 3, 4, 7]),
    };
    expect(detectSupports(mesh).supportLike).toBe(0);
    expect(detectSupports(mesh).suspected).toBe(false);
  });
});
