import { describe, expect, it } from 'vitest';
import { classifyFiles } from '../src/lib/upload/intake';
describe('classifyFiles', () => {
  it('merges STLs, takes one GLB or one image, and rejects the rest', () => {
    expect(classifyFiles([{ name: 'a.STL', type: '' }, { name: 'b.stl', type: '' }])).toMatchObject({ kind: 'stl' });
    expect(classifyFiles([{ name: 'm.glb', type: 'model/gltf-binary' }])).toMatchObject({ kind: 'glb' });
    expect(classifyFiles([{ name: 'board.jpeg', type: 'image/jpeg' }])).toMatchObject({ kind: 'image', contentType: 'image/jpeg' });
    expect(classifyFiles([{ name: 'board.webp', type: 'image/webp' }])).toMatchObject({ kind: 'image', contentType: 'image/webp' });
    expect(classifyFiles([{ name: 'm.gltf', type: '' }])).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('GLB') });
    expect(classifyFiles([{ name: 'a.stl', type: '' }, { name: 'm.glb', type: '' }])).toMatchObject({ kind: 'rejected' });
    expect(classifyFiles([{ name: 'a.glb', type: '' }, { name: 'b.glb', type: '' }])).toMatchObject({ kind: 'rejected' });
  });
});
