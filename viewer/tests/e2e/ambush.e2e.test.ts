// Ambush in the browser, on the real server (docker compose up -d): a unit set aside from the roster menu, the round boundary's Ambusher
// pick, and its placement through the decision box: refused within 9" of an enemy, accepted over 9"; with two accounts and with one account
// holding both teams. Skipped unless TABLETOP_E2E=1. Run: npm run test:e2e.

import { describe, expect, it } from 'vitest';
import type { Geometry, PlacedModel, Placement } from '../../src/lib/geometry';
import type { GameConnection } from '../../src/lib/connection/gameConnection';
import type { Projection } from '../../src/lib/live/apply';
import { effectiveLog } from '../../src/lib/live/apply';
import { acting, teamsOf } from '../../src/lib/lobby/seats';
import { mine } from '../../src/lib/play/askView';
import { MarkerLocks } from '../../src/lib/play/markerLocks';
import { choose, rosterMenu, type MenuContext } from '../../src/lib/play/menus';
import { Names } from '../../src/lib/play/names';
import { UndoPrediction } from '../../src/lib/play/undo';
import type { Ask, TableSize } from '../../src/lib/protocol/wire.generated';
import { loadTestGeometry } from '../geometry';
import { AMBUSHER, oneAccountGame, playerOf, playingGame, until } from './harness';
import { step, type Seat } from './driver';

type XY = { x: number; y: number };
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
const proj = (c: GameConnection): Projection => c.state.projection!;

function unitNamed(c: GameConnection, team: string, name: string): string {
  const n = new Names(c.state.game!, proj(c));
  const found = [...proj(c).units.values()].find((u) => u.definition.name === name && n.teamOfUnit(u.id) === team);
  if (found === undefined) throw new Error(`team ${team} has no unit ${name}`);
  return found.id;
}
const propertyNames = (c: GameConnection, unit: string): string[] => {
  const u = proj(c).units.get(unit)!;
  return [...u.definition.properties, ...u.definition.models.flatMap((s) => s.properties)].map((x) => x.name);
};
const openAsk = (c: GameConnection, question: string, team: string): Ask | undefined =>
  [...proj(c).openAsks.values()].map((x) => x.ask).find((a) => a.question === question && a.teams.includes(team));
/** The live ask_answered events of `question` whose selected pieces hold the unit. */
const deployAnswersNaming = (c: GameConnection, unit: string, question: string) => {
  const asks = new Set(c.state.log.flatMap((e) => (e.payload.kind === 'ask_opened' && e.payload.ask.question === question ? [e.payload.ask.id] : [])));
  return effectiveLog(c.state.log).filter((e) => e.payload.kind === 'ask_answered' && asks.has(e.payload.askId)
    && e.payload.answer?.kind === 'selected_pieces' && e.payload.answer.pieceIds.includes(unit));
};
const answeredBy = (c: GameConnection, askId: string): string | undefined => {
  const e = effectiveLog(c.state.log).find((x) => x.payload.kind === 'ask_answered' && x.payload.askId === askId);
  return e?.payload.kind === 'ask_answered' ? e.payload.teamId : undefined;
};
const modelsOf = (c: GameConnection, unit: string) => [...proj(c).models.values()].filter((m) => m.unitId === unit);
const tablePos = (m: { location: { kind: string } }): XY => (m.location as unknown as { placement: Placement }).placement.position;
function firstOnTable(c: GameConnection, team: string): XY {
  const n = new Names(c.state.game!, proj(c));
  const m = [...proj(c).models.values()].find((x) => x.location.kind === 'table' && n.teamOfModel(x.id) === team);
  if (m === undefined) throw new Error(`team ${team} has no model on the table`);
  return tablePos(m);
}

const mkSeat = (name: string, conn: GameConnection, api: Seat['api'], g: Geometry): Seat => {
  const player = playerOf(conn, g);
  const undo = new UndoPrediction();
  conn.onEvents((events) => undo.events(events));
  return { name, conn, api, box: player.box, undo, violations: player.violations };
};

