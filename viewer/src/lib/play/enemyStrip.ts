// viewer/src/lib/play/enemyStrip.ts
// client/src/Core/EnemyStrip.cs: the strip above the unit card (client UX spec → Unit card, Hovered enemy): the hovered enemy's name,
// Defense and models alive, the distance between the two units, and each of my weapons' printed range within or beyond it. No claim
// about shooting.

import type { Projection } from '../live/apply';
import type { GameInfo } from '../protocol/wire.generated';
import { teamOf } from './sheetFocus';
import type { UnitSheetModel } from './unitSheet';

export interface RangeCheck { readonly weapon: string; readonly range: string; readonly within: boolean | null }
export interface EnemyStripModel {
  readonly unitId: string; readonly name: string; readonly defense: string | null; readonly alive: number; readonly distance: number | null;
  readonly ranges: readonly RangeCheck[];
}

/** The card shows one of my units and the pointer is over a unit of another team. */
export function stripShows(p: Projection, game: GameInfo, myTeam: string | null, cardUnit: string, hoveredUnit: string): boolean {
  const theirs = teamOf(p, game, hoveredUnit);
  return myTeam !== null && teamOf(p, game, cardUnit)?.id === myTeam && theirs !== null && theirs.id !== myTeam;
}

export function enemyStripOf(mine: UnitSheetModel, hovered: UnitSheetModel, distance: number | null): EnemyStripModel {
  return {
    unitId: hovered.unitId, name: hovered.name, defense: hovered.defense, alive: hovered.alive, distance,
    ranges: mine.weapons.filter((w) => w.range !== '-').map((w) => ({ weapon: w.name, range: w.range, within: within(w.range, distance) })),
  };
}

function within(range: string, distance: number | null): boolean | null {
  const digits = /^\d+/.exec(range)?.[0];
  return distance !== null && digits !== undefined ? distance <= Number.parseInt(digits, 10) : null;
}
