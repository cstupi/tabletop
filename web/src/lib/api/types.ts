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
export type Declaration = 'impassable' | 'blocks_line_of_sight' | 'see_in_and_out' | 'cover' | 'difficult' | 'dangerous' | 'dangerous_on_rush_charge' | 'steep';
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
  base: Base; height: number; assetId?: Guid; weapons: Weapon[]; armyBook?: string;
}
export interface Unit { id: Guid; ordinal: number; name: string; stats: Record<string, string>; properties: PropertyInstance[]; models: ModelSlot[]; reserve?: UnitReserve }
export interface UnitReserve { property: string; of: Guid }
export interface Army { id: Guid; name: string; gameSystem: string; edition: string; source: string; units: Unit[] }
export interface TerrainVolume { id: Guid; volume: Volume }

// Identity
/** `username`: what players type to open this account's Shared folder; absent until set. */
export interface AccountRow { id: Guid; name: string; username?: string }
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

// Offers (/v1/offers): a sharer offers assets to a player they have been in a game with, who accepts or declines.
/** `games`: up to three games played together, newest first; `lastPlayed`: the newest one's creation. */
export interface PlayerDto { accountId: Guid; name: string; games: string[]; lastPlayed: string }
export type OfferState = 'open' | 'accepted' | 'declined' | 'withdrawn';
export interface OfferAssetDto {
  id: Guid; name: string; kind: AssetKind; hash: string; byteSize: number; contentType: ContentType;
  scale: number; orientation: Orientation; modelledBase?: Base;
}
/** `folder`: the folder's name when a folder was offered; `assets`: the offered assets the sharer still has. */
export interface OfferDto {
  id: Guid; state: OfferState; sharerId: Guid; sharerName: string; recipientId: Guid; recipientName: string; folder?: string;
  createdAt: string; decidedAt?: string; assets: OfferAssetDto[];
}
/** Exactly one of `assetIds` and `folderId`. */
export interface CreateOffersRequest { recipientIds: Guid[]; assetIds: Guid[] | null; folderId: Guid | null }
/** `notFound`: asset ids that are not your uploaded assets. */
export interface CreateOffersResponse { offers: OfferDto[]; notFound: Guid[] }
export interface AcceptOfferResponse { copied: AssetDto[] }
export interface BlockedDto { accountId: Guid; name: string }

// Shared folders (/v1/shared/{username}): any signed-in player browses a player's Shared folder and copies from it.
/** `assetIds`: the shared models filed in the folder; the Shared folder's `parentId` is absent. */
export interface SharedFolderDto { id: Guid; parentId?: Guid; name: string; assetIds: Guid[] }
export type SharedAssetDto = OfferAssetDto;
export interface SharedLibraryDto { username: string; name: string; folders: SharedFolderDto[]; assets: SharedAssetDto[] }
/** Exactly one of `assetIds` and `folderId`; `destinationId`: your folder the copies go in, or none. */
export interface CopySharedRequest { assetIds: Guid[] | null; folderId: Guid | null; destinationId: Guid | null }
/** `folders`: the new folders, the copied tree's root first; `notFound`: ids that are not shared. */
export interface CopySharedResponse { assets: Guid[]; folders: FolderDto[]; notFound: Guid[] }

// Client releases (GET /v1/client/latest)
export type ClientPlatform = 'windows' | 'macos' | 'linux';
export interface ClientDownload { platform: ClientPlatform; fileName: string; size: number; sha256: string; url: string }
export interface ClientRelease { version: string; date: string; notes: string; protocolVersion?: number; downloads: ClientDownload[]; expiresAt: string }

// Folders
/** `shared`: the account's Shared folder, which any signed-in player opens by its username. */
export interface FolderDto { id: Guid; parentId?: Guid; name: string; assetIds: Guid[]; shared?: boolean }
export interface FolderRequest { name: string; parentId: Guid | null }
/** Bulk filing and deleting: 1 to 200 ids a request; `notFound` lists ids that are not the caller's assets. */
export interface BulkAssetsResponse { done: Guid[]; notFound: Guid[] }

// Armies
export interface ArmySummary { id: Guid; name: string; gameSystem: string; edition: string; source: string; units: number; models: number }
/** Which of Army Forge's two published bases the import writes for the whole army: `round` (circle or oval) or `square` (square or rectangle). */
export interface ImportRequest { source: string }
export interface UnitPatch { unitId: Guid; assetId: Guid | null }
export interface SlotPatch { slotId: Guid; assetId: Guid | null; clearAsset: boolean; base: Base | null; height: number | null }
export interface PatchArmyRequest { name?: string; units?: UnitPatch[]; slots?: SlotPatch[] }
