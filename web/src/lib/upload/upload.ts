// web/src/lib/upload/upload.ts — the presigned path, in the management API's order. Nothing here converts; the plan carries finished bytes.
import { api } from '$lib/api/client';
import { errorMessage } from '$lib/api/errors';
import { ApiError } from '$lib/api/http';
import type { AssetDto, AssetKind, Base, ContentType, Orientation, TerrainVolume, Volume } from '$lib/api/types';
import { sha256Hex } from './sha256';

export type UploadStage = 'hash' | 'create' | 'put' | 'confirm' | 'volumes';
export const STAGE_LABELS: Record<UploadStage, string> = {
  hash: 'Fingerprinting the file', create: 'Reserving space in your library', put: 'Sending the file',
  confirm: 'Checking the file on the server', volumes: 'Saving the collision volume',
};
export interface UploadPlan { name: string; kind: AssetKind; contentType: ContentType; bytes: Uint8Array; scale: number; orientation: Orientation; modelledBase: Base | null; volume?: Volume }
export interface UploadOutcome { asset: AssetDto; triangles?: number; warning?: string; deduplicated: boolean; volumes?: TerrainVolume[] }
/** Bytes sent of the total, during `put`. */
export type SendProgress = (sent: number, total: number) => void;

/** A presigned PUT refused by the store, not by management. */
export class StoreRefused extends Error {
  constructor(public status: number) { super(`The storage server refused the file (${status}).`); }
}

export class UploadError extends Error {
  readonly reexport: boolean;
  constructor(public stage: UploadStage, public cause: unknown) {
    const reexport = stage === 'confirm' && cause instanceof ApiError && cause.status === 422;
    super(reexport
      ? `The server could not read the file: ${(cause as ApiError).detail ?? (cause as ApiError).title}. Re-export it and upload again.`
      : stage === 'put' && cause instanceof StoreRefused
        ? `${cause.message} The upload link may have expired; try again.`
        : stage === 'put' && cause instanceof TypeError
          ? 'Sending the file failed: the storage server could not be reached. Check your connection and try again.'
          : errorMessage(cause, 'upload'));
    this.reexport = reexport;
  }
}

/** XMLHttpRequest where there is one, for upload progress; fetch otherwise. Both reject a network failure with a TypeError. */
function put(url: string, bytes: Uint8Array, contentType: string, onSent: SendProgress): Promise<void> {
  if (typeof XMLHttpRequest === 'undefined') {
    return fetch(url, { method: 'PUT', headers: { 'content-type': contentType }, body: bytes as BodyInit }).then((r) => {
      if (!r.ok) throw new StoreRefused(r.status);
      onSent(bytes.byteLength, bytes.byteLength);
    });
  }
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', url);
    xhr.setRequestHeader('content-type', contentType);
    xhr.upload.onprogress = (e) => onSent(e.loaded, e.lengthComputable ? e.total : bytes.byteLength);
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? (onSent(bytes.byteLength, bytes.byteLength), resolve()) : reject(new StoreRefused(xhr.status)));
    xhr.onerror = () => reject(new TypeError('Network error'));
    xhr.onabort = () => reject(new TypeError('Aborted'));
    xhr.send(bytes as XMLHttpRequestBodyInit);
  });
}

export async function upload(plan: UploadPlan, onStage: (s: UploadStage) => void = () => {}, onSent: SendProgress = () => {}): Promise<UploadOutcome> {
  let stage: UploadStage = 'hash';
  try {
    onStage(stage);
    const hash = await sha256Hex(plan.bytes);
    onStage((stage = 'create'));
    const created = await api.assets.create({ name: plan.name, kind: plan.kind, hash, byteSize: plan.bytes.byteLength, contentType: plan.contentType, scale: plan.scale, orientation: plan.orientation, modelledBase: plan.modelledBase });
    const deduplicated = !created.uploadUrl;
    if (created.uploadUrl) {
      onStage((stage = 'put'));
      await put(created.uploadUrl, plan.bytes, plan.contentType, onSent);
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
