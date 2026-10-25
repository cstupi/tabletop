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
  import DecisionBoxView from './DecisionBox.svelte';
  import GameOverModal from './GameOverModal.svelte';
  import InfoColumn from './InfoColumn.svelte';
  import { intentOf, textFocused } from './keys';
  import { fetchRuleTexts, RuleTexts } from './ruleTexts';
  import { sceneOfPlay } from './scene';
  import type { Selection } from './selectionView';
  import { loadSettings, saveSettings, type PlaySettings, type StorageLike } from './settings';
  import { UndoPrediction } from './undo';

  let { connection, api, view = $bindable(null) }: { connection: GameConnection; api: Api; view?: View | null } = $props();

  let snap = $state.raw<ConnectionState | null>(null);
  let panel = $state.raw<DecisionPanel>({ seat: '', title: '', revealed: null, note: '', controls: [], refusal: null, hint: '', visible: false });
  let box = $state.raw<DecisionBox | null>(null);
  let geometry = $state.raw<Geometry | null>(null);
  let rules = $state.raw(RuleTexts.empty);
  let selected = $state.raw<ReadonlySet<string>>(new Set());
  let settings: PlaySettings = loadSettings(browserStorage());

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
    });
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
        box?.render();
      });
    });
  });

  const meshName = (piece: { mesh: { builtin?: string | undefined; assetId?: string | undefined } }): string => piece.mesh.builtin ?? piece.mesh.assetId ?? 'terrain';
  // D3: the board and every part show the prediction while an undo travels.
  const shown = $derived.by(() => { void predictionTick; return snap === null ? null : prediction.projection(snap); });
  const pendingUndo = $derived.by(() => { void predictionTick; return prediction.pendingUndoTo; });
  const scene = $derived(shown !== null && snap?.game != null
    ? sceneOfPlay(shown, snap.game, geometry?.zonePolygons ?? (() => []), meshName) : null);
  const extent = $derived(scene === null ? undefined : extentOf(scene));
  // The box's glow and marks are not reactive: a new panel (every change of either publishes one) hands the board new ones to redraw by.
  const glow = $derived.by(() => { void panel; const b = box; return (m: string, u: string) => b?.glowOf(m, u) ?? null; });
  const marks = $derived.by(() => { void panel; return box?.marks; });
  const teams = $derived(snap === null ? [] : myTeams(snap));
  const actingTeam = $derived(snap?.projection != null ? acting(snap.projection, teams, snap.log) : null);

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

  const interaction: BoardInteraction = {
    onSelect(hit: BoardHit | null, mods: Modifiers) {
      if (hit?.kind === 'model') clickModel(hit.id, mods);
      else { selected = new Set(); selection = hit === null ? null : { pieceId: hit.id, modelIds: [] }; }   // D3: a marker or terrain piece is selectable too
    },
    onHover(hit: BoardHit | null) {                           // D3: the hovered unit, for the enemy strip and the hover label
      const unit = hit?.kind === 'model' ? snap?.projection?.models.get(hit.id)?.unitId ?? null : null;
      if (unit !== hoveredUnit) hoveredUnit = unit;
    },
    onDragStart() { return false; },                          // hand moves are plan D2's: a drag pans
    onContextMenu(hit: BoardHit | null) {
      if (hit?.kind === 'model' && box?.eligible.has(hit.id) === true) box.model(hit.id, true);
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
    if (e.defaultPrevented || textFocused(e.target) || box === null) return;
    const mods: Modifiers = { shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey, alt: e.altKey };
    const intent = intentOf(e.key, mods);
    if (intent === null) return;
    const used = intent.kind === 'option' ? box.number(intent.key)
      : intent.kind === 'confirm' ? box.enter()
      : intent.kind === 'cancel' ? box.cancel()
      : intent.kind === 'stopHere' ? box.stop()
      : false;
    if (used) e.preventDefault();
  }
</script>

<svelte:window onkeydown={onKeydown} />

<div class="play">
  <div class="stage">
    {#if scene === null || snap === null}
      <p class="muted pad">Loading the game…</p>
    {:else}
      <Board {scene} bind:view {extent} {interaction} selected={drawnSelection} overlays={infoOverlay === null ? [] : [infoOverlay]} models="circles" {glow} {marks} />
    {/if}
  </div>
  <aside class="side">
    {#if box !== null}<DecisionBoxView {box} {panel} />{/if}
    {#if snap !== null && box !== null}
      <InfoColumn
        {connection} {snap} {shown} {scene} {prediction} pendingUndoTo={pendingUndo} {rules} {geometry}
        {selection} {hoveredUnit} myTeam={actingTeam} eligible={infoEligible} getSettings={() => settings} updateSettings={changeSettings}
        onPickModel={pickModel} onTouch={(ids) => { touchedIds = new Set(ids); }} bind:overlay={infoOverlay}
      />
    {/if}
  </aside>
  {#if snap !== null && box !== null}
    <GameOverModal {connection} {snap} myTeams={teams} {actingTeam} />
  {/if}
</div>

<style>
  .play { display: grid; grid-template-columns: minmax(0, 1fr) 24rem; height: 100%; min-height: 0; }
  .stage { position: relative; min-height: 0; min-width: 0; }
  .side { overflow: auto; border-left: 1px solid var(--line); padding: 0.5rem 0.6rem; }
</style>
