// web/src/lib/mesh/stl.ts — STL in, triangle soup out (9 floats per triangle). Per-face normals are discarded; Task 8 recomputes them.
export class StlError extends Error {}

export function parseStl(buffer: ArrayBuffer): Float32Array {
  if (buffer.byteLength >= 84) {
    const count = new DataView(buffer).getUint32(80, true);
    if (84 + 50 * count === buffer.byteLength) return parseBinary(buffer, count);
  }
  const text = new TextDecoder('latin1').decode(new Uint8Array(buffer, 0, Math.min(buffer.byteLength, 512)));
  if (/^\s*solid/.test(text)) return parseAscii(new TextDecoder().decode(buffer));
  throw new StlError('Not an STL file: neither a binary facet table nor an ASCII "solid".');
}

function parseBinary(buffer: ArrayBuffer, count: number): Float32Array {
  const view = new DataView(buffer), out = new Float32Array(count * 9);
  for (let i = 0; i < count; i++) {
    const o = 84 + 50 * i + 12;                                   // skip the facet normal
    for (let k = 0; k < 9; k++) out[i * 9 + k] = view.getFloat32(o + 4 * k, true);
  }
  return out;
}

function parseAscii(text: string): Float32Array {
  const values: number[] = [];
  const re = /vertex\s+([-+.\deE]+)\s+([-+.\deE]+)\s+([-+.\deE]+)/g;
  for (let m = re.exec(text); m; m = re.exec(text)) values.push(Number(m[1]), Number(m[2]), Number(m[3]));
  if (values.length === 0 || values.length % 9 !== 0) throw new StlError('ASCII STL has no complete facets.');
  if (!values.every(Number.isFinite)) throw new StlError('ASCII STL has a vertex that is not a number.');
  return Float32Array.from(values);
}

/** Kits ship as parts already placed relative to each other; merging is concatenation and no fitting is attempted. */
export function mergeSoups(parts: Float32Array[]): Float32Array {
  const out = new Float32Array(parts.reduce((n, p) => n + p.length, 0));
  let o = 0; for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}
