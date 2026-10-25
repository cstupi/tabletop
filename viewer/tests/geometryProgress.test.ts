// The Geometry download's progress: a store every page that waits on loadGeometry() reads.
import { describe, expect, it } from 'vitest';
import { DownloadProgress, type DownloadState } from '../src/lib/geometry/progress';
import { geometryDownload } from '../src/lib/geometry';
import { loadTestGeometry } from './geometry';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

function seen(p: { subscribe(fn: (s: DownloadState) => void): () => void }): DownloadState[] {
  const states: DownloadState[] = [];
  p.subscribe((s) => states.push(s));
  return states;
}

describe('DownloadProgress', () => {
  it('starts unfinished with nothing counted', () => {
    expect(seen(new DownloadProgress())).toEqual([{ done: 0, total: 0, finished: false }]);
  });

  it('reports counts, then finishes', () => {
    const p = new DownloadProgress();
    const states = seen(p);
    p.report(1, 4);
    p.report(3, 9);
    p.finish();
    expect(states).toEqual([
      { done: 0, total: 0, finished: false },
      { done: 1, total: 4, finished: false },
      { done: 3, total: 9, finished: false },
      { done: 3, total: 9, finished: true },
    ]);
  });

  it('a late subscriber gets the current state; unsubscribing stops it', () => {
    const p = new DownloadProgress();
    p.report(2, 3);
    const states: DownloadState[] = [];
    const stop = p.subscribe((s) => states.push(s));
    stop();
    p.finish();
    expect(states).toEqual([{ done: 2, total: 3, finished: false }]);
  });

  it('a restart clears the finish', () => {
    const p = new DownloadProgress();
    p.finish();
    p.restart();
    expect(seen(p)).toEqual([{ done: 0, total: 0, finished: false }]);
  });
});

describe('geometryDownload', () => {
  it('is finished once the runtime has loaded, every resource counted', async () => {
    await loadTestGeometry();
    const base = pathToFileURL(resolve('public/geometry/_framework')).href + '/';
    const [state] = seen(geometryDownload(base));
    expect(state!.finished).toBe(true);
    expect(state!.done).toBe(state!.total);
  });

  it('one store per base', () => {
    expect(geometryDownload('/a/')).toBe(geometryDownload('/a/'));
    expect(geometryDownload('/a/')).not.toBe(geometryDownload('/b/'));
  });
});
