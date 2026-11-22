import { describe, expect, it } from 'vitest';
import { SendError, type ConnectionState, type SendOutcome } from '../src/lib/connection/gameConnection';
import { Applier } from '../src/lib/live/apply';
import { acting, teamsOf } from '../src/lib/lobby/seats';
import { DecisionBox, type Control, type DecisionPanel } from '../src/lib/play/decisionBox';
import { RuleTexts } from '../src/lib/play/ruleTexts';
import type { Board, Command, EventPayload, GameInfo, LegalSet, Refused } from '../src/lib/protocol/wire.generated';
import { P1, P2, TEAM1, TEAM2, askOf, boardOf, gameOf, newId, option, sample, slotOf, unitOf } from './play';

type Deferred = { id: string; resolve: (o: SendOutcome) => void; reject: (e: unknown) => void };

function rig(over: { game?: GameInfo; board?: Board; maxWounds?: Record<string, number>; rules?: RuleTexts; participantId?: string } = {}) {
  const s = sample();
  const game = over.game ?? s.game;
  const applier = new Applier(over.board ?? s.board, game);
  let seq = 0;
  const sent: Command[] = [];
  const pending: Deferred[] = [];
  const listeners = new Set<(r: Refused) => void>();
  const participantId = over.participantId ?? P1;
  const state = (): ConnectionState => ({
    phase: 'connected', game, participantId, board: applier.projection.board, projection: applier.projection, log: applier.log,
    maxWounds: new Map(Object.entries(over.maxWounds ?? {})),
  }) as unknown as ConnectionState;
  const box = new DecisionBox({
    state,
    myTeams: () => teamsOf(game, participantId),
    acting: () => acting(applier.projection, teamsOf(game, participantId), applier.log),
    rules: () => over.rules ?? RuleTexts.empty,
    send: (command) => new Promise<SendOutcome>((resolve, reject) => {
      sent.push(command);
      pending.push({ id: `m${sent.length}`, resolve, reject });
    }),
    onRefused: (l) => { listeners.add(l); return () => { listeners.delete(l); }; },
  });
  const refusedFrame = (i: number, detail: string): Refused => ({ kind: 'refused', messageId: pending[i]!.id, reason: 'illegal', detail });
  const r = {
    s, game, applier, box, sent,
    apply(...payloads: EventPayload[]) { return r.applyAs(P1, ...payloads); },
    applyAs(actor: string, ...payloads: EventPayload[]) {
      const events = payloads.map((p) => ({ seq: ++seq, actor, payload: p }) as never);
      for (const e of events) applier.apply(e);
      return events as never[];
    },
    get panel(): DecisionPanel { return box.panel; },
    /** The card as words: a tile `1. Advance`, a button `Commit [⏎]`, a row `1. Battle Brothers`, a line its text. */
    labels(): string[] {
      return box.panel.controls.flatMap((c) => c.kind === 'group' ? c.tiles.map((t) => (t.key > 0 ? `${t.key}. ${t.word}` : t.word))
        : c.kind === 'button' ? [c.key !== undefined ? `${c.label} [${c.key}]` : c.label]
        : c.kind === 'row' ? [c.key !== '' ? `${c.key}. ${c.label}` : c.label]
        : 'label' in c ? [c.label] : 'text' in c ? [c.text] : []);
    },
    tile(id: string) { return box.panel.controls.flatMap((c) => (c.kind === 'group' ? c.tiles : [])).find((t) => t.id === id); },
    button(id: string): Extract<Control, { kind: 'button' }> | undefined { return box.panel.controls.find((c): c is Extract<Control, { kind: 'button' }> => c.kind === 'button' && c.id === id); },
    accept(i = pending.length - 1) { pending[i]!.resolve({ id: pending[i]!.id, refused: null }); },
    refuse(i: number, detail: string) {
      const frame = refusedFrame(i, detail);
      pending[i]!.resolve({ id: pending[i]!.id, refused: frame });
      for (const l of listeners) l(frame);                       // as GameConnection does: resolve, then onRefused
    },
    refuseLate(i: number, detail: string) { const frame = refusedFrame(i, detail); for (const l of listeners) l(frame); },
    fail(i: number, e: unknown) { pending[i]!.reject(e); },
  };
  return r;
}
const tick = async () => { for (let i = 0; i < 5; i++) await Promise.resolve(); };
const open = (ask: ReturnType<typeof askOf>): EventPayload => ({ kind: 'ask_opened', ask });
const answerOf = (c: Command) => (c as { answer: unknown }).answer;
const opts = (...choices: ReturnType<typeof option>[]): LegalSet => ({ kind: 'options', choices });

