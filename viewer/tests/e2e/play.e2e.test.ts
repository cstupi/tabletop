// A game in play, from the browser modules, against the running stack (docker compose up -d): the decision box on real connections, deployment and
// moves by dragging, and a game from lobby through deployment and a whole round.
// Skipped unless TABLETOP_E2E=1. Run: npm run test:e2e.

import { describe, expect, it } from 'vitest';
import type { PlacedModel, Placement } from '../../src/lib/geometry';
import { DEPLOY_QUESTIONS } from '../../src/lib/play/deployView';
import { ModelDrag } from '../../src/lib/play/modelDrag';
import { loadTestGeometry } from '../geometry';
import { effectiveLog, projectTo } from '../../src/lib/live/apply';
import { build } from '../../src/lib/play/combatLog';
import { headerOf } from '../../src/lib/play/headerView';
import { midDeploy } from '../../src/lib/play/deployView';
import { Names } from '../../src/lib/play/names';
import { UndoPrediction, undoPlanOf } from '../../src/lib/play/undo';
import { waitHolds } from '../../src/lib/play/waiting';
import { refusalText } from '../../src/lib/connection/feedback';
import type { Refused } from '../../src/lib/protocol/wire.generated';
import { boxOf, playerOf, playingGame, until } from './harness';
import { step, type Seat } from './driver';

describe.skipIf(process.env.TABLETOP_E2E !== '1')('browser play, the decision box on the real server', () => {
  it('answers the Asks the server puts to each team and shows its refusals', async () => {
    const game = await playingGame('d1');
    const { a, b, t1 } = game;
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
      await game.cleanup();
    }
  }, 90_000);
});

describe.skipIf(process.env.TABLETOP_E2E !== '1')('browser play, deployment and moves on the real server', () => {
  it('places units by dragging, commits a move and is refused an illegal one', async () => {
    const g = await loadTestGeometry();
    const game = await playingGame('d2');
    const { a, b, t1, t2 } = game;
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
            drag.start(proj(), id, from, [id], box.proposing, box.ghosts);
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
        drag.start(proj(), id, from, null, box.proposing, box.ghosts);
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
      await game.cleanup();
    }
  }, 120_000);
});

