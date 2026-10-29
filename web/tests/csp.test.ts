import { describe, expect, it } from 'vitest';
import config from '../svelte.config.js';

describe('content security policy', () => {
  // three.js's GLTFLoader decodes a GLB's embedded images with fetch() on blob: URLs (ImageBitmapLoader), so the upload
  // preview of a textured GLB draws untextured unless connect-src admits blob:.
  it('lets the mesh preview fetch a GLB\'s embedded textures', () => {
    expect(config.kit?.csp?.directives?.['connect-src']).toContain('blob:');
  });
});
