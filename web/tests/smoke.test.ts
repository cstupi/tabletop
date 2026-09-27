import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

describe('project', () => {
  it('is a static SPA with ssr off', () => {
    expect(readFileSync('src/routes/+layout.ts', 'utf8')).toContain('export const ssr = false');
    expect(readFileSync('svelte.config.js', 'utf8')).toContain("fallback: 'index.html'");
  });
});
