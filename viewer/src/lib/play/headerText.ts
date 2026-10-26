// viewer/src/lib/play/headerText.ts
// The lines client/src/Hud/HeaderBar.cs builds, as text.

import type { Projection } from '../live/apply';
import { REPLAYING, WATCHING, waiting, waitingForPlayers, type OutlineState } from './headerView';
import type { Names } from './names';
import type { TeamWaiting } from './waiting';

/** A team's score in the rulebook's word (p.26 "1 VP for each objective marker"). */
export const scoreText = (score: number): string => `${score} VP`;
/** A team's units in play not yet activated this round, from round 1 while playing. */
export const notActivatedText = (count: number): string => (count === 0 ? 'all activated' : `${count} not activated`);
export const outlineText = (item: { label: string; state: OutlineState }): string => (item.state === 'done' ? `  ✓ ${item.label}` : `  ${item.label}`);

/** HeaderBar's third line: the wait for players, else the replay's or the watcher's line, else the open Asks' waiting line. */
export function waitingLine(
  p: Projection, n: Names, waitingTeams: readonly TeamWaiting[], watching: boolean, mine: readonly string[], replaying = false,
): string | null {
  return waitingForPlayers(waitingTeams) ?? (replaying ? REPLAYING : watching ? WATCHING : waiting(p, n, mine));
}
