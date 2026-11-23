// viewer/src/lib/replay/replayView.ts
// What the replay page shows at a position, from the play page's own models: the top bar (headerView.ts), the unit card of the selected
// unit (unitSheet.ts, unitCard.ts), the units a click selects and the terrain's names. A replay has no team of its own: it watches.

import { labelText } from '../board/style';
import type { Projection } from '../live/apply';
import { activationLine, headerOf } from '../play/headerView';
import { Names } from '../play/names';
import type { RuleTexts } from '../play/ruleTexts';
import { teamOf } from '../play/sheetFocus';
import { unitCardOf, type UnitCardContent } from '../play/unitCard';
import { unitSheetOf } from '../play/unitSheet';
import type { GameInfo, TerrainPlacement } from '../protocol/wire.generated';
import { type Catalog, nameOf } from '../setup/catalog';

export interface ReplayTeam { readonly id: string; readonly name: string; readonly ordinal: number; readonly score: number }
export interface ReplayHeader {
  /** "Round 2 · Team 1's turn". */
  readonly topLine: string;
  readonly teams: readonly ReplayTeam[];
  /** "Team 2 activating: Orcs · Charge"; null between activations. */
  readonly activation: string | null;
  /** "Game over · Team 2 won" once the shown log has ended the game. */
  readonly result: string | null;
}

export function replayHeaderOf(p: Projection, game: GameInfo): ReplayHeader {
  const names = new Names(game, p);
  const model = headerOf(p, game, names);
  const s = p.scheduler;
  const turn = s.activeTeamId === undefined ? '' : ` · ${names.team(s.activeTeamId)}'s turn`;
  // Before round 1 the phase leads ("Deployment · Team 2's turn"); headerOf's "Round 0 · deployment" is the engine's numbering.
  const topLine = s.round > 0 ? model.topLine : s.phase !== undefined && s.phase.length > 0 ? `${labelText(s.phase, 'Before round 1')}${turn}` : `Before round 1${turn}`;
  return {
    topLine,
    teams: [...game.teams].sort((a, b) => a.ordinal - b.ordinal)
      .map((t) => ({ id: t.id, name: names.team(t.id), ordinal: t.ordinal, score: p.scores.get(t.id) ?? 0 })),
    activation: activationLine(p, game, names, null),
    result: model.result,
  };
}

/** The selected unit's card, or null when the unit is not in the projection at this position. */
export function replayCardOf(
  p: Projection, game: GameInfo, unitId: string | null, rules: RuleTexts, maxWounds: ReadonlyMap<string, number>,
): UnitCardContent | null {
  if (unitId === null || !p.units.has(unitId)) return null;
  const sheet = unitSheetOf(p, new Names(game, p), unitId, rules, maxWounds);
  return unitCardOf(sheet, teamOf(p, game, unitId)?.ordinal ?? 0);
}

/** A model click selects its whole unit: the unit and the models of it the projection holds. */
export function unitSelection(p: Projection, modelId: string): { unitId: string; modelIds: ReadonlySet<string> } | null {
  const unitId = p.models.get(modelId)?.unitId;
  if (unitId === undefined) return null;
  return { unitId, modelIds: new Set([...p.models.values()].filter((m) => m.unitId === unitId).map((m) => m.id)) };
}

/** A terrain piece's name: the catalog's label for a built-in, else "Terrain" (TerrainLabel.NameOf). */
export const terrainNamer = (catalog: Catalog | null) => (piece: TerrainPlacement): string =>
  catalog === null ? 'Terrain' : nameOf(piece, catalog);
