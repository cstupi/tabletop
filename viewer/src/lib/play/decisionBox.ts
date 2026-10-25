// viewer/src/lib/play/decisionBox.ts
// client/src/Hud/DecisionBox.cs and DecisionBox.Picks.cs without Godot: a controller that holds the open Ask's handlers and publishes a
// DecisionPanel the Svelte DecisionBox draws. Render, RenderAsk, RenderChoose, Glow, Light, RenderSpend, Number, Resolve, Reveal, Send,
// Report, Answer, Unhook, Reset, RenderPick, RenderTargets and RenderCasualties keep their order; Move and deploy placement are D2's
// (MoveHooks). The browser judges nothing: every answer is AnswerAsk through the host's send.

import type { SendOutcome } from '../connection/gameConnection';
import { SendError, type ConnectionState } from '../connection/gameConnection';
import type { Projection } from '../live/apply';
import { playingAs } from '../lobby/seats';
import type {
  Ask, AskAnswer, AskOption, Assignment, Allocation, Command, GameEvent, Options, PieceSelection, Placement, Point3, Refused, TokenSpend,
} from '../protocol/wire.generated';
import { confirmLabel, mine as myAsk, spendableBy, answeredBy, type AskView } from './askView';
import { CasualtyPick } from './casualtyPick';
import { choicesOf, clickAnswers, decisionOf, number as numberChoices, rows, type Numbered } from './decisionView';
import { midDeploy, offTable, pickable, type MidDeploy } from './deployView';
import { waiting as headerWaiting } from './headerView';
import { Names } from './names';
import { SealedReveals } from './sealedReveals';
import { RuleTexts } from './ruleTexts';
import { mute, unmute, type PlaySettings } from './settings';
import { ofOption } from './spellText';
import { TargetAssignment } from './targetAssignment';
import { waitHolds, waitingTeams } from './waiting';

export type Control =
  | { readonly kind: 'button'; readonly id: string; readonly label: string; readonly disabled: boolean;
      readonly tooltip?: string; readonly pieces?: readonly string[] }        // pieces: lit while the pointer is on it
  | { readonly kind: 'note' | 'label' | 'reason'; readonly text: string; readonly id?: string }   // note and reason in warning colour; a reason with no text draws nothing
  | { readonly kind: 'chip'; readonly id: string; readonly label: string; readonly pressed: boolean; readonly disabled?: boolean }
  | { readonly kind: 'spin'; readonly id: string; readonly label: string; readonly max: number; readonly value: number };

export interface DecisionPanel {
  readonly seat: string; readonly title: string; readonly revealed: string | null; readonly note: string;
  readonly controls: readonly Control[]; readonly refusal: string | null; readonly hint: string; readonly visible: boolean;
}

/** Palette.Selected, Hovered, Ghost. */
export type Glow = 'selected' | 'hovered' | 'ghost';

export interface DecisionHost {
  state(): ConnectionState; myTeams(): readonly string[]; acting(): string | null;
  settings(): PlaySettings; updateSettings(change: (s: PlaySettings) => PlaySettings): void;
  rules(): RuleTexts; send(command: Command): Promise<SendOutcome>;
  /** Every Refused, including one for a send that already settled (GameConnection.onRefused). */
  onRefused(listener: (refused: Refused) => void): () => void;
}

/** DecisionBox.Move.cs's half, filled by D2; null renders a move's title and hint with no controls. */
export interface MoveHooks {
  renderMove(box: DecisionBox, mine: AskView, p: Projection, n: Names): void;
  startDeploy(box: DecisionBox, mid: MidDeploy, n: Names): void;
  keepGhosts(box: DecisionBox): void;
  refused(box: DecisionBox, token: string, detail: string): void;
  unhook(box: DecisionBox): void;
  /** A send that never reached the server (the socket failed): the token's models are the projection's again. */
  failed?(box: DecisionBox, token: string): void;
  /** The answer's send settled as accepted: its events are in the projection, so nothing it dropped is held past it. */
  settled?(box: DecisionBox, token: string): void;
  /** A held ghost landed: its refusal lapses and the move is gated again. */
  dragged?(box: DecisionBox, modelId: string): void;
}

