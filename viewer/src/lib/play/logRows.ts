// viewer/src/lib/play/logRows.ts
// The row text CombatLogPanel.cs and DiceTray.cs draw. Every word is the combat log's.

import { blockChain, pip } from './combatLog';
import { type DieView, type LogEntry, type RollEntry, collapsed, judged, outcome, result, successes, title } from './rollEntry';

export const FILTER_LABELS: readonly string[] = ['All', 'Rolls', 'My units', 'This unit'];   // LogFilter's order

/** `▾`/`▸` + header (+ `  ·  {chain}` folded), a roll collapsed, a line. A roll-off shows its team words between the faces: its collapsed line. */
export function rowText(e: LogEntry, open: boolean): string {
  const mark = open ? '▾' : '▸';
  switch (e.type) {
    case 'block': return `${mark} ${e.header}${open || blockChain(e).length === 0 ? '' : `  ·  ${blockChain(e)}`}`;
    case 'roll': return `${mark} ${collapsed(e, pip)}`;
    case 'line': return e.text;
  }
}

/** Undone, or past a pending undo. */
export const faded = (e: LogEntry, pendingUndoTo: number | null): boolean => e.undone || (pendingUndoTo !== null && e.seq > pendingUndoTo);

/** A Button has no strike style: U+0336 after each character. */
export const strike = (s: string): string => Array.from(s).map((c) => `${c}̶`).join('');

/** A live non-roll line or block, not an undo marker, for a player (not a watcher). */
export const undoHere = (e: LogEntry, watching: boolean): boolean => !watching && e.type !== 'roll' && !e.undone && e.event.payload.kind !== 'undone';

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
