// viewer/src/lib/setup/layouts.ts
// LayoutChoices and the toolbar's Random table / Save as (client/src/Core/LayoutChoices.cs, Panels/SetupToolbar.cs): layouts, predefined
// tables and the random table all end as one ReplaceDraft; the browser rolls nothing, the server lays the table.

import { nameProblem } from '../api/names';
import type { LayoutSummary, Planet, RandomTable, RandomTableRequest } from '../api/types';
import type { ReplaceDraft, TableState } from '../protocol/wire.generated';

export const PLANETS: readonly { readonly planet: Planet; readonly label: string }[] = [
  { planet: 'generic', label: 'Generic planet' }, { planet: 'hospitable', label: 'Hospitable planet' },
  { planet: 'wasteland', label: 'Wasteland planet' }, { planet: 'death', label: 'Death planet' }, { planet: 'ruins', label: 'Ruins planet' },
];

/** Load (a saved layout or a predefined table): the whole draft, size included. */
export const loadEdit = (layout: TableState): ReplaceDraft => ({ kind: 'replace_draft', draft: layout });

/** The request for the current draft: its size and zone rows (left out when there are none); no seed. */
export function randomRequest(draft: TableState, planet: Planet): RandomTableRequest {
  return { size: draft.size, planet, ...(draft.zones.length === 0 ? {} : { zones: draft.zones }) };
}

/** The random table's edit: the draft with its terrain replaced; size, texture, markers and zones kept. */
export const randomEdit = (draft: TableState, answer: RandomTable): ReplaceDraft =>
  ({ kind: 'replace_draft', draft: { ...draft, terrain: answer.terrain } });

/** The line after a random table: how many pieces, and what did not fit or is not in the catalog. */
export function randomSummary(answer: RandomTable): string {
  const n = answer.terrain.length;
  let text = `Placed ${n} piece${n === 1 ? '' : 's'}.`;
  if (answer.unplaced.length > 0) text += ` ${answer.unplaced.length} did not fit: ${answer.unplaced.join(', ')}.`;
  if (answer.unlisted.length > 0) text += ` Not in the catalog: ${answer.unlisted.join(', ')}.`;
  return text;
}

/** Save as: the draft named (nameProblem first); management stores it as the account's layout. */
export function saveRequest(draft: TableState, name: string): { table: TableState } | { problem: string } {
  const problem = nameProblem(name, 'A layout name');
  return problem !== null ? { problem } : { table: { ...draft, name: name.trim() } };
}

/** A saved layout row's text, Godot's: "Name (72×48, 12 terrain)". */
export const layoutLabel = (l: LayoutSummary): string => `${l.name} (${l.size.width}×${l.size.length}, ${l.terrain} terrain)`;

/** A predefined table's row: "Name (built-in, 72×48, 12 terrain)". */
export const builtinLabel = (t: TableState): string =>
  `${t.name ?? 'Table'} (built-in, ${t.size.width}×${t.size.length}, ${t.terrain.length} terrain)`;
