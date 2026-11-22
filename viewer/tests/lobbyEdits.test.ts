import { describe, expect, it } from 'vitest';
import { createTeam, endSetup, startSetup, watch } from '../src/lib/lobby/lobbyEdits';


describe('lobby edits', () => {
  it('watch is pick_team with no team', () => expect(watch()).toEqual({ kind: 'pick_team' }));
  it('the plain edits', () => {
    expect(createTeam()).toEqual({ kind: 'create_team' });
    expect(startSetup()).toEqual({ kind: 'start_setup' });
    expect(endSetup()).toEqual({ kind: 'end_setup' });
  });
});
