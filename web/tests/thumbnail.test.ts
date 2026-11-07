import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../src/lib/api/client';
import { setTokenProvider } from '../src/lib/api/http';
import { thumbnailView, THUMBNAIL_SIZE } from '../src/lib/thumbnail/frame';
import { stepStates } from '../src/lib/upload/upload';

afterEach(() => { vi.unstubAllGlobals(); setTokenProvider(() => null); });

describe('thumbnailView', () => {
  it('looks at the box centre from the front-right and above, far enough to hold its bounding sphere', () => {
    const v = thumbnailView({ min: [-1, -1, 0], max: [1, 1, 2] }, 30);
    expect(v.target).toEqual([0, 0, 1]);
    const [dx, dy, dz] = [v.position[0] - 0, v.position[1] - 0, v.position[2] - 1];
    expect(dx).toBeGreaterThan(0); expect(dy).toBeGreaterThan(0); expect(dz).toBeGreaterThan(0);
    const distance = Math.hypot(dx, dy, dz), radius = Math.sqrt(3);
    expect(distance * Math.sin((15 * Math.PI) / 180)).toBeGreaterThanOrEqual(radius * 0.99);
    expect(THUMBNAIL_SIZE).toBe(256);
  });

  it('frames an empty box as a unit cube', () => {
    const v = thumbnailView({ min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] }, 30);
    expect(v.target).toEqual([0, 0, 0.5]);
    expect(Number.isFinite(v.position[0])).toBe(true);
  });
});

describe('putThumbnail', () => {
  it('PUTs the image bytes with their own content type and the bearer token', async () => {
    setTokenProvider(() => 'jwt');
    const f = vi.fn(async () => new Response(null, { status: 204 })); vi.stubGlobal('fetch', f);
    await api.assets.putThumbnail('a1', new Blob([new Uint8Array([1, 2, 3])], { type: 'image/webp' }));
    const [url, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/v1/assets/a1/thumbnail');
    expect(init.method).toBe('PUT');
    expect(new Headers(init.headers).get('content-type')).toBe('image/webp');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer jwt');
    expect(init.body).toBeInstanceOf(Blob);
  });
});

describe('stepStates', () => {
  it('marks the stages before the current one done, the current one current, the rest to do', () => {
    expect(stepStates(['hash', 'create', 'put', 'confirm'], 'put')).toEqual(['done', 'done', 'current', 'todo']);
    expect(stepStates(['hash', 'create', 'put', 'confirm'], null)).toEqual(['todo', 'todo', 'todo', 'todo']);
    expect(stepStates(['hash', 'create'], 'done')).toEqual(['done', 'done']);
  });
});
