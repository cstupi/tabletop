// viewer/src/lib/games/listModel.ts
// The create dialog's defaults. The page's sections are homeView.ts.

import type { CreateGameRequest, PropertiesSummary } from '../api/types';

/** HomeScreen's Create: the defaults the dialog opens with (the default rule set, else the first, alternating activation, Enforcement 'warn'). */
export function createDefaults(packs: readonly PropertiesSummary[]): CreateGameRequest | null {
  const first = packs.find((p) => p.default) ?? packs[0];
  if (first === undefined) return null;
  return { name: '', propertiesHash: first.contentHash, schedulerShape: 'alternating-activation', enforcement: 'warn', waitsBetweenSteps: false };
}
