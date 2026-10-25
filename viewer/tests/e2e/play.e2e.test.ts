// A game in play, from the browser modules, against the running stack (docker compose up -d): two accounts import an army each over the
// dev route, take a team each, end setup, and answer the server's Asks through the decision box controller on their real connections.
// Skipped unless TABLETOP_E2E=1. Run: npm run test:e2e.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { apiOf } from '../../src/lib/api/client';
import { connectGame, type ConnectionState, type GameConnection } from '../../src/lib/connection/gameConnection';
import { acting, teamsOf } from '../../src/lib/lobby/seats';
import type { Geometry, PlacedModel, Placement } from '../../src/lib/geometry';
import { DecisionBox } from '../../src/lib/play/decisionBox';
import { moveHooks } from '../../src/lib/play/decisionBox.move';
import { DEPLOY_QUESTIONS } from '../../src/lib/play/deployView';
import { ModelDrag } from '../../src/lib/play/modelDrag';
import { Violations } from '../../src/lib/play/violations';
import { loadTestGeometry } from '../geometry';
import { DEFAULT_SETTINGS } from '../../src/lib/play/settings';
import { RuleTexts } from '../../src/lib/play/ruleTexts';
import type { Edit, Team } from '../../src/lib/protocol/wire.generated';

const MANAGEMENT = 'http://127.0.0.1:5100';
const SOCKET = 'ws://127.0.0.1:5200/ws';
const BOOKS = ['z65fgu0l29i4lnlu', '78qp9l5alslt6yj8'];

async function session(name: string): Promise<string> {
  const response = await fetch(`${MANAGEMENT}/v1/dev/sessions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name }) });
  if (!response.ok) throw new Error(`dev session for ${name}: ${response.status}`);
  return ((await response.json()) as { sessionJwt: string }).sessionJwt;
}

async function importArmy(jwt: string, book: string): Promise<string> {
  const list = JSON.parse(readFileSync(`../sim/Lifecycle/Armies/${book}.json`, 'utf8')) as unknown;
  const response = await fetch(`${MANAGEMENT}/v1/dev/armies/import`, {
    method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${jwt}` }, body: JSON.stringify({ source: 'lifecycle', list }),
  });
  if (response.status !== 201) throw new Error(`army import answered ${response.status}: ${await response.text()}`);
  return ((await response.json()) as { id: string }).id;
}

function waitFor(conn: GameConnection, ready: (s: ConnectionState) => boolean, what: string, ms = 15_000): Promise<ConnectionState> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { stop(); reject(new Error(`timed out waiting for ${what}`)); }, ms);
    const stop = conn.subscribe((s) => { if (ready(s)) { clearTimeout(timer); queueMicrotask(() => stop()); resolve(s); } });
  });
}

/** A player's box with D2's move hooks over real geometry. The browser's gate is off (silent), so the server is the one to refuse. */
function playerOf(conn: GameConnection, g: Geometry) {
  const violations = new Violations();
  const hooks = moveHooks({ geometry: () => g, violations, enforcement: () => 'silent' });
  const box = boxOf(conn, hooks);
  return { conn, box, violations, hooks };
}

const boxOf = (conn: GameConnection, hooks: ReturnType<typeof moveHooks> | null = null): DecisionBox => {
  const myTeams = () => (conn.state.game === null ? [] : teamsOf(conn.state.game, conn.state.participantId));
  const box = new DecisionBox({
    state: () => conn.state, myTeams, acting: () => acting(conn.state.projection, myTeams(), conn.state.log),
    settings: () => DEFAULT_SETTINGS, updateSettings: () => {}, rules: () => RuleTexts.empty,
    send: (command) => conn.sendCommand(command), onRefused: (l) => conn.onRefused((r) => l(r)),
  }, hooks);
  conn.subscribe(() => box.render());
  return box;
};

