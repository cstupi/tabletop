# Task 10: The upload sequence — hash, create, PUT, confirm, volumes

**Files:**
- Create: `web/src/lib/upload/sha256.ts`, `web/src/lib/upload/upload.ts`
- Test: `web/tests/upload.test.ts`

**Interfaces:**
- Consumes: `api.assets.create/confirm/putVolumes`, `CreateAssetRequest`, `ConfirmResponse`, `Volume`, `AssetDto`.
- Produces: `sha256Hex(bytes: Uint8Array): Promise<string>` (64 lowercase hex, `crypto.subtle`); `UploadPlan { name: string; kind: AssetKind; contentType: ContentType; bytes: Uint8Array; scale: number; orientation: Orientation; modelledBase: Base | null; volume?: Volume }`; `UploadOutcome { asset: AssetDto; triangles?: number; warning?: string; deduplicated: boolean; volumes?: TerrainVolume[] }`; `upload(plan, onStage?): Promise<UploadOutcome>` with stages `'hash' | 'create' | 'put' | 'confirm' | 'volumes'`; `UploadError(stage, cause)`.
- The sequence is the management plan's, in order, and nothing else: hash → `POST /v1/assets` → `PUT` to `uploadUrl` when present (absent means the bytes are already in this account's library: dedup by hash, no bytes move) → `POST /confirm` → for terrain with a `volume`, `PUT /volumes`. The `PUT` carries exactly the declared `Content-Type`, because the presigned URL was signed for it. `confirm`'s `warning` is passed through untouched — the server's words, above 100,000 triangles, never a refusal.

Spec: management API → Asset bytes never pass through the API ("The client computes the content hash first … lets a duplicate upload be short-circuited before any bytes move"), The triangle count ("The check warns; it does not block"); management plan Task 7 (`CreateAssetResponse.uploadUrl` null = already there; confirm `409` until the bytes exist; `PUT /volumes` `409` unless terrain); mesh conversion → What it does not do.

- [ ] **Step 1: Write the failing tests**

```ts
// web/tests/upload.test.ts
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
```

- [ ] **Step 2: Run; expect failures. Write the two modules:**

```ts
// web/src/lib/upload/sha256.ts
export async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', bytes as BufferSource);
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('');
}
```

```ts
// web/src/lib/upload/upload.ts — the presigned path, in the management API's order. Nothing here converts; the plan carries finished bytes.
import { api } from '$lib/api/client';
import type { AssetDto, AssetKind, Base, ContentType, Orientation, TerrainVolume, Volume } from '$lib/api/types';
import { sha256Hex } from './sha256';

export type UploadStage = 'hash' | 'create' | 'put' | 'confirm' | 'volumes';
export interface UploadPlan { name: string; kind: AssetKind; contentType: ContentType; bytes: Uint8Array; scale: number; orientation: Orientation; modelledBase: Base | null; volume?: Volume }
export interface UploadOutcome { asset: AssetDto; triangles?: number; warning?: string; deduplicated: boolean; volumes?: TerrainVolume[] }

export class UploadError extends Error {
  constructor(public stage: UploadStage, public cause: unknown) { super(`Upload failed while ${stage === 'put' ? 'sending the bytes' : stage}: ${cause instanceof Error ? cause.message : String(cause)}`); }
}

export async function upload(plan: UploadPlan, onStage: (s: UploadStage) => void = () => {}): Promise<UploadOutcome> {
  let stage: UploadStage = 'hash';
  try {
    onStage(stage);
    const hash = await sha256Hex(plan.bytes);
    onStage((stage = 'create'));
    const created = await api.assets.create({ name: plan.name, kind: plan.kind, hash, byteSize: plan.bytes.byteLength, contentType: plan.contentType, scale: plan.scale, orientation: plan.orientation, modelledBase: plan.modelledBase });
    const deduplicated = !created.uploadUrl;
    if (created.uploadUrl) {
      onStage((stage = 'put'));
      const r = await fetch(created.uploadUrl, { method: 'PUT', headers: { 'content-type': plan.contentType }, body: plan.bytes as BodyInit });
      if (!r.ok) throw new Error(`${r.status} ${await r.text()}`);
    }
    onStage((stage = 'confirm'));
    const confirmed = await api.assets.confirm(created.asset.id);
    let volumes: TerrainVolume[] | undefined;
    if (plan.kind === 'terrain' && plan.volume) {
      onStage((stage = 'volumes'));
      volumes = await api.assets.putVolumes(created.asset.id, [plan.volume]);
    }
    return { asset: confirmed.asset, triangles: confirmed.triangles, warning: confirmed.warning, deduplicated, volumes };
  } catch (e) { throw e instanceof UploadError ? e : new UploadError(stage, e); }
}
```

- [ ] **Step 3: Run `npm test`; expect 33 passed, 1 skipped. Commit: `Upload by the presigned sequence: hash, create, PUT, confirm, volumes`.**
