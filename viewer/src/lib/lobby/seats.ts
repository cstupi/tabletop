// viewer/src/lib/lobby/seats.ts
// Seats.Mine and Seats.Teams (client/src/Core/Seats.cs): the account's participant rows and the teams they hold.

import { effectiveLog, type Projection } from '../live/apply';
import type { GameEvent, GameInfo, Participant } from '../protocol/wire.generated';

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

/** Ids order as strings: UUIDv7 ids sort as Guid does. */
export const byId = (a: string, b: string): number => (a < b ? -1 : a > b ? 1 : 0);

/**
 * Seats.Acting: the team this client acts for now: the team of the first open Ask (by id) addressed to one of mine that it has not answered,
 * the first of mine by ordinal; else the active team when it is mine; between turns the latest of mine to start a turn or answer an Ask (from
 * the log); else my first team. Null with no team. The decision box, the unit card, the roster and the camera all read it.
 */
export function acting(p: Projection | null, mine: readonly string[], log?: readonly GameEvent[]): string | null {
  if (mine.length === 0) return null;
  if (mine.length === 1 || p === null) return mine[0]!;
  const open = [...p.openAsks.values()].sort((a, b) => byId(a.ask.id, b.ask.id));
  for (const o of open)
    for (const team of mine)
      if (o.ask.teams.includes(team) && !o.answers.has(team)) return team;
  const active = p.scheduler.activeTeamId;
  if (active !== undefined) return mine.includes(active) ? active : mine[0]!;
  // Between turns (a turn ended, the next not yet started; a round boundary's Ambusher picks): the latest of mine to start a turn or answer an
  // Ask, so a pick is placed by the team that made it and nothing swings to my first team and back.
  if (log !== undefined) {
    const live = effectiveLog(log);
    for (let i = live.length - 1; i >= 0; i--) {
      const e = live[i]!.payload;
      if (e.kind === 'turn_started' && mine.includes(e.teamId)) return e.teamId;
      if (e.kind === 'ask_closed') {
        const answered = e.answers.find((a) => mine.includes(a.teamId));
        if (answered !== undefined) return answered.teamId;
      }
    }
  }
  return mine[0]!;
}

/** "Playing as Team 2" for a client holding several teams; null for one team or none. */
export function playingAs(game: GameInfo, mine: readonly string[], actingTeam: string | null): string | null {
  const team = game.teams.find((t) => t.id === actingTeam);
  return mine.length > 1 && team !== undefined ? `Playing as Team ${team.ordinal}` : null;
}
