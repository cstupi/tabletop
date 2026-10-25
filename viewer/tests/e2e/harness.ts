// What the end-to-end suites share: dev sessions, army import over the dev route, a game taken from the lobby to playing by two accounts
// through the same modules the pages use, and a player's decision box with D2's move hooks.

import { readFileSync } from 'node:fs';
import { expect } from 'vitest';
import { apiOf } from '../../src/lib/api/client';
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

export const MANAGEMENT = 'http://127.0.0.1:5100';
export const SOCKET = 'ws://127.0.0.1:5200/ws';
export const BOOKS = ['z65fgu0l29i4lnlu', '78qp9l5alslt6yj8'];

export async function session(name: string): Promise<string> {
  const response = await fetch(`${MANAGEMENT}/v1/dev/sessions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name }) });
  if (!response.ok) throw new Error(`dev session for ${name}: ${response.status}`);
  return ((await response.json()) as { sessionJwt: string }).sessionJwt;
}

export async function importArmy(jwt: string, book: string): Promise<string> {
  const list = JSON.parse(readFileSync(`../sim/Lifecycle/Armies/${book}.json`, 'utf8')) as unknown;
  const response = await fetch(`${MANAGEMENT}/v1/dev/armies/import`, {
    method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${jwt}` }, body: JSON.stringify({ source: 'lifecycle', list }),
  });
  if (response.status !== 201) throw new Error(`army import answered ${response.status}: ${await response.text()}`);
  return ((await response.json()) as { id: string }).id;
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

/** Two accounts take a team each in a new game and end setup: both connections are playing. */
export async function playingGame(label: string, enforcement: 'strict' | 'warn' | 'silent' = 'strict', options: { layout?: boolean } = {}) {
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
    if (options.layout === true) {                                    // a built-in table with terrain, as the setup page's Load does
      const [table] = await apiA.builtinLayouts();
      await sendOk(a, loadEdit(table!));
    }
    await sendOk(a, { kind: 'end_setup' });
    await Promise.all([waitFor(a, (s) => s.game?.status === 'playing' && s.projection !== null, 'A playing'), waitFor(b, (s) => s.game?.status === 'playing' && s.projection !== null, 'B playing')]);
  } catch (e) { a.close(); b.close(); throw e; }
  return { a, b, t1, t2, jwtA, jwtB, apiA, apiB, armyA, armyB, gameId: created.id };
}

export const until = async (what: string, cond: () => boolean, ms = 10_000) => {
  for (let i = 0; i < ms / 50 && !cond(); i++) await new Promise((r) => setTimeout(r, 50));
  if (!cond()) throw new Error(`timed out waiting for ${what}`);
};

