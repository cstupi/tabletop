// viewer/src/lib/lobby/lobbyView.ts
// LobbyView (client/src/Core/LobbyView.cs), word for word: the army pickers and words the lobby, setup and the create dialog share.

import type { GameInfo, LeaveTeam, PickArmy, PickTeam, TableState } from '../protocol/wire.generated';
import type { ArmySummary } from '../api/types';
import { mine } from './seats';

/** The waiting panel's Join: what it says, whether it can be pressed, and why not. */
export interface TeamChoice {
  readonly teamId: string; readonly text: string; readonly enabled: boolean; readonly why: string; readonly edit: PickTeam | LeaveTeam;
}

/** One army picker: the team it brings an army to (null for a participant with no team), its label, the army picked. */
export interface ArmySeat { readonly teamId: string | null; readonly label: string; readonly armyId: string | null }
/** An army in a picker: its name and the edition it was imported under. */
export const armyLabel = (a: ArmySummary): string => `${a.name} (${a.edition})`;

/** One army in a picker. An army of another edition than the game's is listed disabled, with the reason; the game server refuses it either way. */
export interface ArmyOption {
  readonly army: ArmySummary; readonly label: string; readonly disabledReason: string | null;
  /** The picker's text: the label, and the reason when disabled. */
  readonly text: string;
}
export const REIMPORT_REASON = '3.5 list: re-import it from Army Forge on the website';
export const NO_LONGER_SERVED_REASON = '3.6 list: Army Forge no longer serves 3.5 lists';
/** RuleSets.EditionOf (libs/Tabletop.Content/RuleSets.cs): the army edition a game on each shipped rule set takes. */
export const EDITION_OF: Readonly<Record<string, string>> = { 'grimdark-future': '3.5', 'grimdark-future-3.6': '3.6' };

/** A picker's armies for a game on `gameRules`: the game's edition first, the rest disabled with the reason. A rule set this build does not
 *  ship disables nothing. */
export function armyOptions(gameRules: string, armies: readonly ArmySummary[]): ArmyOption[] {
  const edition = EDITION_OF[gameRules] ?? null;
  const options = armies.map((army): ArmyOption => {
    const label = armyLabel(army);
    const disabledReason = edition === null || army.edition === edition ? null : army.edition === '3.5' ? REIMPORT_REASON : NO_LONGER_SERVED_REASON;
    return { army, label, disabledReason, text: disabledReason === null ? label : `${label} · ${disabledReason}` };
  });
  return [...options.filter((o) => o.disabledReason === null), ...options.filter((o) => o.disabledReason !== null)];
}

export const CREATOR_ONLY = "Only the game's creator changes the configuration.";

const byOrdinal = (game: GameInfo) => [...game.teams].sort((a, b) => a.ordinal - b.ordinal);

/** My army picker: "Your army", or one per team I hold ("Your army for Team 2"), or one for my teamless row. */
export function armySeats(game: GameInfo, participantId: string | null): ArmySeat[] {
  const seats = mine(game, participantId);
  const teamed = byOrdinal(game).flatMap((t) => seats.filter((s) => s.teamId === t.id).map((s) => ({ team: t, seat: s })));
  if (teamed.length === 0) return seats.slice(0, 1).map((s) => ({ teamId: null, label: 'Your army', armyId: s.armyId ?? null }));
  return teamed.map((x) => ({
    teamId: x.team.id, label: teamed.length === 1 ? 'Your army' : `Your army for Team ${x.team.ordinal}`, armyId: x.seat.armyId ?? null,
  }));
}

export const pickArmy = (seat: ArmySeat, armyId: string | null): PickArmy => ({
  kind: 'pick_army',
  ...(armyId === null ? {} : { armyId }),
  ...(seat.teamId === null ? {} : { teamId: seat.teamId }),
});

/** LobbyView.EndSetupText: the game server's DropOffSlab rule removes each piece whose centre is off the table (edge inclusive). */
export function endSetupText(draft: TableState | null): string {
  const off = draft === null ? 0 : draft.terrain.filter((t) => {
    const { x, y } = t.position;
    const { width, length } = draft.size;
    return !(x >= -1e-9 && x <= width + 1e-9 && y >= -1e-9 && y <= length + 1e-9);
  }).length;
  if (off === 0) return 'End setup';
  return off === 1 ? 'End setup (1 piece off the table will be removed)' : `End setup (${off} pieces off the table will be removed)`;
}