/** The roster's Set aside for `unit`, sent as the page sends it; resolves once the projection carries the condition. */
async function setAside(conn: GameConnection, unit: string): Promise<void> {
  await until('the server names the unit as one that may be set aside', () => conn.state.maySetAside.has(unit));   // ModelFacts, as the page waits for it
  const s = conn.state;
  const myTeam = acting(s.projection, teamsOf(s.game!, s.participantId), s.log);
  const ctx: MenuContext = {
    game: s.game!, p: s.projection!, log: s.log, watching: false, myTeam, mine: mine(s.projection!, s.game!, myTeam, new Set()),
    grantable: [], deployInside: new Map(), maySetAside: s.maySetAside, locks: new MarkerLocks(),
  };
  const item = rosterMenu(ctx, unit).find((i) => i.label === 'Set aside');
  expect(item, 'the roster offers Set aside').toBeDefined();
  const next = choose(item!, null);
  expect(next.kind).toBe('send');
  if (next.kind !== 'send') return;
  const outcome = await conn.sendCommand(next.command);
  expect(outcome.refused).toBeNull();
  await until('the unit set aside', () => proj(conn).units.get(unit)!.conditions.has('set-aside'));
}

/** One step, but a deploy pick never takes `avoid`: it presses the first other `pick:` control and Enter. */
async function stepAvoiding(seat: Seat, g: Geometry, avoid: string): Promise<boolean> {
  const { box } = seat;
  const ask = box.renderedAsk;
  const picks = box.panel.controls.flatMap((c) => ('id' in c && c.id !== undefined && c.id.startsWith('pick:') ? [c.id] : []));
  if (box.deployUnit === null && ask !== null && picks.length > 0 && proj(seat.conn).openAsks.get(ask)?.ask.question === 'deploy') {
    const other = picks.find((id) => id !== `pick:${avoid}`);
    if (other === undefined) return false;
    box.press(other);
    for (let i = 0; i < 200 && box.deployUnit === null && box.renderedAsk === ask; i++) await sleep(25);
    return true;
  }
  return step(seat, g);
}

/** Spots on the table, nearest first to `from`, for the Ambusher's placement click. */
function spots(size: TableSize, from: XY): XY[] {
  const out: XY[] = [];
  for (let x = 4; x < size.width; x += 4) for (let y = 4; y < size.length; y += 4) out.push({ x, y });
  return out.sort((s, t) => Math.hypot(s.x - from.x, s.y - from.y) - Math.hypot(t.x - from.x, t.y - from.y));
}

/** Every ghost's base over `reach` inches, edge to edge, from every enemy model on the table. */
function clearOfEnemies(g: Geometry, p: Projection, ghosts: ReadonlyMap<string, Placement>, enemyTeam: string, reach: number, n: Names): boolean {
  const enemies = [...p.models.values()].filter((m) => m.location.kind === 'table' && n.teamOfModel(m.id) === enemyTeam);
  return [...ghosts].every(([id, placement]) => {
    const mine: PlacedModel = { body: { base: p.models.get(id)!.slot.base, height: p.models.get(id)!.slot.height }, placement };
    return enemies.every((e) => g.edgeToEdge(mine, { body: { base: e.slot.base, height: e.slot.height }, placement: (e.location as unknown as { placement: Placement }).placement }) > reach);
  });
}

/** Press the unit's pick (a pick of one answers at once; otherwise Enter), then wait for its placement. */
async function pickUnit(seat: Seat, unit: string): Promise<void> {
  await until('the pick control', () => seat.box.panel.controls.some((c) => 'id' in c && c.id === `pick:${unit}`));
  seat.box.press(`pick:${unit}`);
  if (seat.box.deployUnit === null && seat.box.panel.controls.some((c) => 'id' in c && c.id === 'done')) seat.box.enter();
  await until('the Ambusher mid-deploy', () => seat.box.deployUnit === unit);
}

