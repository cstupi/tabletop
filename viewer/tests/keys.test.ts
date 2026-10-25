import { describe, expect, it } from 'vitest';
import { NO_MODIFIERS } from '../src/lib/board/interaction';
import { intentOf, textFocused } from '../src/lib/play/keys';

const ctrl = { ...NO_MODIFIERS, ctrl: true };

describe('Hotkeys', () => {
  it('digits are options', () => {
    for (let i = 1; i <= 9; i++) expect(intentOf(String(i), NO_MODIFIERS)).toEqual({ kind: 'option', key: i });
    expect(intentOf('0', NO_MODIFIERS)).toBeNull();
  });

  it('Enter, Escape, Q, E, C map', () => {
    expect(intentOf('Enter', NO_MODIFIERS)).toEqual({ kind: 'confirm' });
    expect(intentOf('Escape', NO_MODIFIERS)).toEqual({ kind: 'cancel' });
    expect(intentOf('q', NO_MODIFIERS)).toEqual({ kind: 'turnLeft' });
    expect(intentOf('E', { ...NO_MODIFIERS, shift: true })).toEqual({ kind: 'turnRight' });
    expect(intentOf('c', NO_MODIFIERS)).toEqual({ kind: 'stopHere' });
    expect(intentOf('Shift', NO_MODIFIERS)).toEqual({ kind: 'singleModel' });
    expect(intentOf('l', NO_MODIFIERS)).toEqual({ kind: 'sight' });
    expect(intentOf('g', NO_MODIFIERS)).toEqual({ kind: 'toggleLog' });
    expect(intentOf('v', NO_MODIFIERS)).toEqual({ kind: 'toggleRangeRings' });
    expect(intentOf('Delete', NO_MODIFIERS)).toEqual({ kind: 'remove' });
    expect(intentOf('Backspace', NO_MODIFIERS)).toEqual({ kind: 'remove' });
    expect(intentOf('z', NO_MODIFIERS)).toBeNull();
    expect(intentOf('/', NO_MODIFIERS)).toBeNull();
  });

  it('Ctrl+Z is undo and other Ctrl chords are nothing', () => {
    expect(intentOf('z', ctrl)).toEqual({ kind: 'undo' });
    expect(intentOf('Z', ctrl)).toEqual({ kind: 'undo' });
    expect(intentOf('g', ctrl)).toBeNull();
    expect(intentOf('1', ctrl)).toBeNull();
    expect(intentOf('Delete', ctrl)).toBeNull();
    expect(intentOf('s', ctrl)).toBeNull();
  });

  it('a key in a text field is no hotkey', () => {
    expect(textFocused({ tagName: 'INPUT' } as never)).toBe(true);
    expect(textFocused({ tagName: 'textarea' } as never)).toBe(true);
    expect(textFocused({ tagName: 'SELECT' } as never)).toBe(true);
    expect(textFocused({ tagName: 'DIV', isContentEditable: true } as never)).toBe(true);
    expect(textFocused({ tagName: 'CANVAS' } as never)).toBe(false);
    expect(textFocused({ tagName: 'BUTTON' } as never)).toBe(false);
    expect(textFocused(null)).toBe(false);
  });
});
