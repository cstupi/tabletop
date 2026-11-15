import { describe, expect, it } from 'vitest';
import { DEFAULT_SETTINGS, loadSettings, saveSettings, type StorageLike } from '../src/lib/play/settings';

const memory = (initial: Record<string, string> = {}): StorageLike & { data: Record<string, string> } => ({
  data: { ...initial }, getItem(k) { return this.data[k] ?? null; }, setItem(k, v) { this.data[k] = v; },
});
const throwing: StorageLike = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };

describe('play settings', () => {
  it('defaults when storage is empty, corrupt or throws', () => {
    expect(loadSettings(memory())).toEqual(DEFAULT_SETTINGS);
    expect(loadSettings(memory({ 'tabletop.play': '{nope' }))).toEqual(DEFAULT_SETTINGS);
    expect(loadSettings(memory({ 'tabletop.play': '[1]' }))).toEqual(DEFAULT_SETTINGS);
    expect(loadSettings(memory({ 'tabletop.play': '{"logFilter":"x","rangeRings":3}' }))).toEqual(DEFAULT_SETTINGS);
    expect(loadSettings(throwing)).toEqual(DEFAULT_SETTINGS);
    expect(loadSettings(null)).toEqual(DEFAULT_SETTINGS);
    expect(() => saveSettings(throwing, DEFAULT_SETTINGS)).not.toThrow();
    expect(() => saveSettings(null, DEFAULT_SETTINGS)).not.toThrow();
  });

  it('a saved setting reads back', () => {
    const storage = memory();
    const saved = { rangeRings: false, logOpen: true, logFilter: 'mine' as const, logDetailed: true };
    saveSettings(storage, saved);
    expect(loadSettings(storage)).toEqual(saved);
  });

  it('range rings are on by default, and an earlier save\'s off is not kept', () => {
    expect(DEFAULT_SETTINGS.rangeRings).toBe(true);
    expect(loadSettings(memory({ 'tabletop.play': '{"rangeRings":false,"logOpen":false}' }))).toMatchObject({ rangeRings: true, logOpen: false });
  });

  it('a store with an earlier build\'s muted prompt kinds loads its other settings and drops the key on save', () => {
    const storage = memory({ 'tabletop.play': '{"mutedWindowKinds":["casting"],"logFilter":"mine","logOpen":false}' });
    const s = loadSettings(storage);
    expect(s).toEqual({ ...DEFAULT_SETTINGS, logFilter: 'mine', logOpen: false });
    saveSettings(storage, s);
    expect(storage.data['tabletop.play']).not.toContain('muted');
  });
});