/** The placement half: within 9" of an enemy is refused in the server's words and the models stay on the shelf; over 9" is accepted. */
async function placeRefusedThenAccepted(seat: Seat, conn: GameConnection, g: Geometry, unit: string, enemyTeam: string): Promise<void> {
  const box = seat.box;
  const names = new Names(conn.state.game!, proj(conn));
  const enemy = firstOnTable(conn, enemyTeam);
  const size = proj(conn).board.table.size;
  const free = (at: XY) => { box.placeAt({ x: at.x, y: at.y, z: 0 }); return box.ghosts.size > 0; };

  // Within 9": the first spot near an enemy model that the browser can lay out; the server refuses.
  let near = false;
  for (const s of spots(size, enemy)) {
    if (free(s) && !clearOfEnemies(g, proj(conn), box.ghosts, enemyTeam, 9, names)) { near = true; break; }
    box.cancel();
  }
  expect(near, 'a spot within 9" of an enemy').toBe(true);
  box.enter();
  await until('the refusal', () => box.panel.refusal !== null);
  expect(box.panel.refusal).toMatch(/would be placed within 9" of an enemy unit: Ambushers deploy over 9" away from enemy units \(p\.13\)/);
  expect(modelsOf(conn, unit).every((m) => m.location.kind === 'shelf')).toBe(true);
  expect(box.deployUnit).toBe(unit);

  // Over 9": the farthest spot whose every ghost clears every enemy base by more than 9".
  box.cancel();
  let far = false;
  for (const s of spots(size, enemy).reverse()) {
    if (free(s) && clearOfEnemies(g, proj(conn), box.ghosts, enemyTeam, 9.5, names)) { far = true; break; }
    box.cancel();
  }
  expect(far, 'a spot over 9" from every enemy').toBe(true);
  box.enter();
  await until('the Ambusher on the table', () => modelsOf(conn, unit).every((m) => m.location.kind === 'table'));
  expect(box.panel.refusal).toBeNull();
  expect(proj(conn).scheduler.round).toBeGreaterThanOrEqual(2);
}

describe.skipIf(process.env.TABLETOP_E2E !== '1')('Ambush in the browser', () => {
  it('sets a unit aside, then places it at the round boundary: refused within 9", accepted over 9"', async () => {
    const g = await loadTestGeometry();
    const game = await playingGame('ambush', 'strict', { ambush: AMBUSHER });
    const { a, b, t1, t2 } = game;
    try {
      const unit = unitNamed(a, t1.id, AMBUSHER);
      expect(propertyNames(a, unit)).toContain('Ambush');
      const seatA = mkSeat('A', a, game.apiA, g), seatB = mkSeat('B', b, game.apiB, g);
      await until('the roster to be playable', () => proj(a).models.size > 0);
      await setAside(a, unit);

      const boundary = () => openAsk(a, 'deploy_ambusher', t1.id);
      for (let i = 0; i < 900 && boundary() === undefined; i++) {
        const acted = (await stepAvoiding(seatA, g, unit)) || (await step(seatB, g));
        if (!acted) await sleep(100);
      }
      expect(boundary()).toBeDefined();
      expect(proj(a).scheduler.round).toBeGreaterThanOrEqual(2);
      expect(deployAnswersNaming(a, unit, 'deploy')).toEqual([]);

      await pickUnit(seatA, unit);
      await placeRefusedThenAccepted(seatA, a, g, unit, t2.id);
    } finally {
      await game.cleanup();
    }
  }, 300_000);

  it('one account holding both teams places its Ambusher as team 1', async () => {
    const g = await loadTestGeometry();
    const game = await oneAccountGame('ambush-solo', { ambush: AMBUSHER });
    const { a, t1, t2 } = game;
    try {
      const unit = unitNamed(a, t1.id, AMBUSHER);
      const seat = mkSeat('solo', a, game.api, g);
      const actingTeam = () => acting(a.state.projection, teamsOf(a.state.game!, a.state.participantId), a.state.log);
      // The roster offers Set aside for team 1's unit while team 1 acts.
      for (let i = 0; i < 200 && actingTeam() !== t1.id; i++) if (!(await stepAvoiding(seat, g, unit))) await sleep(100);
      expect(actingTeam()).toBe(t1.id);
      await setAside(a, unit);

      const boundary = () => openAsk(a, 'deploy_ambusher', t1.id);
      for (let i = 0; i < 1200 && boundary() === undefined; i++) if (!(await stepAvoiding(seat, g, unit))) await sleep(100);
      expect(boundary()).toBeDefined();
      await until('the box on the Ambusher pick', () => seat.box.panel.controls.some((c) => 'id' in c && c.id === `pick:${unit}`));
      expect(seat.box.panel.seat).toBe('Playing as Team 1');

      const askId = boundary()!.id;
      await pickUnit(seat, unit);
      expect(seat.box.panel.seat).toBe('Playing as Team 1');
      await until('the answer logged', () => answeredBy(a, askId) !== undefined);
      expect(answeredBy(a, askId)).toBe(t1.id);
      await placeRefusedThenAccepted(seat, a, g, unit, t2.id);
    } finally {
      await game.cleanup();
    }
  }, 360_000);
});
