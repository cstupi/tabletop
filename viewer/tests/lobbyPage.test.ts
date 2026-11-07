import { describe, expect, it } from 'vitest';
import { configChanged, settingsSummary } from '../src/lib/lobby/lobbyPage';
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
  options: [{ key: 'sight', label: 'Unit sight blocking', default: 'each', values: [{ value: 'each', label: 'Each model blocks' }, { value: 'none', label: 'Nothing blocks' }] }],
  presets: [],
} as unknown as RulesOptionCatalog;

describe('the lobby page', () => {
  it('the settings in words, and whether the form changed them', () => {
    const g = game([]);
    expect(settingsSummary(g, catalog)).toEqual([
      { label: 'Activation', value: 'Alternating activation' }, { label: 'Enforcement', value: 'Warn' },
      { label: 'Unit sight blocking', value: 'Each model blocks' },
    ]);
    expect(configChanged(g, catalog, ' Friday ', 'alternating-activation', 'warn', {})).toBe(false);
    expect(configChanged(g, catalog, 'Friday', 'player-turn', 'warn', {})).toBe(true);
    expect(configChanged(g, catalog, 'Friday', 'alternating-activation', 'warn', { sight: 'none' })).toBe(true);
    expect(configChanged(g, catalog, 'Friday', 'alternating-activation', 'warn', {}, `${g.propertiesHash}x`)).toBe(true);
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
    expect(headerOf(snap(g, 'p2'), false)).toMatchObject({ primary: null, waiting: 'Ann ends setup' });
  });

  it('a watcher cannot leave; the connection shows until connected', () => {
    expect(headerOf(snap(game([]), WATCHER_ID), false)).toMatchObject({ canLeave: false, waiting: null });
    expect(headerOf(null, false)).toMatchObject({ title: 'Game', connection: 'Connecting…', status: null });
    expect(headerOf(snap(game([]), 'p1', 'reconnecting'), false).connection).toBe('Reconnecting…');
    expect(headerOf(snap(game([], 'playing'), 'p1'), false)).toMatchObject({ canLeave: false, statusLabel: 'Playing' });
  });
});
