// viewer/src/lib/play/settings.ts
// The play settings Godot's ClientSettings holds (RangeRings, LogOpen, LogFilter, LogDetailed), per viewer: localStorage
// `tabletop.play`. Every access is in try/catch; an unreadable store gives the defaults, and a key the settings no longer hold is ignored.

export type LogFilter = 'all' | 'rolls' | 'mine' | 'unit';

export interface PlaySettings {
  readonly rangeRings: boolean;
  readonly logOpen: boolean;
  readonly logFilter: LogFilter;
  /** The combat log draws each roll as a worked resolution (detailed-log spec). */
  readonly logDetailed: boolean;
}

export interface StorageLike { getItem(key: string): string | null; setItem(key: string, value: string): void }

export const DEFAULT_SETTINGS: PlaySettings = { rangeRings: true, logOpen: true, logFilter: 'all', logDetailed: false };

const KEY = 'tabletop.play';
const FILTERS: readonly string[] = ['all', 'rolls', 'mine', 'unit'];

export function loadSettings(storage: StorageLike | null): PlaySettings {
  try {
    const raw = storage?.getItem(KEY);
    if (raw === null || raw === undefined) return DEFAULT_SETTINGS;
    const v = JSON.parse(raw) as Record<string, unknown>;
    if (typeof v !== 'object' || v === null) return DEFAULT_SETTINGS;
    return {
      // stored as weaponRanges: every earlier save wrote rangeRings false, which nobody chose
      rangeRings: typeof v.weaponRanges === 'boolean' ? v.weaponRanges : DEFAULT_SETTINGS.rangeRings,
      logOpen: typeof v.logOpen === 'boolean' ? v.logOpen : DEFAULT_SETTINGS.logOpen,
      logFilter: typeof v.logFilter === 'string' && FILTERS.includes(v.logFilter) ? (v.logFilter as LogFilter) : DEFAULT_SETTINGS.logFilter,
      logDetailed: typeof v.logDetailed === 'boolean' ? v.logDetailed : DEFAULT_SETTINGS.logDetailed,
    };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

export function saveSettings(storage: StorageLike | null, s: PlaySettings): void {
  try { const { rangeRings, ...rest } = s; storage?.setItem(KEY, JSON.stringify({ ...rest, weaponRanges: rangeRings })); } catch { /* a viewer convenience: nothing depends on it */ }
}
