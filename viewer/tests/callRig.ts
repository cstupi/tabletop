// The calls agreement's rig: a case of tests/fixtures/calls.json (written by tests/Tabletop.Client.Tests/CallsViewerFixtureTests.cs from
// Godot's Core builders) replayed through the browser's own modules, as the pages press them. See calls.agreement.test.ts.
import { readFileSync } from 'node:fs';
import packedCatalog from '@builtin-catalog';
import type { ConnectionState, SendOutcome } from '../src/lib/connection/gameConnection';
import type { Geometry } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import { createTeam, endSetup, startSetup, watch } from '../src/lib/lobby/lobbyEdits';
import { armySeats, pickArmy } from '../src/lib/lobby/lobbyView';
import { columnsOf, moveTo } from '../src/lib/lobby/teamColumns';
import { configurationOf } from '../src/lib/lobby/rulesOptions';
import { acting, teamsOf } from '../src/lib/lobby/seats';
import { mine as myAsk } from '../src/lib/play/askView';
import { build, flatten } from '../src/lib/play/combatLog';
import { DecisionBox } from '../src/lib/play/decisionBox';
import { moveHooks } from '../src/lib/play/decisionBox.move';
import { choicesOf, decisionOf, number as numberChoices } from '../src/lib/play/decisionView';
import { midDeploy, pickable } from '../src/lib/play/deployView';
import { answerOf, gameOverOf } from '../src/lib/play/gameOverView';
import { handMoveCommand, markerMove } from '../src/lib/play/handMove';
import { undoHere } from '../src/lib/play/logRows';
import { MarkerLocks } from '../src/lib/play/markerLocks';
import { choose, pieceMenu, rosterMenu, teamMenu, type MenuContext } from '../src/lib/play/menus';
import { Names } from '../src/lib/play/names';
import { PendingDrops } from '../src/lib/play/pendingDrops';
import { RuleTexts } from '../src/lib/play/ruleTexts';
import { UndoPrediction, undoPlanOf } from '../src/lib/play/undo';
import { Violations } from '../src/lib/play/violations';
import { waitingLines, waitingTeams } from '../src/lib/play/waiting';
import { dropEdit } from '../src/lib/setup/drag';
import {
  addAsset, addBuiltin, clearTerrain, dropBuiltin, objective, overrideVolume, removeMarker, removeTerrain, resetVolumes, scale, setupMenu, swapAsset,
  swapBuiltin, swapZones, tableSize, texture, withBottom, withDeclaration, withFootprint, withKind, withNotes, withTop, zones,
} from '../src/lib/setup/edits';
import { loadEdit, randomEdit } from '../src/lib/setup/layouts';
import { parseCatalog } from '../src/lib/setup/catalog';
import type { Board, Command, Edit, GameEvent, GameInfo, TableState } from '../src/lib/protocol/wire.generated';

/* eslint-disable @typescript-eslint/no-explicit-any */
export interface CallCase {
  name: string; log: string; upTo: number; status: 'playing' | 'lobby' | 'setup'; participantId?: string; creator?: boolean;
  maxWounds: Record<string, number>; deployInside: Record<string, string[]>; maySetAside: string[];
  grantable: { label: string; entryName: string; effectId: string }[]; undoneTo?: number;
  choice: any; decisions: any; message: unknown | null;
}

export interface CallLogFiles { readonly welcome: string; readonly board: string; readonly events: string }

const cache = new Map<string, CallLogFiles>();
/** tests/fixtures/<name>.{welcome,board,events}.json, the raw texts, cached. */
export function readLog(name: string): CallLogFiles {
  let files = cache.get(name);
  if (files === undefined) {
    const text = (s: string): string => readFileSync(`tests/fixtures/${name}.${s}.json`, 'utf8');
    files = { welcome: text('welcome'), board: text('board'), events: text('events') };
    cache.set(name, files);
  }
  return files;
}

const parsed = (log: CallLogFiles) => ({
  welcome: JSON.parse(log.welcome) as { participantId: string; game: GameInfo; options?: any },
  board: (JSON.parse(log.board) as { board: Board }).board,
  events: (JSON.parse(log.events) as { events: GameEvent[] }).events,
});

const catalog = parseCatalog(packedCatalog);

