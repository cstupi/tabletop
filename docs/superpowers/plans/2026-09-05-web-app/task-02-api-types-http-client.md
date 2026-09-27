# Task 2: The API types and the HTTP client

**Files:**
- Create: `web/src/lib/api/types.ts`, `web/src/lib/api/http.ts`, `web/src/lib/api/client.ts`
- Test: `web/tests/api.test.ts`

**Interfaces:**
- Consumes: the management plan's HTTP surface and DTOs (Tasks 4, 5, 7, 8, 10, 13 there); `Tabletop.Content.Army` and `Tabletop.Geometry` JSON shapes (contracts plan Tasks 2, 4, 5).
- Produces: every type in `types.ts` below (names are the management plan's, camelCased); `ApiError(status, title, detail)`; `http.get/post/put/patch/del<T>(path, body?)` reading `Authorization` from a token provider set with `setTokenProvider(() => string | null)` and calling `onUnauthorized` when a bearer request answers `401`; `api.auth.*`, `api.account.*`, `api.assets.*`, `api.folders.*`, `api.armies.*` as listed in `client.ts`.
- Wire conventions copied, not re-derived: enums are snake_case strings (`"miniature"`, `"circle"`, `"rect"`, `"solid"`); `TerrainDeclarations` is a `[Flags]` enum, so its wire form is `"none"` or a comma-joined list such as `"blocks_line_of_sight, cover"` (contracts plan Task 5 asserts `"declarations":"none"`); nulls are omitted, so optional fields are `?:`. Three folder writes answer `204` and no body — `PATCH /v1/folders/{id}` and both `/{id}/assets/{assetId}` verbs (management plan Task 8's `FolderEndpoints`, which is what the client is typed against; that plan's summary table names `FolderDto` for the whole group, and the endpoint code is the authority).

Spec: management API → Shape ("One API, two consumers"), Pagination is keyset; management plan → Interfaces → Produces → HTTP surface (every path and body below).

- [ ] **Step 1: Write `types.ts`**

```ts
// web/src/lib/api/types.ts — the management API's DTOs and the libs' value shapes, as JSON.
export type Guid = string;
export interface Page<T> { items: T[]; next?: Guid }
export interface Problem { title?: string; detail?: string; status?: number }

// Tabletop.Geometry
export type BaseShape = 'circle' | 'oval' | 'rect';
export interface Base { shape: BaseShape; a: number; b: number }
export interface Orientation { pitch: number; yaw: number; roll: number }
export interface Point2 { x: number; y: number }
export interface Yaw { degrees: number }
export type FootprintKind = 'circle' | 'rect';
export interface Footprint { kind: FootprintKind; a: number; b: number }
export type VolumeKind = 'solid' | 'area';
export type Declaration = 'impassable' | 'blocks_line_of_sight' | 'see_in_and_out' | 'cover' | 'difficult' | 'dangerous';
export interface Volume {
  kind: VolumeKind; footprint: Footprint; offset: Point2; yaw: Yaw; bottom: number; top: number;
  declarations: string;           // "none" or "cover, difficult" — see units/volumes.ts
  notes?: string;
}

// Tabletop.Content
export interface PropertyInstance { name: string; parameter?: number }
export interface Weapon { id: Guid; name: string; stats: Record<string, string>; properties: PropertyInstance[] }
export interface ModelSlot {
  id: Guid; ordinal: number; name: string; stats: Record<string, string>; properties: PropertyInstance[];
  base: Base; height: number; assetId?: Guid; weapons: Weapon[];
}
export interface Unit { id: Guid; ordinal: number; name: string; stats: Record<string, string>; properties: PropertyInstance[]; models: ModelSlot[] }
export interface Army { id: Guid; name: string; gameSystem: string; source: string; units: Unit[] }
export interface TerrainVolume { id: Guid; volume: Volume }

// Identity
export interface AccountRow { id: Guid; name: string }
export interface SessionResponse { sessionJwt: string; account: AccountRow }
export interface OtpSentResponse { methodId: string }

// Assets
export type AssetKind = 'miniature' | 'terrain' | 'texture';
export type ContentType = 'model/gltf-binary' | 'image/png' | 'image/jpeg' | 'image/webp';
export interface AssetDto {
  id: Guid; name: string; kind: AssetKind; hash: string; byteSize: number; contentType: ContentType; uploaded: boolean;
  scale: number; orientation: Orientation; modelledBase?: Base;
}
export interface CreateAssetRequest {
  name: string; kind: AssetKind; hash: string; byteSize: number; contentType: ContentType;
  scale: number; orientation: Orientation; modelledBase: Base | null;
}
export interface CreateAssetResponse { asset: AssetDto; uploadUrl?: string }
export interface ConfirmResponse { asset: AssetDto; triangles?: number; warning?: string }
export interface PatchAssetRequest { name?: string; scale?: number; orientation?: Orientation; modelledBase?: Base }
export interface DownloadResponse { url: string; expiresAt: string }

// Folders
export interface FolderDto { id: Guid; parentId?: Guid; name: string; assetIds: Guid[] }
export interface FolderRequest { name: string; parentId: Guid | null }

// Armies
export interface ArmySummary { id: Guid; name: string; gameSystem: string; source: string; units: number; models: number }
export interface ImportRequest { source: string }
export interface UnitPatch { unitId: Guid; assetId: Guid | null }
export interface SlotPatch { slotId: Guid; assetId: Guid | null; clearAsset: boolean; base: Base | null; height: number | null }
export interface PatchArmyRequest { name?: string; units?: UnitPatch[]; slots?: SlotPatch[] }
```

- [ ] **Step 2: Write the failing tests**

```ts
// web/tests/api.test.ts
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, http, setTokenProvider } from '../src/lib/api/http';
import { api } from '../src/lib/api/client';

function fakeFetch(status: number, body: unknown, contentType = 'application/json') {
  const f = vi.fn(async () => new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'content-type': contentType } }));
  vi.stubGlobal('fetch', f);
  return f;
}
afterEach(() => { vi.unstubAllGlobals(); setTokenProvider(() => null); http.onUnauthorized = () => {}; });

describe('http', () => {
  it('sends the bearer token and JSON, and parses the body', async () => {
    setTokenProvider(() => 'jwt-1');
    const f = fakeFetch(200, { id: 'a', name: 'Clay' });
    expect(await http.get('/v1/account')).toEqual({ id: 'a', name: 'Clay' });
    const [url, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/v1/account');
    expect(new Headers(init.headers).get('authorization')).toBe('Bearer jwt-1');
  });

  it('turns problem+json into ApiError and 204 into undefined', async () => {
    fakeFetch(422, { title: 'Unprocessable', detail: 'name is required', status: 422 }, 'application/problem+json');
    await expect(http.post('/v1/folders', { name: '' })).rejects.toMatchObject(new ApiError(422, 'Unprocessable', 'name is required'));
    fakeFetch(204, undefined);
    expect(await http.del('/v1/folders/x')).toBeUndefined();
  });

  it('calls onUnauthorized on a 401 to a bearer request but not to /v1/auth', async () => {
    const seen: string[] = [];
    http.onUnauthorized = () => seen.push('401');
    setTokenProvider(() => 'stale');
    fakeFetch(401, { title: 'Unauthorized' }, 'application/problem+json');
    await expect(http.get('/v1/account')).rejects.toBeInstanceOf(ApiError);
    await expect(http.post('/v1/auth/password', { email: 'a', password: 'b' })).rejects.toBeInstanceOf(ApiError);
    expect(seen).toEqual(['401']);
  });
});

describe('client', () => {
  it('builds keyset query strings and the documented paths', async () => {
    const f = fakeFetch(200, { items: [], next: undefined });
    await api.assets.list({ limit: 20, after: 'abc', kind: 'terrain' });
    await api.armies.list({});
    await api.folders.addAsset('f1', 'a1');
    const urls = f.mock.calls.map((c) => (c as unknown as [string, RequestInit])[0]);
    expect(urls).toEqual(['/v1/assets?limit=20&after=abc&kind=terrain', '/v1/armies', '/v1/folders/f1/assets/a1']);
    expect((f.mock.calls[2] as unknown as [string, RequestInit])[1].method).toBe('PUT');
  });
});
```

- [ ] **Step 3: Run `npm test`; expect failures (modules missing). Write `http.ts`:**

```ts
// web/src/lib/api/http.ts
import type { Problem } from './types';

export const API_BASE: string = (import.meta.env?.PUBLIC_API_BASE as string | undefined) ?? '';

export class ApiError extends Error {
  constructor(public status: number, public title: string, public detail?: string) { super(detail ?? title); }
}

let tokenProvider: () => string | null = () => null;
export function setTokenProvider(p: () => string | null) { tokenProvider = p; }

async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers = new Headers({ accept: 'application/json' });
  if (body !== undefined) headers.set('content-type', 'application/json');
  const token = tokenProvider();
  const isAuthRoute = path.startsWith('/v1/auth/') && !(method === 'DELETE' && path === '/v1/auth/session');
  if (token && !isAuthRoute) headers.set('authorization', `Bearer ${token}`);
  const response = await fetch(API_BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (response.ok) { const text = await response.text(); return (text ? JSON.parse(text) : undefined) as T; }   // 202 and 204 carry no body
  let problem: Problem = {};
  try { problem = (await response.json()) as Problem; } catch { /* no body */ }
  if (response.status === 401 && !isAuthRoute) http.onUnauthorized();
  throw new ApiError(response.status, problem.title ?? response.statusText, problem.detail);
}

export const http = {
  onUnauthorized: (() => {}) as () => void,
  get: <T>(path: string) => send<T>('GET', path),
  post: <T>(path: string, body?: unknown) => send<T>('POST', path, body ?? {}),
  put: <T>(path: string, body?: unknown) => send<T>('PUT', path, body),
  patch: <T>(path: string, body: unknown) => send<T>('PATCH', path, body),
  del: <T = void>(path: string) => send<T>('DELETE', path),
};

export function query(params: Record<string, string | number | undefined>): string {
  const q = Object.entries(params).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`).join('&');
  return q ? `?${q}` : '';
}
```

- [ ] **Step 4: Write `client.ts`**

```ts
// web/src/lib/api/client.ts — one function per management endpoint the web app uses. No games, no layouts, no properties.
import { http, query } from './http';
import type * as T from './types';

