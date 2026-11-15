<!-- viewer/src/lib/play/InfoColumn.svelte -->
<!-- Plan D3 on the play page: the hovered enemy's strip and the unit card (SheetFocus) or the selected terrain piece's card, the latest roll's tray, the combat log (G and its
     button) and Undo (Ctrl+Z). PlayPage holds the selection, the hover and the undo prediction; this reads them and the connection's
     log. A watcher sees the log and the card and no Undo. -->
<script lang="ts">
  import type { Overlay } from '../board/interaction';
  import type { Scene } from '../board/frame';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import type { Geometry } from '../geometry';
  import type { Projection } from '../live/apply';
  import { filter as filterEntries, flatten } from './combatLog';
  import { CombatLogCache } from './combatLogState';
  import CombatLogPanel from './CombatLogPanel.svelte';
  import { noteOf } from './decisionView';
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
  import Icon from '../../../../web/src/lib/components/Icon.svelte';

  const FILTERS: readonly LogFilter[] = ['all', 'rolls', 'mine', 'unit'];

  let {
    connection, snap, shown, scene, prediction, pendingUndoTo, rules, geometry, selection, hoveredUnit, catalog, myTeam, eligible, getSettings, updateSettings,
    onPickModel, onTouch, overlay = $bindable(null),
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
  } = $props();

  const cache = new CombatLogCache();
  // svelte-ignore state_referenced_locally
  let logOpen = $state(getSettings().logOpen);
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
  const log = $derived(snap.game !== null && playing && shown !== null ? cache.of(snap.log, pendingUndoTo, snap.game, shown) : null);

  const focus = $derived(shown !== null && snap.game !== null && playing ? sheetFocusOf(shown, snap.game, myTeam, selection?.pieceId ?? null) : null);

  const sheet = $derived.by(() => {
    if (focus === null || shown === null || snap.game === null) return null;
    return unitSheetOf(shown, new Names(snap.game, shown), focus, rules, snap.maxWounds);
  });

  /** Rebuilt only when what it shows changed (sameCard), so an open tooltip and the scroll survive other frames. */
  const card = $derived.by(() => {
    const s = sheet;
    if (s === null || shown === null || snap.game === null || focus === null) { lastCard = null; return null; }
    const next = unitCardOf(s, teamOf(shown, snap.game, focus)?.ordinal ?? 0, noteOf(shown, snap.game, myTeam, focus));
    if (sameCard(lastCard, next)) return lastCard;
    lastCard = next;
    return next;
  });

  const strip = $derived.by(() => {
    const p = shown, game = snap.game;
    if (p === null || game === null || sheet === null || focus === null || hoveredUnit === null || !p.units.has(hoveredUnit)) return null;
    const measured = measuredUnit(p, game, myTeam, focus);   // the acting shooter's weapons, whatever the card shows
    if (!stripShows(p, game, myTeam, measured, hoveredUnit)) return null;
    const names = new Names(game, p);
    const distance = hoverOf(geometry, p, game, names, hoveredUnit, measured, snap.maxWounds, distances)?.distance ?? null;
    const mine = measured === focus ? sheet : unitSheetOf(p, names, measured, rules, snap.maxWounds);
    return enemyStripOf(mine, unitSheetOf(p, names, hoveredUnit, rules, snap.maxWounds), distance);
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
  /** The tray hides when the open log holds the same roll, and gives its slot to the decision box. */
  const covered = $derived(logOpen && log?.latest != null && flatten(entries).some((e) => e.type === 'roll' && e.record.id === log.latest?.record.id));

  const tray = $derived(!covered && log?.latest != null);

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
  <div class="info">
    {#if strip !== null}<EnemyStrip {strip} />{/if}
    {#if card !== null}
      <UnitCard {card} selectedModel={selection?.modelIds.length === 1 ? selection.modelIds[0] ?? null : null} picked={selection?.pieceId === card.sheet.unitId ? selection.modelIds : []} onPick={onPickModel} />
    {/if}
    {#if terrainCard !== null}<TerrainCard card={terrainCard} />{/if}
    {#if selectionLine !== null && (card === null || selection?.modelIds.length === 1)}<p class="selection">{selectionLine}</p>{/if}
    <section class="panel log" class:open={logOpen} aria-label="Combat log">
      <div class="head">
        <button type="button" class="toggle" tabindex="-1" aria-expanded={logOpen} title="Show or hide the log (G)"
          onmousedown={(e) => e.preventDefault()} onclick={() => setLogOpen(!logOpen)}>
          <span class="chev" class:open={logOpen}><Icon name="chevron" size="1rem" /></span>
          <HudIcon name="log" size={18} />
          <h2>Log</h2>
          <kbd>G</kbd>
        </button>
        {#if logOpen}
          <select class="filter" aria-label="Log filter" value={logFilter} onchange={(ev) => setFilter(ev.currentTarget.value as LogFilter)}>
            {#each FILTERS as f, i (f)}<option value={f}>{FILTER_LABELS[i]}</option>{/each}
          </select>
          <label class="detailed" title="Each roll as tables of its dice, its modifiers and its totals">
            <input type="checkbox" checked={logDetailed} onchange={(ev) => setDetailed(ev.currentTarget.checked)} /> Detailed
          </label>
        {/if}
      </div>
      {#if logOpen}
        <CombatLogPanel
          {entries} filter={logFilter} detailed={logDetailed} {pendingUndoTo} {watching} {rules} reveal={revealRoll} showFilter={false}
          bandOf={(e) => (log !== null && snap.game !== null ? bandOf(e, log.names, snap.game) : 0)}
          onFilter={setFilter} onUndoTo={(seq) => void undoTo(seq)} {onTouch}
        />
      {/if}
      {#if problem !== null}<p role="alert">{problem}</p>{/if}
      {#if tray || !watching}
        <div class="foot">
          {#if tray}<DiceTray roll={log?.latest ?? null} onClick={openAt} />{:else}<span></span>{/if}
          <UndoButton bind:this={undo} plan={log?.undo ?? null} {watching} onUndo={(seq) => void undoTo(seq)} />
        </div>
      {/if}
    </section>
  </div>
{/if}

<style>
  .info { display: flex; flex-direction: column; gap: var(--s3); flex: 1 1 0; min-height: 0; }
  .selection { margin: 0; color: var(--dim); font-size: .9rem; }
  .log { display: flex; flex-direction: column; gap: var(--s2); margin: 0; padding: var(--s3) var(--s4); flex: none; min-height: 0; }
  .log.open { flex: 1 1 10rem; min-height: 10rem; }
  :global([data-theme='fantasy']) .log { padding: calc(var(--s3) + 4px) calc(var(--s4) + 4px); }
  .head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--s2); }
  .toggle { display: inline-flex; align-items: center; gap: var(--s2); min-height: 2rem; padding: 0; border: 0; border-radius: 0; clip-path: none; box-shadow: none;
    background: none; color: var(--text); }
  .toggle:hover:not(:disabled) { background: none; color: var(--accent); }
  .toggle h2 { margin: 0; font-size: .9rem; }
  .chev { display: inline-flex; transition: transform .12s; }
  .chev.open { transform: rotate(90deg); }
  kbd { font-family: var(--font-num); font-size: .7rem; line-height: 1; padding: 2px 4px; border: 1px solid var(--rule); border-radius: 3px; color: var(--dim); }
  .filter { min-height: 2rem; min-width: 0; font-size: .9rem; }
  .detailed { display: flex; flex: none; align-items: center; gap: var(--s1); font-size: .9rem; white-space: nowrap; }
  .foot { display: flex; align-items: center; justify-content: space-between; gap: var(--s2); padding-top: var(--s2); border-top: 1px solid var(--rule); }
  [role='alert'] { margin: 0; }
</style>
