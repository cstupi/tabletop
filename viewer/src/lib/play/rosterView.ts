// viewer/src/lib/play/rosterView.ts
// client/src/Core/RosterView.cs: the roster from the projection only (ReserveOf and Words are rosterReserve.ts).

import type { ModelInPlay, Projection, UnitInPlay } from '../live/apply';
import type { GameInfo } from '../protocol/wire.generated';
import type { Names } from './names';
import { reserveOf } from './rosterReserve';

export { words } from './rosterReserve';

/** inside: the name of the unit whose model holds this one (the roster's "in {Transport}"), else null. reserve: held off the table until a
 * property places it, else null. */
export interface RosterRow {
  readonly unitId: string; readonly name: string; readonly activated: boolean; readonly left: number; readonly max: number;
  readonly offTable: boolean; readonly conditions: readonly string[]; readonly inside: string | null; readonly reserve: string | null;
}
export interface RosterTeam { readonly teamId: string; readonly name: string; readonly ordinal: number; readonly rows: readonly RosterRow[] }

/** My team first, the rest by ordinal, units in army order. `left` is wounds left over models not removed (on the shelf with every wound
 * taken); off table is no model on the table, nor inside a container on it. */
export function rosterOf(p: Projection, game: GameInfo, n: Names, myTeam: string | null, maxWounds: ReadonlyMap<string, number>): RosterTeam[] {
  return [...game.teams].sort((a, b) => (a.id === myTeam ? 0 : 1) - (b.id === myTeam ? 0 : 1) || a.ordinal - b.ordinal).map((t) => ({
    teamId: t.id, name: n.team(t.id), ordinal: t.ordinal,
    rows: [...p.units.values()].filter((u) => game.participants.some((x) => x.id === u.participantId && x.teamId === t.id)).map((u) => rowOf(p, u, maxWounds)),
  }));
}

function rowOf(p: Projection, u: UnitInPlay, maxWounds: ReadonlyMap<string, number>): RosterRow {
  const models = [...p.models.values()].filter((m) => m.unitId === u.id).map((m) => ({ m, max: maxWounds.get(m.id) ?? 1 }));
  const left = models.filter((x) => !(x.m.location.kind === 'shelf' && x.m.wounds >= x.max)).reduce((sum, x) => sum + Math.max(0, x.max - x.m.wounds), 0);
  let holder: ModelInPlay | undefined;
  for (const x of models) {
    const loc = x.m.location;
    if (loc.kind !== 'inside') continue;
    holder = p.models.get(loc.containerId);
    if (holder !== undefined) break;
  }
  const onTable = (m: ModelInPlay): boolean => m.location.kind === 'table'
    || (m.location.kind === 'inside' && p.models.get(m.location.containerId)?.location.kind === 'table');
  const conditions = new Set([...u.conditions, ...models.flatMap((x) => [...x.m.conditions])]);
  const transport = holder === undefined ? undefined : p.units.get(holder.unitId);
  return {
    unitId: u.id, name: u.definition.name, activated: u.actions.size > 0, left, max: models.reduce((sum, x) => sum + x.max, 0),
    offTable: !models.some((x) => onTable(x.m)), conditions: [...conditions].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)),
    inside: transport?.definition.name ?? null, reserve: reserveOf(p, u),
  };
}

/** `●` for wounds left, `○` for taken. */
export const pips = (left: number, max: number): string => '●'.repeat(Math.max(0, left)) + '○'.repeat(Math.max(0, max - left));

/** Pips up to 12 wounds, else `left/max` (Roster.cs). */
export const woundsText = (r: RosterRow): string => (r.max <= 12 ? pips(r.left, r.max) : `${r.left}/${r.max}`);
