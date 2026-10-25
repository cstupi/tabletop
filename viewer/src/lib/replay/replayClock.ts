// viewer/src/lib/replay/replayClock.ts
// client/src/Core/ReplayTimeline.cs (ReplayClock): replay playback, one step every STEP_SECONDS at 1×, faster or slower by speed.

export const SPEEDS: readonly number[] = [0.5, 1, 2, 4, 8];
export const STEP_SECONDS = 1.5;

export class ReplayClock {
  #since = 0;
  #playing = false;
  speed = 1;

  get playing(): boolean { return this.#playing; }

  play(): void { this.#playing = true; this.#since = 0; }
  pause(): void { this.#playing = false; }

  /** Advances the clock by `delta` seconds; true when the next step is due. */
  tick(delta: number): boolean {
    if (!this.#playing) return false;
    this.#since += delta;
    if (this.#since < STEP_SECONDS / this.speed) return false;
    this.#since = 0;
    return true;
  }
}
