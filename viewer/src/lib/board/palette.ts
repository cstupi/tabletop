import type { Glow } from '../play/decisionBox';

/** Palette.Selected, Hovered and Ghost (client/src/Table/Palette.cs): what a model glows in for the open Ask. */
export const GLOW: Record<Glow, string> = { selected: '#ffffff', hovered: '#40e6ff', ghost: 'rgba(230, 230, 255, 0.35)' };

/** One per team ordinal, 1-based; ordinals past the end wrap. */
export const TEAM_COLOURS = ['#4ea1ff', '#ff6b5c', '#ffc44e', '#6ddf7c', '#c98cff', '#4ee0d0'] as const;

export function teamColour(ordinal: number): string {
  const colours = TEAM_COLOURS;
  const index = ((ordinal - 1) % colours.length + colours.length) % colours.length;
  return colours[index]!;
}

/** The closed eight (setup content → Terrain declarations), snake_case as the recording spells them. */
export const DECLARATIONS = [
  'impassable', 'blocks_line_of_sight', 'see_in_and_out', 'cover', 'difficult', 'dangerous',
  'dangerous_on_rush_charge', 'steep',
] as const;

const DECLARATION_COLOURS: Record<string, string> = {
  impassable: '#ff6b5c',
  blocks_line_of_sight: '#c98cff',
  see_in_and_out: '#8ab4ff',
  cover: '#6ddf7c',
  difficult: '#ffc44e',
  dangerous: '#ff9d4e',
  dangerous_on_rush_charge: '#e0a060',
  steep: '#c8a46a',
};

export function declarationColour(declaration: string): string {
  return DECLARATION_COLOURS[declaration] ?? '#8b93a1';
}

/** A volume takes the colour of its first declaration; a volume with none is drawn grey. */
export function terrainStroke(declarations: readonly string[]): string {
  return declarations.length === 0 ? '#8b93a1' : declarationColour(declarations[0]!);
}

export function markerColour(kind: string): string {
  return kind === 'objective' ? '#f2f4f8' : '#8b93a1';
}
