// viewer/src/lib/replay/replayBarView.ts
// client/src/Core/ReplayBarView.cs: the replay bar's labelled step buttons (back by round, turn, action and step; forward by step, action,
// turn and round), a tick per round's start on the scrubber, and the caption, the position's block header.

import type { LogEntry } from '../play/rollEntry';
import type { GameEvent } from '../protocol/wire.generated';
import type { ReplayUnit } from './replayTimeline';

export interface ReplayStepButton { readonly unit: ReplayUnit; readonly back: boolean; readonly label: string; readonly icon: string; readonly tip: string }
export interface ReplayTick { readonly at: number; readonly round: number }

export const BACK: readonly ReplayStepButton[] = [
  { unit: 'round', back: true, label: 'Round', icon: 'skip-back', tip: 'Back to the start of this round, or the round before' },
  { unit: 'turn', back: true, label: 'Turn', icon: 'skip-back', tip: 'Back to the start of this turn, or the turn before' },
  { unit: 'action', back: true, label: 'Action', icon: 'skip-back', tip: 'Back one action' },
  { unit: 'step', back: true, label: 'Step', icon: 'step-back', tip: 'Back one step: a player\'s input and what the rules did with it' },
];

export const FORWARD: readonly ReplayStepButton[] = [
  { unit: 'step', back: false, label: 'Step', icon: 'step', tip: 'Forward one step: a player\'s input and what the rules did with it' },
  { unit: 'action', back: false, label: 'Action', icon: 'skip', tip: 'Forward one action' },
  { unit: 'turn', back: false, label: 'Turn', icon: 'skip', tip: 'Forward to the next turn' },
  { unit: 'round', back: false, label: 'Round', icon: 'skip', tip: 'Forward to the next round' },
];

/** Each round_started in the log as a tick just after it (the timeline's round stop), by the round it starts. */
export function replayTicks(log: readonly GameEvent[]): ReplayTick[] {
  const ticks: ReplayTick[] = [];
  log.forEach((e, i) => { if (e.payload.kind === 'round_started' && e.payload.round > 0) ticks.push({ at: (i + 1) / log.length, round: e.payload.round }); });
  return ticks;
}

/** The latest live action's header, `R2 · ` read as `Round 2 · `; else the round, or "Before round 1". */
export function replayCaption(entries: readonly LogEntry[], round: number): string {
  for (let i = entries.length - 1; i >= 0; i--) {
    const e = entries[i]!;
    if (e.type !== 'block' || e.undone) continue;
    const m = /^R(\d+)( · .*)$/.exec(e.header);
    return m === null ? e.header : `Round ${m[1]}${m[2]}`;
  }
  return round > 0 ? `Round ${round}` : 'Before round 1';
}
