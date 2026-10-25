// viewer/src/lib/lobby/lobbyEdits.ts
// LobbyEdits.cs: the lobby edits the panels and Home send that no other builder makes.

import type { CreateTeam, EndSetup, PickArmy, PickTeam, StartSetup } from '../protocol/wire.generated';

/** Leave every team and watch: pick_team with no team. */
export const watch = (): PickTeam => ({ kind: 'pick_team' });
export const createTeam = (): CreateTeam => ({ kind: 'create_team' });
export const startSetup = (): StartSetup => ({ kind: 'start_setup' });
export const endSetup = (): EndSetup => ({ kind: 'end_setup' });
/** The army chosen in Create game, picked once the creator is connected: no team named. */
export const armyFromCreate = (armyId: string): PickArmy => ({ kind: 'pick_army', armyId });