/** Two accounts take a team each in a new game and end setup: both connections are playing. */
async function playingGame(label: string, enforcement: 'strict' | 'warn' | 'silent' = 'strict') {
  const stamp = Date.now();
  const [jwtA, jwtB] = await Promise.all([`${label}-a-${stamp}`, `${label}-b-${stamp}`].map(session)) as [string, string];
  const apiA = apiOf(async () => jwtA, MANAGEMENT);
  const apiB = apiOf(async () => jwtB, MANAGEMENT);
  const [armyA, armyB] = await Promise.all([importArmy(jwtA, BOOKS[0]!), importArmy(jwtB, BOOKS[1]!)]);
  const properties = (await apiA.listProperties())[0]!;
  const created = await apiA.createGame({
    name: `${label} e2e ${stamp}`, propertiesHash: properties.contentHash, schedulerShape: 'alternating-activation', enforcement, waitsBetweenSteps: false,
  });
  await apiB.joinGame(created.id);
  const a = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtA });
  const b = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtB });
  let t1!: Team, t2!: Team;
  try {
    await Promise.all([waitFor(a, (s) => s.phase === 'connected', 'A connected'), waitFor(b, (s) => s.phase === 'connected', 'B connected')]);
    const sendOk = async (conn: GameConnection, edit: Edit) => expect((await conn.sendEdit(edit)).refused).toBeNull();
    while ((a.state.game?.teams.length ?? 0) < 2) await sendOk(a, { kind: 'create_team' });
    await waitFor(b, (s) => (s.game?.teams.length ?? 0) >= 2, 'B sees two teams');
    [t1, t2] = [...a.state.game!.teams].sort((x, y) => x.ordinal - y.ordinal) as [Team, Team];
    await sendOk(a, { kind: 'pick_team', teamId: t1.id });
    await sendOk(b, { kind: 'pick_team', teamId: t2.id });
    await sendOk(a, { kind: 'pick_army', armyId: armyA, teamId: t1.id });
    await sendOk(b, { kind: 'pick_army', armyId: armyB, teamId: t2.id });
    await waitFor(a, (s) => s.game!.participants.every((p) => p.armyId !== undefined), 'both armies picked');
    await sendOk(a, { kind: 'start_setup' });
    await waitFor(a, (s) => s.game?.status === 'setup' && s.draft !== null, 'A in setup');
    await sendOk(a, { kind: 'end_setup' });
    await Promise.all([waitFor(a, (s) => s.game?.status === 'playing' && s.projection !== null, 'A playing'), waitFor(b, (s) => s.game?.status === 'playing' && s.projection !== null, 'B playing')]);
  } catch (e) { a.close(); b.close(); throw e; }
  return { a, b, t1, t2 };
}

const until = async (what: string, cond: () => boolean, ms = 10_000) => {
  for (let i = 0; i < ms / 50 && !cond(); i++) await new Promise((r) => setTimeout(r, 50));
  if (!cond()) throw new Error(`timed out waiting for ${what}`);
};

