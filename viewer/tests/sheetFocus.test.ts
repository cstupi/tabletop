import { describe, expect, it } from 'vitest';
import { sheetFocusOf, teamOf } from '../src/lib/play/sheetFocus';
import { TEAM_A, activating, blue, blue2, focusGame, marker, red, terrain } from './focusSamples';

describe('SheetFocus', () => {
  it('the selection shows else my activating unit else none', () => {
    expect(sheetFocusOf(activating(null), focusGame, TEAM_A, red.id)).toBe(red.id);   // a selected unit of any team
    expect(sheetFocusOf(activating(blue.id), focusGame, TEAM_A, null)).toBe(blue.id);
    expect(sheetFocusOf(activating(null), focusGame, TEAM_A, null)).toBeNull();
  });

  it('an activation never changes a selection', () => {
    expect(sheetFocusOf(activating(blue.id), focusGame, TEAM_A, blue2.id)).toBe(blue2.id);
    expect(sheetFocusOf(activating(blue.id), focusGame, TEAM_A, red.id)).toBe(red.id);
  });

  it('an enemy activation never takes the card', () => {
    expect(sheetFocusOf(activating(red.id), focusGame, TEAM_A, null)).toBeNull();
    expect(sheetFocusOf(activating(blue.id), focusGame, null, null)).toBeNull();   // a watcher has no team: no activation is theirs
  });

  it('a selected marker falls back', () => {
    expect(sheetFocusOf(activating(blue.id), focusGame, TEAM_A, marker.id)).toBe(blue.id);
  });

  it("a selected terrain piece takes the card's slot", () => {
    const p = activating(blue.id);
    expect(sheetFocusOf(p, focusGame, TEAM_A, (terrain as { id: string }).id)).toBeNull();   // the terrain card shows it instead
  });

  it('team of a unit comes from its participant', () => {
    expect(teamOf(activating(null), focusGame, red.id)?.ordinal).toBe(2);
  });
});
