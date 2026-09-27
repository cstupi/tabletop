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
