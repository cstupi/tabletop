// viewer/src/lib/lobby/lobbyPage.ts
// What the lobby page shows: a card per team (its players and their armies, the one team action I have), the players without a team,
// the game's settings in words, and what a player who is not the creator waits for. Every edit is one LobbyView already builds.

import type { Enforcement, GameInfo, LeaveTeam, PickTeam, RulesOptionCatalog, SchedulerShape } from '../protocol/wire.generated';
import { players, seatChoices, teamChoices } from './lobbyView';
import { mine } from './seats';
import { valueOf } from './rulesOptions';

export const SHAPE_LABEL: Record<SchedulerShape, string> = { 'alternating-activation': 'Alternating activation', 'player-turn': 'Player turn' };
export const ENFORCEMENT_LABEL: Record<Enforcement, string> = { silent: 'Silent', warn: 'Warn', strict: 'Strict' };
/** What each enforcement does, for the control's hint. */
export const ENFORCEMENT_HINT: Record<Enforcement, string> = {
  silent: 'Moves are never checked.',
  warn: 'A move that breaks a rule is shown and asked about first.',
  strict: 'A move that breaks a rule is refused.',
};

export interface CardPlayer {
  readonly participantId: string; readonly name: string; readonly army: string | null; readonly you: boolean;
  /** Index into seatChoices: the creator's placing control. */
  readonly selected: number;
}
export interface TeamAction { readonly label: string; readonly enabled: boolean; readonly why: string; readonly edit: PickTeam | LeaveTeam; readonly leave: boolean }
export interface TeamCard {
  readonly teamId: string; readonly title: string; readonly mine: boolean; readonly players: readonly CardPlayer[];
  readonly action: TeamAction;
}

const youOf = (game: GameInfo, participantId: string | null) => new Set(mine(game, participantId).map((p) => p.id));

/** The teams by ordinal, each with its players and the one team action I have (join, leave, or held by another account). */
export function teamCards(game: GameInfo, participantId: string | null, creator: boolean): TeamCard[] {
  const you = youOf(game, participantId);
  const rows = players(game);
  const choices = teamChoices(game, participantId, creator);
  return [...game.teams].sort((a, b) => a.ordinal - b.ordinal).map((t) => {
    const choice = choices.find((c) => c.teamId === t.id)!;
    const leave = choice.edit.kind === 'leave_team';
    const label = leave ? 'Leave team' : choice.enabled ? 'Join team' : 'Held by another player';
    const onTeam = game.participants.filter((p) => p.teamId === t.id);
    return {
      teamId: t.id, title: `Team ${t.ordinal}`, mine: leave,
      players: onTeam.map((p) => {
        const r = rows.find((x) => x.participantId === p.id)!;
        return { participantId: p.id, name: p.name, army: r.army, you: you.has(p.id), selected: r.selected };
      }),
      action: { label, enabled: choice.enabled, why: choice.why, edit: choice.edit, leave },
    };
  });
}

/** The players with no team, each with the creator's placing choice (0: No team). */
export function unassigned(game: GameInfo, participantId: string | null): CardPlayer[] {
  const you = youOf(game, participantId);
  return players(game).filter((r) => r.selected === 0)
    .map((r) => ({ participantId: r.participantId, name: r.name, army: r.army, you: you.has(r.participantId), selected: 0 }));
}

/** The creator's placing control's options: "No team", "Team 1", … */
export const placeLabels = (game: GameInfo): string[] => seatChoices(game).map((c) => c.label);

/** The creator's name, from the participant rows; "the creator" when they have left. */
export function creatorName(game: GameInfo): string {
  return game.participants.find((p) => p.accountId === game.createdBy)?.name ?? 'the creator';
}

/** What a player who is not the creator waits for; null for the creator, and outside lobby and setup. */
export function waitingFor(game: GameInfo, creator: boolean): string | null {
  if (creator) return null;
  const who = creatorName(game);
  if (game.status === 'lobby') return `Waiting for ${who} to start setup.`;
  if (game.status === 'setup') return `Anyone may edit the table. Waiting for ${who} to end setup.`;
  return null;
}

/** The settings as label and value, for a player who may not change them. */
export function settingsSummary(game: GameInfo, catalog: RulesOptionCatalog): { label: string; value: string }[] {
  return [
    { label: 'Activation', value: SHAPE_LABEL[game.schedulerShape] },
    { label: 'Enforcement', value: ENFORCEMENT_LABEL[game.enforcement] },
    ...catalog.options.map((o) => {
      const v = valueOf(o, game.optionalRules);
      return { label: o.label, value: o.values.find((x) => x.value === v)?.label ?? v };
    }),
  ];
}

/** True when the form differs from the game's configuration: Apply is then worth pressing. */
export function configChanged(
  game: GameInfo, catalog: RulesOptionCatalog, name: string, shape: SchedulerShape, enforcement: Enforcement,
  chosen: Readonly<Record<string, string>>, propertiesHash: string = game.propertiesHash,
): boolean {
  return name.trim() !== game.name || propertiesHash !== game.propertiesHash || shape !== game.schedulerShape || enforcement !== game.enforcement
    || catalog.options.some((o) => valueOf(o, chosen) !== valueOf(o, game.optionalRules));
}
