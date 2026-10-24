// Playback never downloads the .NET runtime: only loadGeometry() fetches it, and no playback module names lib/geometry.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

function sources(path: string): string[] {
  if (statSync(path).isFile()) return /\.(ts|svelte)$/.test(path) ? [path] : [];
  return readdirSync(path).flatMap((n) => sources(join(path, n)));
}

describe('lazy loading', () => {
  it('no playback module imports lib/geometry', () => {
    const roots = ['feed', 'live', 'board', 'timeline', 'recording', 'asks', 'hosted', 'suite', 'components'].map((d) => join('src/lib', d));
    const files = [...roots.flatMap(sources), 'src/App.svelte', 'src/main.ts'];
    expect(files.length).toBeGreaterThan(10);
    const importing = files.filter((f) => /(?:from|import)\s*\(?\s*['"][^'"]*geometry[^'"]*['"]/.test(readFileSync(f, 'utf8')));
    expect(importing).toEqual([]);
  });
});
