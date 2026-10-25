// viewer/src/lib/lobby/seats.ts
// Seats.Mine and Seats.Teams (client/src/Core/Seats.cs): the account's participant rows and the teams they hold.

import type { GameInfo, Participant } from '../protocol/wire.generated';

/** The account's rows: those sharing the account of the row Welcome named (an admin watcher's empty id names none). */
export function mine(game: GameInfo, participantId: string | null): Participant[] {
  const me = game.participants.find((p) => p.id === participantId);
  return me === undefined ? [] : game.participants.filter((p) => p.accountId === me.accountId);
}

/** The account's team ids, by ordinal. */
export function teamsOf(game: GameInfo, participantId: string | null): string[] {
  const held = new Set(mine(game, participantId).map((p) => p.teamId).filter((t): t is string => t !== undefined));
  return [...game.teams].sort((a, b) => a.ordinal - b.ordinal).map((t) => t.id).filter((id) => held.has(id));
}

/** Session.IsCreator: my account made the game. */
export function isCreator(game: GameInfo, participantId: string | null): boolean {
  const me = mine(game, participantId)[0];
  return me !== undefined && me.accountId === game.createdBy;
}