describe.skipIf(process.env.TABLETOP_E2E !== '1')('browser play, a whole round on the real server', () => {
  it('a game goes from lobby through deployment and round 1 from the browser modules', async () => {
    const g = await loadTestGeometry();
    const game = await playingGame('d4', 'warn', { layout: true });
    const { a, b, t1, t2 } = game;
    const refusals: Refused[] = [];
    const deliberate = new Set<string>();
    for (const c of [a, b]) c.onRefused((r) => { refusals.push(r); });
    try {
      const mk = (name: string, conn: typeof a, api: typeof game.apiA): Seat => {
        const player = playerOf(conn, g);
        const undo = new UndoPrediction();
        conn.onEvents((events) => undo.events(events));
        return { name, conn, api, box: player.box, undo, violations: player.violations };
      };
      const seatA = mk('A', a, game.apiA), seatB = mk('B', b, game.apiB);
      for (const c of [a, b]) expect(waitHolds(c.state.game!, c.state.projection!.board, c.state.projection!.lastSeq)).toBe(false);

      // The deliberate refusal: a team answering an Ask that is not addressed to it is refused in Godot's words.
      await until('an open Ask', () => (a.state.projection?.openAsks.size ?? 0) > 0);
      const open = [...a.state.projection!.openAsks.values()][0]!.ask;
      const [stranger, strangerTeam] = open.teams.includes(t1.id) ? [b, t2.id] : [a, t1.id];
      const outcome = await stranger.sendCommand({ kind: 'answer_ask', askId: open.id, answer: { kind: 'passed' }, teamId: strangerTeam });
      const refused = outcome.refused ?? await new Promise<Refused | null>((resolve) => {
        const stop = stranger.onRefused((r) => { if (r.messageId === outcome.id) { stop(); resolve(r); } });
        setTimeout(() => { stop(); resolve(null); }, 3000);
      });
      expect(refused).not.toBeNull();
      deliberate.add(refused!.messageId);
      expect(refusalText(refused!)).toMatch(/^(Not allowed|Against the rules|Not one of the choices offered|Not at this point of the game|Refused): /);

      // Play: each seat whose box has work takes a step. A's first placement is undone at once, then placed again.
      let undone = false;
      let steps = 0;
      const roundOf = () => a.state.projection!.scheduler.round;
      while (roundOf() < 2 && steps < 600) {
        let acted = false;
        for (const seat of [seatA, seatB]) {
          const placing = seat.box.deployUnit;
          if (await step(seat, g)) {
            acted = true;
            steps++;
            if (seat === seatA && !undone && placing !== null && seat.box.deployUnit === null) {
              const plan = undoPlanOf(a.state.log, new Names(a.state.game!, a.state.projection!));
              expect(plan).not.toBeNull();
              await seat.undo.undoTo(a.state, plan!.toSeq, (c) => a.sendCommand(c));
              await until('the undone on both logs', () => [a, b].every((c) => c.state.log.some((e) => e.payload.kind === 'undone')));
              await until('A mid-deploy again', () => midDeploy(a.state.projection!, a.state.log, t1.id)?.unitId === placing
                || midDeploy(a.state.projection!, a.state.log, t2.id)?.unitId === placing);
              undone = true;
            }
          }
        }
        if (!acted) await new Promise((r) => setTimeout(r, 100));
      }
      expect(roundOf()).toBe(2);
      expect(undone).toBe(true);
      await until('both connections to agree', () => a.state.projection!.lastSeq === b.state.projection!.lastSeq);

      const live = effectiveLog(a.state.log);
      const start1 = live.find((e) => e.payload.kind === 'round_started' && e.payload.round === 1)!;
      const start2 = live.find((e) => e.payload.kind === 'round_started' && e.payload.round === 2)!;
      // Every model of both armies went from the shelf to the table before round 1 began.
      const deployed = new Set<string>();
      for (const e of live) {
        if (e.seq >= start1.seq) break;
        if (e.payload.kind === 'models_moved') for (const m of e.payload.moves) if (m.to.kind === 'table') deployed.add(m.modelId);
      }
      const all = [...a.state.projection!.models.keys()];
      expect(all.filter((id) => !deployed.has(id))).toEqual([]);
      // Every unit with a model on the table when round 1 ended was activated in it.
      const end1 = live.filter((e) => e.payload.kind === 'round_ended').find((e) => e.seq < start2.seq && e.seq > start1.seq);
      const atEnd = projectTo(a.state.board!, a.state.game!, a.state.log, end1 === undefined ? start2.seq - 1 : end1.seq);
      const onTable = new Set([...atEnd.models.values()].filter((m) => m.location.kind === 'table').map((m) => m.unitId));
      const recorded = new Set(live.filter((e) => e.seq > start1.seq && e.seq < start2.seq && e.payload.kind === 'action_recorded').map((e) => (e.payload as { unitId: string }).unitId));
      expect([...onTable].filter((u) => !recorded.has(u))).toEqual([]);
      // The combat log has both teams' round 1 headers, the header says round 2, and nothing was refused but the deliberate case.
      const headers = build(a.state.log, new Names(a.state.game!, a.state.projection!)).flatMap((e) => (e.type === 'block' ? [e.header] : []));
      expect(headers.some((h) => h.startsWith('R1 · Team 1 · '))).toBe(true);
      expect(headers.some((h) => h.startsWith('R1 · Team 2 · '))).toBe(true);
      expect(headerOf(a.state.projection!, a.state.game!, new Names(a.state.game!, a.state.projection!)).round).toBe(2);
      expect(refusals.filter((r) => !deliberate.has(r.messageId)).map((r) => refusalText(r))).toEqual([]);
    } finally {
      await game.cleanup();
    }
  }, 300_000);
});
