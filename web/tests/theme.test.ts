import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_THEME, THEME_KEY, THEMES, applyTheme, loadTheme, resolveTheme, saveTheme } from '../src/lib/theme/theme';

afterEach(() => { vi.unstubAllGlobals(); });

const memory = () => {
  const store = new Map<string, string>();
  vi.stubGlobal('localStorage', { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => store.set(k, v) });
  return store;
};
const blocked = () => vi.stubGlobal('localStorage', { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); } });

describe('theme', () => {
  it('has the client\'s two themes, Sci-fi first and default', () => {
    expect(THEMES.map((t) => t.id)).toEqual(['scifi', 'fantasy']);
    expect(DEFAULT_THEME).toBe('scifi');
  });

  it('resolves an unknown or missing id to Sci-fi', () => {
    expect(resolveTheme('fantasy')).toBe('fantasy');
    expect(resolveTheme('scifi')).toBe('scifi');
    expect(resolveTheme('FANTASY')).toBe('scifi');
    expect(resolveTheme(null)).toBe('scifi');
    expect(resolveTheme(42)).toBe('scifi');
  });

  it('remembers the choice per browser', () => {
    const store = memory();
    expect(loadTheme()).toBe('scifi');
    saveTheme('fantasy');
    expect(store.get(THEME_KEY)).toBe('fantasy');
    expect(loadTheme()).toBe('fantasy');
  });

  it('a blocked store reads the default and drops the write', () => {
    blocked();
    expect(loadTheme()).toBe('scifi');
    expect(() => saveTheme('fantasy')).not.toThrow();
  });

  it('applyTheme sets data-theme on the root', () => {
    const root = { dataset: {} as Record<string, string> };
    applyTheme('fantasy', root);
    expect(root.dataset.theme).toBe('fantasy');
  });

  // static/theme-init.js runs before the app (no flash of the wrong theme) and must read the store the same way.
  it('the init script agrees with loadTheme', () => {
    const script = readFileSync(new URL('../static/theme-init.js', import.meta.url), 'utf8');
    for (const stored of [null, 'fantasy', 'scifi', 'other']) {
      const store = memory(); if (stored !== null) store.set(THEME_KEY, stored);
      const root = { dataset: {} as Record<string, string> };
      new Function('document', 'localStorage', script)({ documentElement: root }, localStorage);
      expect(root.dataset.theme).toBe(loadTheme());
    }
    blocked();
    const root = { dataset: {} as Record<string, string> };
    new Function('document', 'localStorage', script)({ documentElement: root }, localStorage);
    expect(root.dataset.theme).toBe('scifi');
  });
});
