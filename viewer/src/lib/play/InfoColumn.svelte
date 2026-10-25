<!-- viewer/src/lib/play/InfoColumn.svelte -->
<!-- Plan D3 on the play page: the hovered enemy's strip and the unit card (SheetFocus), the latest roll's tray, the combat log (G and its
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
  import { enemyStripOf, stripShows } from './enemyStrip';
  import EnemyStrip from './EnemyStrip.svelte';
  import { hoverOf } from './hoverView';
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
  import { unitSheetOf } from './unitSheet';
  import { isWatching } from './waiting';
  import type { UndoPrediction } from './undo';

  let {
    connection, snap, shown, scene, prediction, pendingUndoTo, rules, geometry, selection, hoveredUnit, myTeam, eligible, getSettings, updateSettings,
    onPickModel, onTouch, overlay = $bindable(null),
  }: {
    connection: GameConnection; snap: ConnectionState;
    /** The projection to show: the prediction while an undo travels, else the connection's. */
    shown: Projection | null; scene: Scene | null;
    prediction: UndoPrediction; pendingUndoTo: number | null; rules: RuleTexts; geometry: Geometry | null;
    selection: Selection | null; hoveredUnit: string | null; myTeam: string | null;
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
  let problem = $state<string | null>(null);
  let revealRoll = $state<{ rollId: string; n: number } | null>(null);
  let undo: UndoButton | undefined = $state();
  let lastCard: UnitCardContent | null = null;

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
    const muted = new Set(getSettings().mutedWindowKinds);
    const next = unitCardOf(s, teamOf(shown, snap.game, focus)?.ordinal ?? 0, noteOf(shown, snap.game, myTeam, muted, focus));
    if (sameCard(lastCard, next)) return lastCard;
    lastCard = next;
    return next;
  });

  const strip = $derived.by(() => {
    const p = shown, game = snap.game;
    if (p === null || game === null || sheet === null || focus === null || hoveredUnit === null || !p.units.has(hoveredUnit)) return null;
    if (!stripShows(p, game, myTeam, focus, hoveredUnit)) return null;
    const names = new Names(game, p);
    const distance = hoverOf(geometry, p, game, names, hoveredUnit, focus, snap.maxWounds)?.distance ?? null;
    return enemyStripOf(sheet, unitSheetOf(p, names, hoveredUnit, rules, snap.maxWounds), distance);
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
    const info = hoverOf(geometry, p, game, new Names(game, p), hovered, mySelected, snap.maxWounds);
    if (info === null) { overlay = null; return; }
    const isEligible = eligible.has(hovered) || [...p.models.values()].some((m) => m.unitId === hovered && eligible.has(m.id));
    overlay = hoverOverlay(sc, info, isEligible);
  });

  const selectionLine = $derived(shown !== null && snap.game !== null && playing ? describeSelection(new Names(snap.game, shown), shown, selection) : null);

  const entries = $derived.by(() => {
    if (log === null) return [];
    const n = log.names;
    return filterEntries(log.entries, logFilter, (u) => { const t = n.teamOfUnit(u); return t !== null && t === myTeam; }, focus);
  });
  /** The tray hides when the open log holds the same roll, and gives its slot to the decision box. */
  const covered = $derived(logOpen && log?.latest != null && flatten(entries).some((e) => e.type === 'roll' && e.record.id === log.latest?.record.id));

  function setLogOpen(open: boolean): void {
    logOpen = open;
    updateSettings((s) => ({ ...s, logOpen: open }));
  }
  function setFilter(f: LogFilter): void {
    logFilter = f;
    updateSettings((s) => ({ ...s, logFilter: f }));
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
      <UnitCard {card} selectedModel={selection?.modelIds.length === 1 ? selection.modelIds[0] ?? null : null} onPick={onPickModel} />
    {/if}
    {#if selectionLine !== null}<p class="selection muted">{selectionLine}</p>{/if}
    {#if !covered}<DiceTray roll={log?.latest ?? null} onClick={openAt} />{/if}
    <div class="bar">
      <button type="button" tabindex="-1" aria-pressed={logOpen} onmousedown={(e) => e.preventDefault()} onclick={() => setLogOpen(!logOpen)}>Log (G)</button>
      <UndoButton bind:this={undo} plan={log?.undo ?? null} {watching} onUndo={(seq) => void undoTo(seq)} />
    </div>
    {#if problem !== null}<p class="error" role="alert">{problem}</p>{/if}
    {#if logOpen}
      <CombatLogPanel
        {entries} filter={logFilter} {pendingUndoTo} {watching} {rules} reveal={revealRoll}
        onFilter={setFilter} onUndoTo={(seq) => void undoTo(seq)} {onTouch}
      />
    {/if}
  </div>
{/if}

<style>
  .info { display: flex; flex-direction: column; gap: 0.5rem; margin-top: 0.5rem; }
  .bar { display: flex; gap: 0.4rem; }
  .bar button[aria-pressed='true'] { border-color: var(--muted); }
  p { margin: 0; }
</style>