describe.skipIf(process.env.TABLETOP_E2E !== '1')('browser play, the decision box on the real server', () => {
  it('answers the Asks the server puts to each team and shows its refusals', async () => {
    const { a, b, t1 } = await playingGame('d1');
    try {
      const boxA = boxOf(a), boxB = boxOf(b);
      // Somebody is asked first; the box of the team addressed shows the Ask and the other shows who the game waits on.
      await until('an open Ask', () => (a.state.projection?.openAsks.size ?? 0) > 0);
      const firstSeq = a.state.projection!.lastSeq;
      const open = [...a.state.projection!.openAsks.values()][0]!.ask;
      const addressed = open.teams.includes(t1.id) ? boxA : boxB;
      const other = addressed === boxA ? boxB : boxA;
      expect(other.panel.visible).toBe(true);
      expect(addressed.panel.title).not.toBe('');

      // A refusal shows the server's words over the same Ask: an answer outside the legal set, sent as the box sends one.
      const bogus = await (addressed === boxA ? a : b).sendCommand({
        kind: 'answer_ask', askId: open.id, answer: { kind: 'chosen', optionId: 'not-an-option' }, teamId: open.teams[0]!,
      });
      const refused = bogus.refused ?? await new Promise<{ detail?: string } | null>((resolve) => {
        const stop = (addressed === boxA ? a : b).onRefused((r) => { if (r.messageId === bogus.id) { stop(); resolve(r); } });
        setTimeout(() => { stop(); resolve(null); }, 2000);
      });
      expect(refused).not.toBeNull();

      // Answer the way a player does: the deploy pick is numbered, and its key sends the unit; the server closes the Ask.
      expect(open.question).toBe('deploy');
      expect(addressed.panel.hint).toBe('1–3: pick a unit to deploy');
      expect(addressed.number(1)).toBe(true);
      await until('the Ask to close', () => !a.state.projection!.openAsks.has(open.id));
      expect(a.state.projection!.lastSeq).toBeGreaterThan(firstSeq);
      expect(addressed.panel.refusal).toBeNull();
    } finally {
      a.close();
      b.close();
    }
  }, 90_000);
});

