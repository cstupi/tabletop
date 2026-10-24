// Every case of tests/fixtures/geometry.cases.json (written by tests/Tabletop.Geometry.Wasm.Tests) through the WebAssembly Geometry:
// the answer must be what C# computed by calling Tabletop.Geometry directly.
import { readFileSync } from 'node:fs';
import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import { loadTestGeometry } from './geometry';

interface Case { readonly name: string; readonly method: string; readonly request: unknown; readonly expected: unknown }
const cases = JSON.parse(readFileSync('tests/fixtures/geometry.cases.json', 'utf8')) as Case[];

/** Equal structure; numbers within 1e-9. Returns where they differ, or null. */
function close(a: unknown, b: unknown, path = '$'): string | null {
  if (typeof a === 'number' && typeof b === 'number') return Math.abs(a - b) <= 1e-9 ? null : `${path}: ${a} vs ${b}`;
  if (Array.isArray(a) && Array.isArray(b)) {
    if (a.length !== b.length) return `${path}: length ${a.length} vs ${b.length}`;
    for (let i = 0; i < a.length; i++) { const d = close(a[i], b[i], `${path}[${i}]`); if (d) return d; }
    return null;
  }
  if (a && b && typeof a === 'object' && typeof b === 'object') {
    const ka = Object.keys(a).sort(), kb = Object.keys(b).sort();
    if (ka.join() !== kb.join()) return `${path}: members ${ka} vs ${kb}`;
    for (const k of ka) { const d = close((a as Record<string, unknown>)[k], (b as Record<string, unknown>)[k], `${path}.${k}`); if (d) return d; }
    return null;
  }
  return a === b ? null : `${path}: ${String(a)} vs ${String(b)}`;
}

const FACADE = ['BodyToBody', 'Contacts', 'EdgeToEdge', 'EdgeToMarker', 'EdgeToPoint', 'LandModels', 'LandPiece', 'ModelSurfaces', 'NearestPair',
  'OverlapBases', 'PieceSurfaces', 'RangeOutline', 'RestModel', 'RestPiece', 'TableHoldsBase', 'TurnPiece', 'ZoneContains', 'ZoneHoldsBase', 'ZonePolygons'];

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe('Geometry in WebAssembly', () => {
  it.each(cases.map((c) => [c.name, c] as const))('%s agrees with C#', (_name, c) => {
    const answer = JSON.parse(g.call(c.method, JSON.stringify(c.request)));
    expect(close(c.expected, answer)).toBeNull();
  });

  it('covers every Geometry method', () => {
    expect([...new Set(cases.map((c) => c.method))].sort()).toEqual(FACADE);
  });
});
