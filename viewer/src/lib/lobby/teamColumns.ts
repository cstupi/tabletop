// viewer/src/lib/lobby/teamColumns.ts
// TeamColumns (client/src/Core/TeamColumns.cs), members camelCased: the lobby's and setup's team columns (spec
// 2026-11-07-lobby-and-setup-redesign-design.md): one column per team, then Watching. A column's Join adds a seat for my account (never moves one); a row's ▸ menu and drag move it,
// Watch, or Leave a team while my account holds several (LeaveTeam).
// Editing is Godot's alone: the browser keeps no presence.

import type { GameInfo, LeaveTeam, Participant, PickTeam } from '../protocol/wire.generated';
import { EDITION_OF, armySeats, type ArmySeat } from './lobbyView';
import { mine } from './seats';
import { watch } from './lobbyEdits';

export type ColumnState = 'ready' | 'needsPlayer' | 'needsArmy' | 'watching';

export interface ColumnMove { readonly teamId: string | null; readonly text: string; readonly edit: PickTeam | LeaveTeam }
export interface ColumnMember {
  readonly participantId: string; readonly name: string; readonly army: string | null; readonly noArmyWarns: boolean;
  readonly you: boolean; readonly myArmy: ArmySeat | null; readonly moves: readonly ColumnMove[];
}
export interface ColumnJoin { readonly text: string; readonly edit: PickTeam | null; readonly why: string | null }
export interface TeamColumn {
  readonly teamId: string | null; readonly ordinal: number; readonly title: string; readonly state: ColumnState; readonly stateText: string;
  readonly members: readonly ColumnMember[]; readonly join: ColumnJoin | null;
}
export interface ToDoLine { readonly text: string; readonly missing: boolean }

export const WATCHING = 'Watching', WATCHING_STATE = 'Watches once play starts', JOIN = 'Join', WATCH = 'Watch',
  JOIN_COMPACT = '+', NO_ARMY = 'no army yet', ADD_TEAM = '+ Add team', ADD_TEAM_COMPACT = '+ Team', YOU = '(you)',
  MOVE_MENU = 'Move to…', LEAVE_TEAM = 'Leave team', LEAVE_COMPACT = 'Leave';

/** My row's Leave team tooltip (TeamColumns.LeaveTip): the seat goes (Leave Team N, while I hold several), or my only seat watches. */
export const leaveTip = (leave: ColumnMove, team: string): string =>
  leave.edit.kind === 'leave_team' ? `Give up ${team}; your other teams stay` : `Leave ${team} and watch`;

export function stateText(s: ColumnState): string {
  return s === 'ready' ? 'Ready' : s === 'needsPlayer' ? 'Needs a player' : s === 'needsArmy' ? 'Needs an army' : WATCHING_STATE;
}

const byOrdinal = (game: GameInfo) => [...game.teams].sort((a, b) => a.ordinal - b.ordinal);
const pickTeam = (teamId: string | null, participantId?: string): PickTeam =>
  ({ kind: 'pick_team', ...(teamId === null ? {} : { teamId }), ...(participantId === undefined ? {} : { participantId }) });

/** A team's state from the lobby rows: WaitingForPlayers' rule. */
export function stateOf(game: GameInfo, teamId: string): ColumnState {
  const seats = game.participants.filter((p) => p.teamId === teamId);
  return seats.length === 0 ? 'needsPlayer' : seats.some((p) => p.armyId !== undefined) ? 'ready' : 'needsArmy';
}

/** The creator's name from the participant rows; "the creator" once they have left. */
export function creatorName(game: GameInfo): string {
  return game.participants.find((p) => p.accountId === game.createdBy)?.name ?? 'the creator';
}

/** The army line: its name and the game's edition, null with no army. */
export function armyText(game: GameInfo, p: Participant): string | null {
  const name = p.armyName ?? (p.armyId === undefined ? null : 'army');
  if (name === null) return null;
  const edition = EDITION_OF[game.gameRules];
  return edition === undefined ? name : `${name} · ${edition}`;
}

