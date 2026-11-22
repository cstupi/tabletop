// viewer/src/lib/play/sheetFocus.ts
// client/src/Core/SheetFocus.cs: which unit the unit card shows (client UX spec → Unit card, Which unit): the selected unit; with no unit
// selected, my team's activating unit; otherwise none, and the card hides. A selected terrain piece takes the slot: the terrain card shows
// it, so none. A selection stays until the player clears it, so an activation never changes it, and another team's activation never takes
// the card: the header names that one. While deploying, a deploy pick's tile under the pointer (hovered) takes the card first, then the unit
// my team is placing (placing, PlacingFocus's), so a unit not on the table shows.

import type { Projection } from '../live/apply';
import type { GameInfo, Team } from '../protocol/wire.generated';
import { actionFrame } from './headerView';

export function sheetFocusOf(p: Projection, game: GameInfo, myTeam: string | null, selectedPiece: string | null,
  hovered: string | null = null, placing: string | null = null): string | null {
  if (hovered !== null && p.units.has(hovered)) return hovered;
  if (placing !== null && p.units.has(placing)) return placing;
  if (selectedPiece !== null && p.units.has(selectedPiece)) return selectedPiece;
  if (selectedPiece !== null && p.board.table.terrain.some((t) => t.id === selectedPiece)) return null;
  const actor = actionFrame(p)?.context.actorUnitId;
  return myTeam !== null && actor !== undefined && teamOf(p, game, actor)?.id === myTeam ? actor : null;
}

export function teamOf(p: Projection, game: GameInfo, unitId: string): Team | null {
  const unit = p.units.get(unitId);
  const team = unit === undefined ? undefined : game.participants.find((x) => x.id === unit.participantId)?.teamId;
  return team === undefined ? null : game.teams.find((t) => t.id === team) ?? null;
}
