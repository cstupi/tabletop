// What the end-to-end suites share: dev sessions under fixed names, army import over the dev route, the sweep that deletes what a suite's
// accounts made, a game taken from the lobby to playing by two accounts through the same modules the pages use, and a player's decision
// box with D2's move hooks. Every suite deletes its games and armies in `finally`, so the Open games list stays clean.

import { readFileSync } from 'node:fs';
import { expect } from 'vitest';
import { apiOf } from '../../src/lib/api/client';
import { listGames } from '../../src/lib/live/games';
import { connectGame, type ConnectionState, type GameConnection } from '../../src/lib/connection/gameConnection';
import { acting, teamsOf } from '../../src/lib/lobby/seats';
import type { Geometry } from '../../src/lib/geometry';
import { DecisionBox } from '../../src/lib/play/decisionBox';
import { moveHooks } from '../../src/lib/play/decisionBox.move';
import { Violations } from '../../src/lib/play/violations';
import { DEFAULT_SETTINGS } from '../../src/lib/play/settings';
import { RuleTexts } from '../../src/lib/play/ruleTexts';
import { loadEdit } from '../../src/lib/setup/layouts';
import type { Edit, Team } from '../../src/lib/protocol/wire.generated';

/** Management's /v1: the compose service's port; `TABLETOP_E2E_MANAGEMENT` points the suites elsewhere (e.g. the website's proxy on :5173). */
export const MANAGEMENT = process.env.TABLETOP_E2E_MANAGEMENT ?? 'http://127.0.0.1:5100';
export const SOCKET = 'ws://127.0.0.1:5200/ws';
export const BOOKS = ['z65fgu0l29i4lnlu', '78qp9l5alslt6yj8'];

export async function session(name: string): Promise<string> {
  const response = await fetch(`${MANAGEMENT}/v1/dev/sessions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name }) });
  if (!response.ok) throw new Error(`dev session for ${name}: ${response.status}`);
  return ((await response.json()) as { sessionJwt: string }).sessionJwt;
}

export const AMBUSHER = 'Veterans';

/** The book's list; `ambush` names a unit that also carries Ambush (p.13), added as the list names its rules. */
export async function importArmy(jwt: string, book: string, ambush?: string): Promise<string> {
  const list = JSON.parse(readFileSync(`../sim/Lifecycle/Armies/${book}.json`, 'utf8')) as { units: { name: string; rules: unknown[] }[] };
  if (ambush !== undefined) {
    const unit = list.units.find((u) => u.name === ambush);
    if (unit === undefined) throw new Error(`book ${book} has no unit named ${ambush}`);
    unit.rules.push({ name: 'Ambush', label: 'Ambush' });
  }
  const response = await fetch(`${MANAGEMENT}/v1/dev/armies/import`, {
    method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${jwt}` }, body: JSON.stringify({ source: 'lifecycle', list }),
  });
  if (response.status !== 201) throw new Error(`army import answered ${response.status}: ${await response.text()}`);
  return ((await response.json()) as { id: string }).id;
}

export async function deleteArmy(jwt: string, id: string): Promise<void> {
  await fetch(`${MANAGEMENT}/v1/armies/${id}`, { method: 'DELETE', headers: { authorization: `Bearer ${jwt}` } });
}

/** A fixed e2e account's name: the same account every run, so a sweep finds what an interrupted run left behind. */
export const accountName = (label: string, seat: string): string => `viewer-e2e-${label}-${seat}`;

/** Deletes every game the account created and every army it owns; errors are swallowed (cleanup never fails a test). */
export async function sweep(jwt: string): Promise<void> {
  try {
    const me = ((await (await fetch(`${MANAGEMENT}/v1/account`, { headers: { authorization: `Bearer ${jwt}` } })).json()) as { id: string }).id;
    const api = apiOf(async () => jwt, MANAGEMENT);
    const games = await listGames(jwt, {}, MANAGEMENT);
    await Promise.all(games.filter((g) => g.createdBy === me).map((g) => api.deleteGame(g.id).catch(() => undefined)));
    await Promise.all((await api.listArmies()).map((a) => deleteArmy(jwt, a.id).catch(() => undefined)));
  } catch { /* the stack is down or the account is new: nothing to sweep */ }
}

export function waitFor(conn: GameConnection, ready: (s: ConnectionState) => boolean, what: string, ms = 15_000): Promise<ConnectionState> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { stop(); reject(new Error(`timed out waiting for ${what}`)); }, ms);
    const stop = conn.subscribe((s) => { if (ready(s)) { clearTimeout(timer); queueMicrotask(() => stop()); resolve(s); } });
  });
}

/** A player's box with D2's move hooks over real geometry. The browser's gate is off (silent), so the server is the one to refuse. */
export function playerOf(conn: GameConnection, g: Geometry) {
  const violations = new Violations();
  const hooks = moveHooks({ geometry: () => g, violations, enforcement: () => 'silent' });
  const box = boxOf(conn, hooks);
  return { conn, box, violations, hooks };
}

