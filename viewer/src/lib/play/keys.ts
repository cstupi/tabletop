// viewer/src/lib/play/keys.ts
// Hotkeys.Of (client/src/Core/Hotkeys.cs), the keys the browser binds: digits are options, Enter and Escape confirm and cancel, Q and E
// turn, C stops, Ctrl+Z undoes, G, R and V toggle the log, the roster and the range rings, T every enemy unit's threat, Shift picks models (click picks a model, drag boxes models), L is the sight check, Delete removes.
// Camera and measuring keys belong to Godot's 3D table and have no key here.

import type { Modifiers } from '../board/interaction';

export type Intent =
  | { readonly kind: 'option'; readonly key: number }
  | { readonly kind: 'confirm' | 'cancel' | 'turnLeft' | 'turnRight' | 'stopHere' | 'undo' | 'sight' | 'toggleRangeRings' | 'toggleAllThreats' | 'toggleLog' | 'toggleRoster' | 'singleModel' | 'remove' };

/** KeyboardEvent.key in; Ctrl+Z is undo and every other Ctrl chord is nothing. */
export function intentOf(key: string, mods: Modifiers): Intent | null {
  const k = key.length === 1 ? key.toLowerCase() : key;
  if (mods.ctrl) return k === 'z' ? { kind: 'undo' } : null;
  if (k >= '1' && k <= '9' && k.length === 1) return { kind: 'option', key: Number(k) };
  switch (k) {
    case 'Enter': return { kind: 'confirm' };
    case 'Escape': return { kind: 'cancel' };
    case 'Shift': return { kind: 'singleModel' };
    case 'l': return { kind: 'sight' };
    case 'g': return { kind: 'toggleLog' };
    case 'r': return { kind: 'toggleRoster' };
    case 'q': return { kind: 'turnLeft' };
    case 'e': return { kind: 'turnRight' };
    case 'c': return { kind: 'stopHere' };
    case 'v': return { kind: 'toggleRangeRings' };
    case 't': return { kind: 'toggleAllThreats' };
    case 'Delete': case 'Backspace': return { kind: 'remove' };
    default: return null;
  }
}

/** TableInput.TextFocused: a key typed into an input, textarea, select or contenteditable is no hotkey. */
export function textFocused(target: EventTarget | null): boolean {
  const el = target as { tagName?: string; isContentEditable?: boolean } | null;
  if (el === null || el.tagName === undefined) return false;
  return ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName.toUpperCase()) || el.isContentEditable === true;
}