/** The state a case decides in: a playing log cut at `upTo` (and the Undone marker an undo would append), or a lobby or setup game with the board's table as the draft. */
export function stateOf(c: CallCase, log: CallLogFiles): ConnectionState {
  const { welcome, board, events } = parsed(log);
  const common = {
    participantId: c.participantId ?? null, options: welcome.options ?? { options: [], presets: [] }, zones: null, terrainWords: null, terrainRules: null, markerRange: null, catchingUp: false, closeReason: null,
    maxWounds: new Map(Object.entries(c.maxWounds)), deployInside: new Map(Object.entries(c.deployInside)), maySetAside: new Set(c.maySetAside), threats: new Map(), inCover: new Set(), phase: 'connected' as const,
  };
  if (c.status !== 'playing')
    return { ...common, game: { ...welcome.game, status: c.status }, draft: board.table, board: null, log: [], projection: null } as ConnectionState;
  const applier = new Applier(board, welcome.game);
  applier.applyAll(events.filter((e) => e.seq <= c.upTo));
  if (c.undoneTo !== undefined) applier.apply({ seq: c.upTo + 1, actor: c.participantId, payload: { kind: 'undone', toSeq: c.undoneTo } } as GameEvent);
  return { ...common, game: welcome.game, draft: null, board, log: applier.log, projection: applier.projection } as ConnectionState;
}

function contextOf(c: CallCase, st: ConnectionState): MenuContext {
  const p = st.projection!, game = st.game!;
  const myTeam = acting(p, teamsOf(game, st.participantId), st.log);
  return {
    game, p, log: st.log, watching: false, myTeam, mine: myAsk(p, game, myTeam), grantable: c.grantable,
    deployInside: new Map(Object.entries(c.deployInside)), maySetAside: new Set(c.maySetAside), locks: new MarkerLocks(),
  };
}

function menuOf(c: CallCase, st: ConnectionState) {
  const ctx = contextOf(c, st);
  const open = c.choice.open;
  if (open.piece !== undefined) return pieceMenu(ctx, open.piece, open.shift);
  if (open.team !== undefined) return teamMenu(ctx, open.team, new Names(st.game!, st.projection));
  return rosterMenu(ctx, open.roster!);
}

/** The game and board a waiting case reads: less the players of one team, or less the armies of one. */
function waited(st: ConnectionState, dropPlayers: number, dropArmies: number, board: Board): { game: GameInfo; board: Board } {
  const game = st.game!;
  const teamId = (ordinal: number): string | undefined => game.teams.find((t) => t.ordinal === ordinal)?.id;
  const seatsOf = (ordinal: number): Set<string> => new Set(game.participants.filter((p) => ordinal > 0 && p.teamId === teamId(ordinal)).map((p) => p.id));
  const gone = seatsOf(dropPlayers), noArmy = seatsOf(dropArmies);
  return {
    game: { ...game, participants: game.participants.filter((p) => !gone.has(p.id)) },
    board: { ...board, armies: board.armies.filter((a) => !gone.has(a.participantId) && !noArmy.has(a.participantId)) },
  };
}

/** The decisions a case holds, computed by the browser's functions: only the keys the fixture carries. */
export function decisionsOf(c: CallCase, st: ConnectionState, log: CallLogFiles): any {
  const want = Object.keys(c.decisions);
  const out: any = {};
  const game = st.game!;
  const pid = st.participantId;
  if (st.projection !== null) {
    const p = st.projection;
    const myTeams = teamsOf(game, pid);
    const team = acting(p, myTeams, st.log);
    const names = new Names(game, p);
    const mine = myAsk(p, game, team);
    const mid = [team, ...myTeams].map((t) => midDeploy(p, st.log, t)).find((m) => m !== null) ?? null;
    out.myTeams = myTeams; out.acting = team;
    out.mine = mine === null ? null : { askId: mine.ask.id, mode: mine.mode };
    out.midDeploy = mid === null ? null : { unitId: mid.unitId, question: mid.question, shelved: [...mid.shelvedModelIds] };
    if (want.includes('numbered')) {
      const numbered = c.choice.kind === 'unitKey' && mine !== null && mine.ask.legal.kind === 'options'
        ? numberChoices(choicesOf(mine.ask.legal, p, c.choice.unitId)) : decisionOf(p, game, names, team).numbered;
      out.numbered = numbered.map((n) => [n.key, n.optionId]);
    }
    if (want.includes('pickable')) out.pickable = pickable(p, mine!.ask);
    if (want.includes('menu')) out.menu = menuOf(c, st).map((i) => i.label);
    if (want.includes('undo')) {
      const entries = flatten(build(st.log, names));
      out.undo = c.decisions.undo.here !== undefined
        ? { here: entries.filter((e) => undoHere(e, false)).map((e) => e.seq) }
        : { toSeq: undoPlanOf(st.log, names)?.toSeq ?? null };
    }
  } else {
    if (want.includes('button')) {
      const j = columnsOf(game, pid, c.creator === true)[c.choice.column]!.join;
      out.button = j === null ? null : { text: j.text, enabled: j.edit !== null, why: j.why };
    }
    if (want.includes('lines')) {
      const w = waited(st, c.choice.dropPlayers, c.choice.dropArmies, parsed(log).board);
      out.lines = waitingLines(w.game, waitingTeams(w.game, w.board), pid).map((l) => l.text);
    }
  }
  return out;
}

