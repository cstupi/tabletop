import { describe, expect, it } from 'vitest';
import { armyFromCreate, createTeam, endSetup, startSetup, watch } from '../src/lib/lobby/lobbyEdits';

const A = '019a0000-0000-7000-8000-00000000000a';

describe('lobby edits', () => {
  it('watch is pick_team with no team', () => expect(watch()).toEqual({ kind: 'pick_team' }));
  it('the create game army names no team', () => expect(armyFromCreate(A)).toEqual({ kind: 'pick_army', armyId: A }));
  it('the plain edits', () => {
    expect(createTeam()).toEqual({ kind: 'create_team' });
    expect(startSetup()).toEqual({ kind: 'start_setup' });
    expect(endSetup()).toEqual({ kind: 'end_setup' });
  });
});
