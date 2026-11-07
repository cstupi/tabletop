// Owner ruling 2026-11-07: UI text carries no rulebook page citations. Comments keep theirs.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { sentence } from '../src/lib/play/contactCheck';

const CITATION = /\bp\.\d/;

function sources(path: string): string[] {
  if (statSync(path).isFile()) return /\.(ts|svelte)$/.test(path) && !path.endsWith('.generated.ts') ? [path] : [];
  return readdirSync(path).flatMap((n) => sources(join(path, n)));
}

/** The file without its comments: HTML and block comments, then each line's `//` tail. */
function uncommented(text: string): string[] {
  return text.replace(/<!--[\s\S]*?-->/g, '').replace(/\/\*[\s\S]*?\*\//g, '').split('\n').map((l) => l.replace(/(^|\s)\/\/.*$/, ''));
}

describe('no page citations', () => {
  it('a contact sentence cites no page', () => {
    for (const w of ['overlaps a base', 'path crosses a model', 'ends in impassable terrain', 'within 1" of Orcs', 'within 3" of Orcs'])
      expect(sentence(w, 'APC 1')).not.toMatch(CITATION);
  });

  it('no source line outside comments cites a page', () => {
    const files = [...sources('src')];
    expect(files.length).toBeGreaterThan(50);
    const found = files.flatMap((f) => uncommented(readFileSync(f, 'utf8')).flatMap((l, i) => (CITATION.test(l) ? [`${f}:${i + 1}`] : [])));
    expect(found).toEqual([]);
  });
});
