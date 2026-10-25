// A game in play, from the browser modules, against the running stack (docker compose up -d): two accounts import an army each over the
// dev route, take a team each, end setup, and answer the server's Asks through the decision box controller on their real connections.
// Skipped unless TABLETOP_E2E=1. Run: npm run test:e2e.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { apiOf } from '../../src/lib/api/client';
import { connectGame, type ConnectionState, type GameConnection } from '../../src/lib/connection/gameConnection';
import { acting, teamsOf } from '../../src/lib/lobby/seats';
import { DecisionBox } from '../../src/lib/play/decisionBox';
import { DEFAULT_SETTINGS } from '../../src/lib/play/settings';
import { RuleTexts } from '../../src/lib/play/ruleTexts';
import type { Edit } from '../../src/lib/protocol/wire.generated';

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

const boxOf = (conn: GameConnection): DecisionBox => {
  const myTeams = () => (conn.state.game === null ? [] : teamsOf(conn.state.game, conn.state.participantId));
  const box = new DecisionBox({
    state: () => conn.state, myTeams, acting: () => acting(conn.state.projection, myTeams(), conn.state.log),
    settings: () => DEFAULT_SETTINGS, updateSettings: () => {}, rules: () => RuleTexts.empty,
    send: (command) => conn.sendCommand(command), onRefused: (l) => conn.onRefused((r) => l(r)),
  });
  conn.subscribe(() => box.render());
  return box;
};

describe.skipIf(process.env.TABLETOP_E2E !== '1')('browser play, the decision box on the real server', () => {
  it('answers the Asks the server puts to each team and shows its refusals', async () => {
    const stamp = Date.now();
    const [jwtA, jwtB] = await Promise.all([`d1-a-${stamp}`, `d1-b-${stamp}`].map(session)) as [string, string];
    const apiA = apiOf(async () => jwtA, MANAGEMENT);
    const apiB = apiOf(async () => jwtB, MANAGEMENT);
    const [armyA, armyB] = await Promise.all([importArmy(jwtA, BOOKS[0]!), importArmy(jwtB, BOOKS[1]!)]);
    const properties = (await apiA.listProperties())[0]!;
    const created = await apiA.createGame({
      name: `d1 e2e ${stamp}`, propertiesHash: properties.contentHash, schedulerShape: 'alternating-activation', enforcement: 'strict', waitsBetweenSteps: false,
    });
    await apiB.joinGame(created.id);
    const a = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtA });
    const b = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtB });
    try {
      await Promise.all([waitFor(a, (s) => s.phase === 'connected', 'A connected'), waitFor(b, (s) => s.phase === 'connected', 'B connected')]);
      const sendOk = async (conn: GameConnection, edit: Edit) => expect((await conn.sendEdit(edit)).refused).toBeNull();
      while ((a.state.game?.teams.length ?? 0) < 2) await sendOk(a, { kind: 'create_team' });
      await waitFor(b, (s) => (s.game?.teams.length ?? 0) >= 2, 'B sees two teams');
      const [t1, t2] = [...a.state.game!.teams].sort((x, y) => x.ordinal - y.ordinal);
      await sendOk(a, { kind: 'pick_team', teamId: t1!.id });
      await sendOk(b, { kind: 'pick_team', teamId: t2!.id });
      await sendOk(a, { kind: 'pick_army', armyId: armyA, teamId: t1!.id });
      await sendOk(b, { kind: 'pick_army', armyId: armyB, teamId: t2!.id });
      await waitFor(a, (s) => s.game!.participants.every((p) => p.armyId !== undefined), 'both armies picked');
      await sendOk(a, { kind: 'start_setup' });
      await waitFor(a, (s) => s.game?.status === 'setup' && s.draft !== null, 'A in setup');
      await sendOk(a, { kind: 'end_setup' });
      await Promise.all([waitFor(a, (s) => s.game?.status === 'playing' && s.projection !== null, 'A playing'), waitFor(b, (s) => s.game?.status === 'playing' && s.projection !== null, 'B playing')]);

      const boxA = boxOf(a), boxB = boxOf(b);
      // Somebody is asked first; the box of the team addressed shows the Ask and the other shows who the game waits on.
      const until = async (what: string, cond: () => boolean, ms = 10_000) => {
        for (let i = 0; i < ms / 50 && !cond(); i++) await new Promise((r) => setTimeout(r, 50));
        if (!cond()) throw new Error(`timed out waiting for ${what}`);
      };
      await until('an open Ask', () => (a.state.projection?.openAsks.size ?? 0) > 0);
      const firstSeq = a.state.projection!.lastSeq;
      const open = [...a.state.projection!.openAsks.values()][0]!.ask;
      const addressed = open.teams.includes(t1!.id) ? boxA : boxB;
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
