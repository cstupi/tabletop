// viewer/src/lib/lobby/lobbyView.ts
// LobbyView (client/src/Core/LobbyView.cs), word for word: the lobby's seat and team buttons as the game server's lobby authority has them.

import type { GameInfo, LeaveTeam, Participant, PickArmy, PickTeam, TableState } from '../protocol/wire.generated';
import { mine, teamsOf } from './seats';

export interface TeamChoice {
  readonly teamId: string; readonly text: string; readonly enabled: boolean; readonly why: string; readonly edit: PickTeam | LeaveTeam;
}
/** One army picker: the team it brings an army to (null for a participant with no team), its label, the army picked. */
export interface ArmySeat { readonly teamId: string | null; readonly label: string; readonly armyId: string | null }
/** One row of the Players list. `selected` indexes seatChoices; `army` is the seat's army's name, null with none. */
export interface LobbySeat {
  readonly participantId: string; readonly name: string; readonly seat: string; readonly selected: number; readonly army: string | null;
}

export const CREATOR_ONLY = "Only the game's creator changes the configuration.";
export const NO_TEAM = 'No team';
export const NO_PLAYERS = 'No players yet';

const byOrdinal = (game: GameInfo) => [...game.teams].sort((a, b) => a.ordinal - b.ordinal);

/** The seat's army by name; "army" when the rows carry the pick but not its name; null with no army. */
const armyOf = (p: Participant): string | null => p.armyName ?? (p.armyId === undefined ? null : 'army');

/** Where I sit, and what that means once play starts. */
export function seatLine(game: GameInfo, participantId: string | null): string {
  const seats = mine(game, participantId);
  if (seats.length === 0) return 'You are watching this game.';
  const ordinals = byOrdinal(game).filter((t) => seats.some((s) => s.teamId === t.id)).map((t) => `Team ${t.ordinal}`);
  if (ordinals.length === 0) return 'You have no team: join one, or stay without one to watch once play starts.';
  const army = seats.some((s) => s.teamId !== undefined && s.armyId === undefined) ? ' Pick your army below.' : '';
  const both = ordinals.length > 1 ? ' You play every side you hold.' : '';
  const mates = [...new Set(game.participants
    .filter((p) => p.teamId !== undefined && seats.some((s) => s.teamId === p.teamId) && seats.every((s) => s.accountId !== p.accountId))
    .map((p) => p.name))];
  const withMates = mates.length === 0 ? ''
    : ` Teammates: ${mates.join(', ')}.`;
  return `You are on ${ordinals.join(' and ')}.${army}${both}${withMates}`;
}

export function teamChoices(game: GameInfo, participantId: string | null, creator: boolean): TeamChoice[] {
  const account = mine(game, participantId)[0]?.accountId;
  const playing = teamsOf(game, participantId).length > 0;
  return byOrdinal(game).map((t): TeamChoice => {
    const holders = game.participants.filter((p) => p.teamId === t.id && p.accountId !== account).map((p) => p.name);
    if (game.participants.some((p) => p.teamId === t.id && p.accountId === account)) {
      return {
        teamId: t.id, text: `Leave team ${t.ordinal}`, enabled: true,
        why: 'You play this team; leaving it drops the army you brought to it.', edit: { kind: 'leave_team', teamId: t.id },
      };
    }
    const too = playing ? ' You keep your other team and play both sides.' : '';
    if (holders.length === 0 || creator) {
      const held = holders.length === 0 ? '' : `Held by ${holders.join(', ')}; as the creator you may join it.`;
      return { teamId: t.id, text: `Join team ${t.ordinal}`, enabled: true, why: held + too, edit: { kind: 'pick_team', teamId: t.id } };
    }
    return {
      teamId: t.id, text: `Team ${t.ordinal}: ${holders.join(', ')} · ask the creator to add you`, enabled: false,
      why: `${holders.join(', ')} holds this team; the creator adds you from the Players list.`,
      edit: { kind: 'pick_team', teamId: t.id },
    };
  });
}

/** My army picker: "Your army", or one per team I hold ("Your army for Team 2"), or one for my teamless row. */
export function armySeats(game: GameInfo, participantId: string | null): ArmySeat[] {
  const seats = mine(game, participantId);
  const teamed = byOrdinal(game).flatMap((t) => seats.filter((s) => s.teamId === t.id).map((s) => ({ team: t, seat: s })));
  if (teamed.length === 0) return seats.slice(0, 1).map((s) => ({ teamId: null, label: 'Your army', armyId: s.armyId ?? null }));
  return teamed.map((x) => ({
    teamId: x.team.id, label: teamed.length === 1 ? 'Your army' : `Your army for Team ${x.team.ordinal}`, armyId: x.seat.armyId ?? null,
  }));
}

export const pickArmy = (seat: ArmySeat, armyId: string | null): PickArmy => ({
  kind: 'pick_army',
  ...(armyId === null ? {} : { armyId }),
  ...(seat.teamId === null ? {} : { teamId: seat.teamId }),
});

/** The Players list's team choices: No team first, then the teams by ordinal. */
export function seatChoices(game: GameInfo): { teamId: string | null; label: string }[] {
  return [{ teamId: null, label: NO_TEAM }, ...byOrdinal(game).map((t) => ({ teamId: t.id, label: `Team ${t.ordinal}` }))];
}

/** The Players list: every participant, teamless ones included, with the choice that names their team. */
export function players(game: GameInfo): LobbySeat[] {
  const choices = seatChoices(game);
  return game.participants.map((p) => {
    const at = Math.max(0, choices.findIndex((c) => c.teamId === (p.teamId ?? null)));
    return { participantId: p.id, name: p.name, seat: choices[at]!.label, selected: at, army: armyOf(p) };
  });
}

/** A team card's players, one line each with the army that player brings; a new team says it has none. */
export function members(game: GameInfo, teamId: string): string {
  const lines = game.participants.filter((p) => p.teamId === teamId).map((p) => {
    const army = armyOf(p);
    return army === null ? `${p.name} · no army yet` : `${p.name} ⚔ ${army}`;
  });
  return lines.length === 0 ? NO_PLAYERS : lines.join('\n');
}

/** The creator placing a participant: the server's path onto a team another participant holds. */
export function place(game: GameInfo, participantId: string, choice: number): PickTeam {
  const teamId = seatChoices(game)[choice]!.teamId;
  return { kind: 'pick_team', ...(teamId === null ? {} : { teamId }), participantId };
}

/** What a participant who is not the creator waits for, by status; null for the creator. */
export function waitingForCreator(game: GameInfo, creator: boolean): string | null {
  if (creator) return null;
  if (game.status === 'lobby') return 'Waiting for the creator to start setup.';
  if (game.status === 'setup') return 'Anyone may edit the table. Waiting for the creator to end setup.';
  return null;
}

/** LobbyView.EndSetupText: the game server's DropOffSlab rule removes each piece whose centre is off the table (edge inclusive). */
export function endSetupText(draft: TableState | null): string {
  const off = draft === null ? 0 : draft.terrain.filter((t) => {
    const { x, y } = t.position;
    const { width, length } = draft.size;
    return !(x >= -1e-9 && x <= width + 1e-9 && y >= -1e-9 && y <= length + 1e-9);
  }).length;
  if (off === 0) return 'End setup';
  return off === 1 ? 'End setup (1 piece off the table will be removed)' : `End setup (${off} pieces off the table will be removed)`;
}
