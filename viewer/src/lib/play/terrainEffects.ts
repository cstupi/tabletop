// viewer/src/lib/play/terrainEffects.ts
// client/src/Core/TerrainEffects.cs: the one mapping from a terrain declaration to its icon and colour, which the hover panel's chips,
// the terrain card's rows, the board's terrain colours and the setup legend read. TERRAIN_EFFECTS is in the label's order.

import { declarationsOf } from '../live/project';
import type { TerrainPlacement } from '../protocol/wire.generated';
import { declarationWords, type TerrainWords } from '../setup/terrainCard';

export interface TerrainEffect { readonly name: string; readonly icon: string; readonly colour: string }
export interface TerrainEffectRow extends TerrainEffect { readonly word: string; readonly rule: string | null }

/** Welcome's terrain rules by declaration name (ConnectionState.terrainRules): one sentence of what each does to a unit. */
export type TerrainRules = { readonly [name: string]: string };

export const TERRAIN_EFFECTS: readonly TerrainEffect[] = [
  { name: 'cover', icon: 'cover', colour: '#4FB86A' },
  { name: 'difficult', icon: 'difficult', colour: '#E0B43C' },
  { name: 'dangerous', icon: 'dangerous', colour: '#F0722E' },
  { name: 'dangerous_on_rush_charge', icon: 'unstable', colour: '#D9965A' },
  { name: 'steep', icon: 'steep', colour: '#A8865C' },
  { name: 'impassable', icon: 'impassable', colour: '#D2455A' },
  { name: 'blocks_line_of_sight', icon: 'blocks-sight', colour: '#9A6CE0' },
  { name: 'see_in_and_out', icon: 'see-in-out', colour: '#4E9CE6' },
];

/** A volume with no declaration. */
export const OPEN_GROUND: TerrainEffect = { name: 'open', icon: 'open-ground', colour: '#8B93A1' };

/** Which effect tints a volume of several: what stops or harms a unit first, then sight, then cover and movement. */
const STRENGTH = ['impassable', 'dangerous', 'dangerous_on_rush_charge', 'blocks_line_of_sight', 'cover', 'difficult', 'steep', 'see_in_and_out'];

export const effectOf = (name: string): TerrainEffect => TERRAIN_EFFECTS.find((e) => e.name === name) ?? OPEN_GROUND;

export function primaryEffect(declarations: readonly string[]): TerrainEffect {
  const strongest = STRENGTH.find((n) => declarations.includes(n));
  return strongest === undefined ? OPEN_GROUND : effectOf(strongest);
}

/** The declarations' effects in the label's order, in the server's words and with its sentences where given; Open alone with none. */
export function effectRows(declarations: readonly string[], words?: TerrainWords | null, rules?: TerrainRules | null): TerrainEffectRow[] {
  const shown = new Map(declarationWords(words));
  const rows = TERRAIN_EFFECTS.filter((e) => declarations.includes(e.name))
    .map((e) => ({ ...e, word: shown.get(e.name) ?? e.name, rule: rules?.[e.name] ?? null }));
  return rows.length > 0 ? rows : [{ ...OPEN_GROUND, word: 'Open', rule: null }];
}

/** Every declaration any of the piece's volumes carries. */
export const pieceDeclarations = (t: TerrainPlacement): string[] => [...new Set(t.volumes.flatMap((tv) => declarationsOf(tv.volume.declarations)))];