const tick = async (): Promise<void> => { for (let i = 0; i < 8; i++) await Promise.resolve(); };

/** What reached send when the browser's control for this case's choice was pressed; null when nothing was sent. */
export async function replay(c: CallCase, st: ConnectionState, g: Geometry, log: CallLogFiles): Promise<unknown | null> {
  const choice = c.choice;
  if (st.projection === null) {
    if (choice.kind === 'drop') return dropEdit(st.draft as TableState, choice.pieceId, choice.current[choice.pieceId], choice.yaws[choice.pieceId]);
    return editOf(c, st, g, log);
  }
  const sent: Command[] = [];
  const game = st.game!, p = st.projection, pid = st.participantId;
  const send = (command: Command): Promise<SendOutcome> => { sent.push(command); return Promise.resolve({ id: `m${sent.length}`, refused: null }); };

  switch (choice.kind) {
    case 'menu': {
      const item = menuOf(c, st).find((i) => i.label === choice.label)!;
      const step = choose(item, choice.input ?? null);
      return step.kind === 'send' ? step.command : null;
    }
    case 'undoButton': {
      const plan = undoPlanOf(st.log, new Names(game, p));
      if (plan === null) return null;
      await new UndoPrediction().undoTo(st, plan.toSeq, send);
      return sent[0] ?? null;
    }
    case 'undoRow': {
      const e = flatten(build(st.log, new Names(game, p))).find((x) => x.seq === choice.seq)!;
      if (!undoHere(e, false)) return null;
      await new UndoPrediction().undoTo(st, e.seq, send);
      return sent[0] ?? null;
    }
    case 'drop': {
      const grabs = new Map<string, any>(Object.entries(choice.grabs));
      const landings = new Map<string, any>(Object.keys(choice.current).map((id) => [id, { position: choice.current[id], yaw: choice.yaws[id] }]));
      if ((choice.modelIds as string[]).every((id) => p.models.has(id))) return handMoveCommand(grabs, landings);
      if (p.markers.has(choice.pieceId) || p.board.table.markers.some((m) => m.id === choice.pieceId))
        return markerMove(choice.pieceId, choice.current[choice.pieceId], choice.yaws[choice.pieceId]);
      return null;
    }
    case 'gameOver': {
      const team = acting(p, teamsOf(game, pid), st.log);
      return answerOf(gameOverOf(p, game, team), choice.optionId, team);
    }
  }

  const myTeams = teamsOf(game, pid);
  const box = new DecisionBox({
    state: () => st, myTeams: () => myTeams, acting: () => acting(p, myTeams, st.log),
    rules: () => RuleTexts.empty,
    send, onRefused: () => () => {},
  }, moveHooks({ geometry: () => g, violations: new Violations(), enforcement: () => 'silent', drops: new PendingDrops() }));
  box.render();
  switch (choice.kind) {
    case 'option': box.press(`opt:${choice.optionId}`); break;
    case 'key': box.number(choice.key); break;
    case 'unitKey': box.unit(choice.unitId); box.number(choice.key); break;
    case 'click': box.target(choice.pieceId); break;
    case 'pass': box.press('pass'); break;
    case 'confirm': box.enter(); break;
    case 'spend':
      for (const [id, n] of Object.entries<number>(choice.values)) box.spin(`spin:${id}`, n);
      box.enter(); break;
    case 'pick':
      for (const id of choice.clicks as string[]) box.target(id);
      if (choice.done) box.enter();
      break;
    case 'pickKey': box.number(choice.key); break;
    case 'assign':
      for (const step of choice.steps as any[]) {
        if (step.pick !== undefined) box.press(`chip:${step.pick}`);
        else if (step.aim !== undefined) box.target(step.aim);
        else if (step.clear !== undefined) box.cancel();
      }
      box.enter(); break;
    case 'casualties':
      for (const step of choice.steps as any[]) {
        if (step.click !== undefined) box.model(step.click, false);
        else if (step.remove !== undefined) box.model(step.remove, true);
        else if (step.auto !== undefined) box.press('auto');
        else if (step.clear !== undefined) box.press('clear');
      }
      box.enter(); break;
    case 'move':
      box.draggedAll(Object.entries<any>(choice.ghosts));
      if (choice.stop) box.stop(); else box.enter();
      break;
    case 'embark': box.target(choice.containerId); break;
    case 'deploy':
      box.placeAt(choice.at);
      box.draggedAll(Object.entries<any>(choice.ghosts ?? {}));
      box.enter(); break;
    default: throw new Error(`no replay for choice ${choice.kind}`);
  }
  await tick();
  return sent[0] ?? null;
}

