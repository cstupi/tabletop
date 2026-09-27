// web/tests/fixtures/make-fixtures.ts — deterministic STLs. Units are millimetres as slicers export them.
import { mkdirSync, writeFileSync } from 'node:fs';
type V = [number, number, number];
const tris: V[][] = [];
function quad(a: V, b: V, c: V, d: V) { tris.push([a, b, c], [a, c, d]); }
function box(cx: number, cy: number, cz: number, sx: number, sy: number, sz: number) {
  const [x0, x1, y0, y1, z0, z1] = [cx - sx / 2, cx + sx / 2, cy - sy / 2, cy + sy / 2, cz - sz / 2, cz + sz / 2] as const;
  quad([x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]); quad([x0, y0, z1], [x0, y1, z1], [x1, y1, z1], [x1, y0, z1]);
  quad([x0, y0, z0], [x0, y0, z1], [x1, y0, z1], [x1, y0, z0]); quad([x1, y1, z0], [x1, y1, z1], [x0, y1, z1], [x0, y1, z0]);
  quad([x0, y0, z0], [x0, y1, z0], [x0, y1, z1], [x0, y0, z1]); quad([x1, y0, z0], [x1, y0, z1], [x1, y1, z1], [x1, y1, z0]);
}
/** A cylinder along `up` (2 = Z, 1 = Y), base at 0. */
function cylinder(r: number, h: number, segments: number, up: 1 | 2) {
  const P = (a: number, z: number, rr = r): V => { const x = rr * Math.cos(a), y = rr * Math.sin(a); return up === 2 ? [x, y, z] : [x, z, -y]; };
  for (let i = 0; i < segments; i++) {
    const a0 = (2 * Math.PI * i) / segments, a1 = (2 * Math.PI * (i + 1)) / segments;
    quad(P(a0, 0), P(a1, 0), P(a1, h), P(a0, h));
    tris.push([P(a0, 0, 0), P(a1, 0), P(a0, 0)], [P(a0, h, 0), P(a0, h), P(a1, h)]);
  }
}
function ellipsoid(rx: number, ry: number, rz: number, rings: number, segs: number) {
  const P = (t: number, p: number): V => [rx * Math.sin(t) * Math.cos(p), ry * Math.cos(t), rz * Math.sin(t) * Math.sin(p)];
  for (let i = 0; i < rings; i++) for (let j = 0; j < segs; j++) {
    const t0 = (Math.PI * i) / rings, t1 = (Math.PI * (i + 1)) / rings, p0 = (2 * Math.PI * j) / segs, p1 = (2 * Math.PI * (j + 1)) / segs;
    quad(P(t0, p0), P(t1, p0), P(t1, p1), P(t0, p1));
  }
}
function binary(): Buffer {
  const buf = Buffer.alloc(84 + 50 * tris.length); buf.write('binary stl fixture', 0, 'latin1'); buf.writeUInt32LE(tris.length, 80);
  tris.forEach((t, i) => { const o = 84 + 50 * i; buf.fill(0, o, o + 12); t.forEach((v, k) => v.forEach((c, m) => buf.writeFloatLE(c, o + 12 + 12 * k + 4 * m))); buf.writeUInt16LE(0, o + 48); });
  return buf;
}
function ascii(): string {
  return `solid cube\n${tris.map((t) => `facet normal 0 0 0\nouter loop\n${t.map((v) => `vertex ${v.join(' ')}`).join('\n')}\nendloop\nendfacet`).join('\n')}\nendsolid cube\n`;
}
function emit(name: string, body: Buffer | string) { writeFileSync(`tests/fixtures/${name}`, body); tris.length = 0; }

mkdirSync('tests/fixtures', { recursive: true });
box(10, 10, 10, 20, 20, 20);                          emit('cube-ascii.stl', ascii());                 // 12 triangles, 0..20 mm
cylinder(10, 40, 64, 2);                              emit('cylinder-binary.stl', binary());           // 256 triangles, Z up, taller than wide
cylinder(8, 30, 32, 2);                               emit('kit-body.stl', binary());
box(14, 0, 22, 12, 4, 4);                             emit('kit-arm.stl', binary());                   // sits beside the body, already placed
box(0, 0, 35, 20, 20, 10); for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) box(-7.5 + 5 * i, -7.5 + 5 * j, 15, 0.8, 0.8, 30);
                                                      emit('supported-binary.stl', binary());          // a blob on sixteen 0.8 mm pillars
ellipsoid(8, 20, 8, 120, 200);                        emit('dense-binary.stl', binary());              // 48,000 triangles, Y up
console.log('fixtures written');
