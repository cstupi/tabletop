import { describe, expect, it } from 'vitest';
import { armyLabel, armyOptions, armySeats, endSetupText, pickArmy } from '../src/lib/lobby/lobbyView';
import { isCreator } from '../src/lib/lobby/seats';
import type { GameInfo, Participant, TableState } from '../src/lib/protocol/wire.generated';

const T1 = 't1', T2 = 't2';
const row = (id: string, account: string, name: string, extra: Partial<Participant> = {}): Participant => ({ id, accountId: account, name, ...extra });
const game = (participants: Participant[], status: GameInfo['status'] = 'lobby'): GameInfo => ({
  id: 'g', name: 'G', status, createdBy: 'a1', gameRules: 'r', propertiesHash: 'h', schedulerShape: 'alternating-activation',
  enforcement: 'warn', waitsBetweenSteps: false, teams: [{ id: T1, ordinal: 1 }, { id: T2, ordinal: 2 }], participants, optionalRules: {},
});

describe('LobbyView', () => {
  it('the army label is the name and the edition', () => {
    expect(armyLabel({ id: 'a', name: 'Orcs', gameSystem: 'gf', source: 'x', units: 1, models: 1, edition: '3.6' })).toBe('Orcs (3.6)');
  });

  it("army options list the game's edition first and the other disabled with the reason", () => {
    const old = { id: 'o', name: 'Orcs', gameSystem: 'gf', source: 'x', units: 1, models: 1, edition: '3.5' };
    const current = { id: 'e', name: 'Elves', gameSystem: 'gf', source: 'y', units: 2, models: 3, edition: '3.6' };
    const in36 = armyOptions('grimdark-future-3.6', [old, current]);
    expect(in36.map((o) => [o.army.id, o.label, o.disabledReason])).toEqual([
      ['e', 'Elves (3.6)', null], ['o', 'Orcs (3.5)', '3.5 list: re-import it from Army Forge on the website'],
    ]);
    expect(in36.map((o) => o.text)).toEqual(['Elves (3.6)', 'Orcs (3.5) · 3.5 list: re-import it from Army Forge on the website']);
    expect(armyOptions('grimdark-future', [old, current]).map((o) => [o.army.id, o.disabledReason])).toEqual([
      ['o', null], ['e', '3.6 list: Army Forge no longer serves 3.5 lists'],
    ]);
  });

  it('the creator is the account that made the game', () => {
    const g = game([row('p1', 'a1', 'Ann'), row('p2', 'a2', 'Bo', { teamId: T2 })]);
    expect(isCreator(g, 'p1')).toBe(true);
    expect(isCreator(g, 'p2')).toBe(false);
  });

  it('one army picker per team I hold, labelled per team when several', () => {
    const one = game([row('p1', 'a1', 'Ann', { teamId: T1, armyId: 'x' })]);
    expect(armySeats(one, 'p1')).toEqual([{ teamId: T1, label: 'Your army', armyId: 'x' }]);
    const two = game([row('p1', 'a1', 'Ann', { teamId: T1 }), row('p2', 'a1', 'Ann', { teamId: T2 })]);
    expect(armySeats(two, 'p1').map((s) => s.label)).toEqual(['Your army for Team 1', 'Your army for Team 2']);
    expect(armySeats(game([row('p1', 'a1', 'Ann')]), 'p1')).toEqual([{ teamId: null, label: 'Your army', armyId: null }]);
    expect(pickArmy({ teamId: T1, label: '', armyId: null }, 'z')).toEqual({ kind: 'pick_army', armyId: 'z', teamId: T1 });
    expect(pickArmy({ teamId: null, label: '', armyId: 'x' }, null)).toEqual({ kind: 'pick_army' });
  });

  it('endSetupText counts pieces whose centre is off the slab', () => {
    const piece = (x: number, y: number) => ({ id: `${x}`, position: { x, y, z: 0 } });
    const draft = { size: { width: 72, length: 48 }, terrain: [piece(76, 4), piece(72, 48), piece(10, 10)] } as unknown as TableState;
    expect(endSetupText(draft)).toBe('End setup (1 piece off the table will be removed)');
    const three = { ...draft, terrain: [piece(76, 4), piece(-1, 4), piece(10, 49)] } as unknown as TableState;
    expect(endSetupText(three)).toBe('End setup (3 pieces off the table will be removed)');
    expect(endSetupText({ ...draft, terrain: [piece(10, 10)] } as unknown as TableState)).toBe('End setup');
    expect(endSetupText(null)).toBe('End setup');
  });
});
