# Task 6: Units, base presets and the volume wire helpers

**Files:**
- Create: `web/src/lib/units/units.ts`, `web/src/lib/units/bases.ts`, `web/src/lib/units/volumes.ts`
- Test: `web/tests/units.test.ts`

**Interfaces:**
- Consumes: `Base`, `BaseShape`, `Volume`, `Declaration` from `api/types.ts`.
- Produces: `MM_PER_INCH = 25.4`, `mmToInches(mm)`, `inchesToMm(inches)`, `round(n, places = 3)`; `BASE_PRESETS: BasePreset[]` (`{ label, base }`), `presetFor(base): BasePreset | undefined` (match within 0.005"), `baseLabel(base): string` (the preset label or `"32 × 20 mm oval"` style text), `circle(mm)`, `oval(lengthMm, widthMm)`, `square(mm)`; `DECLARATIONS: Declaration[]`, `DECLARATION_LABELS: Record<Declaration, string>`, `parseDeclarations(wire: string): Set<Declaration>`, `formatDeclarations(set: Set<Declaration>): string`, `defaultVolume(width, length, top): Volume`.
- Presets are UI over a shape, not the stored value (geometry values → Bases: "`32mm round` is a *name*, `1.26` is the value"). The list: rounds 25, 28.5, 32, 40, 50, 60, 80, 100, 130, 160 mm; ovals 60×35, 75×42, 90×52, 105×70, 120×92, 170×105 mm; squares 20, 25, 40, 50 mm.
- The `[Flags]` wire format is what `System.Text.Json`'s `JsonStringEnumConverter(SnakeCaseLower)` writes for a flags enum: `"none"`, one name, or names joined by `", "` in ascending flag order — `impassable, blocks_line_of_sight, see_in_and_out, cover, difficult, dangerous`.

Spec: geometry values → Bases (three shapes, presets are names), Collision volumes and Declarations (the six, per volume), Where volumes come from ("rect footprint, `bottom 0`, `top` the mesh height, `kind: solid`"); `architecture.md` → Web frontend ("Base sizes are a preset dropdown (25/32/40mm round, oval sizes)").

- [ ] **Step 1: Write the failing tests**

```ts
// web/tests/units.test.ts
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
```

- [ ] **Step 2: Run; expect failures. Write the three modules:**

```ts
// web/src/lib/units/units.ts
export const MM_PER_INCH = 25.4;
export const mmToInches = (mm: number) => mm / MM_PER_INCH;
export const inchesToMm = (inches: number) => inches * MM_PER_INCH;
export const round = (n: number, places = 3) => Math.round(n * 10 ** places) / 10 ** places;
```

```ts
// web/src/lib/units/bases.ts — presets are names over a shape; the stored value is always inches.
import type { Base } from '$lib/api/types';
import { inchesToMm, mmToInches, round } from './units';

export interface BasePreset { label: string; base: Base }
export const circle = (mm: number): Base => ({ shape: 'circle', a: mmToInches(mm), b: mmToInches(mm) });
export const oval = (lengthMm: number, widthMm: number): Base => ({ shape: 'oval', a: mmToInches(lengthMm), b: mmToInches(widthMm) });
export const rect = (lengthMm: number, widthMm: number): Base => ({ shape: 'rect', a: mmToInches(lengthMm), b: mmToInches(widthMm) });

export const BASE_PRESETS: BasePreset[] = [
  ...[25, 28.5, 32, 40, 50, 60, 80, 100, 130, 160].map((mm) => ({ label: `${mm} mm round`, base: circle(mm) })),
  ...([[60, 35], [75, 42], [90, 52], [105, 70], [120, 92], [170, 105]] as const).map(([l, w]) => ({ label: `${l} × ${w} mm oval`, base: oval(l, w) })),
  ...[20, 25, 40, 50].map((mm) => ({ label: `${mm} mm square`, base: rect(mm, mm) })),
  ...([[221, 119]] as const).map(([l, w]) => ({ label: `${l} × ${w} mm vehicle`, base: rect(l, w) })),
];

const close = (x: number, y: number) => Math.abs(x - y) < 0.005;
export const presetFor = (b: Base) => BASE_PRESETS.find((p) => p.base.shape === b.shape && close(p.base.a, b.a) && close(p.base.b, b.b));

export function baseLabel(b: Base): string {
  const preset = presetFor(b);
  if (preset) return preset.label;
  const mm = (x: number) => round(inchesToMm(x), 1);
  if (b.shape === 'circle') return `${mm(b.a)} mm round (custom)`;
  return `${mm(b.a)} × ${mm(b.b)} mm ${b.shape} (custom)`;
}
```

```ts
// web/src/lib/units/volumes.ts — the six declarations and the flags wire format; the bounding-box default.
import type { Declaration, Volume } from '$lib/api/types';

export const DECLARATIONS: Declaration[] = ['impassable', 'blocks_line_of_sight', 'see_in_and_out', 'cover', 'difficult', 'dangerous'];
export const DECLARATION_LABELS: Record<Declaration, string> = {
  impassable: 'Impassable', blocks_line_of_sight: 'Blocks line of sight', see_in_and_out: 'See in and out',
  cover: 'Cover', difficult: 'Difficult', dangerous: 'Dangerous',
};

export function parseDeclarations(wire: string): Set<Declaration> {
  return new Set(wire.split(',').map((s) => s.trim()).filter((s): s is Declaration => (DECLARATIONS as string[]).includes(s)));
}
export function formatDeclarations(set: Set<Declaration>): string {
  const names = DECLARATIONS.filter((d) => set.has(d));
  return names.length === 0 ? 'none' : names.join(', ');
}

/** Every terrain asset gets this at upload: the mesh's bounding box as one solid prism (geometry values → Where volumes come from). */
export const defaultVolume = (width: number, length: number, top: number): Volume =>
  ({ kind: 'solid', footprint: { kind: 'rect', a: width, b: length }, offset: { x: 0, y: 0 }, yaw: { degrees: 0 }, bottom: 0, top, declarations: 'none' });
```

- [ ] **Step 3: Run `npm test`; expect 14 passed. Commit: `Add unit conversion, base presets and the declarations wire format`.**
