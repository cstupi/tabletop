// viewer/src/lib/games/homeView.ts
// HomeView (client/src/Core/HomeView.cs): the games page's sections, each game's edition, players, state line, primary action and ▸ menu.

import type { GameSummary } from '../live/games';
import { ago } from './present';

export type HomeAction = 'play' | 'continue' | 'open' | 'replay' | 'join' | 'end' | 'delete' | 'leave';

export interface HomeCard {
  readonly game: GameSummary;
  readonly edition: string;
  readonly players: readonly string[];
  readonly morePlayers: number;
  readonly state: string;
  readonly stateWarns: boolean;
  readonly primary: HomeAction;
  readonly menu: readonly HomeAction[];
}

export interface HomeLists { readonly yours: readonly HomeCard[]; readonly open: readonly HomeCard[]; readonly ended: readonly HomeCard[] }

export const PLAYERS_SHOWN = 3;

export const ACTION_LABEL: Record<HomeAction, string> = {
  play: 'Play', continue: 'Continue', open: 'Open', replay: 'Replay', join: 'Join', end: 'End game', delete: 'Delete game', leave: 'Leave',
};

/** My games not ended (cards), the others not ended (rows with Join), my ended games (rows with Replay; empty unless asked for). Newest first. */
export function homeListsOf(games: readonly GameSummary[], me: string | null, search: string, endedToo: boolean, now: Date = new Date()): HomeLists {
  const needle = search.trim().toLowerCase();
  const shown = games.filter((g) => needle === '' || g.name.toLowerCase().includes(needle))
    .slice().sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id));
  const cards = (keep: (g: GameSummary) => boolean) => shown.filter(keep).map((g) => cardOf(g, me, now));
  return {
    yours: cards((g) => g.joined && g.status !== 'ended'),
    open: cards((g) => !g.joined && g.status !== 'ended'),
    ended: endedToo ? cards((g) => g.joined && g.status === 'ended') : [],
  };
}

export function cardOf(g: GameSummary, me: string | null, now: Date = new Date()): HomeCard {
  const names = g.participantNames ?? [];
  const [state, stateWarns] = stateOf(g, me, now);
  return {
    game: g, edition: editionOf(g.ruleSet), players: names.slice(0, PLAYERS_SHOWN), morePlayers: Math.max(0, names.length - PLAYERS_SHOWN),
    state, stateWarns, primary: primaryOf(g), menu: menuOf(g, me),
  };
}

function primaryOf(g: GameSummary): HomeAction {
  if (!g.joined) return 'join';
  switch (g.status) {
    case 'lobby': return 'open';
    case 'setup': return 'continue';
    case 'playing': return 'play';
    default: return 'replay';
  }
}

/** Management takes End and Delete from the creator alone; End only before the end. Leave: any game of mine not ended. */
function menuOf(g: GameSummary, me: string | null): HomeAction[] {
  if (!g.joined) return [];
  const creator = me !== null && g.createdBy === me;
  const menu: HomeAction[] = [];
  if (g.status === 'playing') menu.push('replay');
  if (creator && g.status !== 'ended') menu.push('end');
  if (creator) menu.push('delete');
  if (g.status !== 'ended') menu.push('leave');
  return menu;
}

function stateOf(g: GameSummary, me: string | null, now: Date): [string, boolean] {
  const p = g.progress ?? null;
  if (g.status === 'lobby' && (g.teams ?? 0) > 0) return [`${g.teamsReady ?? 0} of ${g.teams} teams ready`, false];
  if (g.status === 'setup') return ['Laying the table', false];
  if (g.status === 'playing' && p !== null) {
    if (me !== null && p.waitingOn.includes(me)) return ['Your move', true];
    const round = p.round > 0 ? `Round ${p.round}` : 'Deployment';
    return [p.actingTeam != null ? `${round} · Team ${p.actingTeam}'s turn` : round, false];
  }
  if (g.status === 'ended' && p?.outcome != null) {
    const w = p.outcome;
    return [w.length === 0 ? 'No result' : w.length === 1 ? `Team ${w[0]} wins` : 'Draw', false];
  }
  return [ago(g.createdAt, now), false];
}

/** The version in a rule set's name ("Grimdark Future v3.6.0" → "3.6.0"); the name when it carries none. */
export function editionOf(ruleSet: string | null | undefined): string {
  if (ruleSet == null) return '';
  return /\d+(\.\d+)+/.exec(ruleSet)?.[0] ?? ruleSet;
}
