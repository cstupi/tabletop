// viewer/src/lib/play/rosterReserve.ts
// client/src/Core/RosterView.cs: ReserveOf and Words, shared by the unit card's chip and the roster (plan D4).

import type { Projection, UnitInPlay } from '../live/apply';

/** "Reserve: {property} ({unit})" for a unit the army holds off the table (every model on the shelf) until that property, held at a model
 * or a unit of the same army, places it (the snapshot's Unit.reserve, as imported); null for any other unit. The property is the
 * snapshot's name, not one the client knows. */
export function reserveOf(p: Projection, u: UnitInPlay): string | null {
  const r = u.definition.reserve;
  if (r === undefined) return null;
  for (const m of p.models.values()) if (m.unitId === u.id && (m.location.kind !== 'shelf' || m.wounds > 0)) return null;   // placed (and maybe since destroyed): no longer held
  const holder = p.models.get(r.of);
  const at = p.units.get(r.of)?.definition.name ?? (holder !== undefined ? p.units.get(holder.unitId)?.definition.name : undefined);
  return at === undefined ? `Reserve: ${r.property}` : `Reserve: ${r.property} (${at})`;
}

/** A server name as shown: first letter capitalised, a hyphen as a space. */
export const words = (name: string): string => (name.length === 0 ? name : name[0]!.toUpperCase() + name.slice(1).replaceAll('-', ' '));
