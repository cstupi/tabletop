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

/** The owner's "a model of the other team": a unit on no team of mine. A watcher holds none, so every unit is another's. */
export function ofAnotherTeam(game: GameInfo, p: Projection, myTeams: readonly string[], unitId: string): boolean {
  const u = p.units.get(unitId);
  if (u === undefined) return false;
  const team = game.participants.find((x) => x.id === u.participantId)?.teamId;
  return team == null || !myTeams.includes(team);
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
