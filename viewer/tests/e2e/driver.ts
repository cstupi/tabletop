// One seat played through the page's own modules: the decision box, its move hooks and the undo prediction. `step` does what a player
// does with the keyboard and the board for whatever the box shows, and resolves once that send has settled.

import type { Api } from '../../src/lib/api/client';
import type { GameConnection } from '../../src/lib/connection/gameConnection';
import type { Geometry, PlacedModel, Placement, Point3 } from '../../src/lib/geometry';
import type { DecisionBox } from '../../src/lib/play/decisionBox';
import type { UndoPrediction } from '../../src/lib/play/undo';
import type { Violations } from '../../src/lib/play/violations';
import type { TableState, ZoneLegs } from '../../src/lib/protocol/wire.generated';

export interface Seat {
  readonly name: string; readonly conn: GameConnection; readonly api: Api; readonly box: DecisionBox; readonly undo: UndoPrediction;
  readonly violations: Violations;
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** The first polygon of the team's zone, by its centroid (area weighted). */
export function zoneCentre(g: Geometry, table: TableState, ordinal: number, legs?: ZoneLegs): Point3 {
  const zone = table.zones.find((z) => z.teamOrdinal === ordinal)!;
  const poly = g.zonePolygons(zone.shape, table.size, legs)[0]!;
  let a = 0, cx = 0, cy = 0;
  for (let i = 0; i < poly.length; i++) {
    const p = poly[i]!, q = poly[(i + 1) % poly.length]!;
    const cross = p.x * q.y - q.x * p.y;
    a += cross; cx += (p.x + q.x) * cross; cy += (p.y + q.y) * cross;
  }
  return Math.abs(a) < 1e-9 ? { x: poly[0]!.x, y: poly[0]!.y, z: 0 } : { x: cx / (3 * a), y: cy / (3 * a), z: 0 };
}

/** Waits until `done` or the timeout; a timeout is not an error here: the caller's loop reads the box again. */
async function settle(done: () => boolean, ms = 8_000): Promise<void> {
  for (let i = 0; i < ms / 25 && !done(); i++) await sleep(25);
}

/** The deploy placement: the unit laid out at the zone's centre, or the nearest spot that fits and keeps every base wholly in the zone. */
async function placeUnit(seat: Seat, g: Geometry): Promise<void> {
  const { box, conn, violations } = seat;
  const p = conn.state.projection!;
  const game = conn.state.game!;
  const team = game.teams.find((t) => box.deployUnit !== null && game.participants.some((x) => x.teamId === t.id && x.id === p.units.get(box.deployUnit!)?.participantId))!;
  const table = p.board.table;
  const zone = table.zones.find((z) => z.teamOrdinal === team.ordinal)!;
  const legs = conn.state.zones ?? undefined;
  const centre = zoneCentre(g, table, team.ordinal, legs);
  const spots: { x: number; y: number }[] = [{ x: centre.x, y: centre.y }];
  for (let x = 3; x < table.size.width; x += 3) for (let y = 3; y < table.size.length; y += 3) if (g.zoneContains(zone.shape, table.size, { x, y }, legs)) spots.push({ x, y });
  spots.splice(1, spots.length - 1, ...spots.slice(1).sort((s, t) => Math.hypot(s.x - centre.x, s.y - centre.y) - Math.hypot(t.x - centre.x, t.y - centre.y)));
  const bodyOf = (id: string): PlacedModel['body'] => { const m = p.models.get(id)!; return { base: m.slot.base, height: m.slot.height }; };
  const whole = (ghosts: ReadonlyMap<string, Placement>) => [...ghosts].every(([id, placement]) => g.zoneHoldsBase(zone.shape, table.size, { body: bodyOf(id), placement }, legs));
  let fallback: { x: number; y: number } | null = null;
  let chosen = false;
  for (const spot of spots) {
    box.placeAt({ x: spot.x, y: spot.y, z: 0 });
    if (box.ghosts.size === 0) continue;                               // nothing fits here: the pick stays armed
    if (violations.marked.size === 0 && whole(box.ghosts)) { chosen = true; break; }
    fallback ??= spot;
    box.cancel();                                                      // overlaps, or not wholly in the zone: another spot
  }
  if (!chosen) {
    if (fallback === null) throw new Error('no spot in the zone holds the unit');
    box.placeAt({ x: fallback.x, y: fallback.y, z: 0 });
  }
  const unit = box.deployUnit;
  box.enter();
  await settle(() => box.deployUnit !== unit || box.panel.refusal !== null);
}

/**
 * One step for the seat if its box shows an Ask or a deploy placement: choose (an option labelled Hold when there is one, else key 1),
 * move (commit where the ghosts stand), deploy pick (key 1) and placement, pick pieces (the first eligible, then Enter), assign (the first
 * candidate, then Enter; Pass when none), allocate (Auto, then Enter), token spend and confirm (Enter). False when there was nothing to do.
 */
export async function step(seat: Seat, g: Geometry): Promise<boolean> {
  const { box } = seat;
  if (box.deployUnit !== null) { await placeUnit(seat, g); return true; }
  const ask = box.renderedAsk;
  if (ask === null) return false;
  const controls = box.panel.controls;
  const ids = controls.flatMap((c) => ('id' in c && c.id !== undefined ? [c.id] : []));
  const has = (id: string) => ids.includes(id);
  const options = controls.flatMap((c) => (c.kind === 'button' && c.id.startsWith('opt:') ? [c] : []));
  if (options.length > 0) {
    const hold = options.find((o) => /\bhold\b/i.test(o.label));
    if (hold !== undefined) box.press(hold.id); else if (!box.number(1)) box.press(options[0]!.id);
  } else if (has('commit')) box.enter();
  else if (has('done')) {
    const pick = ids.find((id) => id.startsWith('pick:'));
    if (pick !== undefined) box.press(pick);
    box.enter();
  } else if (has('fire')) {
    const first = box.targets?.candidates[0];
    if (first !== undefined) { box.target(first); box.enter(); } else if (has('pass')) box.press('pass'); else return false;
  } else if (has('auto')) { box.press('auto'); box.enter(); }
  else if (has('spend') || has('enter')) box.enter();
  else if (has('pass')) box.press('pass');
  else return false;
  await settle(() => box.renderedAsk !== ask || box.deployUnit !== null || box.panel.refusal !== null);
  return true;
}
