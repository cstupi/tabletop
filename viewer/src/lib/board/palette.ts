import type { Glow } from '../play/decisionBox';
import { effectOf, primaryEffect } from '../play/terrainEffects';

/** Palette.Selected, Hovered and Ghost (client/src/Table/Palette.cs): what a model glows in for the open Ask. */
export const GLOW: Record<Glow, string> = { selected: '#ffffff', hovered: '#40e6ff', ghost: 'rgba(230, 230, 255, 0.35)' };

/** One per team ordinal, 1-based; ordinals past the end wrap. */
export const TEAM_COLOURS = ['#4ea1ff', '#ff6b5c', '#ffc44e', '#ff6fb5', '#c98cff', '#3fd0e0'] as const;

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

/** A declaration's colour: its terrain effect's (terrainEffects.ts, Godot's TerrainEffects), the chips' and the legend's. */
export function declarationColour(declaration: string): string {
  return effectOf(declaration).colour;
}

/** A volume takes its strongest effect's colour (primaryEffect); a volume with none is drawn grey. */
export function terrainStroke(declarations: readonly string[]): string {
  return primaryEffect(declarations).colour;
}

export function markerColour(kind: string): string {
  return kind === 'objective' ? '#f2f4f8' : '#8b93a1';
}