export const boxOf = (conn: GameConnection, hooks: ReturnType<typeof moveHooks> | null = null): DecisionBox => {
  const myTeams = () => (conn.state.game === null ? [] : teamsOf(conn.state.game, conn.state.participantId));
  const box = new DecisionBox({
    state: () => conn.state, myTeams, acting: () => acting(conn.state.projection, myTeams(), conn.state.log),
    settings: () => DEFAULT_SETTINGS, updateSettings: () => {}, rules: () => RuleTexts.empty,
    send: (command) => conn.sendCommand(command), onRefused: (l) => conn.onRefused((r) => l(r)),
  }, hooks);
  conn.subscribe(() => box.render());
  return box;
};

/** Two accounts take a team each in a new game and end setup: both connections are playing. `cleanup` closes them and deletes the game and
 *  both armies; call it in `finally`. A failure on the way cleans up before it throws. */
export async function playingGame(label: string, enforcement: 'strict' | 'warn' | 'silent' = 'strict', options: { layout?: boolean; ambush?: string } = {}) {
  const [jwtA, jwtB] = await Promise.all([accountName(label, 'a'), accountName(label, 'b')].map(session)) as [string, string];
  await Promise.all([sweep(jwtA), sweep(jwtB)]);
  const cleanup = async () => { a?.close(); b?.close(); await Promise.all([sweep(jwtA), sweep(jwtB)]); };
  const apiA = apiOf(async () => jwtA, MANAGEMENT);
  const apiB = apiOf(async () => jwtB, MANAGEMENT);
  let a: GameConnection | undefined, b: GameConnection | undefined;
  let t1!: Team, t2!: Team;
  let armyA!: string, armyB!: string, gameId!: string;
  try {
    [armyA, armyB] = await Promise.all([importArmy(jwtA, BOOKS[0]!, options.ambush), importArmy(jwtB, BOOKS[1]!)]);
    const properties = (await apiA.listProperties())[0]!;
    const created = await apiA.createGame({
      name: `${label} e2e`, propertiesHash: properties.contentHash, schedulerShape: 'alternating-activation', enforcement, waitsBetweenSteps: false,
    });
    gameId = created.id;
    await apiB.joinGame(created.id);
    a = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtA });
    b = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtB });
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
    if (options.layout === true) {                                    // a built-in table with terrain, as the setup page's Load does
      const [table] = await apiA.builtinLayouts();
      await sendOk(a, loadEdit(table!));
    }
    await sendOk(a, { kind: 'end_setup' });
    await Promise.all([waitFor(a, (s) => s.game?.status === 'playing' && s.projection !== null, 'A playing'), waitFor(b, (s) => s.game?.status === 'playing' && s.projection !== null, 'B playing')]);
  } catch (e) { await cleanup(); throw e; }
  return { a: a!, b: b!, t1, t2, jwtA, jwtB, apiA, apiB, armyA, armyB, gameId, cleanup };
}

/** One account takes both teams, an army each (team 1's carrying `ambush`), and ends setup: one connection, playing. */
export async function oneAccountGame(label: string, options: { ambush?: string } = {}) {
  const jwt = await session(accountName(label, 'a'));
  await sweep(jwt);
  const api = apiOf(async () => jwt, MANAGEMENT);
  let a: GameConnection | undefined;
  const cleanup = async () => { a?.close(); await sweep(jwt); };
  let t1!: Team, t2!: Team;
  try {
    const army1 = await importArmy(jwt, BOOKS[0]!, options.ambush);
    const army2 = await importArmy(jwt, BOOKS[1]!);
    const properties = (await api.listProperties())[0]!;
    const created = await api.createGame({
      name: `${label} e2e`, propertiesHash: properties.contentHash, schedulerShape: 'alternating-activation', enforcement: 'strict', waitsBetweenSteps: false,
    });
    a = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwt });
    await waitFor(a, (s) => s.phase === 'connected', 'connected');
    const sendOk = async (edit: Edit) => expect((await a!.sendEdit(edit)).refused).toBeNull();
    while ((a.state.game?.teams.length ?? 0) < 2) await sendOk({ kind: 'create_team' });
    [t1, t2] = [...a.state.game!.teams].sort((x, y) => x.ordinal - y.ordinal) as [Team, Team];
    await sendOk({ kind: 'pick_team', teamId: t1.id });
    await sendOk({ kind: 'pick_team', teamId: t2.id });           // the second keeps the first: one account plays both sides
    await sendOk({ kind: 'pick_army', armyId: army1, teamId: t1.id });
    await sendOk({ kind: 'pick_army', armyId: army2, teamId: t2.id });
    await sendOk({ kind: 'start_setup' });
    await waitFor(a, (s) => s.game?.status === 'setup' && s.draft !== null, 'in setup');
    await sendOk({ kind: 'end_setup' });
    await waitFor(a, (s) => s.game?.status === 'playing' && s.projection !== null, 'playing');
  } catch (e) { await cleanup(); throw e; }
  return { a: a!, t1, t2, jwt, api, cleanup };
}

export const until = async (what: string, cond: () => boolean, ms = 10_000) => {
  for (let i = 0; i < ms / 50 && !cond(); i++) await new Promise((r) => setTimeout(r, 50));
  if (!cond()) throw new Error(`timed out waiting for ${what}`);
};

