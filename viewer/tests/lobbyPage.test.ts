import { describe, expect, it } from 'vitest';
import {
  configChanged, creatorName, placeLabels, settingsSummary, teamCards, unassigned, waitingFor,
} from '../src/lib/lobby/lobbyPage';
import { headerOf, WATCHER_ID } from '../src/lib/game/header';
import type { ConnectionState } from '../src/lib/connection/gameConnection';
import type { GameInfo, Participant, RulesOptionCatalog } from '../src/lib/protocol/wire.generated';

const T1 = 't1', T2 = 't2';
const row = (id: string, account: string, name: string, extra: Partial<Participant> = {}): Participant => ({ id, accountId: account, name, ...extra });
const game = (participants: Participant[], status: GameInfo['status'] = 'lobby'): GameInfo => ({
  id: 'g', name: 'Friday', status, createdBy: 'a1', gameRules: 'r', propertiesHash: 'h', schedulerShape: 'alternating-activation',
  enforcement: 'warn', waitsBetweenSteps: false, teams: [{ id: T2, ordinal: 2 }, { id: T1, ordinal: 1 }], participants, optionalRules: {},
});
const catalog: RulesOptionCatalog = {
  options: [{ key: 'sight', label: 'Unit sight blocking', page: 'p.5', default: 'each', values: [{ value: 'each', label: 'Each model blocks' }, { value: 'none', label: 'Nothing blocks' }] }],
  presets: [],
} as unknown as RulesOptionCatalog;

describe('the lobby page', () => {
  it('a card per team by ordinal, with its players, armies and my one action', () => {
    const g = game([row('p1', 'a1', 'Ann', { teamId: T1, armyId: 'x', armyName: 'Battle Brothers' }), row('p2', 'a2', 'Bo', { teamId: T2 }), row('p3', 'a3', 'Cy')]);
    const cards = teamCards(g, 'p1', true);
    expect(cards.map((c) => c.title)).toEqual(['Team 1', 'Team 2']);
    expect(cards[0]).toMatchObject({ mine: true, players: [{ name: 'Ann', army: 'Battle Brothers', you: true, selected: 1 }], action: { label: 'Leave team', leave: true } });
    expect(cards[1]).toMatchObject({ mine: false, players: [{ name: 'Bo', army: null, you: false, selected: 2 }], action: { label: 'Join team', enabled: true } });
    const asBo = teamCards(g, 'p3', false);
    expect(asBo[1]!.action).toMatchObject({ label: 'Held by another player', enabled: false });
    expect(asBo[1]!.action.why).toMatch(/creator adds you/);
  });

  it('the players without a team, and the placing labels', () => {
    const g = game([row('p1', 'a1', 'Ann', { teamId: T1 }), row('p3', 'a3', 'Cy')]);
    expect(unassigned(g, 'p3')).toEqual([{ participantId: 'p3', name: 'Cy', army: null, you: true, selected: 0 }]);
    expect(placeLabels(g)).toEqual(['No team', 'Team 1', 'Team 2']);
  });

  it('names the creator in what the others wait for', () => {
    const g = game([row('p1', 'a1', 'Ann'), row('p2', 'a2', 'Bo')]);
    expect(creatorName(g)).toBe('Ann');
    expect(waitingFor(g, false)).toBe('Waiting for Ann to start setup.');
    expect(waitingFor(g, true)).toBeNull();
    expect(waitingFor(game([row('p2', 'a2', 'Bo')], 'setup'), false)).toBe('Anyone may edit the table. Waiting for the creator to end setup.');
    expect(waitingFor(game([], 'playing'), false)).toBeNull();
  });

  it('the settings in words, and whether the form changed them', () => {
    const g = game([]);
    expect(settingsSummary(g, catalog)).toEqual([
      { label: 'Activation', value: 'Alternating activation' }, { label: 'Enforcement', value: 'Warn' },
      { label: 'Unit sight blocking', value: 'Each model blocks' },
    ]);
    expect(configChanged(g, catalog, ' Friday ', 'alternating-activation', 'warn', {})).toBe(false);
    expect(configChanged(g, catalog, 'Friday', 'player-turn', 'warn', {})).toBe(true);
    expect(configChanged(g, catalog, 'Friday', 'alternating-activation', 'warn', { sight: 'none' })).toBe(true);
    expect(configChanged(g, catalog, 'Saturday', 'alternating-activation', 'warn', {})).toBe(true);
  });
});

describe('the header strip', () => {
  const snap = (g: GameInfo | null, participantId: string | null, phase: ConnectionState['phase'] = 'connected') =>
    ({ phase, game: g, participantId, draft: null } as unknown as ConnectionState);

  it('lobby: name, status, Leave; Start setup is the panel\'s, not the strip\'s', () => {
    const h = headerOf(snap(game([row('p1', 'a1', 'Ann')]), 'p1'), true);
    expect(h).toMatchObject({ title: 'Friday', statusLabel: 'Lobby', connection: null, primary: null, waiting: null, canLeave: true });
  });

  it('setup: End setup for the creator, the wait for everyone else', () => {
    const g = game([row('p1', 'a1', 'Ann'), row('p2', 'a2', 'Bo')], 'setup');
    expect(headerOf(snap(g, 'p1'), true).primary).toEqual({ label: 'End setup', edit: { kind: 'end_setup' } });
    expect(headerOf(snap(g, 'p2'), false)).toMatchObject({ primary: null, waiting: 'Anyone may edit the table. Waiting for Ann to end setup.' });
  });

  it('a watcher cannot leave; the connection shows until connected', () => {
    expect(headerOf(snap(game([]), WATCHER_ID), false)).toMatchObject({ canLeave: false, waiting: null });
    expect(headerOf(null, false)).toMatchObject({ title: 'Game', connection: 'Connecting…', status: null });
    expect(headerOf(snap(game([]), 'p1', 'reconnecting'), false).connection).toBe('Reconnecting…');
    expect(headerOf(snap(game([], 'playing'), 'p1'), false)).toMatchObject({ canLeave: false, statusLabel: 'Playing' });
  });
});
