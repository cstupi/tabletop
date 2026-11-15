// viewer/src/lib/play/logRows.ts
// What the log panel and DiceTray.cs draw besides logSheet.ts's text. Every word is the combat log's.

import type { GameInfo } from '../protocol/wire.generated';
import { pip } from './combatLog';
import type { Names } from './names';
import { type DieView, type LogEntry, type RollEntry, collapsed, judged, outcome, result, successes, title } from './rollEntry';

export const FILTER_LABELS: readonly string[] = ['All', 'Rolls', 'My units', 'This unit'];   // LogFilter's order

/** CombatLogRows.Band: the team ordinal whose band leads a block's header (its actor's team); 0 for a block with no actor of a known team
 * and any other entry. */
export function bandOf(e: LogEntry, n: Names, game: GameInfo): number {
  if (e.type !== 'block' || e.actor === null) return 0;
  const team = n.teamOfUnit(e.actor);
  return team === null ? 0 : game.teams.find((t) => t.id === team)?.ordinal ?? 0;
}

/** Undone, or past a pending undo. */
export const faded = (e: LogEntry, pendingUndoTo: number | null): boolean => e.undone || (pendingUndoTo !== null && e.seq > pendingUndoTo);

/** CombatLog.UndoHere: a live entry, not an undo marker, for a player (not a watcher): the picked entry Undo to here undoes to. */
export const undoHere = (e: LogEntry, watching: boolean): boolean => !watching && !e.undone && e.event.payload.kind !== 'undone';

export interface TrayLines { title: string; faces: readonly DieView[]; result: string | null; success: boolean; tooltip: string }

/** The latest roll as the dice tray shows it: its title, its faces, its result (or an unjudged roll's outcome), the collapsed line as the
 * tooltip. A roll-off whose dice carry team words reads as that line alone. */
export function trayLines(r: RollEntry | null): TrayLines | null {
  if (r === null) return null;
  const tooltip = collapsed(r, pip);
  if (r.dieLabels !== null && r.dieLabels.length > 0) return { title: tooltip, faces: [], result: null, success: false, tooltip };
  const text = judged(r) ? result(r) : outcome(r);
  return { title: title(r), faces: r.dice, result: text.length > 0 ? text : null, success: judged(r) && successes(r) > 0, tooltip };
}
