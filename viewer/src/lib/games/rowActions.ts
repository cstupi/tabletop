// viewer/src/lib/games/rowActions.ts
// GameRowActions.Of (client/src/Core/GameRowActions.cs): what the list offers on a game. Management takes End and Delete from the
// game's creator alone, and End only means something before the game has ended. Replay: a game this account is in that has a log.

import type { GameSummary } from '../live/games';

export interface RowActions { readonly canEnd: boolean; readonly canDelete: boolean; readonly canReplay: boolean }

export function rowActionsOf(row: GameSummary | null, me: string | null): RowActions {
  if (row === null || me === null) return { canEnd: false, canDelete: false, canReplay: false };
  const canReplay = row.joined && (row.status === 'playing' || row.status === 'ended');
  if (row.createdBy !== me) return { canEnd: false, canDelete: false, canReplay };
  return { canEnd: row.status !== 'ended', canDelete: true, canReplay };
}
