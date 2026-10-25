// SheetFocusTests' table: two blue units for participant A (team A), one red for B.
import { Applier, type Projection } from '../src/lib/live/apply';
import type { GameInfo } from '../src/lib/protocol/wire.generated';
import { boardOf, emptyTable, ev, gameOf, markerOf, newId, slotOf, unitOf } from './play';

export const TEAM_A = newId(), TEAM_B = newId(), PA = newId(), PB = newId();
const unitWith = (name: string) => unitOf(name, [slotOf({ name: 'Trooper' })]);
export const blue = unitWith('Blue'), blue2 = unitWith('Blue Two'), red = unitWith('Red');
export const focusGame: GameInfo = gameOf({ teams: [{ id: TEAM_A, ordinal: 1 }, { id: TEAM_B, ordinal: 2 }] }, [
  { id: PA, accountId: newId(), name: 'Clay', teamId: TEAM_A, armyId: newId(), armyName: 'Blue' },
  { id: PB, accountId: newId(), name: 'Sam', teamId: TEAM_B, armyId: newId(), armyName: 'Red' },
]);
export const marker = markerOf('smoke');
export const terrain = { id: newId(), name: 'Ruin' } as never;
const table = { ...emptyTable(), markers: [marker], terrain: [terrain] };
export const focusBoard = boardOf([{ participantId: PA, units: [blue, blue2] }, { participantId: PB, units: [red] }], table);

export function activating(actor: string | null): Projection {
  const a = new Applier(focusBoard, focusGame);
  if (actor !== null) a.apply(ev(1, { kind: 'sequence_started', frameId: newId(), sequence: 'advance', context: { actorUnitId: actor, targetUnitIds: [], weaponIds: [], action: 'Advance' } }));
  return a.projection;
}

