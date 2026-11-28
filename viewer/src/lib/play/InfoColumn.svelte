<!-- viewer/src/lib/play/InfoColumn.svelte -->
<!-- Plan D3 on the play page: the hovered enemy's strip and the unit card (SheetFocus) or the selected terrain piece's card, the latest roll's tray, the combat log (G and its
     button) and Undo (Ctrl+Z), each part in its HUD frame rect (PlayPage's place and measure). PlayPage holds the selection, the hover and
     the undo prediction; this reads them and the connection's log. A watcher sees the log and the card and no Undo. -->
<script lang="ts">
  import type { Overlay } from '../board/interaction';
  import type { Scene } from '../board/frame';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import type { Geometry } from '../geometry';
  import type { Projection } from '../live/apply';
  import { filter as filterEntries } from './combatLog';
  import { CombatLogCache } from './combatLogState';
  import CombatLogPanel from './CombatLogPanel.svelte';
  import DiceTray from './DiceTray.svelte';
  import { enemyStripOf, measuredUnit, stripShows } from './enemyStrip';
  import EnemyStrip from './EnemyStrip.svelte';
  import { HoverDistances, hoverOf } from './hoverView';
  import { hoverOverlay } from './hoverLabels';
  import { intentOf, textFocused } from './keys';
  import { Names } from './names';
  import type { RuleTexts } from './ruleTexts';
  import { describeSelection, type Selection } from './selectionView';
  import { sheetFocusOf, teamOf } from './sheetFocus';
  import type { LogFilter, PlaySettings } from './settings';
  import UndoButton from './UndoButton.svelte';
  import { sameCard, unitCardOf, type UnitCardContent } from './unitCard';
  import UnitCard from './UnitCard.svelte';
  import TerrainCard from './TerrainCard.svelte';
  import type { Catalog } from '../setup/catalog';
  import { terrainCardOf } from '../setup/terrainCard';
  import { unitSheetOf } from './unitSheet';
  import { isWatching } from './waiting';
  import type { UndoPrediction } from './undo';
  import { FILTER_LABELS, bandOf } from './logRows';
  import HudIcon from './HudIcon.svelte';
  import type { HudFrame, HudPart } from './hudFrame';

  const FILTERS: readonly LogFilter[] = ['all', 'rolls', 'mine', 'unit'];

  let {
    connection, snap, shown, scene, prediction, pendingUndoTo, rules, geometry, selection, hoveredUnit, catalog, myTeam, eligible, getSettings, updateSettings,
    onPickModel, onTouch, overlay = $bindable(null), pickHovered = null, placing = null, frame, place, measure, logOpen = $bindable(false),
  }: {
    connection: GameConnection; snap: ConnectionState;
    /** The projection to show: the prediction while an undo travels, else the connection's. */
    shown: Projection | null; scene: Scene | null;
    prediction: UndoPrediction; pendingUndoTo: number | null; rules: RuleTexts; geometry: Geometry | null;
    selection: Selection | null; hoveredUnit: string | null; myTeam: string | null;
    /** The terrain names and the terrain card's catalog. */
    catalog: Catalog;
    /** The open Ask's clickable pieces. */
    eligible: ReadonlySet<string>;
    getSettings: () => PlaySettings; updateSettings: (change: (s: PlaySettings) => PlaySettings) => void;
    onPickModel: (modelId: string) => void; onTouch: (pieceIds: readonly string[]) => void;
    /** The hover label, drawn over the board. */
    overlay?: Overlay | null;
    /** A deploy pick's row under the pointer, and the unit my team is placing (PlacingFocus's): the card shows a unit not yet on the table. */
    pickHovered?: string | null; placing?: string | null;
    /** The HUD frame, a part's wrapper style in it and the action that reports a wrapper's size (PlayPage). */
    frame: HudFrame; place: (part: HudPart, width: number, fills?: boolean) => string;
    measure: (node: HTMLElement, part: HudPart) => { destroy: () => void };
    /** The log's drawer is open (G): the page's frame gives it the right column's top. */
    logOpen?: boolean;
  } = $props();

  const cache = new CombatLogCache();
  // svelte-ignore state_referenced_locally
  logOpen = getSettings().logOpen;
  // svelte-ignore state_referenced_locally
  let logFilter = $state<LogFilter>(getSettings().logFilter);
  // svelte-ignore state_referenced_locally
  let logDetailed = $state(getSettings().logDetailed);
  let problem = $state<string | null>(null);
  let revealRoll = $state<{ rollId: string; n: number } | null>(null);
  let undo: UndoButton | undefined = $state();
  let lastCard: UnitCardContent | null = null;
  /** The strip's and the overlay's hover distance, kept per placements: a batch that moved neither unit asks the geometry nothing. */
  const distances = new HoverDistances();

  const playing = $derived(snap.game !== null && (snap.game.status === 'playing' || snap.game.status === 'ended'));
  const watching = $derived(isWatching(snap));
  const log = $derived(snap.game !== null && playing && shown !== null ? cache.of(snap.log, pendingUndoTo, snap.game, shown, snap.participantId) : null);

  const focus = $derived(shown !== null && snap.game !== null && playing ? sheetFocusOf(shown, snap.game, myTeam, selection?.pieceId ?? null, pickHovered, placing) : null);

  const sheet = $derived.by(() => {
    if (focus === null || shown === null || snap.game === null) return null;
    return unitSheetOf(shown, new Names(snap.game, shown), focus, rules, snap.maxWounds);
  });

  /** Rebuilt only when what it shows changed (sameCard), so an open tooltip and the scroll survive other frames. */
  const card = $derived.by(() => {
    const s = sheet;
    if (s === null || shown === null || snap.game === null || focus === null) { lastCard = null; return null; }
    const next = unitCardOf(s, teamOf(shown, snap.game, focus)?.ordinal ?? 0, snap.inCover.has(focus));
    if (sameCard(lastCard, next)) return lastCard;
    lastCard = next;
    return next;
  });

  const strip = $derived.by(() => {
    const p = shown, game = snap.game;
    if (p === null || game === null || sheet === null || focus === null || hoveredUnit === null || !p.units.has(hoveredUnit)) return null;
    const measured = measuredUnit(p, game, myTeam, focus);   // the distance from the acting shooter, whatever the card shows
    if (!stripShows(p, game, myTeam, measured, hoveredUnit)) return null;
    const names = new Names(game, p);
    const distance = hoverOf(geometry, p, game, names, hoveredUnit, measured, snap.maxWounds, distances)?.distance ?? null;
    return enemyStripOf(unitSheetOf(p, names, hoveredUnit, rules, snap.maxWounds), distance);
  });

  /** HoverLabels: the distance follows the name only with one of my units selected. */
  const mySelected = $derived.by(() => {
    const p = shown, game = snap.game, id = selection?.pieceId;
    if (p === null || game === null || id === undefined) return null;
    const team = teamOf(p, game, id);
    return team !== null && team.id === myTeam ? id : null;
  });

  $effect(() => {
    const p = shown, game = snap.game, hovered = hoveredUnit, sc = scene;
    if (p === null || game === null || hovered === null || sc === null || !playing) { overlay = null; return; }
    const info = hoverOf(geometry, p, game, new Names(game, p), hovered, mySelected, snap.maxWounds, distances);
    if (info === null) { overlay = null; return; }
    const isEligible = eligible.has(hovered) || [...p.models.values()].some((m) => m.unitId === hovered && eligible.has(m.id));
    overlay = hoverOverlay(sc, info, isEligible);
  });

  /** A selected terrain piece takes the unit card's place (SheetFocus names no unit then); read-only in play. */
  const terrainCard = $derived(shown !== null && snap.game !== null && playing
    ? terrainCardOf(shown.board.table, snap.game.status, selection?.pieceId ?? null, catalog, snap.terrainWords, snap.terrainRules) : null);

  const selectionLine = $derived(shown !== null && snap.game !== null && playing ? describeSelection(new Names(snap.game, shown), shown, selection) : null);

  const entries = $derived.by(() => {
    if (log === null) return [];
    const n = log.names;
    return filterEntries(log.entries, logFilter, (u) => { const t = n.teamOfUnit(u); return t !== null && t === myTeam; }, focus);
  });
  /** The open log holds the latest roll: the frame gives the tray no slot then (HudFrame's covered), and it hides. */
  const tray = $derived(!frame.covered.has('dice') && log?.latest != null);

  function setLogOpen(open: boolean): void {
    logOpen = open;
    updateSettings((s) => ({ ...s, logOpen: open }));
  }
  function setFilter(f: LogFilter): void {
    logFilter = f;
    updateSettings((s) => ({ ...s, logFilter: f }));
  }
  function setDetailed(on: boolean): void {
    logDetailed = on;
    updateSettings((s) => ({ ...s, logDetailed: on }));
  }

  /** The tray's click: open the log there, on this roll. */
  function openAt(rollId: string): void {
    setLogOpen(true);
    revealRoll = { rollId, n: (revealRoll?.n ?? 0) + 1 };
  }

  /** Session.UndoToAsync through the prediction: a refused or failed undo shows `Undo failed: {message}`. */
  async function undoTo(seq: number): Promise<void> {
    problem = null;
    try {
      const outcome = await prediction.undoTo(connection.state, seq, (c) => connection.sendCommand(c));
      if (outcome?.refused != null) problem = `Undo failed: ${outcome.refused.detail}`;
    } catch (e) {
      problem = `Undo failed: ${e instanceof Error ? e.message : String(e)}`;
    }
  }

  $effect(() => connection.onRefused((refused, text) => {
    const was = prediction.pendingUndoTo;
    prediction.refused(refused.messageId);
    if (was !== null && prediction.pendingUndoTo === null) problem = `Undo failed: ${text ?? refused.detail}`;
  }));

  function onKeydown(e: KeyboardEvent): void {
    if (e.defaultPrevented || textFocused(e.target) || !playing) return;
    const intent = intentOf(e.key, { shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey, alt: e.altKey });
    if (intent?.kind === 'undo') { if (!watching) { undo?.press(); e.preventDefault(); } }
    else if (intent?.kind === 'toggleLog') { setLogOpen(!logOpen); e.preventDefault(); }
  }
</script>

<svelte:window onkeydown={onKeydown} />

{#if playing}
  <div class="part" style={place('undoButton', 38, true)}>
    <UndoButton bind:this={undo} plan={log?.undo ?? null} {watching} onUndo={(seq) => void undoTo(seq)} />
  </div>
  <div class="part" style={place('logButton', 38, true)}>
    <button type="button" class="corner-button" class:open={logOpen} tabindex="-1" aria-pressed={logOpen} aria-label="Log" title="Log (G)"
      onmousedown={(e) => e.preventDefault()} onclick={() => setLogOpen(!logOpen)}><HudIcon name="log" size={22} /></button>
  </div>
  {#if logOpen}
    <div class="part" style={place('logPanel', 640, true)}>
      <section class="panel log" aria-label="Combat log">
        <div class="head">
          <h2><HudIcon name="log" size={18} />Log</h2>
          <select class="filter" aria-label="Log filter" value={logFilter} onchange={(ev) => setFilter(ev.currentTarget.value as LogFilter)}>
            {#each FILTERS as f, i (f)}<option value={f}>{FILTER_LABELS[i]}</option>{/each}
          </select>
          <label class="detailed" title="Each roll as tables of its dice, its modifiers and its totals">
            <input type="checkbox" checked={logDetailed} onchange={(ev) => setDetailed(ev.currentTarget.checked)} /> Detailed
          </label>
        </div>
        <CombatLogPanel
          {entries} filter={logFilter} detailed={logDetailed} {pendingUndoTo} {watching} {rules} reveal={revealRoll} showFilter={false}
          bandOf={(e) => (log !== null && snap.game !== null ? bandOf(e, log.names, snap.game) : 0)}
          onFilter={setFilter} onUndoTo={(seq) => void undoTo(seq)} {onTouch}
        />
      </section>
    </div>
  {/if}
  <div class="part cards" style={place('unitCard', 480)} use:measure={'unitCard'}>
    {#if problem !== null}<p class="problem panel" role="alert">{problem}</p>{/if}
    {#if strip !== null}<EnemyStrip {strip} />{/if}
    {#if card !== null}
      <UnitCard {card} selectedModel={selection?.modelIds.length === 1 ? selection.modelIds[0] ?? null : null} picked={selection?.pieceId === card.sheet.unitId ? selection.modelIds : []} onPick={onPickModel} />
    {/if}
    {#if terrainCard !== null}<TerrainCard card={terrainCard} />{/if}
    {#if selectionLine !== null && (card === null || selection?.modelIds.length === 1)}<p class="selection panel">{selectionLine}</p>{/if}
  </div>
  <div class="part" style={place('dice', 480)} use:measure={'dice'}>
    {#if tray}<div class="panel roll"><DiceTray roll={log?.latest ?? null} onClick={openAt} /></div>{/if}
  </div>
{/if}

<style>
  /* Each part at its frame rect (PlayPage's place): the wrapper caps it, its own content scrolls inside. */
  .part { position: absolute; z-index: 2; display: flex; flex-direction: column; gap: var(--space-2); min-height: 0; overflow: hidden; box-sizing: border-box; }
  .part > :global(*) { flex: 0 1 auto; min-height: 0; max-height: none; margin: 0; }
  .part :global(.corner-button) { display: inline-flex; align-items: center; justify-content: center; width: 100%; height: 100%; min-height: 0; padding: 0; line-height: 0; }
  .part .open { background-color: var(--btn-hover); color: var(--accent); }
  .log { display: flex; flex-direction: column; gap: var(--space-2); margin: 0; padding: var(--space-2); flex: 1 1 auto; height: 100%; box-sizing: border-box; }
  :global([data-theme='fantasy']) .log { padding: calc(var(--space-2) + var(--frame-inset)); }
  .cards { justify-content: flex-end; }
  .problem, .selection { margin: 0; padding: var(--space-2); }
  .selection { color: var(--dim); font-size: .9rem; }
  .roll { margin: 0; padding: var(--space-2); }
  .head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--space-2); }
  .head h2 { display: inline-flex; align-items: center; gap: var(--space-2); margin: 0; font-size: .9rem; }
  .filter { min-height: 2rem; min-width: 0; font-size: .9rem; }
  .detailed { display: flex; flex: none; align-items: center; gap: var(--space-1); font-size: .9rem; white-space: nowrap; }
</style>
