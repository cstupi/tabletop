// viewer/src/lib/play/scene.ts
// The board of a game in play: sceneOfTable for terrain, markers and zones, then every model on the table. Where the shapes come from is
// the projection alone; nothing here decides a rule.

import { baseOutline, type MarkerShape, type ModelShape, type OffTable, type Scene } from '../board/frame';
import { sceneOfTable, type ZonePolygons } from '../board/table';
import { rotate } from '../board/transform';
import type { Projection } from '../live/apply';
import type { GameInfo, Marker, Placement, TerrainPlacement } from '../protocol/wire.generated';

/**
 * Ghosts (a move's or a deploy's proposed placements) override where a model is drawn; shelf and inside models go to offTable; markers
 * placed in play are added after the setup ones (a moved setup marker is drawn once, at its new spot), an objective coloured by its
 * controller (projection.control).
 */
export function sceneOfPlay(
  p: Projection, game: GameInfo, zones: ZonePolygons, name: (piece: TerrainPlacement) => string, ghosts: ReadonlyMap<string, Placement> = new Map(),
): Scene {
  const base = sceneOfTable(p.board.table, zones, name);
  const teamOf = new Map(game.participants.filter((x) => x.teamId !== undefined).map((x) => [x.id, x.teamId!]));
  const ordinalOf = new Map(game.teams.map((t) => [t.id, t.ordinal]));
  const ordinalOfModel = (unitId: string): number => {
    const participant = p.units.get(unitId)?.participantId;
    const team = participant === undefined ? undefined : teamOf.get(participant);
    return team === undefined ? 1 : ordinalOf.get(team) ?? 1;
  };

  const markerShape = (m: Marker): MarkerShape => {
    const controller = p.control.get(m.id);
    const ordinal = controller === undefined ? undefined : ordinalOf.get(controller);
    return {
      id: m.id, kind: m.kind, label: m.label ?? m.kind, centre: { x: m.position.x, y: m.position.y }, radius: m.diameter / 2,
      ...(ordinal === undefined ? {} : { teamOrdinal: ordinal }),
    };
  };
  const setupIds = new Set(p.board.table.markers.map((m) => m.id));
  const markers: MarkerShape[] = [
    ...p.board.table.markers.map((m) => markerShape(p.markers.get(m.id) ?? m)),
    ...[...p.markers.values()].filter((m) => !setupIds.has(m.id)).map(markerShape),
  ];

  const models: ModelShape[] = [];
  const offTable: OffTable[] = [];
  for (const m of p.models.values()) {
    const unit = p.units.get(m.unitId);
    const placement = ghosts.get(m.id) ?? (m.location.kind === 'table' ? m.location.placement : undefined);
    const teamOrdinal = ordinalOfModel(m.unitId);
    if (placement === undefined) {
      offTable.push({
        id: m.id, label: m.slot.name, teamOrdinal, where: m.location.kind,
        ...(m.location.kind === 'inside' ? { containerLabel: p.models.get(m.location.containerId)?.slot.name ?? m.location.containerId } : {}),
      });
      continue;
    }
    const centre = { x: placement.position.x, y: placement.position.y };
    const yaw = placement.yaw.degrees;
    const { shape: baseShape, a, b } = m.slot.base;
    const reach = rotate({ x: 0, y: a / 2 }, yaw);
    models.push({
      id: m.id, unitId: m.unitId, unitName: unit?.definition.name ?? m.unitId, label: m.slot.name,
      teamId: teamOf.get(unit?.participantId ?? '') ?? '', teamOrdinal,
      centre, shape: baseOutline({ baseShape, baseA: a, baseB: b } as never, centre, yaw),
      facing: [centre, { x: centre.x + reach.x, y: centre.y + reach.y }],
      wounds: m.wounds, spellTokens: m.spellTokens, conditions: [...m.conditions].sort(),
      elevated: placement.position.z > 0.01, z: placement.position.z,
      circle: { kind: 'circle', centre, radius: Math.max(a, b) / 2 },
    });
  }
  return { ...base, markers, models, offTable };
}
