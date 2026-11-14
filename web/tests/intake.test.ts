import { describe, expect, it } from 'vitest';
import { assetName, classifyFiles, PICKS } from '../src/lib/upload/intake';
import { MAX_NAME } from '../src/lib/validation';
describe('classifyFiles', () => {
  it('merges STLs, takes one GLB or one image, and rejects the rest', () => {
    expect(classifyFiles([{ name: 'a.STL', type: '' }, { name: 'b.stl', type: '' }])).toMatchObject({ kind: 'stl' });
    expect(classifyFiles([{ name: 'm.glb', type: 'model/gltf-binary' }])).toMatchObject({ kind: 'glb' });
    expect(classifyFiles([{ name: 'board.jpeg', type: 'image/jpeg' }])).toMatchObject({ kind: 'image', contentType: 'image/jpeg' });
    expect(classifyFiles([{ name: 'board.webp', type: 'image/webp' }])).toMatchObject({ kind: 'image', contentType: 'image/webp' });
    expect(classifyFiles([{ name: 'm.gltf', type: '' }])).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('GLB') });
    expect(classifyFiles([{ name: 'a.stl', type: '' }, { name: 'm.glb', type: '' }])).toMatchObject({ kind: 'rejected' });
  });
  it('takes several GLBs and images as a batch, each its own asset', () => {
    const b = classifyFiles([{ name: 'a.glb', type: '' }, { name: 'B.GLB', type: '' }, { name: 'board.png', type: '' }]);
    expect(b).toMatchObject({ kind: 'batch', items: [{ contentType: 'model/gltf-binary' }, { contentType: 'model/gltf-binary' }, { contentType: 'image/png' }] });
    expect(classifyFiles([{ name: 'a.jpg', type: '' }, { name: 'b.webp', type: '' }])).toMatchObject({ kind: 'batch' });
    expect(classifyFiles([{ name: 'a.glb', type: '' }, { name: 'b.stl', type: '' }])).toMatchObject({ kind: 'rejected' });
    expect(classifyFiles([{ name: 'a.glb', type: '' }, { name: 'notes.txt', type: '' }])).toMatchObject({ kind: 'rejected' });
  });
  it('names an asset after its file', () => {
    expect(assetName('Killer Tank.glb')).toBe('Killer Tank');
    expect(assetName('kit.part.STL')).toBe('kit.part');
    expect(assetName('x'.repeat(200) + '.glb')).toHaveLength(MAX_NAME);
  });
  it('opens the file picker on GLB, with STL and textures as their own choices', () => {
    expect(PICKS.map((p) => p.accept)).toEqual(['.glb', '.stl', '.png,.jpg,.jpeg,.webp']);
    expect(PICKS[0]!.label).toBe('Choose GLB files');
  });
});