/** The open Ask's handlers: set by its render, dropped by unhook whenever the Ask ends by any route. */
export interface Handlers {
  onEnter: (() => void) | null; onStop: (() => void) | null; onCancel: (() => boolean) | null; onNumber: ((optionId: string) => void) | null;
  onTarget: ((id: string) => void) | null; onModel: ((id: string, right: boolean) => void) | null; onUnit: ((id: string) => void) | null;
}

const noHandlers = (): Handlers => ({ onEnter: null, onStop: null, onCancel: null, onNumber: null, onTarget: null, onModel: null, onUnit: null });
const MAX_SEEN = 16;

export class DecisionBox {
  /** What D2's move rendering sets, as the Godot partial class sets its fields. */
  readonly handlers: Handlers = noHandlers();
  /** TableInput.Proposing: the models being positioned (a move step's, a placement's ghosts). */
  proposing: ReadonlySet<string> = new Set();
  /** The proposed models' placements as the board draws them (a ghost's position and the facing the player gave it). */
  readonly ghosts = new Map<string, Placement>();
  /** TableInput.PlacementPoint: a deploy's click on the table, its own slot so a menu pick cannot take it. */
  placementPoint: ((at: Point3) => void) | null = null;
  readonly #host: DecisionHost;
  readonly #moves: MoveHooks | null;
  readonly #listeners = new Set<(panel: DecisionPanel) => void>();
  readonly #stopRefused: () => void;
  readonly #reveals = new SealedReveals();
  readonly #actions = new Map<string, () => void>();
  readonly #glow = new Set<string>();
  readonly #ghost = new Set<string>();
  readonly #seen = new Map<string, Refused>();
  #eligible: ReadonlySet<string> = new Set();
  #marks: ReadonlyMap<string, string> = new Map();
  #targets: TargetAssignment | null = null;
  #title = ''; #note = ''; #hint = '';
  #seat = '';
  #controls: Control[] = [];
  #segmentLength = 0;
  #numbered: readonly Numbered[] = [];
  #renderedAsk: string | null = null;
  #answering: string | null = null;
  #refusalAsk: string | null = null;
  #revealed: string | null = null;
  #refusal: string | null = null;
  #lastToken = 0;
  #lastId: string | null = null;
  #lit: readonly string[] | null = null;
  #deployUnit: string | null = null;
  #panel: DecisionPanel = { seat: '', title: '', revealed: null, note: '', controls: [], refusal: null, hint: '', visible: false };

  constructor(host: DecisionHost, moves: MoveHooks | null = null) {
    this.#host = host;
    this.#moves = moves;
    this.#stopRefused = host.onRefused((r) => this.#onRefusedFrame(r));
  }

