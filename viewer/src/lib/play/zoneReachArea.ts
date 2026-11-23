// viewer/src/lib/play/zoneReachArea.ts
// client/src/Core/ZoneReachArea.cs: where a deploy pick's unit may stand, from the reach its Ask carries (PieceSelection.zoneReach: a Scout's 12" or
// 3.6's 9", 0 inside the zone): the placing team's deployment zone grown by the reach (Geometry.zoneReachOutline), as one ring the board draws in the
// team's colour, labelled `Scout 9"`; and each ghost whose base is not fully within it (Geometry.zoneHoldsBase, the server's ZoneBreach), as the
// decision box's words. Every number is the server's; the zone's shape comes from the board and Welcome's legs.

import { placed, type Geometry } from '../geometry';
import type { Projection } from '../live/apply';
import type { GameInfo, Placement, ZoneLegs } from '../protocol/wire.generated';
import type { MidDeploy } from './deployView';
import type { RangeRing } from './rangeRings';
import { teamOf } from './sheetFocus';

const inchesText = (d: number): string => `${Math.round(d * 10) / 10}"`;

interface Zone { readonly shape: string; readonly ordinal: number }

function zoneOf(p: Projection, game: GameInfo, unit: string): Zone | null {
  const team = teamOf(p, game, unit);
  if (team === null) return null;
  const zone = p.board.table.zones.find((z) => z.teamOrdinal === team.ordinal);
  return zone === undefined ? null : { shape: zone.shape, ordinal: team.ordinal };
}

/** ZoneReachArea.Label: `Scout 9"`; a reach of 0 is the zone itself. */
export const zoneReachLabel = (reach: number): string => (reach > 0 ? `Scout ${inchesText(reach)}` : 'Your zone');

/** The team ordinal whose colour the ring takes, or null. */
export const zoneReachOrdinal = (p: Projection, game: GameInfo, mid: MidDeploy): number | null => zoneOf(p, game, mid.unitId)?.ordinal ?? null;

/** The ring, or null when the pick carries no reach for its unit or the table has no zone for the team. */
export function zoneReachRingOf(g: Geometry, p: Projection, game: GameInfo, mid: MidDeploy, legs: ZoneLegs | null): RangeRing | null {
  const zone = zoneOf(p, game, mid.unitId);
  if (mid.zoneReach === null || zone === null) return null;
  const segments = g.zoneReachOutline(zone.shape, p.board.table.size, mid.zoneReach, legs ?? undefined);
  return { inches: mid.zoneReach, label: zoneReachLabel(mid.zoneReach), segments, kind: 'reach' };
}

/** The ring, built again only when the unit, reach, zone, table or legs change. */
export class ZoneReachCache {
  #key: string | null = null;
  #ring: RangeRing | null = null;

  of(g: Geometry, p: Projection, game: GameInfo, mid: MidDeploy, legs: ZoneLegs | null): RangeRing | null {
    const zone = zoneOf(p, game, mid.unitId);
    const t = p.board.table;
    const key = `${mid.unitId}|${mid.zoneReach}|${zone?.shape}|${t.size.width}x${t.size.length}|${legs?.sideBattleLong},${legs?.sideBattleShort}`;
    if (key === this.#key) return this.#ring;
    this.#key = key;
    this.#ring = zoneReachRingOf(g, p, game, mid, legs);
    return this.#ring;
  }
}

/** ZoneReachArea.Breaches: each proposed model whose base is not fully within the reach. */
export function zoneReachBreaches(g: Geometry, p: Projection, game: GameInfo, mid: MidDeploy, legs: ZoneLegs | null, proposed: ReadonlyMap<string, Placement>): Map<string, string> {
  const found = new Map<string, string>();
  const zone = zoneOf(p, game, mid.unitId);
  const reach = mid.zoneReach;
  if (reach === null || zone === null) return found;
  for (const [id, at] of proposed) {
    const m = p.models.get(id);
    if (m === undefined) continue;
    if (!g.zoneHoldsBase(zone.shape, p.board.table.size, placed(m.slot, at), legs ?? undefined, reach))
      found.set(id, reach > 0 ? `placed beyond ${inchesText(reach)} of its deployment zone` : 'placed outside its deployment zone');
  }
  return found;
}

/** ZoneReachArea.With: a model's earlier words first (contacts, keep-away), else its reach words; a pick with no reach adds none. */
export function withZoneReach(earlier: Map<string, string>, g: Geometry, p: Projection, game: GameInfo, mid: MidDeploy | null, legs: ZoneLegs | null,
  proposed: ReadonlyMap<string, Placement>): Map<string, string> {
  if (mid === null || mid.zoneReach === null) return earlier;
  for (const [id, words] of zoneReachBreaches(g, p, game, mid, legs, proposed)) if (!earlier.has(id)) earlier.set(id, words);
  return earlier;
}
