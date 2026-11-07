import { describe, expect, it } from 'vitest';
import { DECLARATIONS, declarationColour, terrainStroke } from '../src/lib/board/palette';
import { effectRows, OPEN_GROUND, primaryEffect, TERRAIN_EFFECTS } from '../src/lib/play/terrainEffects';

// TerrainEffectsTests
describe('terrain effects', () => {
  it('give every declaration one effect with its own icon and colour', () => {
    expect(TERRAIN_EFFECTS.map((e) => e.name).sort()).toEqual([...DECLARATIONS].sort());
    expect(new Set(TERRAIN_EFFECTS.map((e) => e.colour)).size).toBe(TERRAIN_EFFECTS.length);
    expect(new Set(TERRAIN_EFFECTS.map((e) => e.icon)).size).toBe(TERRAIN_EFFECTS.length);
    expect(TERRAIN_EFFECTS.map((e) => e.colour)).not.toContain(OPEN_GROUND.colour);
  });

  it('list a volume\'s effects in the label\'s order with the server\'s word and rule', () => {
    const rows = effectRows(['see_in_and_out', 'cover', 'difficult'], { see_in_and_out: 'Dense' }, { cover: '+1 to Defense rolls against shooting.' });
    expect(rows.map((r) => r.word)).toEqual(['Cover', 'Difficult', 'Dense']);
    expect(rows.map((r) => r.icon)).toEqual(['cover', 'difficult', 'see-in-out']);
    expect(rows.map((r) => r.rule)).toEqual(['+1 to Defense rolls against shooting.', null, null]);
  });

  it('tint a volume by its strongest effect, as the board draws it', () => {
    expect(primaryEffect(['blocks_line_of_sight', 'impassable']).name).toBe('impassable');
    expect(primaryEffect(['see_in_and_out', 'cover', 'difficult']).name).toBe('cover');
    expect(primaryEffect([])).toBe(OPEN_GROUND);
    expect(terrainStroke(['cover', 'dangerous'])).toBe(declarationColour('dangerous'));
  });
});
