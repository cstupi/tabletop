// viewer/src/lib/games/listModel.ts
// What the games page shows: the account's games, and the open ones it may join.

import type { CreateGameRequest, PropertiesSummary } from '../api/types';
import type { GameSummary } from '../live/games';

export interface GameLists { readonly mine: readonly GameSummary[]; readonly open: readonly GameSummary[] }

/** Mine: joined, newest first. Open: not joined and in lobby or setup (teams still editable). */
export function listsOf(all: readonly GameSummary[]): GameLists {
  const newestFirst = (a: GameSummary, b: GameSummary) => b.createdAt.localeCompare(a.createdAt);
  return {
    mine: all.filter((game) => game.joined).sort(newestFirst),
    open: all.filter((game) => !game.joined && (game.status === 'lobby' || game.status === 'setup')),
  };
}

/** HomeScreen's Create: the defaults the dialog opens with (the default rule set, else the first, alternating activation, Enforcement 'warn'). */
export function createDefaults(packs: readonly PropertiesSummary[]): CreateGameRequest | null {
  const first = packs.find((p) => p.default) ?? packs[0];
  if (first === undefined) return null;
  return { name: '', propertiesHash: first.contentHash, schedulerShape: 'alternating-activation', enforcement: 'warn', waitsBetweenSteps: false };
}