describe.skipIf(process.env.TABLETOP_E2E !== '1')('browser play, deployment and moves on the real server', () => {
  it('places units by dragging, commits a move and is refused an illegal one', async () => {
    const g = await loadTestGeometry();
    const { a, b, t1, t2 } = await playingGame('d2');
    try {
      const players = new Map([[t1.id, playerOf(a, g)], [t2.id, playerOf(b, g)]]);
      const ordinals = new Map([[t1.id, t1.ordinal], [t2.id, t2.ordinal]]);
      const proj = () => a.state.projection!;
      const openAsk = () => [...proj().openAsks.values()].map((x) => x.ask)[0];
      const size = () => proj().board.table.size;
      const bodyOf = (id: string): PlacedModel['body'] => { const m = proj().models.get(id)!; return { base: m.slot.base, height: m.slot.height }; };
      const positionOf = (id: string) => (proj().models.get(id)!.location as { placement: Placement }).placement.position;
      const zoneOf = (team: string) => proj().board.table.zones.find((z) => z.teamOrdinal === ordinals.get(team))!;

      /** Spots in the team's zone: a grid of candidates the real geometry says hold the point. */
      const spots = (team: string): { x: number; y: number }[] => {
        const zone = zoneOf(team);
        const out: { x: number; y: number }[] = [];
        for (let x = 4; x < size().width; x += 4) for (let y = 4; y < size().length; y += 4) if (g.zoneContains(zone.shape, size(), { x, y })) out.push({ x, y });
        return out;
      };
      const inZone = (team: string, ghosts: ReadonlyMap<string, Placement>): boolean =>
        [...ghosts].every(([id, placement]) => g.zoneHoldsBase(zoneOf(team).shape, size(), { body: bodyOf(id), placement }));

      let dragged = false;
      for (let guard = 0; guard < 60; guard++) {
        let open = openAsk();
        for (let i = 0; i < 100 && open === undefined; i++) { await new Promise((r) => setTimeout(r, 50)); open = openAsk(); }
        if (open === undefined) throw new Error('no Ask opened');
        if (!DEPLOY_QUESTIONS.includes(open.question)) break;
        const team = open.teams[0]!;
        const { box, violations } = players.get(team)!;
        expect(box.number(1)).toBe(true);
        await until('the placement', () => box.deployUnit !== null);
        const unit = box.deployUnit!;
        let placed = false;
        for (const spot of spots(team)) {
          box.placeAt({ x: spot.x, y: spot.y, z: 0 });
          if (box.ghosts.size === 0) continue;
          if (!dragged) {                                                 // the deployment drag, through the real landing geometry
            const drag = new ModelDrag(g);
            const [id] = [...box.ghosts.keys()] as [string];
            const from = box.ghosts.get(id)!.position;
            drag.start(proj(), id, from, true, box.proposing, box.ghosts);
            drag.move(proj(), { x: from.x + 0.6, y: from.y }, false, null, box.ghosts);
            for (const [model, placement] of drag.end() ?? []) box.dragged(model, placement);
          }
          if (violations.marked.size === 0 && inZone(team, box.ghosts)) { placed = true; dragged = true; break; }
          box.cancel();                                                   // overlaps, or not wholly in the zone: another spot
        }
        expect(placed).toBe(true);
        expect(box.panel.controls.some((c) => 'id' in c && c.id === 'place')).toBe(true);
        box.enter();
        await until(`${unit} on the table`, () => [...proj().models.values()].filter((m) => m.unitId === unit).every((m) => m.location.kind === 'table'));
        expect(box.panel.refusal).toBeNull();
      }
      expect(dragged).toBe(true);

      /** The first Ask offering an Advance to a team: pick it, and the move Ask comes up for the same player. */
      const advance = async () => {
        let found: { team: string; optionId: string } | null = null;
        await until('an Advance option', () => {
          for (const { ask } of proj().openAsks.values()) {
            if (ask.legal.kind !== 'options') continue;
            const choice = ask.legal.choices.find((c) => /advance/i.test(c.label));
            if (choice !== undefined) { found = { team: ask.teams[0]!, optionId: choice.id }; return true; }
          }
          return false;
        }, 20_000);
        const player = players.get(found!.team)!;
        player.box.press(`opt:${found!.optionId}`);
        await until('the move Ask', () => player.box.panel.controls.some((c) => 'id' in c && c.id === 'commit'));
        return player;
      };
      /** The unit's ghosts dragged by `dy` along the table's length through the real drag. */
      const nudge = (player: Awaited<ReturnType<typeof advance>>, dy: number) => {
        const { box, hooks } = player;
        const drag = new ModelDrag(g);
        const [id] = [...box.ghosts.keys()] as [string];
        const from = box.ghosts.get(id)!.position;
        drag.start(proj(), id, from, false, box.proposing, box.ghosts);
        drag.move(proj(), { x: from.x, y: from.y + dy }, false, hooks.region(), box.ghosts);
        for (const [model, placement] of drag.end() ?? []) box.dragged(model, placement);
      };

      // A legal move: the ghosts go toward the middle of the table by less than the allowance, and Commit moves the models.
      const first = await advance();
      const models = [...first.box.ghosts.keys()];
      const before = models.map((id) => positionOf(id));
      for (const dy of [2, -2, 3, -3, 1, -1, 4, -4]) {                  // the first nudge the browser's own contact marks find clear
        nudge(first, dy);
        if (first.violations.marked.size === 0) break;
        first.box.cancel();
      }
      expect(first.violations.marked.size).toBe(0);
      first.box.enter();
      await until(`the models to move or a refusal`, () => first.box.panel.refusal !== null || models.some((id, i) => Math.abs(positionOf(id).y - before[i]!.y) > 1));
      expect(first.box.panel.refusal).toBeNull();

      // An illegal one: far past the allowance. The browser sends it (its gate is off), the server refuses it, and the ghosts come back marked.
      const second = await advance();
      const target = [...second.box.ghosts.keys()];
      nudge(second, 30);
      second.box.enter();
      await until('the refusal', () => second.box.panel.refusal !== null);
      expect(second.box.panel.refusal).toMatch(/^The server refused: /);
      expect(second.box.ghosts.size).toBe(target.length);
      expect(second.violations.marked.size).toBeGreaterThan(0);
      expect(second.box.panel.controls.some((c) => 'id' in c && c.id === 'commit')).toBe(true);
      expect(second.box.cancel()).toBe(true);                             // Esc puts the ghosts back and clears the marks
      expect(second.violations.marked.size).toBe(0);
    } finally {
      a.close();
      b.close();
    }
  }, 120_000);
});
