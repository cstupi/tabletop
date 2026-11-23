// viewer/src/lib/play/waiting.ts
// libs/Tabletop.Protocol/WaitingForPlayers.cs and client/src/Core/WaitingView.cs: while setup has ended and deployment waits for a team
// with no player or no army, everyone reads one line per team; any participant may join a team no one holds, and a player on a team with
// no army picks it. A watcher (admin) is told and offered nothing.

import type { ConnectionState } from '../connection/gameConnection';
import { mine as seatsOf, teamsOf } from '../lobby/seats';
import type { ArmySeat, TeamChoice } from '../lobby/lobbyView';
import type { Board, GameInfo } from '../protocol/wire.generated';

/** A team deployment waits on: hasPlayer false, no participant holds it; true, no seat on it has an army with a unit. */
export interface TeamWaiting { readonly teamId: string; readonly ordinal: number; readonly hasPlayer: boolean; readonly text: string }

const waitingOf = (teamId: string, ordinal: number, hasPlayer: boolean): TeamWaiting => ({
  teamId, ordinal, hasPlayer, text: hasPlayer ? `Team ${ordinal} has no army` : `Waiting for a player on Team ${ordinal}`,
});

/** Every team that holds deployment, by ordinal; empty when none does. */
export function waitingTeams(game: GameInfo, board: Board): TeamWaiting[] {
  const out: TeamWaiting[] = [];
  for (const t of [...game.teams].sort((a, b) => a.ordinal - b.ordinal)) {
    const seats = new Set(game.participants.filter((p) => p.teamId === t.id).map((p) => p.id));
    if (seats.size === 0) out.push(waitingOf(t.id, t.ordinal, false));
    else if (!board.armies.some((a) => seats.has(a.participantId) && a.snapshot.units.length > 0)) out.push(waitingOf(t.id, t.ordinal, true));
  }
  return out;
}

/** The game is playing, its log is still empty, and some team waits. */
export function waitHolds(game: GameInfo, board: Board | null, lastSeq: number): boolean {
  return game.status === 'playing' && board !== null && lastSeq === 0 && waitingTeams(game, board).length > 0;
}

export const WAITING_TITLE = 'Deployment starts once every team has a player and an army.';

/** One team deployment waits on: what everyone reads, and what this client may do about it. */
export interface WaitingLine { readonly text: string; readonly join: TeamChoice | null; readonly army: ArmySeat | null }

export function waitingLines(game: GameInfo, waiting: readonly TeamWaiting[], participantId: string | null): WaitingLine[] {
  const seats = seatsOf(game, participantId);
  return waiting.map((w): WaitingLine => ({
    text: w.text,
    join: !w.hasPlayer && seats.length > 0
      ? { teamId: w.teamId, text: `Join Team ${w.ordinal}`, enabled: true, why: 'Take this team and bring an army to it.', edit: { kind: 'pick_team', teamId: w.teamId } }
      : null,
    army: w.hasPlayer && seats.some((s) => s.teamId === w.teamId) ? { teamId: w.teamId, label: `Army for Team ${w.ordinal}`, armyId: null } : null,
  }));
}

/**
 * Whether the panel showing `shown` rebuilds for `now`. A game or board update that leaves the lines as they are keeps the panel's
 * controls, so an open army dropdown stays open; a picked army that reaches the board changes them.
 */
export function linesChanged(shown: readonly WaitingLine[] | null, now: readonly WaitingLine[]): boolean {
  if (shown === null || shown.length !== now.length) return true;
  return shown.some((a, i) => {
    const b = now[i]!;
    return a.text !== b.text || (a.join?.teamId ?? null) !== (b.join?.teamId ?? null) || (a.army?.teamId ?? null) !== (b.army?.teamId ?? null);
  });
}

/** Session.IsWatching without the replay arm: an admin watcher, or from playing on a participant with no team while no wait holds. */
export function isWatching(state: ConnectionState): boolean {
  const game = state.game;
  if (game === null) return false;
  if (state.participantId === '00000000-0000-0000-0000-000000000000') return true;
  if (game.status !== 'playing' && game.status !== 'ended') return false;
  const p = state.projection;
  const waiting = game.status === 'playing' && p !== null && waitHolds(game, p.board, p.lastSeq) ? waitingTeams(game, p.board) : [];
  return teamsOf(game, state.participantId).length === 0 && waiting.length === 0;
}
