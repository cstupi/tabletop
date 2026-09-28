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
