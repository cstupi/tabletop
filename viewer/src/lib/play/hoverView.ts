// viewer/src/lib/play/hoverView.ts
// client/src/Core/HoverView.cs without From/To: what hovering a unit shows (spec: in-game UX → Hover a unit): its name in its team's
// colour; with a unit selected, the distance between the two units; the wounds left of each model that takes more than one. The
// browser draws no line between the bases: plan B exports no closest points.

import type { Geometry, PlacedModel } from '../geometry';
import type { Projection } from '../live/apply';
import type { GameInfo } from '../protocol/wire.generated';
import type { Names } from './names';

export interface HoverInfo {
  readonly unitId: string; readonly name: string; readonly teamOrdinal: number; readonly distance: number | null;
  readonly wounds: readonly { modelId: string; left: number; max: number }[];
}

/** The unit's models on the table, by slot ordinal, each with its placed body. */
function onTable(p: Projection, unitId: string) {
  return [...p.models.values()].filter((m) => m.unitId === unitId && m.location.kind === 'table').sort((a, b) => a.slot.ordinal - b.slot.ordinal)
    .map((m) => ({ model: m, placed: { body: { base: m.slot.base, height: m.slot.height }, placement: (m.location as { placement: PlacedModel['placement'] }).placement } satisfies PlacedModel }));
}

/** Null when the hovered unit is unknown or has no model on the table. `selected` is the caller's selection when it is one of my units,
 * else null. Distance is null without one, when it is the hovered unit, or when none of its models is on the table; it is the least
 * base-edge distance over the pairs of models (p.5: from/to the closest model in each unit). Team ordinal 0: the unit's participant has
 * no team. An id absent from `maxWounds` takes one wound. */
export function hoverOf(
  g: Geometry | null, p: Projection, game: GameInfo, n: Names, hovered: string, selected: string | null, maxWounds: ReadonlyMap<string, number>,
): HoverInfo | null {
  const unit = p.units.get(hovered);
  if (unit === undefined) return null;
  const models = onTable(p, hovered);
  if (models.length === 0) return null;
  const teamId = game.participants.find((x) => x.id === unit.participantId)?.teamId;
  const ordinal = game.teams.find((t) => t.id === teamId)?.ordinal ?? 0;

  let distance: number | null = null;
  const mine = g !== null && selected !== null && selected !== hovered ? onTable(p, selected) : [];
  for (const a of mine) for (const b of models) {
    const d = g!.edgeToEdge(a.placed, b.placed);
    if (distance === null || d < distance) distance = d;
  }

  const wounds = models.map((m) => ({ id: m.model.id, max: maxWounds.get(m.model.id) ?? 1, taken: m.model.wounds })).filter((m) => m.max > 1)
    .map((m) => ({ modelId: m.id, left: Math.max(0, m.max - m.taken), max: m.max }));
  return { unitId: hovered, name: n.unit(hovered), teamOrdinal: ordinal, distance, wounds };
}
