import type {
  RecordedBoard, RecordedFrame, RecordedModel, RecordedTerrain, RecordedVolume,
} from '../protocol/recording.generated';
import { type Point, rotate } from './transform';

export type Shape =
  | { readonly kind: 'circle'; readonly centre: Point; readonly radius: number }
  | { readonly kind: 'polygon'; readonly points: readonly Point[] };

/** Tabletop.Geometry.Bodies.OvalVertices. */
export const OVAL_VERTICES = 64;

export type ModelShape = {
  readonly id: string; readonly unitId: string; readonly unitName: string; readonly label: string;
  readonly teamId: string; readonly teamOrdinal: number;
  readonly centre: Point; readonly shape: Shape; readonly facing: readonly [Point, Point] | null;
  readonly wounds: number; readonly spellTokens: number; readonly conditions: readonly string[];
  readonly elevated: boolean;
  /** The model as a circle at its centre (radius: the base's longer side / 2), and its height above the table. */
  readonly circle: Shape; readonly z: number;
};
export type TerrainShape = {
  readonly id: string; readonly volumeId: string; readonly label: string;
  readonly kind: string; readonly declarations: readonly string[];
  readonly shape: Shape; readonly bottom: number; readonly top: number; readonly notes?: string;
};
export type MarkerShape = {
  readonly id: string; readonly kind: string; readonly label: string;
  readonly centre: Point; readonly radius: number; readonly teamOrdinal?: number;
  /** The rule set's marker range about the centre (Welcome.markerRange), drawn as a ring; absent draws none. */
  readonly reach?: number;
};
export type ZoneShape = {
  readonly id: string; readonly shape: string; readonly teamOrdinal: number; readonly polygons: readonly Shape[];
};
export type OffTable = {
  readonly id: string; readonly label: string; readonly teamOrdinal: number;
  readonly where: string; readonly containerLabel?: string;
};
export type Scene = {
  readonly table: { readonly width: number; readonly length: number };
  readonly terrain: readonly TerrainShape[];
  readonly zones: readonly ZoneShape[];
  readonly markers: readonly MarkerShape[];
  readonly models: readonly ModelShape[];
  readonly offTable: readonly OffTable[];
};

/** Tabletop.Geometry.Bodies.BaseOutline, ported: circle, rect and a 64-gon oval, all placed by yaw. */
export function baseOutline(model: RecordedModel, centre: Point, yaw: number): Shape {
  const place = (local: readonly Point[]): Shape => ({
    kind: 'polygon',
    points: local.map((p) => {
      const turned = rotate(p, yaw);
      return { x: turned.x + centre.x, y: turned.y + centre.y };
    }),
  });
  if (model.baseShape === 'circle') return { kind: 'circle', centre, radius: model.baseA / 2 };
  if (model.baseShape === 'rect') {
    const [hw, hl] = [model.baseB / 2, model.baseA / 2];        // width across x, length along the facing axis y
    return place([{ x: -hw, y: -hl }, { x: hw, y: -hl }, { x: hw, y: hl }, { x: -hw, y: hl }]);
  }
  return place(
    Array.from({ length: OVAL_VERTICES }, (_, i) => {
      const t = (2 * Math.PI * i) / OVAL_VERTICES;
      return { x: (model.baseB / 2) * Math.cos(t), y: (model.baseA / 2) * Math.sin(t) };
    }),
  );
}

/** Tabletop.Geometry.WorldVolumes.WorldOutline, ported: the piece's position, yaw and uniform scale applied. */
export function volumeOutline(terrain: RecordedTerrain, volume: RecordedVolume): Shape {
  return worldOutline({ x: terrain.x, y: terrain.y }, terrain.yaw, terrain.scale, {
    circle: volume.footprint === 'circle', a: volume.a, b: volume.b, offsetX: volume.offsetX, offsetY: volume.offsetY, yaw: volume.yaw,
  });
}

/** The wire-shaped core of WorldOutline: a piece at `at` (inches, yaw degrees, uniform scale) and one volume's footprint, offset and
 *  own yaw. Recordings and setup drafts both come through here. */
