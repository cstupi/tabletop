// viewer/src/lib/play/keepAwayArea.ts
// client/src/Core/KeepAwayArea.cs: the area a placement may not reach, from the KeepAway its Ask carries (an Ambusher, a 3.6 Scout or Reinforcement
// copy, a 3.6 Fanatic or Vanguard): each listed unit's models on the table grown by its distance, less the area an ignoredNear model lifts
// (Geometry.clearanceOutline), as one ring the board draws; and each ghost whose base would break it (Geometry.clearanceBreaks), as the decision box's
// words. Every number is the server's.

import { placed, type Geometry, type PlacedModel } from '../geometry';
import type { Projection } from '../live/apply';
import type { Ask, KeepAway, Placement } from '../protocol/wire.generated';
import type { MidDeploy } from './deployView';
import type { RangeRing } from './rangeRings';

/** KeepAwayArea.Shade: the fill and rim, a dark shade apart from every team colour, the move area's bands and the activated green. */
export const SHADE = '#0d0d12';
/** KeepAwayArea.Rim: the outline and the label's words, light over the shade's dark halo, so the edge reads on a dark board and a light one. */
export const SHADE_WORDS = '#e6e9ee';

/** KeepAwayArea.For: the keep-away the placement under way is held to: the deploy pick's for its unit, else the open move Ask's. */
export function keepAwayFor(mid: MidDeploy | null, move: Ask | null): KeepAway | null {
  if (mid?.keepAway != null) return mid.keepAway;
  return move?.legal.kind === 'move_region' ? move.legal.keepAway ?? null : null;
}

interface Read { readonly from: PlacedModel[]; readonly over: number[]; readonly units: string[]; readonly lifted: PlacedModel[]; readonly key: string }

function read(p: Projection, keep: KeepAway, moving?: ReadonlySet<string>): Read {
  const by = new Map<string, number>();
  for (const u of keep.units) by.set(u.unitId, Math.max(by.get(u.unitId) ?? 0, u.over));
  const from: PlacedModel[] = [], over: number[] = [], units: string[] = [], key: string[] = [];
  for (const m of [...p.models.values()].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))) {
    const d = by.get(m.unitId);
    if (m.location.kind !== 'table' || d === undefined || moving?.has(m.id) === true) continue;
    const at = m.location.placement;
    from.push(placed(m.slot, at)); over.push(d); units.push(m.unitId);
    key.push(`${m.id}:${d}:${at.position.x},${at.position.y},${at.position.z},${at.yaw.degrees}`);
  }
  const lifted: PlacedModel[] = [];
  for (const id of keep.ignoredNear ?? []) {
    const m = p.models.get(id);
    if (m?.location.kind !== 'table') continue;
    const at = m.location.placement;
    lifted.push(placed(m.slot, at));
    key.push(`L${id}:${at.position.x},${at.position.y}`);
  }
  return { from, over, units, lifted, key: `${keep.ignoredWithin ?? 0}|${key.join(';')}` };
}

const inchesText = (d: number): string => `${Math.round(d * 10) / 10}"`;

/** "Too close 9\"": the placing rule's own distance, the shortest listed (a unit kept farther shows in the ring's shape and the breach words). */
export const keepAwayLabel = (over: readonly number[]): string => `Too close ${inchesText(Math.min(...over))}`;

/** The ring, or null when no listed model is on the table. */
export function keepAwayRingOf(g: Geometry, p: Projection, keep: KeepAway): RangeRing | null {
  const r = read(p, keep);
  if (r.from.length === 0) return null;
  return { inches: Math.max(...r.over), label: keepAwayLabel(r.over), segments: g.clearanceOutline(r.from, r.over, r.lifted, keep.ignoredWithin ?? 0), kind: 'keepAway' };
}

/** The ring, built again only when the keep-away or a listed or lifting model's placement changes (an enemy moved). */
export class KeepAwayCache {
  #keep: KeepAway | null = null;
  #key: string | null = null;
  #ring: RangeRing | null = null;

  of(g: Geometry, p: Projection, keep: KeepAway): RangeRing | null {
    const key = read(p, keep).key;
    if (keep === this.#keep && key === this.#key) return this.#ring;
    this.#keep = keep; this.#key = key;
    this.#ring = keepAwayRingOf(g, p, keep);
    return this.#ring;
  }
}

/** KeepAwayArea.Breaches: each proposed model whose base breaks the keep-away, with the words the decision box shows. */
export function keepAwayBreaches(g: Geometry, p: Projection, keep: KeepAway, proposed: ReadonlyMap<string, Placement>, unitName: (id: string) => string): Map<string, string> {
  const found = new Map<string, string>();
  const r = read(p, keep, new Set(proposed.keys()));
  if (r.from.length === 0) return found;
  for (const [id, at] of proposed) {
    const m = p.models.get(id);
    if (m === undefined) continue;
    const hit = g.clearanceBreaks(placed(m.slot, at), r.from, r.over, r.lifted, keep.ignoredWithin ?? 0);
    if (hit >= 0) found.set(id, `placed within ${inchesText(r.over[hit]!)} of ${unitName(r.units[hit]!)}`);
  }
  return found;
}

/** KeepAwayArea.With: a model's contact words first, else its keep-away words; a null keep-away adds none. */
export function withKeepAway(contacts: Map<string, string>, g: Geometry, p: Projection, keep: KeepAway | null, proposed: ReadonlyMap<string, Placement>,
  unitName: (id: string) => string): Map<string, string> {
  if (keep === null) return contacts;
  for (const [id, words] of keepAwayBreaches(g, p, keep, proposed, unitName)) if (!contacts.has(id)) contacts.set(id, words);
  return contacts;
}