/** The teams by ordinal, then Watching; `compact` (setup's sidebar) leaves Watching out while no one watches. */
export function columnsOf(game: GameInfo, participantId: string | null, creator: boolean, compact = false): TeamColumn[] {
  const rows = mine(game, participantId);
  const account = rows[0]?.accountId;
  const held = rows.filter((p) => p.teamId !== undefined);
  const columns: TeamColumn[] = byOrdinal(game).map((t) => {
    const state = stateOf(game, t.id);
    const members = game.participants.filter((p) => p.teamId === t.id).map((p) => member(game, p, participantId, creator, state === 'needsArmy'));
    let join: ColumnJoin | null = null;
    // takes my teamless row, or adds a seat; never moves one. A team other accounts hold is joined like an empty one.
    if (account !== undefined && rows.every((p) => p.teamId !== t.id)) join = { text: JOIN, edit: pickTeam(t.id), why: null };
    return { teamId: t.id, ordinal: t.ordinal, title: `Team ${t.ordinal}`, state, stateText: stateText(state), members, join };
  });
  const watching = game.participants.filter((p) => p.teamId === undefined).map((p) => member(game, p, participantId, creator, false));
  if (!compact || watching.length > 0) {
    columns.push({
      teamId: null, ordinal: 0, title: WATCHING, state: 'watching', stateText: WATCHING_STATE, members: watching,
      join: held.length > 0 ? { text: WATCH, edit: watch(), why: null } : null,
    });
  }
  return columns;
}

function member(game: GameInfo, p: Participant, participantId: string | null, creator: boolean, teamNeedsArmy: boolean): ColumnMember {
  const you = mine(game, participantId).some((s) => s.id === p.id);
  const myArmy = you ? armySeats(game, participantId).find((a) => a.teamId === (p.teamId ?? null)) ?? null : null;
  return {
    participantId: p.id, name: p.name, army: armyText(game, p), noArmyWarns: teamNeedsArmy, you, myArmy, moves: moves(game, participantId, creator, p.id),
  };
}

/** Where this viewer may send a row: the creator any row, anyone else their own; never a team the row's account already holds (a team
 *  other accounts hold is open to it). Last, for a row on a team: "Leave Team N" (LeaveTeam) on my row while my account holds several
 *  teams, else "Watch". */
export function moves(game: GameInfo, participantId: string | null, creator: boolean, rowId: string): ColumnMove[] {
  const row = game.participants.find((p) => p.id === rowId);
  const rows = mine(game, participantId);
  if (row === undefined || rows.length === 0 || (!creator && rows.every((s) => s.id !== rowId))) return [];
  const out: ColumnMove[] = [];
  for (const t of byOrdinal(game)) {
    if (row.teamId === t.id || game.participants.some((p) => p.teamId === t.id && p.accountId === row.accountId)) continue;
    out.push({ teamId: t.id, text: `Move to Team ${t.ordinal}`, edit: pickTeam(t.id, rowId) });
  }
  if (row.teamId !== undefined) {
    const at = row.teamId;
    const several = row.accountId === rows[0]!.accountId && rows.filter((s) => s.teamId !== undefined).length > 1;
    const ordinal = game.teams.find((t) => t.id === at)?.ordinal;
    out.push(several ? { teamId: null, text: `Leave Team ${ordinal}`, edit: { kind: 'leave_team', teamId: at } } : { teamId: null, text: WATCH, edit: pickTeam(null, rowId) });
  }
  return out;
}

/** A row dropped on a column (null: Watching): the move it makes, or null. */
export function moveTo(game: GameInfo, participantId: string | null, creator: boolean, rowId: string, teamId: string | null): PickTeam | LeaveTeam | null {
  return moves(game, participantId, creator, rowId).find((m) => m.teamId === teamId)?.edit ?? null;
}

/** The Still to do lines, from the lobby rows alone; empty outside lobby and setup. */
export function stillToDo(game: GameInfo, creator: boolean): ToDoLine[] {
  if (game.status !== 'lobby' && game.status !== 'setup') return [];
  const who = creatorName(game);
  const lines: ToDoLine[] = [];
  if (game.teams.length === 0) lines.push({ text: creator ? 'Add a team for each side' : `${who} adds the teams`, missing: true });
  const teams = byOrdinal(game).map((t) => ({ ordinal: t.ordinal, state: stateOf(game, t.id) }));
  for (const t of teams) if (t.state === 'needsPlayer') lines.push({ text: `Team ${t.ordinal} needs a player`, missing: true });
  for (const t of teams) if (t.state === 'needsArmy') lines.push({ text: `Team ${t.ordinal} needs an army`, missing: true });
  if (game.status === 'lobby') lines.push({ text: creator ? 'You start setup' : `${who} starts setup`, missing: false });
  else {
    lines.push({ text: 'Anyone may edit the table', missing: false });
    lines.push({ text: creator ? 'You end setup' : `${who} ends setup`, missing: false });
  }
  return lines;
}

/** A column folded to one line for setup's head (TeamColumns.Line): "Name · Army", "Name · no army yet" while the team has none, else the name. */
export const columnLine = (column: TeamColumn): string => column.members.length === 0 ? 'No players yet'
  : column.members.map((m) => m.army !== null ? `${m.name} · ${m.army}` : m.noArmyWarns ? `${m.name} · ${NO_ARMY}` : m.name).join(', ');
