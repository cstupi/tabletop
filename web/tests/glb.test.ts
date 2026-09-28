import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { exportGlb, readGlb } from '../src/lib/mesh/glb';
import { creasedNormals } from '../src/lib/mesh/normals';
import { parseStl } from '../src/lib/mesh/stl';
import { bounds } from '../src/lib/mesh/types';
import { weld } from '../src/lib/mesh/weld';

describe('glb', () => {
  it('writes a GLB that reads back with the same triangles and extent', async () => {
    const b = readFileSync('tests/fixtures/cylinder-binary.stl');
    const mesh = creasedNormals(weld(parseStl(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength))));
    const glb = await exportGlb(mesh, 'cylinder');
    expect(new TextDecoder().decode(glb.subarray(0, 4))).toBe('glTF');
    expect(new DataView(glb.buffer, glb.byteOffset).getUint32(4, true)).toBe(2);
    const json = JSON.parse(new TextDecoder().decode(glb.subarray(20, 20 + new DataView(glb.buffer, glb.byteOffset).getUint32(12, true))));
    expect(json.meshes[0].primitives[0].attributes).toHaveProperty('NORMAL');
    expect(json.images).toBeUndefined();
    const back = await readGlb(glb);
    expect(back.triangles).toBe(256);
    expect(bounds(back.positions)).toEqual(bounds(mesh.positions));
  });
});