export function worldOutline(
  at: Point, pieceYaw: number, scale: number,
  volume: { readonly circle: boolean; readonly a: number; readonly b: number; readonly offsetX: number; readonly offsetY: number; readonly yaw: number },
): Shape {
  const offset = rotate({ x: volume.offsetX * scale, y: volume.offsetY * scale }, pieceYaw);
  const centre = { x: at.x + offset.x, y: at.y + offset.y };
  if (volume.circle) return { kind: 'circle', centre, radius: volume.a * scale };
  const hw = (volume.a * scale) / 2;
  const hl = (volume.b * scale) / 2;
  const yaw = pieceYaw + volume.yaw;
  return {
    kind: 'polygon',
    points: [{ x: -hw, y: -hl }, { x: hw, y: -hl }, { x: hw, y: hl }, { x: -hw, y: hl }].map((p) => {
      const turned = rotate(p, yaw);
      return { x: turned.x + centre.x, y: turned.y + centre.y };
    }),
  };
}

export function sceneOf(board: RecordedBoard, frame: RecordedFrame): Scene {
  const definitions = new Map(board.models.map((m) => [m.id, m]));
  const models: ModelShape[] = [];
  const offTable: OffTable[] = [];

  for (const state of frame.models) {
    const definition = definitions.get(state.id);
    if (definition === undefined) continue;                    // read.ts already refused this file
    if (state.where !== 'table' || state.x === undefined || state.y === undefined) {
      offTable.push({
        id: state.id, label: definition.name, teamOrdinal: definition.teamOrdinal, where: state.where,
        ...(state.containerId !== undefined
          ? { containerLabel: definitions.get(state.containerId)?.name ?? state.containerId }
          : {}),
      });
      continue;
    }
    const centre = { x: state.x, y: state.y };
    const yaw = state.yaw ?? 0;
    const reach = rotate({ x: 0, y: definition.baseA / 2 }, yaw);
    models.push({
      id: state.id, unitId: definition.unitId, unitName: definition.unitName, label: definition.name,
      teamId: definition.teamId, teamOrdinal: definition.teamOrdinal,
      centre, shape: baseOutline(definition, centre, yaw),
      facing: [centre, { x: centre.x + reach.x, y: centre.y + reach.y }],
      wounds: state.wounds, spellTokens: state.spellTokens, conditions: state.conditions,
      elevated: (state.z ?? 0) > 0.01, z: state.z ?? 0,
      circle: { kind: 'circle', centre, radius: Math.max(definition.baseA, definition.baseB) / 2 },
    });
  }

  const terrain: TerrainShape[] = board.terrain.flatMap((piece) =>
    piece.volumes.map((volume) => ({
      id: piece.id, volumeId: volume.id, label: piece.mesh, kind: volume.kind,
      declarations: volume.declarations, shape: volumeOutline(piece, volume),
      bottom: piece.z + volume.bottom * piece.scale, top: piece.z + volume.top * piece.scale,
      ...(volume.notes !== undefined ? { notes: volume.notes } : {}),
    })),
  );

  // A marker state carries its own definition, so board.markers is never consulted here: a marker
  // dropped during play is in no board block, and where both name one they agree.
  const ordinalOfTeam = new Map(board.models.map((m) => [m.teamId, m.teamOrdinal]));
  const markers: MarkerShape[] = frame.markers.map((state) => {
    const ordinal = state.controlledBy === undefined ? undefined : ordinalOfTeam.get(state.controlledBy);
    return {
      id: state.id, kind: state.kind, label: state.label ?? state.id,
      centre: { x: state.x, y: state.y }, radius: state.diameter / 2,
      ...(ordinal === undefined ? {} : { teamOrdinal: ordinal }),
    };
  });

  // Rings are already resolved and already in the corner frame; an empty list draws no outline.
  const zones: ZoneShape[] = board.zones.map((zone) => ({
    id: zone.id, shape: zone.shape, teamOrdinal: zone.teamOrdinal,
    polygons: zone.polygons.map((points) => ({ kind: 'polygon', points }) as Shape),
  }));

  return { table: { width: board.width, length: board.length }, terrain, zones, markers, models, offTable };
}
