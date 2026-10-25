// viewer/src/lib/board/style.ts
// How the board looks, decided without a canvas: the theme's board colours (the --board-* custom properties in src/app.css, read at draw
// time so a theme switch redraws in the new one), terrain fills by kind, zone and token colours, and the words a label may show.

import { terrainStroke } from './palette';

export interface BoardColours {
  /** The table's slab. */
  readonly bg: string;
  readonly grid: string;
  /** Every 12". */
  readonly gridMajor: string;
  readonly edge: string;
  readonly label: string;
  readonly labelDim: string;
  readonly select: string;
}

/** The Sci-fi values: what the board draws in when no stylesheet answers (a test, a detached canvas). */
export const BOARD_DEFAULTS: BoardColours = {
  bg: '#0E1A22', grid: '#3FD0E014', gridMajor: '#3FD0E033', edge: '#3FD0E066', label: '#DCEBF2', labelDim: '#7A8C96', select: '#FFFFFF',
};

const PROPERTIES: Record<keyof BoardColours, string> = {
  bg: '--board-bg', grid: '--board-grid', gridMajor: '--board-grid-major', edge: '--board-edge',
  label: '--board-label', labelDim: '--board-label-dim', select: '--board-select',
};

/** The board colours from a custom-property reader (getComputedStyle(document.documentElement).getPropertyValue); blanks fall back. */
export function boardColoursOf(read: (property: string) => string): BoardColours {
  const out: Record<string, string> = {};
  for (const [key, property] of Object.entries(PROPERTIES)) {
    const value = read(property).trim();
    out[key] = value.length > 0 ? value : BOARD_DEFAULTS[key as keyof BoardColours];
  }
  return out as unknown as BoardColours;
}

/** `#rgb`, `#rrggbb` or `#rrggbbaa` with its alpha multiplied by `alpha`, as rgba(); anything else is returned unchanged. */
export function withAlpha(colour: string, alpha: number): string {
  const hex = /^#([0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$/i.exec(colour.trim())?.[1];
  if (hex === undefined) return colour;
  const full = hex.length === 3 ? [...hex].map((c) => c + c).join('') : hex;
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16));
  const a = full.length === 8 ? parseInt(full.slice(6, 8), 16) / 255 : 1;
  return `rgba(${r}, ${g}, ${b}, ${+(a * alpha).toFixed(3)})`;
}

export interface TerrainStyle { readonly fill: string; readonly stroke: string; readonly width: number }

/** A solid piece reads as a body (a denser fill, a firm edge); an area as ground (a light wash, a thinner edge). Hue: its first declaration. */
export function terrainStyle(kind: string, declarations: readonly string[]): TerrainStyle {
  const hue = terrainStroke(declarations);
  return kind === 'solid'
    ? { fill: withAlpha(hue, 0.34), stroke: withAlpha(hue, 0.95), width: 2 }
    : { fill: withAlpha(hue, 0.16), stroke: withAlpha(hue, 0.7), width: 1.25 };
}

/** Pixels per inch from which terrain and marker names show on the board without a selection. */
export const LABEL_SCALE = 18;

const UUID = /^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i;
const WIRE_NAME = /^[a-z0-9]+([-_][a-z0-9]+)*$/;

/**
 * A terrain or marker label as words: a catalog name stays as it is, a built-in id or snake_case name becomes a sentence-case phrase
 * ("ruined-wall" → "Ruined wall"), and an id (an asset's UUID) becomes `fallback`.
 */
export function labelText(label: string, fallback = 'Terrain'): string {
  const text = label.trim();
  if (text.length === 0 || UUID.test(text)) return fallback;
  if (!WIRE_NAME.test(text)) return text;
  const words = text.replace(/[-_]+/g, ' ');
  return words[0]!.toUpperCase() + words.slice(1);
}
