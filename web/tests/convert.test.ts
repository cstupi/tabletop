import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { convert, describeGlb } from '../src/lib/mesh/convert';
import { BUDGETS } from '../src/lib/mesh/decimate';

const file = (name: string) => { const b = readFileSync(name); return { name, bytes: b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength) }; };

describe('convert', () => {
  it('merges a kit, meets the budget, carries normals, guesses, warns, and describes its own GLB', async () => {
    const steps: string[] = [];
    const r = await convert({ kind: 'miniature', files: [file('tests/fixtures/kit-body.stl'), file('tests/fixtures/kit-arm.stl')] }, (s) => steps.push(s.step));
    expect(r.trianglesBefore).toBe(140);
    expect(r.trianglesAfter).toBe(140);
    expect(r.proposal.upAxis).toBe('z');
    expect(r.supports.suspected).toBe(false);
    expect(steps).toEqual(['parse', 'weld', 'decimate', 'normals', 'export', 'analyse']);
    const d = await describeGlb(r.glb);
    expect(d.triangles).toBe(140);
    expect(d.bounds.max[0]).toBeCloseTo(14, 4);      // centred: the kit spanned x = -8..20
    expect(d.bounds.min[2]).toBeCloseTo(0, 4);       // standing on z = 0
  });
  it('decimates a dense terrain piece to its budget and flags supports on the supported one', async () => {
    const dense = await convert({ kind: 'terrain', files: [file('tests/fixtures/dense-binary.stl')] });
    expect(dense.trianglesBefore).toBe(47_800);   // 48,000 in the file; convert measures after the weld, which drops the north-pole fan
    expect(dense.trianglesAfter).toBeLessThanOrEqual(BUDGETS.terrain);
    expect(dense.volume.top).toBeCloseTo(40 / 25.4, 3);
    const supported = await convert({ kind: 'miniature', files: [file('tests/fixtures/supported-binary.stl')] });
    expect(supported.supports.suspected).toBe(true);
  });
  it('takes a forced up-axis for the recentre, so the other axis is the one floored', async () => {
    const z = await convert({ kind: 'terrain', upAxis: 'z', files: [file('tests/fixtures/dense-binary.stl')] });
    expect(z.bounds.min[2]).toBeCloseTo(0, 3);
    expect(z.bounds.min[1]).toBeCloseTo(-20, 1);     // 40 long along Y, centred
    expect(z.proposal.upAxis).toBe('z');
    const y = await convert({ kind: 'terrain', upAxis: 'y', files: [file('tests/fixtures/dense-binary.stl')] });
    expect(y.bounds.min[1]).toBeCloseTo(0, 3);
    expect(y.bounds.min[2]).toBeLessThan(-1);
    expect(y.proposal.upAxis).toBe('y');
  });
  const real = 'tests/fixtures/real';
  it.skipIf(!existsSync(real))('converts every real STL dropped in tests/fixtures/real within budget', async () => {
    for (const name of readdirSync(real).filter((n) => n.toLowerCase().endsWith('.stl'))) {
      const r = await convert({ kind: 'miniature', files: [file(`${real}/${name}`)] });
      expect(r.trianglesAfter, name).toBeLessThanOrEqual(BUDGETS.miniature);
      expect(r.glb.byteLength, name).toBeGreaterThan(0);
    }
  });
});