/** The lobby and setup controls, by the browser function each calls. */
function editOf(c: CallCase, st: ConnectionState, g: Geometry, log: CallLogFiles): Edit | null {
  const x = c.choice, game = st.game!, pid = st.participantId, draft = st.draft as TableState;
  const entry = (id: string) => catalog.terrain.get(id)!;
  const draws = (d: number[]): (() => number) => { const q = [...d]; return () => q.shift()!; };
  const piece = (id: string) => draft.terrain.find((t) => t.id === id)!;
  switch (x.control as string) {
    case 'columnJoin': return columnsOf(game, pid, c.creator === true)[x.column]!.join?.edit ?? null;
    case 'columnMove': {
      const team = x.team < 0 ? null : [...game.teams].sort((a, b) => a.ordinal - b.ordinal)[x.team]!.id;
      return moveTo(game, pid, c.creator === true, x.participantId, team);
    }
    case 'watch': return watch();
    case 'createTeam': return createTeam();
    case 'startSetup': return startSetup();
    case 'endSetup': return endSetup();
    case 'armySeat': { const seats = armySeats(game, pid); return x.seat < seats.length ? pickArmy(seats[x.seat]!, x.armyId) : null; }
    case 'waitingJoin': case 'waitingArmy': {
      const w = waited(st, x.dropPlayers, x.dropArmies, parsed(log).board);
      const lines = waitingLines(w.game, waitingTeams(w.game, w.board), pid);
      if (x.control === 'waitingJoin') return lines.find((l) => l.join !== null)?.join?.edit ?? null;
      const seat = lines.find((l) => l.army !== null)?.army;
      return seat === undefined || seat === null ? null : pickArmy(seat, x.armyId ?? ARMY);
    }
    case 'configure': {
      const options = st.options;
      const chosen: Record<string, string> = {};
      for (const o of options.options.slice(0, 1)) chosen[o.key] = o.values[1]!.value;
      return configurationOf(game, x.name, x.shape, x.enforcement, options, chosen);
    }
    case 'configureHash': {
      const options = st.options;
      const chosen: Record<string, string> = {};
      for (const o of options.options.slice(0, 1)) chosen[o.key] = o.values[1]!.value;
      return configurationOf(game, x.name, x.shape, x.enforcement, options, chosen, x.propertiesHash);
    }
    case 'size': return tableSize(x.width, x.length);
    case 'texture': return texture(x.builtin);
    case 'addBuiltin': return addBuiltin(entry(x.name), draft.size, draws(x.draws));
    case 'dropBuiltin': return dropBuiltin(g, entry(x.name), draft.terrain, { x: x.x, y: x.y });
    case 'addAsset': return addAsset(x.assetId, x.volumes, draft.size, draws(x.draws));
    case 'random': return randomEdit(draft, { seed: 0, terrain: x.terrain, unplaced: [], unlisted: [] });
    case 'clear': return clearTerrain(draft);
    case 'load': return loadEdit(x.layout);
    case 'objective': return objective(draft.size, draft.markers);
    case 'zones': return zones(x.style);
    case 'swapZones': return swapZones(x.a, x.b);
    case 'menuTurnLeft': case 'menuTurnRight': case 'menuFacing': case 'menuRemove': case 'menuRemoveMarker':
      return setupMenu(g, draft, x.pieceId).find((i) => i.label === x.label)!.edit(x.input ?? '');
    case 'scale': return scale(x.pieceId, x.scale);
    case 'swapMesh': return swapBuiltin(x.pieceId, x.builtin);
    case 'swapAsset': return swapAsset(x.pieceId, x.assetId);
    case 'remove': return removeTerrain(x.pieceId);
    case 'removeMarker': return removeMarker(x.markerId);
    case 'resetVolumes': return resetVolumes(x.pieceId, x.volumes);
    case 'volKind': return overrideVolume(piece(x.pieceId), x.volumeId, withKind(x.kind));
    case 'volA': return overrideVolume(piece(x.pieceId), x.volumeId, withFootprint('a', x.value));
    case 'volB': return overrideVolume(piece(x.pieceId), x.volumeId, withFootprint('b', x.value));
    case 'volBottom': return overrideVolume(piece(x.pieceId), x.volumeId, withBottom(x.value));
    case 'volTop': return overrideVolume(piece(x.pieceId), x.volumeId, withTop(x.value));
    case 'volCover': case 'volCoverOff': return overrideVolume(piece(x.pieceId), x.volumeId, withDeclaration('cover', x.on));
    case 'volNotes': case 'volNotesClear': return overrideVolume(piece(x.pieceId), x.volumeId, withNotes(x.text));
  }
  throw new Error(`no replay for control ${x.control}`);
}

const ARMY = '019a0000-0000-7000-8000-0000000000b1';
