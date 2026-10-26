// viewer/src/lib/play/settings.ts
// The play settings Godot's ClientSettings holds (MutedWindowKinds, RangeRings, LogOpen, LogFilter, LogDetailed), per viewer: localStorage
// `tabletop.play`. Every access is in try/catch; an unreadable store gives the defaults.

export type LogFilter = 'all' | 'rolls' | 'mine' | 'unit';

export interface PlaySettings {
  readonly mutedWindowKinds: readonly string[];
  readonly rangeRings: boolean;
  readonly logOpen: boolean;
  readonly logFilter: LogFilter;
  /** The combat log draws each roll as a worked resolution (detailed-log spec). */
  readonly logDetailed: boolean;
}

export interface StorageLike { getItem(key: string): string | null; setItem(key: string, value: string): void }

export const DEFAULT_SETTINGS: PlaySettings = { mutedWindowKinds: [], rangeRings: false, logOpen: true, logFilter: 'all', logDetailed: false };

const KEY = 'tabletop.play';
const FILTERS: readonly string[] = ['all', 'rolls', 'mine', 'unit'];

export function loadSettings(storage: StorageLike | null): PlaySettings {
  try {
    const raw = storage?.getItem(KEY);
    if (raw === null || raw === undefined) return DEFAULT_SETTINGS;
    const v = JSON.parse(raw) as Record<string, unknown>;
    if (typeof v !== 'object' || v === null) return DEFAULT_SETTINGS;
    return {
      mutedWindowKinds: Array.isArray(v.mutedWindowKinds) && v.mutedWindowKinds.every((k) => typeof k === 'string')
        ? [...(v.mutedWindowKinds as string[])] : DEFAULT_SETTINGS.mutedWindowKinds,
      rangeRings: typeof v.rangeRings === 'boolean' ? v.rangeRings : DEFAULT_SETTINGS.rangeRings,
      logOpen: typeof v.logOpen === 'boolean' ? v.logOpen : DEFAULT_SETTINGS.logOpen,
      logFilter: typeof v.logFilter === 'string' && FILTERS.includes(v.logFilter) ? (v.logFilter as LogFilter) : DEFAULT_SETTINGS.logFilter,
      logDetailed: typeof v.logDetailed === 'boolean' ? v.logDetailed : DEFAULT_SETTINGS.logDetailed,
    };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

export function saveSettings(storage: StorageLike | null, s: PlaySettings): void {
  try { storage?.setItem(KEY, JSON.stringify(s)); } catch { /* a viewer convenience: nothing depends on it */ }
}

/** ClientSettings.Mute. */
export const mute = (s: PlaySettings, kind: string): PlaySettings =>
  s.mutedWindowKinds.includes(kind) ? s : { ...s, mutedWindowKinds: [...s.mutedWindowKinds, kind] };

/** ClientSettings.Unmute. */
export const unmute = (s: PlaySettings, kind: string): PlaySettings => ({ ...s, mutedWindowKinds: s.mutedWindowKinds.filter((k) => k !== kind) });
