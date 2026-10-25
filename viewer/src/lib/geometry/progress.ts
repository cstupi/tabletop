// viewer/src/lib/geometry/progress.ts
// The Geometry runtime's download as a store (Svelte's subscribe contract): resources fetched of those requested, as dotnet.js's
// onDownloadResourceProgress reports them, and whether the load has settled.

export interface DownloadState { readonly done: number; readonly total: number; readonly finished: boolean }

const START: DownloadState = { done: 0, total: 0, finished: false };

export class DownloadProgress {
  private state: DownloadState = START;
  private readonly listeners = new Set<(s: DownloadState) => void>();

  subscribe(fn: (s: DownloadState) => void): () => void {
    this.listeners.add(fn);
    fn(this.state);
    return () => { this.listeners.delete(fn); };
  }

  report(done: number, total: number): void { this.set({ done, total, finished: false }); }
  /** Loaded or failed: either way nothing is downloading. */
  finish(): void { this.set({ ...this.state, finished: true }); }
  restart(): void { this.set(START); }

  private set(s: DownloadState): void {
    this.state = s;
    for (const fn of this.listeners) fn(s);
  }
}
