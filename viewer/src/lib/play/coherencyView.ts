// viewer/src/lib/play/coherencyView.ts
// client/src/Core/CoherencyView.cs: the unit coherency of the units being placed (a move's or a deployment's ghosts), measured as the server
// measures it (Geometry.coherencyGaps over the rule set's Welcome.coherency): each ghost's ring at the chain distance from its base edge
// (Geometry.rangeOutline), a line per model out of coherency to the model it is measured to, and each such model's words for the decision box.

import type { CoherencyGap, Geometry, PlacedModel, Placement, Segment } from '../geometry';
import type { Projection } from '../live/apply';
import type { UnitCoherency } from '../protocol/wire.generated';

export interface CoherencyRing { readonly modelId: string; readonly segments: readonly Segment[]; readonly z: number }
export interface CoherencyLine {
  readonly modelId: string; readonly toId: string; readonly break: CoherencyGap['break'];
  readonly from: { x: number; y: number; z: number }; readonly to: { x: number; y: number; z: number }; readonly text: string;
}
/** `blocks`: the server refuses this placement (a move of a unit in coherency before it), so the words are marks; otherwise warnings. */
export interface CoherencyPicture {
  readonly rings: readonly CoherencyRing[]; readonly lines: readonly CoherencyLine[]; readonly words: ReadonlyMap<string, string>; readonly blocks: boolean;
}

export const NO_COHERENCY: CoherencyPicture = { rings: [], lines: [], words: new Map(), blocks: false };

/** `0.#`. */
const inches = (d: number): string => String(Math.round(d * 10) / 10);

/** `1.4" apart, needs 1"`. */
export const gapText = (g: CoherencyGap): string => `${g.distance.toFixed(1)}" apart, needs ${inches(g.needed)}"`;

/** `judged`: the server judges this placement's coherency (a move answer); a deployment's ghosts only warn. */
/** Each ring's outline about its base's centre, by geometry, base, facing and distance: a ghost's ring is that outline moved to it, so a drag frame
 *  builds none (an oval's outline costs the browser's WebAssembly a tenth of a second or more). */
const outlines = new WeakMap<Geometry, Map<string, Segment[]>>();
function ringOf(g: Geometry, body: PlacedModel, chain: number): Segment[] {
  let known = outlines.get(g);
  if (known === undefined) { known = new Map(); outlines.set(g, known); }
  const { base } = body.body, yaw = body.placement.yaw.degrees;
  const key = `${base.shape} ${base.a} ${base.b} ${yaw} ${chain}`;
  let about = known.get(key);
  if (about === undefined) {
    if (known.size >= 256) known.clear();
    about = g.rangeOutline([{ body: body.body, placement: { position: { x: 0, y: 0, z: 0 }, yaw: body.placement.yaw } }], chain);
    known.set(key, about);
  }
  const { x, y } = body.placement.position;
  return about.map((s) => ({ a: { x: s.a.x + x, y: s.a.y + y }, b: { x: s.b.x + x, y: s.b.y + y } }));
}

/** Whether the unit stood in coherency before the move, by geometry and where its models stood: the same every drag frame of an Ask. */
const stood = new WeakMap<Geometry, { key: string; inCoherency: boolean }>();
function stoodInCoherency(g: Geometry, before: readonly PlacedModel[], c: UnitCoherency): boolean {
  const key = JSON.stringify([c.chain, c.elevated, c.spread, before]);
  const known = stood.get(g);
  if (known !== undefined && known.key === key) return known.inCoherency;
  const inCoherency = g.coherencyGaps(before, c.chain, c.elevated, c.spread).length === 0;
  stood.set(g, { key, inCoherency });
  return inCoherency;
}

export function coherencyOf(g: Geometry, p: Projection, placed: ReadonlyMap<string, Placement>, coherency: UnitCoherency | null | undefined,
  name: (id: string) => string, judged: boolean): CoherencyPicture {
  if (coherency == null || placed.size === 0) return NO_COHERENCY;
  const c = coherency;
  const rings: CoherencyRing[] = [], lines: CoherencyLine[] = [], words = new Map<string, string>();
  let blocks = false;
  const body = (id: string, at: Placement): PlacedModel => { const m = p.models.get(id)!; return { body: { base: m.slot.base, height: m.slot.height }, placement: at }; };
  const units = new Set([...placed.keys()].filter((id) => p.models.has(id)).map((id) => p.models.get(id)!.unitId));
  for (const unit of units) {
    const members = [...p.models.values()].filter((m) => m.unitId === unit);
    const now = members.flatMap((m) => {
      const at = placed.get(m.id) ?? (m.location.kind === 'table' ? m.location.placement : null);
      return at === null ? [] : [{ id: m.id, body: body(m.id, at) }];
    });
    for (const x of now) if (placed.has(x.id)) rings.push({ modelId: x.id, segments: ringOf(g, x.body, c.chain), z: x.body.placement.position.z });
    const gaps = g.coherencyGaps(now.map((x) => x.body), c.chain, c.elevated, c.spread);
    if (gaps.length === 0) continue;
    const before = members.flatMap((m) => m.location.kind === 'table' ? [body(m.id, m.location.placement)] : []);
    blocks ||= judged && stoodInCoherency(g, before, c);
    for (const gap of gaps) {
      const id = now[gap.model]!.id, to = now[gap.to]!.id;
      const zFrom = now[gap.model]!.body.placement.position.z, zTo = now[gap.to]!.body.placement.position.z;
      lines.push({ modelId: id, toId: to, break: gap.break, from: { x: gap.onModel.x, y: gap.onModel.y, z: zFrom }, to: { x: gap.onTo.x, y: gap.onTo.y, z: zTo }, text: gapText(gap) });
      words.set(id, gap.break === 'chain'
        ? `${name(id)} out of coherency: move within ${inches(gap.needed)}" of another model`
        : `${name(id)} out of coherency: move within ${inches(gap.needed)}" of ${name(to)}`);
    }
  }
  return { rings, lines, words, blocks };
}

/** A model's marks: its contact words first, else its coherency words when the picture blocks. */
export function withCoherency(marks: ReadonlyMap<string, string>, picture: CoherencyPicture): Map<string, string> {
  const all = new Map(marks);
  if (picture.blocks) for (const [id, w] of picture.words) if (!all.has(id)) all.set(id, w);
  return all;
}

/** A model's warnings: its own first (a dangerous terrain test), else its coherency words when the picture does not block. */
export function coherencyWarnings(warnings: ReadonlyMap<string, string>, picture: CoherencyPicture): Map<string, string> {
  const all = new Map(warnings);
  if (!picture.blocks) for (const [id, w] of picture.words) if (!all.has(id)) all.set(id, w);
  return all;
}

