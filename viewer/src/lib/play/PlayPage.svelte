<!-- viewer/src/lib/play/PlayPage.svelte -->
<!-- A game in play (from `playing` on): the board of the projection, the decision box and the Game over modal. The page binds; the
     decisions are in decisionBox.ts, scene.ts and keys.ts. Moves and deploy placement are plan D2's. -->
<script lang="ts">
  import { untrack } from 'svelte';
  import type { Api } from '../api/client';
  import type { BoardHit, BoardInteraction, Modifiers, Overlay } from '../board/interaction';
  import { extentOf } from '../board/table';
  import type { View } from '../board/transform';
  import Board from '../components/Board.svelte';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { loadGeometry, type Geometry } from '../geometry';
  import { acting, teamsOf } from '../lobby/seats';
  import { DecisionBox, type DecisionPanel } from './decisionBox';
  import { moveHooks } from './decisionBox.move';
  import DecisionBoxView from './DecisionBox.svelte';
  import GameOverModal from './GameOverModal.svelte';
  import Banner from './Banner.svelte';
  import HeaderBar from './HeaderBar.svelte';
  import InfoColumn from './InfoColumn.svelte';
  import Roster from './Roster.svelte';
  import { Names } from './names';
  import { mine as askMine } from './askView';
  import { refusalText } from '../connection/feedback';
  import { choose, pieceMenu, rosterMenu, tableMenu, teamMenu, type MenuContext, type MenuStep } from './menus';
  import type { MenuItem } from './menuModel';
  import PieceMenu from './PieceMenu.svelte';
  import WaitingPanel from './WaitingPanel.svelte';
  import { intentOf, textFocused } from './keys';
  import { isWatching } from './waiting';
  import { RangeRingCache, ringsOverlay } from './rangeRings';
  import { SightCheck, sightOverlay } from './sightCheck';
  import { teamColour } from '../board/palette';
  import { fetchRuleTexts, RuleTexts, type GrantChoice } from './ruleTexts';
  import { sceneOfPlay } from './scene';
  import type { Selection } from './selectionView';
  import { loadSettings, saveSettings, type PlaySettings, type StorageLike } from './settings';
  import { UndoPrediction } from './undo';
  // D2: moves, deploys and hand moves
  import { MarkerLocks } from './markerLocks';
  import { PendingDrops } from './pendingDrops';
  import { relocations } from './relocations';
  import { PlayDrags } from './playDrag';
  import { Violations } from './violations';

  let { connection, api, view = $bindable(null) }: { connection: GameConnection; api: Api; view?: View | null } = $props();

  let snap = $state.raw<ConnectionState | null>(null);
  let panel = $state.raw<DecisionPanel>({ seat: '', title: '', revealed: null, note: '', controls: [], refusal: null, hint: '', visible: false });
  let box = $state.raw<DecisionBox | null>(null);
  let geometry = $state.raw<Geometry | null>(null);
  let rules = $state.raw(RuleTexts.empty);
  let grants = $state.raw<readonly GrantChoice[]>([]);
  let selected = $state.raw<ReadonlySet<string>>(new Set());
  let settings = $state.raw<PlaySettings>(loadSettings(browserStorage()));

  // --- D3 (info): the selection and hover the card reads, the log's table highlight, the undo prediction ---
  let selection = $state.raw<Selection | null>(null);
  let hoveredUnit = $state<string | null>(null);
  let touchedIds = $state.raw<ReadonlySet<string>>(new Set());
  let infoOverlay = $state.raw<Overlay | null>(null);
  const prediction = new UndoPrediction();
  let predictionTick = $state(0);
  $effect(() => {
    const conn = connection;
    const stops = [
      prediction.subscribe(() => { predictionTick++; }),
      conn.onEvents((events) => prediction.events(events)),
      conn.subscribe((s) => { if (s.phase === 'closed' || s.board === null) prediction.reset(); }),
    ];
    return () => { for (const stop of stops) stop(); prediction.reset(); };
  });
  // --- end D3 ---

  /** localStorage can throw (a private window, blocked site data): the settings then live for the page only. */
  function browserStorage(): StorageLike | null {
    try { return window.localStorage; } catch { return null; }
  }

  const myTeams = (s: ConnectionState): string[] => (s.game === null ? [] : teamsOf(s.game, s.participantId));

  // ---- D2: moves, deploys and hand moves ----
  // The box's move half (ghosts, gate, commit), the drag over it, and what the board draws for them. Violations, drops and locks are the
  // page's: they outlive a box, and each change of them redraws the board through `version`.
  let version = $state(0);
  const bump = (): void => { version++; };
  const violations = new Violations((id) => connection.state.projection?.models.get(id)?.slot.name ?? null);   // the box names the model, as the server does
  const drops = new PendingDrops();
  const locks = new MarkerLocks();
  const moves = moveHooks({ geometry: () => geometry, violations, enforcement: () => connection.state.game?.enforcement ?? 'strict', drops });
  const drags = new PlayDrags({
    state: () => connection.state, box: () => box, hooks: () => moves, geometry: () => geometry, violations, drops, locks,
    send: (command) => connection.sendCommand(command),
    select: (ids, unit) => {
      selected = ids;
      if (unit === null) return;
      const all = [...(snap?.projection?.models.values() ?? [])].filter((m) => m.unitId === unit).map((m) => m.id);
      selection = { pieceId: unit, modelIds: all.every((id) => ids.has(id)) ? [] : [...ids] };   // D3's selection: the unit, or the one model
      box?.unit(unit);
    },
    changed: bump,
  });
  $effect(() => {
    const conn = connection;
    const stops = [
      violations.subscribe(bump), locks.subscribe(bump),
      conn.onEvents((events) => {                                         // a move's event (a hand move's, or a step's for an answer) ends the hold
        for (const e of events) for (const m of relocations(e)) drops.consume(m);
        bump();
      }),
      conn.onRefused((refused) => { drags.refused(refused); }),
    ];
    return () => { for (const stop of stops) stop(); };
  });
  $effect(() => { void panel; untrack(() => drags.boxChanged()); });      // the Ask ended mid-drag: its ghosts are dropped, not sent
  // ---- end D2 ----

  $effect(() => {
    const conn = connection;
    const b = new DecisionBox({
      state: () => conn.state,
      myTeams: () => myTeams(conn.state),
      acting: () => { const s = conn.state; return acting(s.projection, myTeams(s), s.log); },
      settings: () => settings,
      updateSettings: (change) => { settings = change(settings); saveSettings(browserStorage(), settings); },
      rules: () => rules,
      send: (command) => conn.sendCommand(command),
      onRefused: (listener) => conn.onRefused((refused) => listener(refused)),
    }, moves);
    const stops = [
      b.subscribe((p) => { panel = p; }),
      conn.subscribe((s) => { snap = s; b.render(); }),
      conn.onEvents((events) => { b.reveal(events); b.render(); }),
    ];
    box = b;
    return () => { for (const stop of stops) stop(); b.dispose(); box = null; };
  });

  $effect(() => {
    loadGeometry().then((g) => { geometry = g; }).catch(() => { /* zones stay undrawn */ });
  });

  // The rule texts come once per property set, from playing on; a failure caches nothing and the next game frame tries again.
  const fetched = new Set<string>();
  $effect(() => {
    const game = snap?.game;
    if (game === null || game === undefined || (game.status !== 'playing' && game.status !== 'ended')) return;
    const hash = game.propertiesHash;
    untrack(() => {
      if (fetched.has(hash)) return;
      fetched.add(hash);
      void fetchRuleTexts(api, hash).then((got) => {
        if (got === null) { fetched.delete(hash); return; }
        rules = got.rules;
        grants = got.grants;
        box?.render();
      });
    });
  });

  const meshName = (piece: { mesh: { builtin?: string | undefined; assetId?: string | undefined } }): string => piece.mesh.builtin ?? piece.mesh.assetId ?? 'terrain';
  // D2: the box's ghosts, the drops waiting for their events and the models in hand, as the board draws them.
  const ghostsDrawn = $derived.by(() => { void panel; void version; return new Map([...(box?.ghosts ?? []), ...drags.drawn()]); });
  // D3: the board and every part show the prediction while an undo travels.
  const shown = $derived.by(() => { void predictionTick; return snap === null ? null : prediction.projection(snap); });
  const pendingUndo = $derived.by(() => { void predictionTick; return prediction.pendingUndoTo; });
  const scene = $derived(shown !== null && snap?.game != null
    ? sceneOfPlay(shown, snap.game, geometry?.zonePolygons ?? (() => []), meshName, ghostsDrawn) : null);
  const extent = $derived(scene === null ? undefined : extentOf(scene));
  // The box's glow and marks are not reactive: a new panel (every change of either publishes one) hands the board new ones to redraw by.
  const glow = $derived.by(() => { void panel; const b = box; return (m: string, u: string) => b?.glowOf(m, u) ?? null; });
  const marks = $derived.by(() => { void panel; return box?.marks; });
  const teams = $derived(snap === null ? [] : myTeams(snap));
  const actingTeam = $derived(snap?.projection != null ? acting(snap.projection, teams, snap.log) : null);

  // D2: the board's overlays (allowance rings, rulers, landing rings, violation labels) and the models in breach.
  const warned = $derived.by(() => { void version; return new Set(violations.marked.keys()); });
  const overlays = $derived.by(() => {
    void panel; void version;
    if (scene === null) return [];
    const ordinal = snap?.game?.teams.find((t) => t.id === actingTeam)?.ordinal ?? 1;
    const centreOf = (id: string) => scene.models.find((m) => m.id === id)?.centre ?? null;
    return drags.overlays(centreOf, ordinal);
  });

  /** TableInput.OnLeftDown: a click on a model the open Ask offers answers (the model or its unit); any other model click selects its unit. */
  function clickModel(id: string, mods: Modifiers = { shift: false, ctrl: false, alt: false }): void {
    const b = box;
    const p = snap?.projection;
    if (b === null || p === null || p === undefined) return;
    const unit = p.models.get(id)?.unitId;
    const eligible = b.eligible;
    if (eligible.has(id)) b.model(id, false);
    if (eligible.has(id) || (unit !== undefined && eligible.has(unit))) { b.target(eligible.has(id) ? id : unit!); return; }
    if (unit === undefined) return;
    // D3: a plain click selects the unit, Shift the one model (the card names which).
    selection = { pieceId: unit, modelIds: mods.shift ? [id] : [] };
    selected = mods.shift ? new Set([id]) : new Set([...p.models.values()].filter((m) => m.unitId === unit).map((m) => m.id));
    b.unit(unit);
  }

  // D4: range rings for my selected unit when V has them on (RangeRingNode).
  const ringCache = new RangeRingCache();
  const ringsDrawn = $derived.by((): Overlay | null => {
    const p = shown, game = snap?.game, unit = selection?.pieceId;
    if (!settings.rangeRings || geometry === null || p === null || game == null || unit === undefined) return null;
    const u = p.units.get(unit);
    const team = u === undefined ? undefined : game.participants.find((x) => x.id === u.participantId)?.teamId;
    if (team === undefined || team !== actingTeam) return null;                  // mine only
    const ordinal = game.teams.find((t) => t.id === team)?.ordinal ?? 1;
    return ringsOverlay(ringCache.of(geometry, p, unit), teamColour(ordinal));
  });

  // D4: the sight check — L held with a unit selected, or the shooter of my open target pick, over the hovered unit.
  let sightHeld = $state(false);
  let sightTick = $state(0);
  const sight = new SightCheck((observer, target) => connection.querySight(observer, target));
  $effect(() => sight.subscribe(() => { sightTick++; }));
  $effect(() => {
    void panel; void hoveredUnit; void sightHeld;
    const s = snap, b = box, sel = selection;
    untrack(() => { if (s?.projection != null && s.game !== null && b !== null) sight.update(s.projection, s.game, b, hoveredUnit, sel?.pieceId ?? null, sightHeld); });
  });
  const sightView = $derived.by(() => {
    void sightTick; void panel;
    return shown === null || snap?.game == null ? null : sight.view(shown, snap.game, new Names(snap.game, shown), box?.targets != null);
  });
  const sightDrawn = $derived(sightView === null || scene === null ? null : sightOverlay(sightView, scene));

  // D4: the menus (GameScreen.Play: ItemsFor, OpenTeamMenu, the roster's and the table's) and the hand actions they send.
  let menu = $state.raw<{ items: readonly MenuItem[]; at: { x: number; y: number } } | null>(null);
  let pending = $state.raw<{ item: MenuItem; what: 'piece' | 'table' } | null>(null);
  let problem = $state<string | null>(null);
  let problemTimer: ReturnType<typeof setTimeout> | undefined;
  let pointerAt = { x: 0, y: 0 };
  function report(text: string): void { problem = text; clearTimeout(problemTimer); problemTimer = setTimeout(() => { problem = null; }, 8000); }
  $effect(() => () => { clearTimeout(problemTimer); });

  function menuContext(): MenuContext | null {
    const s = snap;
    if (s === null || s.projection === null || s.game === null) return null;
    return {
      game: s.game, p: s.projection, log: s.log, watching: isWatching(s), myTeam: actingTeam,
      mine: askMine(s.projection, s.game, actingTeam, new Set(settings.mutedWindowKinds)), grantable: grants, deployInside: s.deployInside, locks,
    };
  }
  function openMenu(items: readonly MenuItem[] | null, at: { x: number; y: number }): void {
    pending = null;
    menu = items === null || items.length === 0 ? null : { items, at };
  }
  async function sendHand(command: Parameters<GameConnection['sendCommand']>[0]): Promise<void> {
    try {
      const outcome = await connection.sendCommand(command);
      if (outcome.refused !== null) report(refusalText(outcome.refused) ?? outcome.refused.detail ?? 'The server refused that.');
    } catch (e) { report(`Could not send: ${e instanceof Error ? e.message : String(e)}`); }
  }
  function onStep(item: MenuItem, step: MenuStep): void {
    switch (step.kind) {
      case 'send': void sendHand(step.command); break;
      case 'local': item.local?.(); break;
      case 'pick': pending = { item, what: step.what }; break;
      case 'invalid': report('That is not a valid value.'); break;
      case 'dialog': break;
    }
  }
  /** The pick a menu item asked for: the next piece or table click. */
  function resolvePick(hit: BoardHit | null, at: { x: number; y: number }): void {
    const wait = pending;
    pending = null;
    if (wait === null) return;
    if (wait.what === 'piece') { if (hit !== null) onStep(wait.item, choose(wait.item, hit.id)); else report('Click a piece.'); return; }
    onStep(wait.item, choose(wait.item, `${at.x},${at.y},0`));
  }
  const teamMenuAt = (teamId: string, at: { x: number; y: number }): void => {
    const c = menuContext();
    if (c !== null) openMenu(teamMenu(c, teamId, new Names(c.game, c.p)), at);
  };
  const rosterMenuAt = (unitId: string, at: { x: number; y: number }): void => {
    const c = menuContext();
    if (c !== null) openMenu(rosterMenu(c, unitId), at);
  };

  /** The roster's and header's unit pick selects the unit as a click on its model does. */
  function pickUnit(unit: string): void {
    const p = snap?.projection;
    if (p === null || p === undefined) return;
    selection = { pieceId: unit, modelIds: [] };
    selected = new Set([...p.models.values()].filter((m) => m.unitId === unit).map((m) => m.id));
    box?.unit(unit);
  }
  let gameOver: GameOverModal | undefined = $state();

  const interaction: BoardInteraction = {
    onSelect(hit: BoardHit | null, mods: Modifiers, at: { x: number; y: number }) {
      if (pending !== null) { resolvePick(hit, at); return; }              // D4: a menu item waits for this click
      if (drags.placeAt(at)) return;                                       // D2: a deploy's click on the table
      if (hit?.kind === 'model') clickModel(hit.id, mods);
      else { selected = new Set(); selection = hit === null ? null : { pieceId: hit.id, modelIds: [] }; }   // D3: a marker or terrain piece is selectable too
    },
    onHover(hit: BoardHit | null) {                           // D3: the hovered unit, for the enemy strip and the hover label
      const unit = hit?.kind === 'model' ? snap?.projection?.models.get(hit.id)?.unitId ?? null : null;
      if (unit !== hoveredUnit) hoveredUnit = unit;
    },
    onDragStart: (hit, at, mods) => drags.onDragStart(hit, at, mods),    // D2: a model, or a marker, in hand; anything else pans
    onDragMove: (at, mods) => drags.onDragMove(at, mods),
    onDragEnd: (at, mods) => drags.onDragEnd(at, mods),
    onDragCancel: () => drags.onDragCancel(),
    onContextMenu(hit: BoardHit | null, at: { x: number; y: number }, _screen: unknown, mods?: Modifiers) {
      pending = null;
      if (hit?.kind === 'model' && box?.eligible.has(hit.id) === true) { box.model(hit.id, true); return; }   // the casualty pick's, not a menu
      const c = menuContext();
      if (c === null) return;
      if (hit !== null && hit.kind !== 'terrain') openMenu(pieceMenu(c, hit.id, mods?.shift === true), pointerAt);
      else if (mods?.alt === true) openMenu(tableMenu(c, { x: at.x, y: at.y, z: 0 }), pointerAt);
    },
  };

  // --- D3 helpers: what the card and the log hand back to the page ---
  /** The table lights the selection and what a clicked log row touched. */
  const drawnSelection = $derived(touchedIds.size === 0 ? selected : new Set([...selected, ...touchedIds]));
  const infoEligible = $derived.by(() => { void panel; return box?.eligible ?? new Set<string>(); });
  function changeSettings(change: (s: PlaySettings) => PlaySettings): void { settings = change(settings); saveSettings(browserStorage(), settings); }
  /** A card group's click: that model, selected alone (Shift's one-model selection). */
  function pickModel(id: string): void {
    const unit = snap?.projection?.models.get(id)?.unitId;
    if (unit === undefined) return;
    selection = { pieceId: unit, modelIds: [id] };
    selected = new Set([id]);
  }
  // --- end D3 helpers ---

  function onKeydown(e: KeyboardEvent): void {
    if (e.key === 'Escape' && pending !== null) { pending = null; e.preventDefault(); return; }
    if (e.defaultPrevented || textFocused(e.target) || box === null) return;
    const mods: Modifiers = { shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey, alt: e.altKey };
    const intent = intentOf(e.key, mods);
    if (intent === null) return;
    if (drags.onKey(intent, e.shiftKey)) { e.preventDefault(); return; }   // D2: Q/E, 1–5 and Enter while a drag is in hand
    if (intent.kind === 'sight') { sightHeld = true; e.preventDefault(); return; }
    if (intent.kind === 'toggleRangeRings') { changeSettings((x) => ({ ...x, rangeRings: !x.rangeRings })); e.preventDefault(); return; }
    const used = intent.kind === 'option' ? box.number(intent.key)
      : intent.kind === 'confirm' ? box.enter()
      : intent.kind === 'cancel' ? box.cancel()
      : intent.kind === 'stopHere' ? box.stop()
      : false;
    if (used) e.preventDefault();
  }
