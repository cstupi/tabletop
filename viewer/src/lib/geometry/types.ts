import type { Base, Placement, Point3, TableSize, TerrainPlacement, Yaw, ZoneLegs } from '../protocol/wire.generated';

export type { Base, Placement, Point3, TableSize, TerrainPlacement, Yaw };

/** A table point in inches. Answers also carry the wire Point2's `length`, which callers ignore. */
export interface XY { readonly x: number; readonly y: number }
export interface ModelBody { readonly base: Base; readonly height: number }
export interface PlacedModel { readonly body: ModelBody; readonly placement: Placement }
export interface HeldModel { readonly id: string; readonly at: PlacedModel; readonly to: XY }
/** Where a held model is drawn (0.5" over the highest top under it) and where it lands. */
export interface HeldEnd { readonly id: string; readonly drawn: Point3; readonly lands: Point3 }
/** A charge's landing pull (Collision.Abut): the target's models and how near a landing must end to be drawn flush against one. */
export interface Abutment { readonly models: readonly PlacedModel[]; readonly within: number }
export interface PieceEnd { readonly drawn: Point3; readonly lands: Point3 }
export type ContactKind = 'on_base' | 'crosses' | 'too_close' | 'in_impassable';
/** A model on the table (`at` set) or off it; proposed placements go in `contacts`' map. */
export interface ContactBody { readonly id: string; readonly unitId: string; readonly body: ModelBody; readonly at?: Placement }
export interface ContactMove {
  readonly allowance: number; readonly impassable: readonly string[]; readonly difficult: readonly string[]; readonly difficultCap?: number;
}
export interface ContactTerms {
  readonly separation?: number; readonly passesModels: boolean; readonly landing: boolean;
  readonly move?: ContactMove; readonly size?: TableSize; readonly unit?: number;
}
export interface Contact { readonly modelId: string; readonly kind: ContactKind; readonly unitId?: string }
export interface Nearest { readonly distance: number; readonly from: number; readonly to: number }
export interface Segment { readonly a: XY; readonly b: XY }
type Terrain = readonly TerrainPlacement[];

/** Tabletop.Geometry in the browser. Every call is synchronous once loaded; inches, table frame, Z up. */
export interface Geometry {
  /** Lifted held models in drag order (DragBodies.Models, lifted): `walls` are volume ids (wallsOf); `abut` a charge's pull (abutmentOf). */
  landModels(held: readonly HeldModel[], stationary: readonly PlacedModel[], walls: readonly string[], terrain: Terrain, abut?: Abutment): HeldEnd[];
  /** A lifted setup piece held at `at` (its yaw included), cursor at `to` (DragBodies.Piece, lifted). */
  landPiece(terrain: Terrain, pieceId: string, at: Placement, to: XY): PieceEnd;
  /** A piece turned in place to `yaw`: where it stands, or the nearest spot clear of Impassable pieces, rested. */
  turnPiece(terrain: Terrain, pieceId: string, yaw: Yaw): Point3;
  modelSurfaces(model: PlacedModel, terrain: Terrain): number[];
  /** The open surface nearest `level`; omitted, the highest (the browser's drop). */
  restModel(model: PlacedModel, terrain: Terrain, level?: number): number;
  pieceSurfaces(terrain: Terrain, pieceId: string, at: Placement): number[];
  restPiece(terrain: Terrain, pieceId: string, at: Placement, level?: number): number;
  /** Each proposed model's first contact (Contacts.Of); a model with none is absent. */
  contacts(models: readonly ContactBody[], proposed: ReadonlyMap<string, Placement>, terrain: Terrain, terms: ContactTerms): Map<string, Contact>;
  overlapBases(a: PlacedModel, b: PlacedModel): boolean;
  edgeToEdge(a: PlacedModel, b: PlacedModel): number;
  bodyToBody(a: PlacedModel, b: PlacedModel): number;
  edgeToPoint(model: PlacedModel, point: XY): number;
  edgeToMarker(model: PlacedModel, centre: XY, diameter: number): number;
  /** The closest pair edge to edge, by index into `from` and `to`; null when either is empty. */
  nearestPair(from: readonly PlacedModel[], to: readonly PlacedModel[]): Nearest | null;
  /** The union outline of the rings at `range` from each model's base edge (RangeOutline.Of). */
  rangeOutline(models: readonly PlacedModel[], range: number): Segment[];
  /** `legs`: Side Battle's, from the game's Welcome; absent is ZoneLegs.Default. */
  zonePolygons(shape: string, size: TableSize, legs?: ZoneLegs): XY[][];
  zoneContains(shape: string, size: TableSize, point: XY, legs?: ZoneLegs): boolean;
  /** The whole base inside one of the zone's polygons (p.6 "fully within"). */
  zoneHoldsBase(shape: string, size: TableSize, model: PlacedModel, legs?: ZoneLegs): boolean;
  tableHoldsBase(size: TableSize, model: PlacedModel): boolean;
  /** Containment.ExitYaw (p.7, p.15): a facing a disembarking model's position is legal at, or null; `impassable` are the volume ids the Ask names. */
  exitYaw(body: ModelBody, container: PlacedModel, within: number, at: XY, terrain: Terrain, impassable: readonly string[], size: TableSize): Yaw | null;
  /** One facade method by name on a JSON request: the agreement test's door. */
  call(method: string, json: string): string;
}