  get panel(): DecisionPanel { return this.#panel; }
  subscribe(listener: (panel: DecisionPanel) => void): () => void {
    this.#listeners.add(listener);
    listener(this.#panel);
    return () => { this.#listeners.delete(listener); };
  }
  dispose(): void { this.#stopRefused(); this.#unhook(); this.#listeners.clear(); }

  get eligible(): ReadonlySet<string> { return this.#eligible; }
  set eligible(ids: ReadonlySet<string>) { this.#eligible = ids; }
  get marks(): ReadonlyMap<string, string> { return this.#marks; }
  get targets(): TargetAssignment | null { return this.#targets; }
  get deployUnit(): string | null { return this.#deployUnit; }
  set deployUnit(unit: string | null) { this.#deployUnit = unit; }
  get renderedAsk(): string | null { return this.#renderedAsk; }
  get host(): DecisionHost { return this.#host; }

  // ---- for D2's move rendering: the box's texts and controls, as the partial class reaches its fields ----
  setTexts(texts: { title?: string; hint?: string; note?: string }): void {
    if (texts.title !== undefined) this.#title = texts.title;
    if (texts.hint !== undefined) this.#hint = texts.hint;
    if (texts.note !== undefined) this.#note = texts.note;
  }
  add(control: Control, action?: () => void): void {
    this.#controls.push(control);
    if (action !== undefined && control.id !== undefined) this.#actions.set(control.id, action);
  }
  /** Replace(id): the control with this id, in place. */
  replace(id: string, control: Control | ((old: Control) => Control)): void {
    const i = this.#controls.findIndex((c) => 'id' in c && c.id === id);
    if (i < 0) return;
    this.#controls[i] = typeof control === 'function' ? control(this.#controls[i]!) : control;
    this.#publish();
  }
  /** A held proposed model landed at `at`: the ghost moves there, and the move is gated again. */
  dragged(modelId: string, at: Placement): void {
    if (!this.ghosts.has(modelId)) return;
    this.ghosts.set(modelId, at);
    this.#moves?.dragged?.(this, modelId);
  }
  /** A click on the table while a deploy waits for one. */
  placeAt(at: Point3): boolean {
    const place = this.placementPoint;
    if (place === null) return false;
    place(at);
    return true;
  }
  /** Reset and Render: an Esc that starts the Ask or the placement over. */
  rebuild(): void { this.#reset(); this.#renderedAsk = null; this.render(); }
  /** AnswerAsk for the open Ask, as the box answers it; the token a refusal arrives under. */
  answer(ask: Ask, answer: AskAnswer): string { void this.#answer(ask, answer); return String(this.#lastToken); }
  /** A command that is no answer (a placement's MoveModels); `failed` words a send that never reached the server. Returns its token. */
  sendCommand(command: Command, failed: (message: string) => string): string {
    this.#controls = this.#controls.map((c) => (c.kind === 'button' || c.kind === 'chip' ? { ...c, disabled: true } : c));
    this.handlers.onEnter = null; this.handlers.onCancel = null; this.#moves?.unhook(this);
    const token = ++this.#lastToken;
    this.#lastId = null;
    this.#publish();
    this.#host.send(command).then((outcome) => {
      if (token !== this.#lastToken) return;
      this.#lastId = outcome.id;
      const refused = outcome.refused ?? this.#seen.get(outcome.id);
      if (refused !== undefined) this.#refused(String(token), refused);
    }, (e: unknown) => {
      if (token !== this.#lastToken) return;
      this.#moves?.failed?.(this, String(token));
      this.#refusal = failed(e instanceof SendError || e instanceof Error ? e.message : String(e)); this.#refusalAsk = null;
      this.#reset(); this.render();
    });
    return String(token);
  }

  /** Glow(ids): the open Ask's glow, a unit id lighting each of the unit's models. */
  glow(ids: Iterable<string>): void { this.#glow.clear(); for (const id of ids) this.#glow.add(id); }
  /** Reports a failed send, or a refusal, the way Godot's Report does. */
  refuse(text: string): void { this.#refusal = text; this.#refusalAsk = this.#renderedAsk; }

  /** Frame.Render: every connection state change. */
  render(): void {
    this.#renderAsk();
    this.#seat = (this.#title !== '' && this.#renderedAsk !== null) || this.#deployUnit !== null ? this.#playingAs() : '';
    this.#publish();
  }

  #playingAs(): string {
    const game = this.#host.state().game;
    return game === null ? '' : playingAs(game, this.#host.myTeams(), this.#host.acting()) ?? '';
  }

  #publish(): void {
    this.#panel = {
      seat: this.#seat, title: this.#title, revealed: this.#revealed, note: this.#note, controls: [...this.#controls], refusal: this.#refusal,
      hint: this.#hint, visible: this.#title !== '' || this.#note !== '' || this.#refusal !== null || this.#revealed !== null,
    };
    for (const l of [...this.#listeners]) l(this.#panel);
  }

  #renderAsk(): void {
    const st = this.#host.state();
    const p = st.projection, game = st.game;
    if (p === null || game === null || game.status !== 'playing') {
      if (st.phase === 'reconnecting') this.#moves?.keepGhosts(this);
      this.#reset(); this.#renderedAsk = null; this.#title = this.#note = this.#hint = '';
      return;
    }
    const fresh = this.#reveals.drain();                                    // every reveal since the last render, in order
    if (fresh.length > 0) this.#revealed = fresh.join('\n');
    const muted = new Set(this.#host.settings().mutedWindowKinds);
    const team = this.#host.acting();
    const mine = myAsk(p, game, team, muted);
    if ((mine?.ask.id ?? null) !== this.#refusalAsk) this.#refusal = null;           // a different Ask, or none: the refusal is spent
    const names = new Names(game, p);
    const model = decisionOf(p, game, names, team, muted);
    if (mine === null) {
      const myTeams = this.#host.myTeams();
      const mid = [team, ...myTeams].map((t) => midDeploy(p, st.log, t)).find((m) => m !== null) ?? null;   // a pick by any team of mine is placed
      if (this.#renderedAsk !== null || (mid?.unitId ?? null) !== this.#deployUnit) { this.#reset(); this.#renderedAsk = null; }
      this.#note = '';                                                       // a sealed Ask's reveal (its own line) stays up until the next Ask
      const waits = game.status === 'playing' && waitHolds(game, p.board, p.lastSeq) ? waitingTeams(game, p.board) : [];
      if (mid === null && waits.length > 0) { this.#title = waits.map((w) => w.text).join('\n'); this.#hint = ''; }
      else if (mid === null) { this.#title = headerWaiting(p, names, myTeams) ?? ''; this.#hint = ''; }   // the header's waiting line, its one source
      else if (this.#deployUnit === null) this.#moves?.startDeploy(this, mid, names);
      return;
    }
    if (this.#renderedAsk === mine.ask.id) return;
    this.#reset(); this.#renderedAsk = mine.ask.id; this.#answering = team; this.#note = '';
    if (fresh.length === 0) this.#revealed = null;                          // a reveal is cleared only after a render has shown it
    this.#title = model.title; this.#hint = model.hint; this.#numbered = model.numbered;
    this.glow(mine.highlights);
    if (model.autoPass && this.#refusal === null) {   // a failed auto-pass falls through to the normal render with its Pass button, so a dead socket cannot loop
      const kind = mine.ask.windowKind!;
      this.#send(mine.ask, { kind: 'passed' }); this.#note = `auto-passed (muted: ${spaced(kind)})`;
      this.add(button('unmute', 'Unmute'), () => this.#host.updateSettings((s) => unmute(s, kind)));
      return;
    }
    switch (mine.mode) {
      case 'choose': this.#renderChoose(mine); break;
      case 'move': this.#moves?.renderMove(this, mine, p, names); break;
      case 'tokenSpend': this.#renderSpend(mine, p, names); break;
      case 'confirm': {                                                      // "Continue" for a step gate, "End the round" at the round boundary
        this.handlers.onEnter = () => this.#send(mine.ask, { kind: 'confirmed' });
        this.add(button('enter', `${confirmLabel(mine.ask)} (Enter)`), this.handlers.onEnter);
        break;
      }
      default: this.#renderPicks(mine, p, names); break;                    // PickPieces, Assign, Allocate
    }
    if (mine.ask.allowPass) this.add(button('pass', 'Pass'), () => this.#send(mine.ask, { kind: 'passed' }));
    const kind = mine.ask.windowKind;
    if (kind !== undefined) this.add(button('mute', `Mute ${spaced(kind)} windows`), () => this.#host.updateSettings((s) => mute(s, kind)));
    const who = answeredBy(st.log, mine.ask.id, team, st.participantId, names);
    if (who !== null) this.#note = `${who} answered for your team`;
  }

  /** Replaces the first controls of the box (a list that rebuilds itself) with new ones. */
  #segment(items: ReadonlyArray<readonly [Control, (() => void)?]>): void {
    this.#controls.splice(0, this.#segmentLength, ...items.map((i) => i[0]));
    this.#segmentLength = items.length;
    for (const [c, a] of items) if (a !== undefined && c.id !== undefined) this.#actions.set(c.id, a);
    this.#publish();
  }

  /**
   * Numbered options. A click on a piece exactly one choice names picks that choice. A piece several choices name (activate: one option
   * per unit and action) is not a pick: the click selects its unit as any click does, and the box narrows to that unit's options,
   * renumbered from 1, until another unit is clicked or Esc widens it again.
   */
  #renderChoose(mine: AskView): void {
    const options = mine.ask.legal as Options;
    const all = options.choices;
    let narrowed = false;
    const full = this.#numbered;
    const send = (c: AskOption) => () => this.#send(mine.ask, { kind: 'chosen', optionId: c.id });
    const show = (shown: readonly AskOption[], numbered: readonly Numbered[]): void => {
      if (this.#lit !== null) this.#light(null);
      this.#numbered = numbered;
      const items: Array<readonly [Control, (() => void)?]> = [];
      for (const row of rows(shown)) {
        const c = row.option;
        if (c === null) { items.push([{ kind: 'note', text: row.note! }]); continue; }
        const key = this.#numbered.find((n) => n.optionId === c.id)?.key ?? 0;
        const tip = mine.ask.question === 'cast' ? ofOption(this.#host.rules(), c.id, c.label) : null;
        items.push([{ kind: 'button', id: `opt:${c.id}`, label: key > 0 ? `${key}. ${c.label}` : c.label, disabled: false, pieces: c.pieces,
          ...(tip !== null ? { tooltip: tip } : {}) }, send(c)]);
      }
      this.#segment(items);
    };
    show(all, full);
    this.handlers.onNumber = (option) => this.#send(mine.ask, { kind: 'chosen', optionId: option });
    this.#eligible = clickAnswers(options);
    this.handlers.onTarget = (clicked) => {
      const hits = all.filter((c) => this.#resolve(clicked, c.pieces) !== null);
      if (hits.length === 1) this.#send(mine.ask, { kind: 'chosen', optionId: hits[0]!.id });
    };
    this.handlers.onUnit = (unit) => {
      const p = this.#host.state().projection;
      if (p === null) return;
      const hits = choicesOf(options, p, unit);
      if (hits.length === 0) { if (narrowed) { narrowed = false; show(all, full); } return; }
      narrowed = true; show(hits, numberChoices(hits));
    };
    this.handlers.onCancel = () => { if (!narrowed) return false; narrowed = false; show(all, full); return true; };
  }

  /** The glow a model draws in for the open Ask: lit by the hovered option, else a pick's ghost, else the Ask's glow. */
  glowOf(modelId: string, unitId: string): Glow | null {
    if (this.#lit !== null) return this.#lit.includes(modelId) || this.#lit.includes(unitId) ? 'hovered' : null;
    if (this.#ghost.has(modelId) || this.#ghost.has(unitId)) return 'ghost';
    return this.#glow.has(modelId) || this.#glow.has(unitId) ? 'selected' : null;
  }

  /** The pieces lit by the option button under the pointer, the rest of the Ask's glow dark; null puts the Ask's glow back. */
  #light(pieces: readonly string[] | null): void { this.#lit = pieces; }

  hover(controlId: string | null): void {
    const c = controlId === null ? undefined : this.#controls.find((x) => 'id' in x && x.id === controlId);
    this.#light(c?.kind === 'button' && c.pieces !== undefined ? c.pieces : null);
    this.#publish();
  }

  /**
   * p.13: "may spend any number of spell tokens", capped per model by what that model holds. So one box per model, each bounded by its
   * own maximum, and no total. spendableBy drops the other team's spenders, which the map carries because the window addresses both.
   */
  #renderSpend(mine: AskView, p: Projection, names: Names): void {
    const team = this.#host.acting()!;
    const spendable = spendableBy(p, this.#host.state().game!, team, mine.ask.legal as TokenSpend);
    const values = new Map<string, number>([...spendable.keys()].map((id) => [id, 0]));
    const spin = (id: string): Control => ({ kind: 'spin', id: `spin:${id}`, label: names.piece(id), max: spendable.get(id)!, value: values.get(id)! });
    this.#spins = { values, max: spendable, rebuild: () => this.#segment([...values.keys()].map((id) => [spin(id)] as const)) };
    for (const id of spendable.keys()) this.add(spin(id));
    this.#segmentLength = spendable.size;
    const spend = () => this.#send(mine.ask, { kind: 'tokens_spent', counts: Object.fromEntries([...values].filter(([, v]) => v > 0)) });
    this.handlers.onEnter = spend;
    this.add(button('spend', 'Spend (Enter)'), spend);
    if (mine.answeredTeams.length > 0) this.add({ kind: 'label', text: mine.answeredTeams.map((t) => `${t} has answered`).join(', ') });
  }
  #spins: { values: Map<string, number>; max: ReadonlyMap<string, number>; rebuild: () => void } | null = null;

  spin(controlId: string, value: number): void {
    const s = this.#spins;
    const id = controlId.startsWith('spin:') ? controlId.slice(5) : null;
    if (s === null || id === null || !s.values.has(id)) return;
    s.values.set(id, Math.max(0, Math.min(s.max.get(id)!, Math.trunc(value))));
    s.rebuild();
  }

  /** The press of a button or chip. */
  press(controlId: string): void {
    const c = this.#controls.find((x) => 'id' in x && x.id === controlId);
    if (c === undefined || ((c.kind === 'button' || c.kind === 'chip') && c.disabled === true)) return;
    this.#actions.get(controlId)?.();
  }

  number(key: number): boolean {
    const pick = this.handlers.onNumber;
    if (pick === null) return false;
    const entry = this.#numbered.find((n) => n.key === key);
    if (entry === undefined) return false;
    pick(entry.optionId);
    return true;
  }
  enter(): boolean { const f = this.handlers.onEnter; if (f === null) return false; f(); return true; }
  cancel(): boolean { return this.handlers.onCancel?.() === true; }
  stop(): boolean { const f = this.handlers.onStop; if (f === null) return false; f(); return true; }

  target(pieceId: string): void { this.handlers.onTarget?.(pieceId); }
  model(modelId: string, right: boolean): void { this.handlers.onModel?.(modelId, right); }
  unit(unitId: string): void { this.handlers.onUnit?.(unitId); }

  /** What a table click names in a legal set, which holds model ids or unit ids: the clicked id itself, its unit, or the set's model of the clicked unit. */
  #resolve(clicked: string, ids: readonly string[]): string | null {
    if (ids.includes(clicked)) return clicked;
    const p = this.#host.state().projection;
    if (p === null) return null;
    const unit = p.models.get(clicked)?.unitId;
    if (unit !== undefined && ids.includes(unit)) return unit;
    if (p.units.has(clicked)) for (const id of ids) if (p.models.get(id)?.unitId === clicked) return id;
    return null;
  }

  /** A sealed Ask withholds every answer until it closes; the closing frame carries them all, and that frame is the reveal. */
  reveal(events: readonly GameEvent[]): void {
    const st = this.#host.state();
    if (st.game === null || st.projection === null) return;
    this.#reveals.read(events, new Names(st.game, st.projection));
  }

  #send(ask: Ask, answer: AskAnswer): void { void this.#answer(ask, answer); }

  /** A failed send: if the Ask is still the one on screen, rebuild it from scratch and show the error; a later Ask is left alone. */
  #report(askId: string, message: string): void {
    if (this.#renderedAsk !== askId) return;
    this.#refusal = `Could not send the answer: ${message}`; this.#refusalAsk = askId; this.#renderedAsk = null; this.render();
  }

  async #answer(ask: Ask, answer: AskAnswer): Promise<void> {
    this.#controls = this.#controls.map((c) => (c.kind === 'button' || c.kind === 'chip' ? { ...c, disabled: true } : c));   // SetButtons(true)
    this.#unhook();                                                          // one answer per Ask: no second click or key can double-send
    const token = ++this.#lastToken;                                         // before the send, so a Refused that lands first still matches
    this.#lastId = null;
    this.#publish();
    try {
      const teamId = this.#answering ?? undefined;                           // for the team the Ask was rendered for
      const outcome = await this.#host.send({ kind: 'answer_ask', askId: ask.id, answer, ...(teamId === undefined ? {} : { teamId }) });
      if (outcome.refused === null) this.#moves?.settled?.(this, String(token));
      if (token !== this.#lastToken) return;
      this.#lastId = outcome.id;
      const refused = outcome.refused ?? this.#seen.get(outcome.id);         // a Refused that landed before the promise settled
      if (refused !== undefined) this.#refused(String(token), refused);
    } catch (e) {
      this.#moves?.failed?.(this, String(token));
      this.#report(ask.id, e instanceof SendError || e instanceof Error ? e.message : String(e));
    }
  }

  #onRefusedFrame(r: Refused): void {
    this.#seen.set(r.messageId, r);
    if (this.#seen.size > MAX_SEEN) this.#seen.delete(this.#seen.keys().next().value!);
    if (this.#lastId !== null && r.messageId === this.#lastId) this.#refused(String(this.#lastToken), r);
  }

  /** Godot's _onRefused: the server's words over the same Ask, rebuilt. */
  #refused(token: string, r: Refused): void {
    if (this.#lastId === null || r.messageId !== this.#lastId) return;
    this.#lastId = null;                                                      // a Refused answers once
    this.#refusal = `The server refused: ${r.detail}`; this.#refusalAsk = this.#renderedAsk;
    this.#moves?.refused(this, token, r.detail ?? '');                        // a refused move or placement comes back where it was dropped, marked
    this.#reset(); this.#renderedAsk = null; this.render();
  }

  /** The open Ask's handlers and table state, dropped whenever it ends by any route: our answer, a teammate's, a server close, a replacing Ask. */
  #unhook(): void {
    Object.assign(this.handlers, noHandlers());
    this.#glow.clear(); this.#lit = null; this.#ghost.clear(); this.#deployUnit = null;
    this.#targets = null; this.#marks = new Map(); this.#spins = null;
    this.#eligible = new Set(); this.proposing = new Set(); this.ghosts.clear(); this.placementPoint = null;
    this.#moves?.unhook(this);
  }

  /** Unhook, and the box's controls and the table's highlights with it. */
  #reset(): void {
    this.#unhook(); this.#numbered = [];
    this.#controls = []; this.#segmentLength = 0; this.#actions.clear();
  }

  #renderPicks(mine: AskView, p: Projection, names: Names): void {
    switch (mine.mode) {
      case 'pickPieces': this.#renderPick(mine.ask, mine.ask.legal as PieceSelection, p, names); break;
      case 'allocate': this.#renderCasualties(mine.ask, mine.ask.legal as Allocation, p); break;
      case 'assign': this.#renderTargets(mine.ask, mine.ask.legal as Assignment, names); break;
    }
  }

  /** Pick Min–Max pieces: a click on an eligible piece toggles it, a shelved one is a numbered entry (a deploy). One pick of one answers at once. */
  #renderPick(ask: Ask, sel: PieceSelection, p: Projection, names: Names): void {
    const picked: string[] = [];
    const done = () => { if (picked.length >= sel.min && picked.length <= sel.max) this.#send(ask, { kind: 'selected_pieces', pieceIds: [...picked] }); };
    const eligible = pickable(p, ask);                                       // a deploy pick: units with a model on the shelf (passengers spec)
    const shelved = offTable(p, eligible);
    const refresh = () => {
      const items: Array<readonly [Control, (() => void)?]> = [
        [{ kind: 'label', text: sel.max === 1 && picked.length === 1 ? names.piece(picked[0]!) : `${picked.length} of ${sel.min}–${sel.max}` }],
      ];
      for (const id of shelved) {
        const key = this.#numbered.find((n) => n.optionId === id)?.key ?? 0;
        items.push([button(`pick:${id}`, key > 0 ? `${key}. ${names.piece(id)}` : names.piece(id)), () => toggle(id)]);
      }
      items.push([{ ...button('done', 'Done (Enter)'), disabled: picked.length < sel.min || picked.length > sel.max }, done]);
      this.#segment(items);
    };
    const toggle = (id: string) => {
      const at = picked.indexOf(id);
      if (at >= 0) { picked.splice(at, 1); this.#ghost.delete(id); }
      else if (picked.length < sel.max) { picked.push(id); this.#ghost.add(id); }
      else return;
      if (sel.max === 1 && picked.length === 1) { done(); return; }
      refresh();
    };
    this.handlers.onNumber = (option) => { if (eligible.includes(option)) toggle(option); };
    this.#eligible = new Set(eligible);
    this.handlers.onTarget = (clicked) => { const id = this.#resolve(clicked, eligible); if (id !== null) toggle(id); };
    this.handlers.onEnter = done;
    refresh();
  }

  /**
   * declare_targets: weapon groups as chips, all picked to start (p.8: one group per weapon type). A click on an enemy aims every picked
   * group that can shoot it (p.8); a chip picks its group alone. Enter fires once a group has a target (an unaimed group holds fire),
   * Esc clears the assignments.
   */
  #renderTargets(ask: Ask, legal: Assignment, names: Names): void {
    const t = this.#targets = new TargetAssignment(legal);
    const fire = () => { const a = t.answer(); if (a !== null) this.#send(ask, a); };
    const show = () => {
      if (this.#targets !== t) return;
      const items: Array<readonly [Control, (() => void)?]> = t.groups.map((g) => {
        const target = t.assigned.get(g.id);
        return [{ kind: 'chip', id: `chip:${g.id}`, label: TargetAssignment.chip(g, target === undefined ? null : names.unit(target)), pressed: t.picked.has(g.id) } as Control,
          () => { t.pickOnly(g.id); show(); }] as const;
      });
      items.push([{ ...button('fire', 'Fire (Enter)'), disabled: t.answer() === null }, fire]);
      this.#eligible = new Set(t.candidates); this.glow(t.candidates);       // only units a picked group can shoot; two targets taken (p.8): nothing else glows
      this.#segment(items);
    };
    this.handlers.onTarget = (clicked) => { const unit = this.#resolve(clicked, legal.targets); if (unit !== null && t.aim(unit)) show(); };
    this.handlers.onEnter = fire;
    this.handlers.onCancel = () => { if (t.assigned.size === 0) return false; t.clear(); show(); return true; };
    show();
  }

  /**
   * remove_casualties: a click marks a model, each further click on a many-wound model adds a wound, a right-click takes one away. Auto
   * fills the rest in the Ask's default order. Enter confirms once the count is exact.
   */
  #renderCasualties(ask: Ask, legal: Allocation, p: Projection): void {
    const facts = this.#host.state().maxWounds;
    const left = new Map(legal.targets.map((id) => [id, Math.max(0, (facts.get(id) ?? 1) - (p.models.get(id)?.wounds ?? 0))] as const));
    const pick = new CasualtyPick(legal, left);
    this.#title = pick.title; this.#hint = pick.hint;
    const confirm = () => { const a = pick.answer(); if (a !== null) this.#send(ask, a); };
    const show = () => {
      const marks = new Map<string, string>();
      for (const [id, n] of pick.counts) if (n > 0 && p.models.has(id)) marks.set(id, left.get(id)! > 1 ? `✕ ${n}/${left.get(id)}` : '✕');
      this.#marks = marks;
      this.#segment([
        [{ kind: 'label', text: pick.status }],
        [button('auto', 'Auto'), () => { pick.auto(); show(); }],
        [button('clear', 'Clear'), () => { pick.clear(); show(); }],
        [{ ...button('confirm', 'Confirm (Enter)'), disabled: !pick.complete }, confirm],
      ]);
    };
    this.#eligible = new Set(legal.targets);
    this.handlers.onModel = (id, right) => { if (!left.has(id)) return; if (right) pick.remove(id); else pick.click(id); show(); };
    this.handlers.onEnter = confirm;
    show();
  }
}

/** A window kind in words: `before_shooting` is "before shooting". */
const spaced = (kind: string): string => kind.replaceAll('_', ' ');
const button = (id: string, label: string): Extract<Control, { kind: 'button' }> => ({ kind: 'button', id, label, disabled: false });
