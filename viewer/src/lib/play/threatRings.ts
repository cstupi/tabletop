import type { AskMode } from './askView';
// viewer/src/lib/play/threatRings.ts
// client/src/Core/ThreatRings.cs and ThreatRingCache: a unit's reach in its next activation, from the server's UnitThreat
// (ModelFacts.threats), drawn as rangeRings.ts draws. One "Charge" outline at the charge move round every model on the table, and one
// "Shoot" outline per distinct shootMove + range round the models with that reach, ascending. Every distance is the server's.

import { placed, type Geometry, type Placement } from '../geometry';
import type { ModelInPlay, Projection } from '../live/apply';
import type { GameInfo, UnitThreat } from '../protocol/wire.generated';
import type { RangeRing } from './rangeRings';

interface Carrier { readonly model: ModelInPlay; readonly placement: Placement }

function onTable(p: Projection, unitId: string): Carrier[] {
  const out: Carrier[] = [];
  for (const m of p.models.values()) if (m.unitId === unitId && m.location.kind === 'table') out.push({ model: m, placement: m.location.placement });
  return out;
}

const inchesText = (d: number): string => `${Math.round(d * 10) / 10}"`;

export function threatRingsOf(g: Geometry, p: Projection, unitId: string, threat: UnitThreat): RangeRing[] {
  const models = onTable(p, unitId);
  const rings: RangeRing[] = [];
  if (threat.charge != null && models.length > 0)
    rings.push({ inches: threat.charge, label: `Charge ${inchesText(threat.charge)}`, segments: g.rangeOutline(models.map((c) => placed(c.model.slot, c.placement)), threat.charge), kind: 'charge' });
  if (threat.shootMove != null) {
    const reaches = new Map<number, Carrier[]>();
    for (const c of models) {
      const range = threat.ranges[c.model.id];
      if (range === undefined) continue;
      const d = threat.shootMove + range;
      reaches.set(d, [...(reaches.get(d) ?? []), c]);
    }
    for (const [d, carriers] of reaches) rings.push({ inches: d, label: `Shoot ${inchesText(d)}`, segments: g.rangeOutline(carriers.map((c) => placed(c.model.slot, c.placement)), d), kind: 'shoot' });
  }
  return rings.sort((a, b) => a.inches - b.inches);
}

/** Two threats that draw the same rings. */
export function sameThreat(a: UnitThreat, b: UnitThreat): boolean {
  const ak = Object.keys(a.ranges), bk = Object.keys(b.ranges);
  return (a.charge ?? null) === (b.charge ?? null) && (a.shootMove ?? null) === (b.shootMove ?? null)
    && ak.length === bk.length && ak.every((k) => b.ranges[k] === a.ranges[k]);
}

const teamOf = (game: GameInfo, participantId: string): string | null => game.participants.find((x) => x.id === participantId)?.teamId ?? null;

/** The owner's enemy: a unit not on the team I act for now (seats.ts acting), so one account holding both teams sees the other side's.
 *  A watcher acts for none (null) and sees every unit's. */
export function ofAnotherTeam(game: GameInfo, p: Projection, acting: string | null, unitId: string): boolean {
  const u = p.units.get(unitId);
  if (u === undefined) return false;
  return acting === null || teamOf(game, u.participantId) !== acting;
}

/** ThreatRings.Pinned: the enemy unit whose threat stays drawn: the selected unit when it is of another team; in deployment selecting a unit of my
 *  own (picking a unit to deploy, or its ghost) or a terrain piece keeps the one before; from round 1 on it clears it, as no selection always does. */
export function pinnedThreat(game: GameInfo, p: Projection, acting: string | null, selected: string | null, before: string | null): string | null {
  if (selected === null) return null;
  return ofAnotherTeam(game, p, acting, selected) ? selected : p.scheduler.round === 0 ? before : null;
}

/** ThreatRings.Wanted: the selected and hovered enemies' threats draw except while I pick shooting targets or allocate wounds. */
export function threatsWanted(mode: AskMode | null): boolean { return mode !== 'assign' && mode !== 'allocate'; }

/** ThreatRings.Shown: the pinned unit (pinnedThreat), then the hovered one; each of another team, with a threat, once. */
export function shownThreats(game: GameInfo, p: Projection, acting: string | null, pinned: string | null, hovered: string | null, threats: ReadonlyMap<string, UnitThreat>): string[] {
  const shown: string[] = [];
  for (const id of [pinned, hovered]) if (id !== null && !shown.includes(id) && threats.has(id) && ofAnotherTeam(game, p, acting, id)) shown.push(id);
  return shown;
}

/** One enemy team's threat areas under the T key: a Charge area and a Shoot area, in the team's colour. */
export interface TeamThreat { readonly teamId: string; readonly ordinal: number; readonly rings: readonly RangeRing[] }