</script>

<svelte:window onpointerdowncapture={(e) => { pointerAt = { x: e.clientX, y: e.clientY }; }} onkeydown={onKeydown} onkeyup={(e) => { if (e.key === 'l' || e.key === 'L') sightHeld = false; }} onblur={() => { sightHeld = false; }} />

<div class="play">
  {#if snap !== null && shown !== null && snap.game !== null}
    <div class="top"><HeaderBar {snap} {shown} myTeams={teams} {actingTeam} onGameOver={() => gameOver?.open()} onTeamMenu={teamMenuAt} /></div>
    <div class="roster"><Roster {snap} {shown} myTeam={actingTeam} selected={selection?.pieceId ?? null} onPick={pickUnit} onMenu={rosterMenuAt} /></div>
  {/if}
  <div class="stage">
    {#if scene === null || snap === null}
      <p class="loading" role="status">Loading the game…</p>
    {:else}
      <Board {scene} bind:view {extent} {interaction} selected={drawnSelection} overlays={[...overlays, ...(ringsDrawn === null ? [] : [ringsDrawn]), ...(sightDrawn === null ? [] : [sightDrawn]), ...(infoOverlay === null ? [] : [infoOverlay])]} {warned} dimmed={sightView?.dimmed} models="circles" {glow} {marks} />
    {/if}
    <div class="float banner"><Banner {connection} /></div>
    {#if pending !== null || problem !== null}
      <div class="float foot">
        {#if pending !== null}<p class="notice" role="status">{pending.item.label}: {pending.what === 'piece' ? 'click a piece' : 'click the table'}. Esc cancels.</p>{/if}
        {#if problem !== null}<p role="alert">{problem}</p>{/if}
      </div>
    {/if}
  </div>
  <aside class="side" aria-label="Decision, unit and log">
    {#if snap !== null}<WaitingPanel {connection} {api} {snap} />{/if}
    {#if box !== null}<DecisionBoxView {box} {panel} />{/if}
    {#if snap !== null && box !== null}
      <InfoColumn
        {connection} {snap} {shown} {scene} {prediction} pendingUndoTo={pendingUndo} {rules} {geometry}
        {selection} {hoveredUnit} myTeam={actingTeam} eligible={infoEligible} getSettings={() => settings} updateSettings={changeSettings}
        onPickModel={pickModel} onTouch={(ids) => { touchedIds = new Set(ids); }} bind:overlay={infoOverlay}
      />
    {/if}
  </aside>
  {#if menu !== null}<PieceMenu items={menu.items} at={menu.at} {onStep} onClose={() => (menu = null)} />{/if}
  {#if snap !== null && box !== null}
    <GameOverModal bind:this={gameOver} {connection} {snap} myTeams={teams} {actingTeam} />
  {/if}
</div>

<style>
  /* The top bar over the roster and the board; the right column the full height. Nothing floats over the board but the banner and the
     pick hint or refusal at its foot. */
  .play { display: grid; height: 100%; min-height: 0; grid-template-columns: auto minmax(0, 1fr) 23rem; grid-template-rows: auto minmax(0, 1fr);
    grid-template-areas: 'top top side' 'roster stage side'; }
  .top { grid-area: top; min-width: 0; }
  .roster { grid-area: roster; min-height: 0; }
  .stage { grid-area: stage; position: relative; min-height: 0; min-width: 0; }
  .stage > .loading { padding: var(--s4); }
  .float { position: absolute; z-index: 2; left: 50%; transform: translateX(-50%); pointer-events: none; max-width: calc(100% - 2 * var(--s4)); }
  .float.banner { top: 20%; }
  .float.foot { bottom: var(--s4); display: flex; flex-direction: column; gap: var(--s2); align-items: center; }
  .float.foot p { margin: 0; box-shadow: 0 2px 10px #0000004d; }
  .side { grid-area: side; display: flex; flex-direction: column; gap: var(--s3); min-height: 0; overflow: hidden; padding: var(--s3);
    border-left: 1px solid var(--rule); background: color-mix(in srgb, var(--panel-solid) 60%, transparent); }
  @media (max-width: 75rem) { .play { grid-template-columns: auto minmax(0, 1fr) 20rem; } }
  /* A short window: the column scrolls as a whole, each part at its own height, the log at least 24rem. */
  @media (max-height: 52rem) {
    .side { overflow-y: auto; }
    .side :global(:is(.decision, .card)) { flex: none; max-height: none; }
    .side :global(.info) { flex: 1 0 auto; }
    .side :global(.log.open) { flex: 1 0 24rem; height: 24rem; }
  }
</style>
