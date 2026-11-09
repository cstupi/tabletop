import { describe, expect, it } from 'vitest';
import { BOARD_DEFAULTS, boardColoursOf, labelText, terrainStyle, withAlpha } from '../src/lib/board/style';

describe('the board colours', () => {
  it('reads each --board-* property and falls back where the stylesheet gives nothing', () => {
    const css: Record<string, string> = { '--board-bg': ' #5E7A45', '--board-select': '#FFF6D8' };
    const colours = boardColoursOf((name) => css[name] ?? '');
    expect(colours.bg).toBe('#5E7A45');
    expect(colours.select).toBe('#FFF6D8');
    expect(colours.grid).toBe(BOARD_DEFAULTS.grid);
    expect(colours.label).toBe(BOARD_DEFAULTS.label);
    expect(colours.activated).toBe(BOARD_DEFAULTS.activated);
    expect(boardColoursOf((name) => (name === '--board-activated' ? '#B8F27A' : '')).activated).toBe('#B8F27A');
  });

  it('withAlpha multiplies a hex colour\'s alpha and leaves other colours alone', () => {
    expect(withAlpha('#ff0000', 0.5)).toBe('rgba(255, 0, 0, 0.5)');
    expect(withAlpha('#f00', 1)).toBe('rgba(255, 0, 0, 1)');
    expect(withAlpha('#3FD0E033', 1)).toBe('rgba(63, 208, 224, 0.2)');
    expect(withAlpha('rgb(1, 2, 3)', 0.5)).toBe('rgb(1, 2, 3)');
  });
});

describe('terrain', () => {
  it('fills a solid piece denser than an area, in its first declaration\'s hue', () => {
    const solid = terrainStyle('solid', ['impassable']);
    const area = terrainStyle('area', ['cover']);
    expect(solid.fill).toBe('rgba(210, 69, 90, 0.34)');
    expect(area.fill).toBe('rgba(79, 184, 106, 0.16)');
    expect(solid.width).toBeGreaterThan(area.width);
  });

  it('labels in words: catalog names stay, wire names read as phrases, ids never show', () => {
    expect(labelText('Ruined wall')).toBe('Ruined wall');
    expect(labelText('ruined-wall')).toBe('Ruined wall');
    expect(labelText('blocks_line_of_sight')).toBe('Blocks line of sight');
    expect(labelText('0198b7ac-0001-7000-8000-000000000001')).toBe('Terrain');
    expect(labelText('0198b7ac0001700080000000000000a1', '')).toBe('');
    expect(labelText('')).toBe('Terrain');
    expect(labelText('Objective 1')).toBe('Objective 1');
  });
});
