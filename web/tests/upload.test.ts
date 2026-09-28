import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../src/lib/api/client';
import { sha256Hex } from '../src/lib/upload/sha256';
import { upload, UploadError } from '../src/lib/upload/upload';
import type { AssetDto } from '../src/lib/api/types';
import { defaultVolume } from '../src/lib/units/volumes';

const asset = (over: Partial<AssetDto> = {}): AssetDto => ({ id: 'a1', name: 'Ruin', kind: 'terrain', hash: 'h', byteSize: 3, contentType: 'model/gltf-binary', uploaded: false, scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 }, ...over });
afterEach(() => vi.restoreAllMocks());

describe('sha256Hex', () => {
  it('matches the well-known digest of "abc"', async () => {
    expect(await sha256Hex(new TextEncoder().encode('abc'))).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
  });
});

describe('upload', () => {
  const bytes = new Uint8Array([1, 2, 3]);
  const plan = { name: 'Ruin', kind: 'terrain' as const, contentType: 'model/gltf-binary' as const, bytes, scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 }, modelledBase: null, volume: defaultVolume(6, 4, 3) };

  it('runs create → PUT → confirm → volumes with the declared content type', async () => {
    const create = vi.spyOn(api.assets, 'create').mockResolvedValue({ asset: asset(), uploadUrl: 'http://localhost:9000/tabletop-blobs/o/h?sig' });
    const put = vi.fn(async () => new Response(null, { status: 200 })); vi.stubGlobal('fetch', put);
    vi.spyOn(api.assets, 'confirm').mockResolvedValue({ asset: asset({ uploaded: true }), triangles: 12 });
    const vols = vi.spyOn(api.assets, 'putVolumes').mockResolvedValue([{ id: 'v1', volume: plan.volume }]);
    const stages: string[] = [];
    const out = await upload(plan, (s) => stages.push(s));
    expect(create.mock.calls[0]![0]).toMatchObject({ name: 'Ruin', kind: 'terrain', hash: await sha256Hex(bytes), byteSize: 3, contentType: 'model/gltf-binary', modelledBase: null });
    const [url, init] = put.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain('localhost:9000'); expect(init.method).toBe('PUT'); expect(new Headers(init.headers).get('content-type')).toBe('model/gltf-binary');
    expect(vols).toHaveBeenCalledWith('a1', [plan.volume]);
    expect(out).toMatchObject({ triangles: 12, deduplicated: false, volumes: [{ id: 'v1' }] });
    expect(stages).toEqual(['hash', 'create', 'put', 'confirm', 'volumes']);
  });

  it('skips the PUT when the bytes are already there, and skips volumes for a miniature', async () => {
    vi.spyOn(api.assets, 'create').mockResolvedValue({ asset: asset({ uploaded: true, kind: 'miniature' }) });
    const put = vi.fn(); vi.stubGlobal('fetch', put);
    vi.spyOn(api.assets, 'confirm').mockResolvedValue({ asset: asset({ uploaded: true, kind: 'miniature' }), triangles: 12, warning: undefined });
    const vols = vi.spyOn(api.assets, 'putVolumes');
    const out = await upload({ ...plan, kind: 'miniature', modelledBase: { shape: 'circle', a: 1.26, b: 1.26 }, volume: undefined });
    expect(put).not.toHaveBeenCalled(); expect(vols).not.toHaveBeenCalled(); expect(out.deduplicated).toBe(true);
  });

  it('reports the failing stage', async () => {
    vi.spyOn(api.assets, 'create').mockResolvedValue({ asset: asset(), uploadUrl: 'http://x/put' });
    vi.stubGlobal('fetch', vi.fn(async () => new Response('denied', { status: 403 })));
    await expect(upload(plan)).rejects.toMatchObject({ stage: 'put' } satisfies Partial<UploadError>);
  });
});
