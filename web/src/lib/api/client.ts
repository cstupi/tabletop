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
    deleteMany: (assetIds: T.Guid[]) => http.post<T.BulkAssetsResponse>('/v1/assets/delete', { assetIds }),
    share: (assetIds: T.Guid[]) => http.post<T.BulkAssetsResponse>('/v1/assets/share', { assetIds }),
    unshare: (assetIds: T.Guid[]) => http.post<T.BulkAssetsResponse>('/v1/assets/unshare', { assetIds }),
    confirm: (id: T.Guid) => http.post<T.ConfirmResponse>(`/v1/assets/${id}/confirm`),
    download: (id: T.Guid) => http.get<T.DownloadResponse>(`/v1/assets/${id}/download`),
    getVolumes: (id: T.Guid) => http.get<T.TerrainVolume[]>(`/v1/assets/${id}/volumes`),
    putVolumes: (id: T.Guid, volumes: T.Volume[]) => http.put<T.TerrainVolume[]>(`/v1/assets/${id}/volumes`, volumes),
  },
  shared: {
    list: (p: { limit?: number; after?: T.Guid; kind?: T.AssetKind; q?: string }) => http.get<T.Page<T.SharedAssetDto>>(`/v1/shared-assets${query(p)}`),
    get: (id: T.Guid) => http.get<T.SharedAssetDto>(`/v1/shared-assets/${id}`),
    download: (id: T.Guid) => http.get<T.DownloadResponse>(`/v1/shared-assets/${id}/download`),
    copy: (assetIds: T.Guid[], folderId: T.Guid | null) => http.post<T.CopySharedResponse>('/v1/shared-assets/copy', { assetIds, folderId } satisfies T.CopySharedRequest),
  },
  folders: {
    list: () => http.get<T.FolderDto[]>('/v1/folders'),
    create: (r: T.FolderRequest) => http.post<T.FolderDto>('/v1/folders', r),
    patch: (id: T.Guid, r: T.FolderRequest) => http.patch<void>(`/v1/folders/${id}`, r),
    delete: (id: T.Guid) => http.del(`/v1/folders/${id}`),
    addAsset: (id: T.Guid, assetId: T.Guid) => http.put<void>(`/v1/folders/${id}/assets/${assetId}`),
    removeAsset: (id: T.Guid, assetId: T.Guid) => http.del(`/v1/folders/${id}/assets/${assetId}`),
    addAssets: (id: T.Guid, assetIds: T.Guid[]) => http.post<T.BulkAssetsResponse>(`/v1/folders/${id}/assets`, { assetIds }),
    removeAssets: (id: T.Guid, assetIds: T.Guid[]) => http.post<T.BulkAssetsResponse>(`/v1/folders/${id}/assets/remove`, { assetIds }),
  },
  armies: {
    import: (source: string) => http.post<T.Army>('/v1/armies/import', { source } satisfies T.ImportRequest),
    list: (p: { limit?: number; after?: T.Guid }) => http.get<T.Page<T.ArmySummary>>(`/v1/armies${query(p)}`),
    get: (id: T.Guid) => http.get<T.Army>(`/v1/armies/${id}`),
    patch: (id: T.Guid, r: T.PatchArmyRequest) => http.patch<T.Army>(`/v1/armies/${id}`, r),
    delete: (id: T.Guid) => http.del(`/v1/armies/${id}`),
  },
  client: {
    /** The newest published client build; a 404 before the first release. */
    latest: () => http.get<T.ClientRelease>('/v1/client/latest'),
  },
};