export const api = {
  auth: {
    sendOtp: (email: string) => http.post<T.OtpSentResponse>('/v1/auth/otp', { email }),
    verifyOtp: (methodId: string, code: string) => http.post<T.SessionResponse>('/v1/auth/otp/verify', { methodId, code }),
    password: (email: string, password: string) => http.post<T.SessionResponse>('/v1/auth/password', { email, password }),
    signup: (email: string, password: string) => http.post<T.SessionResponse>('/v1/auth/password/signup', { email, password }),
    startReset: (email: string) => http.post<void>('/v1/auth/password/reset', { email }),
    completeReset: (token: string, password: string) => http.post<T.SessionResponse>('/v1/auth/password/reset/complete', { token, password }),
    refresh: (sessionJwt: string) => http.post<T.SessionResponse>('/v1/auth/session/refresh', { sessionJwt }),
    logout: () => http.del('/v1/auth/session'),
  },
  account: {
    get: () => http.get<T.AccountRow>('/v1/account'),
    rename: (name: string) => http.patch<T.AccountRow>('/v1/account', { name }),
    delete: () => http.del('/v1/account'),
  },
  assets: {
    list: (p: { limit?: number; after?: T.Guid; kind?: T.AssetKind }) => http.get<T.Page<T.AssetDto>>(`/v1/assets${query(p)}`),
    create: (r: T.CreateAssetRequest) => http.post<T.CreateAssetResponse>('/v1/assets', r),
    get: (id: T.Guid) => http.get<T.AssetDto>(`/v1/assets/${id}`),
    patch: (id: T.Guid, r: T.PatchAssetRequest) => http.patch<T.AssetDto>(`/v1/assets/${id}`, r),
    delete: (id: T.Guid) => http.del(`/v1/assets/${id}`),
    confirm: (id: T.Guid) => http.post<T.ConfirmResponse>(`/v1/assets/${id}/confirm`),
    download: (id: T.Guid) => http.get<T.DownloadResponse>(`/v1/assets/${id}/download`),
    getVolumes: (id: T.Guid) => http.get<T.TerrainVolume[]>(`/v1/assets/${id}/volumes`),
    putVolumes: (id: T.Guid, volumes: T.Volume[]) => http.put<T.TerrainVolume[]>(`/v1/assets/${id}/volumes`, volumes),
  },
  folders: {
    list: () => http.get<T.FolderDto[]>('/v1/folders'),
    create: (r: T.FolderRequest) => http.post<T.FolderDto>('/v1/folders', r),
    patch: (id: T.Guid, r: T.FolderRequest) => http.patch<void>(`/v1/folders/${id}`, r),
    delete: (id: T.Guid) => http.del(`/v1/folders/${id}`),
    addAsset: (id: T.Guid, assetId: T.Guid) => http.put<void>(`/v1/folders/${id}/assets/${assetId}`),
    removeAsset: (id: T.Guid, assetId: T.Guid) => http.del(`/v1/folders/${id}/assets/${assetId}`),
  },
  armies: {
    import: (source: string) => http.post<T.Army>('/v1/armies/import', { source } satisfies T.ImportRequest),
    list: (p: { limit?: number; after?: T.Guid }) => http.get<T.Page<T.ArmySummary>>(`/v1/armies${query(p)}`),
    get: (id: T.Guid) => http.get<T.Army>(`/v1/armies/${id}`),
    patch: (id: T.Guid, r: T.PatchArmyRequest) => http.patch<T.Army>(`/v1/armies/${id}`, r),
    delete: (id: T.Guid) => http.del(`/v1/armies/${id}`),
  },
};
```

- [ ] **Step 5: Run `npm test`; expect 5 passed. Commit: `Add the typed management API client`.**
