// viewer/src/lib/lobby/lobbyEdits.ts
// LobbyEdits.cs: the lobby edits the panels send that no other builder makes.

import type { CreateTeam, EndSetup, PickTeam, StartSetup } from '../protocol/wire.generated';

/** Leave every team and watch: pick_team with no team. */
export const watch = (): PickTeam => ({ kind: 'pick_team' });
export const createTeam = (): CreateTeam => ({ kind: 'create_team' });
export const startSetup = (): StartSetup => ({ kind: 'start_setup' });
export const endSetup = (): EndSetup => ({ kind: 'end_setup' });