/** ThreatRings.All, the T key: per team other than the one I act for, the union of its units' reach by kind, one Charge area (each unit's charge
 *  round its models) and one Shoot area (each model's shootMove + range), labelled with the distance, or the shortest–longest when they differ. */
export function allThreatsOf(g: Geometry, game: GameInfo, p: Projection, acting: string | null, threats: ReadonlyMap<string, UnitThreat>): TeamThreat[] {
  type Carrier = { model: ModelInPlay; placement: Placement; range: number };
  const charge = new Map<string, Carrier[]>(), shoot = new Map<string, Carrier[]>();
  const add = (into: Map<string, Carrier[]>, team: string, c: Carrier) => { const list = into.get(team); if (list === undefined) into.set(team, [c]); else list.push(c); };
  for (const m of p.models.values()) {
    if (m.location.kind !== 'table') continue;
    const u = p.units.get(m.unitId), t = threats.get(m.unitId);
    if (u === undefined || t === undefined) continue;
    const team = teamOf(game, u.participantId);
    if (team === null || team === acting) continue;
    const placement = m.location.placement;
    if (t.charge != null) add(charge, team, { model: m, placement, range: t.charge });
    const range = t.ranges[m.id];
    if (t.shootMove != null && range !== undefined) add(shoot, team, { model: m, placement, range: t.shootMove + range });
  }
  const area = (by: Map<string, Carrier[]>, team: string, word: string, kind: 'charge' | 'shoot'): RangeRing[] => {
    const cs = by.get(team);
    if (cs === undefined) return [];
    const ranges = cs.map((c) => c.range), lo = Math.min(...ranges), hi = Math.max(...ranges);
    const label = lo === hi ? `${word} ${inchesText(hi)}` : `${word} ${Math.round(lo * 10) / 10}–${inchesText(hi)}`;
    return [{ inches: hi, label, segments: g.rangeOutline(cs.map((c) => placed(c.model.slot, c.placement)), hi, ranges), kind }];
  };
  return [...game.teams].sort((a, b) => a.ordinal - b.ordinal)
    .map((t) => ({ teamId: t.id, ordinal: t.ordinal, rings: [...area(charge, t.id, 'Charge', 'charge'), ...area(shoot, t.id, 'Shoot', 'shoot')] }))
    .filter((t) => t.rings.length > 0);
}

const samePlacement = (a: Placement, b: Placement): boolean =>
  a.position.x === b.position.x && a.position.y === b.position.y && a.position.z === b.position.z && a.yaw.degrees === b.yaw.degrees;

/** One unit's threat rings, recomputed only when the unit, its models' slots or placements, or its threat change. */
export class ThreatRingCache {
  #unit: string | null = null;
  #key: readonly Carrier[] | null = null;
  #threat: UnitThreat | null = null;
  #rings: readonly RangeRing[] = [];

  of(g: Geometry, p: Projection, unitId: string, threat: UnitThreat): readonly RangeRing[] {
    const key = onTable(p, unitId);
    const prev = this.#key, was = this.#threat;
    if (prev !== null && was !== null && unitId === this.#unit && sameThreat(threat, was) && key.length === prev.length
      && key.every((c, i) => c.model.id === prev[i]!.model.id && c.model.slot === prev[i]!.model.slot && samePlacement(c.placement, prev[i]!.placement))) return this.#rings;
    this.#unit = unitId;
    this.#key = key;
    this.#threat = threat;
    this.#rings = threatRingsOf(g, p, unitId, threat);
    return this.#rings;
  }
}

/** AllThreatCache: the T key's areas, recomputed only when the team acted for, a unit with a threat, its models' slots or placements, or its
 *  threat change; otherwise the same array, so the drawn overlay stands. */
export class AllThreatCache {
  #acting: string | null = null;
  #key: { unit: string; at: readonly Carrier[]; threat: UnitThreat }[] | null = null;
  #teams: readonly TeamThreat[] = [];

  of(g: Geometry, game: GameInfo, p: Projection, acting: string | null, threats: ReadonlyMap<string, UnitThreat>): readonly TeamThreat[] {
    const key = [...threats.entries()].filter(([id]) => ofAnotherTeam(game, p, acting, id)).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([unit, threat]) => ({ unit, at: onTable(p, unit), threat }));
    const prev = this.#key;
    if (prev !== null && acting === this.#acting && key.length === prev.length && key.every((k, i) => k.unit === prev[i]!.unit && sameThreat(k.threat, prev[i]!.threat)
      && k.at.length === prev[i]!.at.length && k.at.every((c, j) => c.model.id === prev[i]!.at[j]!.model.id && c.model.slot === prev[i]!.at[j]!.model.slot && samePlacement(c.placement, prev[i]!.at[j]!.placement)))) return this.#teams;
    this.#acting = acting;
    this.#key = key;
    this.#teams = allThreatsOf(g, game, p, acting, threats);
    return this.#teams;
  }
}
