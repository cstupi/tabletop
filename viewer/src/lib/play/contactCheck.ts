// viewer/src/lib/play/contactCheck.ts
// client/src/Core/ContactCheck.cs over Geometry.contacts (the moved Contacts.Of): the contacts a drag shows before the server judges it.
// The server stays the judge: a unit a mover already stood within the separation of is left to it. No `unit` cap: the difficult cap is
// the server's to judge (no reach outline in the browser).

import type { Contact, ContactBody, Geometry, Placement, TableSize } from '../geometry';
import type { Projection } from '../live/apply';
import type { MoveRegion } from '../protocol/wire.generated';

/** The projection's models as contact bodies: `at` for a model on the table. */
export function bodiesOf(p: Projection): ContactBody[] {
  return [...p.models.values()].map((m): ContactBody => ({
    id: m.id, unitId: m.unitId, body: { base: m.slot.base, height: m.slot.height },
    ...(m.location.kind === 'table' ? { at: m.location.placement } : {}),
  }));
}

/** A move answer's landings: on a base, in impassable terrain unless the region ignores terrain, through a model unless the region passes
 *  models, within its Separation. A path stays on the `size` table (p.7). */
export function move(g: Geometry, p: Projection, legal: MoveRegion, proposed: ReadonlyMap<string, Placement>, size: TableSize): Map<string, Contact> {
  return g.contacts(bodiesOf(p), proposed, p.board.table.terrain, {
    ...(legal.separation === undefined ? {} : { separation: legal.separation }),
    passesModels: legal.passesModels, landing: !legal.ignoresTerrain,
    move: {
      allowance: legal.allowance, impassable: legal.impassableVolumeIds, difficult: legal.difficultVolumeIds,
      ...(legal.difficultCap === undefined ? {} : { difficultCap: legal.difficultCap }),
      ...(legal.steepVolumeIds === undefined ? {} : { steep: legal.steepVolumeIds }),
    },
    size,
  });
}

/** A hand placement's landings (a deployment, a hand move): on a base only. */
export function placement(g: Geometry, p: Projection, proposed: ReadonlyMap<string, Placement>): Map<string, Contact> {
  return g.contacts(bodiesOf(p), proposed, p.board.table.terrain, { passesModels: true, landing: false });
}

/** `{n:0.#}`: at most one decimal, no trailing zero. */
const tenth = (n: number): string => String(Math.round(n * 10) / 10);

export function words(c: Contact, separation: number | undefined, unitName: (id: string) => string): string {
  switch (c.kind) {
    case 'on_base': return 'overlaps a base';
    case 'crosses': return 'path crosses a model';
    case 'in_impassable': return 'ends in impassable terrain';
    default: return `within ${tenth(separation ?? 0)}" of ${c.unitId !== undefined ? unitName(c.unitId) : 'another unit'}`;
  }
}

/** The words as the decision box's sentence about `subject` (the model's name), as the server words its own refusals; words that are not
 *  a contact's stay as they are. */
export function sentence(words: string, subject: string): string {
  switch (words) {
    case 'overlaps a base': return `${subject} would be placed on another model's base (p.7)`;
    case 'path crosses a model': return `${subject} would move through another model (p.7)`;
    case 'ends in impassable terrain': return `${subject} would end its move in impassable terrain (p.11, p.7)`;
    default: return words.startsWith('within ') ? `${subject} would end ${words}${words.startsWith('within 1"') ? ' (p.7)' : ''}` : words;
  }
}

/** Each contact as words, by model. */
export function allWords(found: ReadonlyMap<string, Contact>, separation: number | undefined, unitName: (id: string) => string): Map<string, string> {
  return new Map([...found].map(([id, c]) => [id, words(c, separation, unitName)] as const));
}