describe('the decision box controller', () => {
  it('nothing open for my team shows a quiet band per waiting team', () => {
    const r = rig();
    r.apply(open(askOf({ kind: 'confirmation' }, { question: 'end_round', teams: [TEAM2], allowPass: false })));
    r.box.render();
    expect(r.panel.head).toBeNull();
    expect(r.panel.quiet).toEqual([{ ordinal: 2, team: 'Team 2', doing: 'ending the round' }]);
    expect(r.panel.visible).toBe(true);
    expect(r.panel.controls).toEqual([]);
  });

  it('while deployment waits a band names each team\'s wait', () => {
    const game = gameOf({}, [{ id: P1, accountId: newId(), name: 'Clay', teamId: TEAM1, armyId: newId() }]);
    const r = rig({ game });
    r.box.render();
    expect(r.panel.quiet).toEqual([{ ordinal: 2, team: 'Team 2', doing: 'waiting for a player' }]);
  });

  it('choose numbers its options and a number key answers Chosen', () => {
    const r = rig();
    const ask = askOf(opts(option('a', 'Advance', [r.s.squad.id]), option('h', 'Hold', [r.s.squad.id])));
    r.apply(open(ask));
    r.box.render();
    expect(r.panel.head).toEqual({ icon: 'activation', title: 'Activate a unit', subject: null, prompt: null });
    expect(r.labels().slice(0, 2)).toEqual(['1. Advance', '2. Hold']);
    const group = r.panel.controls.find((c) => c.kind === 'group');
    expect(group?.kind === 'group' && [group.group.unit, group.tiles.map((t) => t.icon)]).toEqual(['Battle Brothers', ['advance', 'hold']]);
    expect(r.box.number(2)).toBe(true);
    expect(r.sent).toEqual([{ kind: 'answer_ask', askId: ask.id, answer: { kind: 'chosen', optionId: 'h' }, teamId: TEAM1 }]);
    expect(r.box.number(0)).toBe(false);
  });

  it('a click on a piece one choice names answers it', () => {
    const r = rig();
    const ask = askOf(opts(option('a', 'Advance', [r.s.squad.id]), option('h', 'Hold', [r.s.squad.id]), option('s', 'Strike', [r.s.rivals.id])), { question: 'charge_target' });
    r.apply(open(ask));
    r.box.render();
    expect([...r.box.eligible]).toEqual([r.s.rivals.id]);
    r.box.target(r.s.squad.id);
    expect(r.sent).toHaveLength(0);
    r.box.target(r.s.rival.id);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'chosen', optionId: 's' });
  });

  it('picking a unit to activate is never answered by a click on it, even with one action', () => {
    const r = rig();
    r.apply(open(askOf(opts(option('i', 'Idle', [r.s.squad.id])))));   // question: activate
    r.box.render();
    expect([...r.box.eligible]).toEqual([]);
    r.box.target(r.s.model.id);
    expect(r.sent).toHaveLength(0);
  });

  // The owner, 2026-11-21: "Its automatically moving to the shooting phase when i reclick the unit."
  it('a click on my acting unit that one option names only selects it', () => {
    const r = rig();
    r.apply(open(askOf(opts(option('s', 'Shoot', [r.s.squad.id]), option('c', 'Charge', [r.s.rivals.id])), { question: 'shoot_or_not' })));
    r.box.render();
    r.box.target(r.s.model.id);
    r.box.target(r.s.squad.id);
    expect(r.sent).toHaveLength(0);
    r.box.target(r.s.rival.id);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'chosen', optionId: 'c' });
  });

  it('a unit several choices name narrows the box, renumbered, and Esc widens it', () => {
    const r = rig();
    const ask = askOf(opts(option('a', 'Advance', [r.s.squad.id]), option('s', 'Strike', [r.s.rivals.id]), option('h', 'Hold', [r.s.squad.id])));
    r.apply(open(ask));
    r.box.render();
    expect(r.labels().slice(0, 3)).toEqual(['1. Advance', '2. Strike', '3. Hold']);
    r.box.unit(r.s.squad.id);
    expect(r.labels().slice(0, 2)).toEqual(['1. Advance', '2. Hold']);
    expect(r.panel.controls.flatMap((c) => (c.kind === 'group' ? c.tiles : []))).toHaveLength(2);
    expect(r.box.cancel()).toBe(true);
    expect(r.labels().slice(0, 3)).toEqual(['1. Advance', '2. Strike', '3. Hold']);
    expect(r.box.cancel()).toBe(false);
    r.box.unit(r.s.squad.id);
    r.box.number(2);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'chosen', optionId: 'h' });
  });

  it('hovering an option lights its pieces and darkens the rest of the glow', () => {
    const r = rig();
    const ask = askOf(opts(option('a', 'Advance', [r.s.squad.id]), option('s', 'Strike', [r.s.rivals.id])));
    r.apply(open(ask));
    r.box.render();
    expect(r.box.glowOf(r.s.model.id, r.s.squad.id)).toBe('selected');
    expect(r.box.glowOf(r.s.rival.id, r.s.rivals.id)).toBe('selected');
    r.box.hover('opt:a');
    expect(r.box.glowOf(r.s.model.id, r.s.squad.id)).toBe('hovered');
    expect(r.box.glowOf(r.s.rival.id, r.s.rivals.id)).toBeNull();
    r.box.hover(null);
    expect(r.box.glowOf(r.s.model.id, r.s.squad.id)).toBe('selected');
    expect(r.box.glowOf(r.s.rival.id, r.s.rivals.id)).toBe('selected');
  });

  it('hovering an option that names one unit hovers that unit', () => {
    const r = rig();
    r.apply(open(askOf(opts(option('a', 'Advance', [r.s.model.id]), option('b', 'Both', [r.s.squad.id, r.s.rivals.id])))));
    r.box.render();
    expect(r.box.hoveredUnit).toBeNull();
    r.box.hover('opt:a');
    expect(r.box.hoveredUnit).toBe(r.s.squad.id);
    r.box.hover('opt:b');
    expect(r.box.hoveredUnit).toBeNull();
    r.box.hover('opt:a');
    r.box.hover(null);
    expect(r.box.hoveredUnit).toBeNull();
  });

  it('a cast option carries its spell\'s tooltip', () => {
    const rules = RuleTexts.from([{ name: 'Hex', kind: 'spell', takesParameter: false, ruleText: 'Pick one enemy unit.' }]);
    const r = rig({ rules });
    r.apply(open(askOf(opts(option('Hex|m', 'Hex (3)', [r.s.model.id])), { question: 'cast' })));
    r.box.render();
    expect(r.tile('opt:Hex|m')!.tooltip).toBe('Hex\nCasting value 3\nPick one enemy unit.');
    const plain = rig({ rules });
    plain.apply(open(askOf(opts(option('Hex|m', 'Hex (3)')), { question: 'activate' })));
    plain.box.render();
    expect(plain.tile('opt:Hex|m')!.tooltip).toBeUndefined();
  });

  it('token spend offers a box per caster of mine and sends only non-zero spends', () => {
    const r = rig();
    const ask = askOf({ kind: 'token_spend', maxByModel: { [r.s.model.id]: 2, [r.s.rival.id]: 1 } }, { question: 'spend_tokens', teams: [TEAM1, TEAM2], sealed: true });
    r.apply(open(ask));
    r.box.render();
    const spins = r.panel.controls.filter((c) => c.kind === 'spin');
    expect(spins).toEqual([{ kind: 'spin', id: `spin:${r.s.model.id}`, label: 'Battle Brothers (Battle Brother)', max: 2, value: 0, pips: '' }]);
    r.box.spin(`spin:${r.s.model.id}`, 5);
    expect(r.panel.controls.find((c) => c.kind === 'spin')).toMatchObject({ value: 2 });
    r.box.press('spend');
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'tokens_spent', counts: { [r.s.model.id]: 2 } });
    const zero = rig();
    zero.apply(open(ask));
    zero.box.render();
    zero.box.enter();
    expect(answerOf(zero.sent[0]!)).toEqual({ kind: 'tokens_spent', counts: {} });
  });

  it('confirm answers on Enter and the end_round button says End the round with its key cap', () => {
    const r = rig();
    r.apply(open(askOf({ kind: 'confirmation' }, { question: 'end_round', prompt: 'End the round', teams: [TEAM1, TEAM2], allowPass: false })));
    r.box.render();
    expect(r.labels()).toEqual(['End the round [⏎]']);
    expect(r.button('enter')!.primary).toBe(true);
    expect(r.box.enter()).toBe(true);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'confirmed' });
  });

  it('pick pieces toggles a piece and one pick of one answers at once', () => {
    const r = rig();
    const two = askOf({ kind: 'pieces', eligible: [r.s.squad.id, r.s.rivals.id], min: 1, max: 2 }, { question: 'spell_targets', allowPass: false });
    r.apply(open(two));
    r.box.render();
    expect(r.labels()[0]).toBe('0 of 1–2');
    expect(r.button('done')!.disabled).toBe(true);
    r.box.target(r.s.squad.id);
    expect(r.labels()[0]).toBe('1 of 1–2');
    expect(r.box.glowOf(r.s.model.id, r.s.squad.id)).toBe('ghost');
    expect(r.button('done')!.disabled).toBe(false);
    r.box.target(r.s.squad.id);
    expect(r.labels()[0]).toBe('0 of 1–2');
    expect(r.box.glowOf(r.s.model.id, r.s.squad.id)).toBe('selected');
    r.box.target(r.s.squad.id); r.box.target(r.s.rival.id);
    expect(r.box.enter()).toBe(true);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'selected_pieces', pieceIds: [r.s.squad.id, r.s.rivals.id] });

    const one = rig();
    one.apply(open(askOf({ kind: 'pieces', eligible: [one.s.rivals.id], min: 1, max: 1 }, { question: 'charge_target', allowPass: false })));
    one.box.render();
    expect(one.labels()).toEqual(['Rivals']);
    one.box.target(one.s.rival.id);
    expect(answerOf(one.sent[0]!)).toEqual({ kind: 'selected_pieces', pieceIds: [one.s.rivals.id] });
  });

  it('a deploy pick lists shelved units as rows with their pips and key caps', () => {
    const r = rig();
    r.apply(open(askOf({ kind: 'pieces', eligible: [r.s.squad.id], min: 1, max: 1 }, { question: 'deploy', prompt: 'Deploy one unit', allowPass: false })));
    r.box.render();
    expect(r.labels()).toEqual(['1. Battle Brothers']);
    expect(r.panel.controls[0]).toMatchObject({ kind: 'row', pips: '●', ordinal: 1 });
    expect(r.box.number(1)).toBe(true);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'selected_pieces', pieceIds: [r.s.squad.id] });
  });

  it('targets aim the picked groups, a chip picks its group alone, Clear targets or Esc clears, Enter fires', () => {
    const r = rig();
    const ask = askOf({ kind: 'assignment', items: [option('g1', 'Rifles', ['w1']), option('g2', 'Pistols', ['w2'])], targets: [r.s.rivals.id], maxDistinctTargets: 2 },
      { question: 'declare_targets', allowPass: false });
    r.apply(open(ask));
    r.box.render();
    expect(r.panel.controls.filter((c) => c.kind === 'chip').map((c) => c.kind === 'chip' && [c.label, c.pressed, c.target])).toEqual([['Rifles ×1', true, null], ['Pistols ×1', true, null]]);
    expect(r.button('fire')!.disabled).toBe(true);
    expect([...r.box.eligible]).toEqual([r.s.rivals.id]);
    r.box.target(r.s.rival.id);
    expect(r.panel.controls.filter((c) => c.kind === 'chip').map((c) => c.kind === 'chip' && c.target)).toEqual([{ name: 'Rivals', ordinal: 2 }, { name: 'Rivals', ordinal: 2 }]);
    expect(r.box.targets!.assigned.size).toBe(2);
    r.box.press('chip:g2');
    expect(r.panel.controls.filter((c) => c.kind === 'chip').map((c) => c.kind === 'chip' && c.pressed)).toEqual([false, true]);
    expect(r.box.cancel()).toBe(true);
    expect(r.box.targets!.assigned.size).toBe(0);
    expect(r.box.cancel()).toBe(false);
    expect(r.button('clear-targets')!.disabled).toBe(true);
    r.box.target(r.s.rivals.id);
    expect(r.button('clear-targets')!.disabled).toBe(false);
    r.box.press('clear-targets');
    expect(r.box.targets!.assigned.size).toBe(0);
    expect(r.button('clear-targets')!.disabled).toBe(true);
    expect(r.sent).toEqual([]);
    r.box.target(r.s.rivals.id);
    expect(r.box.enter()).toBe(true);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'assigned', targets: { g1: r.s.rivals.id, g2: r.s.rivals.id } });
  });

  it('casualties add on click, take one away on right-click, fill on Auto and mark ✕ n/left', () => {
    const tough = unitOf('Tough', [slotOf({ name: 'Boss' })]);
    const board = boardOf([{ participantId: P1, units: [tough] }, { participantId: P2, units: [unitOf('Rivals')] }]);
    const t = rig({ board, maxWounds: { [tough.models[0]!.id]: 3 } });
    const boss = tough.models[0]!.id;
    const ask = askOf({ kind: 'allocation', targets: [boss], amount: 2 }, { question: 'remove_casualties', prompt: 'Assign 2 wound(s)', allowPass: false });
    t.apply(open(ask));
    t.box.render();
    expect(t.panel.head).toEqual({ icon: 'wound', title: 'Assign 2 wounds', subject: 'Tough', prompt: null });
    expect(t.panel.controls[0]).toEqual({ kind: 'counter', id: 'counter', text: '0 of 2', pips: '○○' });
    t.box.model(boss, false);
    expect(t.box.marks.get(boss)).toBe('✕ 1/3');
    expect(t.labels()[0]).toBe('1 of 2');
    t.box.model(boss, true);
    expect(t.box.marks.size).toBe(0);
    t.box.press('auto');
    expect(t.box.marks.get(boss)).toBe('✕ 2/3');
    expect(t.button('confirm')!.disabled).toBe(false);
    t.box.press('clear');
    expect(t.button('confirm')!.disabled).toBe(true);
    t.box.press('auto');
    t.box.enter();
    expect(answerOf(t.sent[0]!)).toEqual({ kind: 'allocated', counts: { [boss]: 2 } });
  });

  it('a window that allows a pass is shown with its choices and Pass, never passed for the player, and Pass sends once', () => {
    const r = rig();
    r.apply(open(askOf(opts(option('a', 'Advance')), { windowKind: 'casting', allowPass: true })));
    r.box.render();
    r.box.render();
    expect(r.sent).toHaveLength(0);
    expect(r.tile('opt:a')!.disabled).toBe(false);
    expect(r.panel.note).toBe('');
    expect(r.panel.controls.filter((c) => c.kind === 'button').map((b) => b.id)).toEqual(['pass']);
    r.box.press('pass');
    r.box.press('pass');
    expect(r.sent).toHaveLength(1);
    expect(answerOf(r.sent[0]!)).toEqual({ kind: 'passed' });
  });

  it('a charge move Ask offers Pass only when the server allows one', () => {
    const r = rig();
    const charge = (allowPass: boolean): LegalSet => ({ kind: 'move_region', modelIds: [r.s.model.id], allowance: 12, impassableVolumeIds: [], difficultVolumeIds: [],
      stoppable: false, passesModels: false, ignoresTerrain: false, contactWith: [r.s.rival.id], contactGrace: 0.5 });
    r.apply(open(askOf(charge(false), { question: 'move', prompt: 'Charge: move into contact', allowPass: false })));
    r.box.render();
    expect(r.button('pass')).toBeUndefined();
    const s2 = rig();
    s2.apply(open(askOf(charge(true), { question: 'move', prompt: 'Charge: move into contact', allowPass: true })));
    s2.box.render();
    expect(s2.button('pass')).toBeDefined();
  });

  it('a refused pass renders the Ask with its Pass button', async () => {
    const r = rig();
    r.apply(open(askOf(opts(option('a', 'Advance')), { windowKind: 'casting', allowPass: true })));
    r.box.render();
    r.box.press('pass');
    r.refuse(0, 'not now');
    await tick();
    expect(r.panel.refusal).toBe('The server refused: not now');
    expect(r.button('pass')).toBeDefined();
    expect(r.tile('opt:a')!.disabled).toBe(false);
    expect(r.sent).toHaveLength(1);
    r.box.render();
    expect(r.sent).toHaveLength(1);
  });

  it('a refusal re-renders the Ask with the server\'s words and a new Ask clears them', async () => {
    const r = rig();
    const ask = askOf(opts(option('a', 'Advance'), option('h', 'Hold')));
    r.apply(open(ask));
    r.box.render();
    r.box.number(1);
    expect(r.tile('opt:a')!.disabled).toBe(true);
    r.refuse(0, 'illegal here');
    await tick();
    expect(r.panel.refusal).toBe('The server refused: illegal here');
    expect(r.tile('opt:a')!.disabled).toBe(false);
    expect(r.panel.head?.title).toBe('Activate a unit');
    r.apply({ kind: 'ask_closed', askId: ask.id, answers: [] }, open(askOf(opts(option('x', 'X')), { prompt: 'Another' })));
    r.box.render();
    expect(r.panel.refusal).toBeNull();
    expect(r.panel.head?.title).toBe('Another');
  });

  it('a Refused that arrives after its send settled still shows', async () => {
    const r = rig();
    r.apply(open(askOf(opts(option('a', 'Advance')))));
    r.box.render();
    r.box.number(1);
    r.accept(0);                                                         // another player's frame settled it as accepted
    await tick();
    expect(r.panel.refusal).toBeNull();
    r.refuseLate(0, 'too late');
    expect(r.panel.refusal).toBe('The server refused: too late');
    expect(r.tile('opt:a')!.disabled).toBe(false);
    r.refuseLate(0, 'again');
    expect(r.panel.refusal).toBe('The server refused: too late');
  });

  it('a failed send shows Could not send the answer', async () => {
    const r = rig();
    r.apply(open(askOf(opts(option('a', 'Advance')))));
    r.box.render();
    r.box.number(1);
    r.fail(0, new SendError('Not connected to the game yet.', 'not_connected'));
    await tick();
    expect(r.panel.refusal).toBe('Could not send the answer: Not connected to the game yet.');
    expect(r.tile('opt:a')!.disabled).toBe(false);
  });

  it('a second Enter after an answer sends nothing', () => {
    const r = rig();
    r.apply(open(askOf({ kind: 'confirmation' }, { question: 'confirm', allowPass: false })));
    r.box.render();
    expect(r.box.enter()).toBe(true);
    expect(r.box.enter()).toBe(false);
    r.box.press('enter');
    expect(r.sent).toHaveLength(1);
  });

  it('the answer names the team the Ask was rendered for', () => {
    const account = newId();
    const game = gameOf({}, [{ id: P1, accountId: account, name: 'Clay', teamId: TEAM1 }, { id: P2, accountId: account, name: 'Clay', teamId: TEAM2 }]);
    const r = rig({ game });
    r.apply(open(askOf({ kind: 'confirmation' }, { id: '0198b7ac-9000-7000-8000-000000000001', question: 'confirm', teams: [TEAM2], allowPass: false })));
    r.box.render();
    expect(r.panel.seat).toBe('Playing as Team 2');
    r.apply(open(askOf({ kind: 'confirmation' }, { id: '0198b7ac-0001-7000-8000-000000000001', question: 'confirm', teams: [TEAM1], allowPass: false })));
    r.box.enter();                                                       // acting is now Team 1, but the box answers for Team 2
    expect((r.sent[0] as { teamId?: string }).teamId).toBe(TEAM2);
  });

  it('a teammate\'s answer reads Name answered for your team', () => {
    const r = rig();
    const ask = askOf(opts(option('a', 'Advance')), { teams: [TEAM1] });
    r.apply(open(ask));
    r.applyAs(P2, { kind: 'ask_answered', askId: ask.id, teamId: TEAM1, outsideLegalSet: false });
    r.apply(open(ask));                                                  // re-opened (an undo of the close): the answer stands in the log
    r.box.render();
    expect(r.panel.note).toBe('Bo answered for your team');
  });

  it('a reveal shows until the render after a new Ask', () => {
    const r = rig();
    const sealed = askOf(opts(option('a', 'A')), { teams: [TEAM1, TEAM2], sealed: true });
    r.box.reveal(r.apply(open(sealed)));
    r.apply({ kind: 'ask_answered', askId: sealed.id, teamId: TEAM1, answer: { kind: 'passed' }, outsideLegalSet: false });
    r.box.reveal(r.apply({ kind: 'ask_closed', askId: sealed.id, answers: [
      { teamId: TEAM1, answer: { kind: 'passed' } }, { teamId: TEAM2, answer: { kind: 'passed' } }] }));
    r.box.render();
    expect(r.panel.revealed).toEqual([[{ ordinal: 1, team: 'Team 1', choice: 'passed' }, { ordinal: 2, team: 'Team 2', choice: 'passed' }]]);
    expect(r.panel.visible).toBe(true);
    r.box.render();
    expect(r.panel.revealed).toEqual([[{ ordinal: 1, team: 'Team 1', choice: 'passed' }, { ordinal: 2, team: 'Team 2', choice: 'passed' }]]);
    r.apply(open(askOf(opts(option('b', 'B')), { prompt: 'Next' })));
    r.box.render();
    expect(r.panel.revealed).toBeNull();
    expect(r.panel.head?.title).toBe('Next');
  });

  it('Playing as Team 2 heads the box for an account with two teams', () => {
    const account = newId();
    const game = gameOf({}, [{ id: P1, accountId: account, name: 'Clay', teamId: TEAM1 }, { id: P2, accountId: account, name: 'Clay', teamId: TEAM2 }]);
    const two = rig({ game });
    two.apply(open(askOf({ kind: 'confirmation' }, { question: 'confirm', teams: [TEAM2], allowPass: false })));
    two.box.render();
    expect(two.panel.seat).toBe('Playing as Team 2');
    const one = rig();
    one.apply(open(askOf({ kind: 'confirmation' }, { question: 'confirm', allowPass: false })));
    one.box.render();
    expect(one.panel.seat).toBe('');
  });

  it('the panel is hidden with nothing to show and publishes to subscribers', () => {
    const r = rig();
    const seen: DecisionPanel[] = [];
    r.box.subscribe((p) => seen.push(p));
    r.box.render();
    expect(r.panel.visible).toBe(false);
    r.apply(open(askOf({ kind: 'confirmation' }, { question: 'confirm', allowPass: false })));
    r.box.render();
    expect(seen.at(-1)!.visible).toBe(true);
  });
});
