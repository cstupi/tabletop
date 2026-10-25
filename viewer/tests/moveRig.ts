// A decision box with D2's move hooks over a scripted connection: the sends wait until the test settles them.
import type { ConnectionState, SendOutcome } from '../src/lib/connection/gameConnection';
import type { Contact, Geometry } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import { acting, teamsOf } from '../src/lib/lobby/seats';
import { DecisionBox, type Control, type DecisionPanel } from '../src/lib/play/decisionBox';
import { moveHooks } from '../src/lib/play/decisionBox.move';
import { PendingDrops } from '../src/lib/play/pendingDrops';
import { RuleTexts } from '../src/lib/play/ruleTexts';
import { DEFAULT_SETTINGS } from '../src/lib/play/settings';
import { Violations } from '../src/lib/play/violations';
import type { Command, Enforcement, EventPayload, GameInfo, Location, MoveRegion, Participant, Placement, Refused } from '../src/lib/protocol/wire.generated';
import { P1, P2, askOf, boardOf, emptyTable, gameOf, slotOf, unitOf } from './play';

export const at = (x: number, y: number, yaw = 0): Placement => ({ position: { x, y, z: 0 }, yaw: { degrees: yaw } });
export const onTable = (x: number, y: number): Location => ({ kind: 'table', placement: at(x, y) });

export function region(over: Partial<MoveRegion> & { modelIds: string[] }): MoveRegion {
  return { kind: 'move_region', allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false, ignoresTerrain: false, ...over };
}

export function moveRig(over: { enforcement?: Enforcement; squad?: number; participantId?: string; participants?: Participant[] } = {}) {
  const squad = unitOf('Squad', Array.from({ length: over.squad ?? 2 }, (_, i) => slotOf({ name: `S${i + 1}`, ordinal: i + 1 })));
  const rivals = unitOf('Rivals', [slotOf({ name: 'Rival' })]);
  const transport = unitOf('Truck', [slotOf({ name: 'Truck', base: { shape: 'circle', a: 4, b: 4 } })]);
  const reserve = unitOf('Reserve', [slotOf({ name: 'R1', ordinal: 1 }), slotOf({ name: 'R2', ordinal: 2 })]);
  const game: GameInfo = gameOf({ enforcement: over.enforcement ?? 'warn' }, over.participants);
  const me = over.participantId ?? P1;
  const board = boardOf([{ participantId: P1, units: [squad, transport, reserve] }, { participantId: P2, units: [rivals] }], emptyTable());
  const applier = new Applier(board, game);
  let seq = 0;
  const apply = (...payloads: EventPayload[]): void => { for (const p of payloads) applier.apply({ seq: ++seq, actor: P1, payload: p } as never); };
  const ids = squad.models.map((m) => m.id);
  apply({ kind: 'models_moved', moves: [
    ...ids.map((id, i) => ({ modelId: id, from: { kind: 'shelf' } as const, to: onTable(10 + 2 * i, 10) })),
    { modelId: rivals.models[0]!.id, from: { kind: 'shelf' }, to: onTable(40, 30) },
    { modelId: transport.models[0]!.id, from: { kind: 'shelf' }, to: onTable(20, 10) },
  ] });

  const sent: Command[] = [];
  const pending: { id: string; resolve: (o: SendOutcome) => void; reject: (e: unknown) => void }[] = [];
  const listeners = new Set<(r: Refused) => void>();
  const violations = new Violations();
  const drops = new PendingDrops();
  let found = new Map<string, Contact>();
  const contactCalls: unknown[][] = [];
  const geometry = {
    contacts: (...args: unknown[]) => { contactCalls.push(args); return found; },
  } as unknown as Geometry;
  let reconnecting = false;
  let enforcement = game.enforcement;
  const state = (): ConnectionState => (reconnecting
    ? { phase: 'reconnecting', game: null, participantId: me, board: null, projection: null, log: [], maxWounds: new Map() }
    : { phase: 'connected', game, participantId: me, board: applier.projection.board, projection: applier.projection, log: applier.log, maxWounds: new Map() }) as unknown as ConnectionState;
  const hooks = moveHooks({ geometry: () => geometry, violations, enforcement: () => enforcement, drops });
  const box = new DecisionBox({
    state, myTeams: () => teamsOf(game, me), acting: () => acting(applier.projection, teamsOf(game, me), applier.log),
    settings: () => DEFAULT_SETTINGS, updateSettings: () => {}, rules: () => RuleTexts.empty,
    send: (command) => new Promise<SendOutcome>((resolve, reject) => { sent.push(command); pending.push({ id: `m${sent.length}`, resolve, reject }); }),
    onRefused: (l) => { listeners.add(l); return () => { listeners.delete(l); }; },
  }, hooks);

  const r = {
    game, applier, box, hooks, violations, drops, sent, squad, rivals, transport, reserve, ids, apply,
    rival: rivals.models[0]!.id, truck: transport.models[0]!.id, reserveIds: reserve.models.map((m) => m.id),
    contactCalls,
    setFound(next: Map<string, Contact>) { found = next; },
    setEnforcement(e: Enforcement) { enforcement = e; },
    reconnect(on: boolean) { reconnecting = on; },
    openMove(legal: MoveRegion, over: Parameters<typeof askOf>[1] = {}) {
      const ask = askOf(legal, { question: 'advance', prompt: 'Advance', ...over });
      apply({ kind: 'ask_opened', ask });
      box.render();
      return ask;
    },
    /** A deploy pick of the Reserve unit for team 1 that has closed: the unit's models are still on the shelf. */
    openDeployPick() {
      const ask = askOf({ kind: 'pieces', eligible: [reserve.id], min: 1, max: 1 }, { question: 'deploy', prompt: 'Deploy a unit' });
      apply({ kind: 'ask_opened', ask });
      apply({ kind: 'ask_closed', askId: ask.id, answers: [{ teamId: ask.teams[0]!, answer: { kind: 'selected_pieces', pieceIds: [reserve.id] } }] } as never);
      box.render();
      return ask;
    },
    get panel(): DecisionPanel { return box.panel; },
    control(id: string): Control | undefined { return box.panel.controls.find((c) => 'id' in c && c.id === id); },
    disabled(id: string): boolean { const c = r.control(id); return c?.kind === 'button' ? c.disabled : false; },
    reason(): string { const c = r.control('reason'); return c?.kind === 'reason' ? c.text : ''; },
    accept(i = pending.length - 1) { pending[i]!.resolve({ id: pending[i]!.id, refused: null }); },
    refuse(i: number, detail: string) {
      const frame: Refused = { kind: 'refused', messageId: pending[i]!.id, reason: 'illegal', detail };
      pending[i]!.resolve({ id: pending[i]!.id, refused: frame });
      for (const l of listeners) l(frame);
    },
    fail(i: number, e: unknown) { pending[i]!.reject(e); },
  };
  return r;
}

export const tick = async (): Promise<void> => { for (let i = 0; i < 6; i++) await Promise.resolve(); };
