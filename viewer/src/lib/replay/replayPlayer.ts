// viewer/src/lib/replay/replayPlayer.ts
// client/src/Hud/ReplayBar.cs without Godot: the replay bar's buttons, Play/Pause and the per-frame advance over a ReplayTimeline and a
// ReplayClock. The page calls `advance(seconds)` from its own frame loop and redraws on `subscribe`.

import { ReplayClock } from './replayClock';
import type { ReplayTimeline, ReplayUnit } from './replayTimeline';

export interface ReplayBarModel {
  readonly position: number; readonly count: number; readonly playing: boolean; readonly speed: number; readonly caption: string;
}

export class ReplayPlayer {
  readonly #timeline: ReplayTimeline;
  readonly #ended: () => boolean;
  readonly #clock = new ReplayClock();
  readonly #listeners = new Set<() => void>();

  /** `ended`: the game has ended, so playing stops at the end (and Play starts again from the start) rather than waiting for events. */
  constructor(timeline: ReplayTimeline, ended: () => boolean) {
    this.#timeline = timeline;
    this.#ended = ended;
  }

  get timeline(): ReplayTimeline { return this.#timeline; }
  get playing(): boolean { return this.#clock.playing; }
  get speed(): number { return this.#clock.speed; }
  set speed(value: number) { this.#clock.speed = value; this.#changed(); }

  get bar(): ReplayBarModel {
    const t = this.#timeline;
    return { position: t.position, count: t.count, playing: this.#clock.playing, speed: this.#clock.speed, caption: t.caption };
  }

  subscribe(listener: () => void): () => void {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  /** A step button: pauses, then jumps to the unit's next or previous stop. */
  step(unit: ReplayUnit, back: boolean): void {
    this.#clock.pause();
    this.#timeline.seek(back ? this.#timeline.previous(unit) : this.#timeline.next(unit));
    this.#changed();
  }

  /** The scrubber: jumps there; playing carries on from it. */
  seek(position: number): void {
    if (position === this.#timeline.position) return;
    this.#timeline.seek(position);
    this.#changed();
  }

  togglePlay(): void {
    if (this.#clock.playing) this.#clock.pause();
    else {
      if (this.#timeline.atEnd && this.#ended()) this.#timeline.seek(0);   // play again from the start
      this.#clock.play();
    }
    this.#changed();
  }

  pause(): void {
    if (!this.#clock.playing) return;
    this.#clock.pause();
    this.#changed();
  }

  /** The frame loop: `seconds` since the last call. Steps forward one step when due; at the end of an ended game, pauses. */
  advance(seconds: number): void {
    if (!this.#clock.tick(seconds)) return;
    const t = this.#timeline;
    if (t.atEnd) {
      if (this.#ended()) { this.#clock.pause(); this.#changed(); }
      return;
    }
    t.seek(t.next('step'));
    this.#changed();
  }

  /** The log grew or the game record changed: redraw the bar (the position stays where it is). */
  refresh(): void { this.#changed(); }

  #changed(): void { for (const listener of [...this.#listeners]) listener(); }
}
