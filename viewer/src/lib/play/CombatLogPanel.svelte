<!-- viewer/src/lib/play/CombatLogPanel.svelte -->
<!-- CombatLogPanel.cs: the play log, a filter over the combat log's entries drawn as logSheet.ts's text, summary or Detailed (monospace).
     The whole log is one <pre>: a span per paragraph holding its lines and their line ends, so a drag selects across any number of entries
     and a copy holds plain lines laid out as shown (strike, fading, the team edge and the picked tint are CSS, never text). A click that
     selects nothing picks the entry under it (lit; its pieces lit on the board); Undo to here undoes to the picked entry, Copy puts the whole
     log as shown on the clipboard. Renders only; every word is logSheet.ts's. -->
<script lang="ts">
  import { tick } from 'svelte';
  import { FILTER_LABELS, faded, undoHere } from './logRows';
  import { indented, sheetOf, sheetText, type LogParagraph } from './logSheet';
  import { touched } from './logText';
  import type { LogEntry } from './rollEntry';
  import type { RuleTexts } from './ruleTexts';
  import type { LogFilter } from './settings';
  import { tooltip as spellTooltip } from './spellText';
  import { teamColour } from '../board/palette';
  import HudIcon from './HudIcon.svelte';

  const FILTERS: readonly LogFilter[] = ['all', 'rolls', 'mine', 'unit'];

  let { entries, filter, detailed = false, pendingUndoTo, watching, rules, reveal = null, showFilter = true, bandOf = () => 0, onFilter, onDetailed, onUndoTo, onTouch }: {
    entries: readonly LogEntry[]; filter: LogFilter; pendingUndoTo: number | null; watching: boolean; rules: RuleTexts;
    /** The team ordinal whose colour edges a block's header (logRows.bandOf); 0 for none. */
    bandOf?: (e: LogEntry) => number;
    /** The detailed log: tables of each roll's dice, its modifiers and totals (ClientSettings.LogDetailed). */
    detailed?: boolean;
    onDetailed?: (on: boolean) => void;
    /** False when the page draws the filter in its own header. */
    showFilter?: boolean;
    /** Pick the entry holding this RollRecord and scroll to it; `n` makes a repeat request new. */
    reveal?: { rollId: string; n: number } | null;
    onFilter: (f: LogFilter) => void; onUndoTo: (seq: number) => void; onTouch: (pieceIds: readonly string[]) => void;
  } = $props();

  const paragraphs = $derived(sheetOf(entries, detailed));
  let picked = $state('');
  let scroller: HTMLPreElement | undefined = $state();
  let toEnd = true;

  const keyOf = (e: LogEntry): string => (e.type === 'roll' ? `r${e.record.id}` : `${e.type}${e.seq}`);
  const pickedEntry = $derived(paragraphs.find((p) => keyOf(p.entry) === picked)?.entry ?? null);
  const canUndo = $derived(pickedEntry !== null && undoHere(pickedEntry, watching));

  /** A paragraph's text: its indented lines, each ending its line, so the <pre> reads (and copies) one line each. */
  const textOf = (p: LogParagraph): string => indented(p).map((l) => `${l}\n`).join('');

  /** A paragraph's click, unless it ended a drag that selected text: picks it, or lets go of it. */
  function pick(p: LogParagraph): void {
    if ((window.getSelection()?.toString() ?? '').length > 0) return;
    const key = keyOf(p.entry);
    picked = picked === key ? '' : key;
    onTouch(picked === '' ? [] : touched(p.entry.event));
  }

  async function copyAll(): Promise<void> {
    try { await navigator.clipboard.writeText(sheetText(paragraphs)); } catch { /* no clipboard: the text stays selectable */ }
  }

  function onScroll(): void {
    if (scroller !== undefined) toEnd = scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2;
  }

  // The newest entry stays in view; scrolling away from the end lets go, scrolling back to it follows again.
  $effect(() => {
    void paragraphs;
    if (toEnd) void tick().then(() => { if (scroller !== undefined) scroller.scrollTop = scroller.scrollHeight; });
  });

  // A change of the panel's height (the card above it coming or going) keeps the newest entry in view too.
  $effect(() => {
    const el = scroller;
    if (el === undefined) return;
    const observer = new ResizeObserver(() => { if (toEnd) el.scrollTop = el.scrollHeight; });
    observer.observe(el);
    return () => observer.disconnect();
  });

  $effect(() => {
    const r = reveal;
    if (r === null) return;
    const i = paragraphs.findIndex((p) => p.entries.some((e) => e.type === 'roll' && e.record.id === r.rollId));
    if (i < 0) return;
    picked = keyOf(paragraphs[i]!.entry);
    toEnd = false;
    void tick().then(() => scroller?.querySelector(`[data-p="${i}"]`)?.scrollIntoView({ block: 'nearest' }));
  });

  const titleOf = (e: LogEntry): string => (e.type === 'block' && e.spell !== null ? spellTooltip(rules, e.spell, e.spellCost) ?? '' : '');
</script>

<section class="log" aria-label="Combat log">
  <div class="bar">
    {#if showFilter}
      <select class="filter" aria-label="Log filter" value={filter} onchange={(ev) => onFilter(ev.currentTarget.value as LogFilter)}>
        {#each FILTERS as f, i (f)}<option value={f}>{FILTER_LABELS[i]}</option>{/each}
      </select>
      {#if onDetailed !== undefined}
        <label class="detailed"><input type="checkbox" checked={detailed} onchange={(ev) => onDetailed(ev.currentTarget.checked)} /> Detailed</label>
      {/if}
    {/if}
    <span class="grow"></span>
    {#if !watching}
      <button type="button" class="flat icon" tabindex="-1" disabled={!canUndo} title={canUndo ? 'Undo to here' : 'Undo to here: pick an entry first'}
        aria-label="Undo to here" onmousedown={(e) => e.preventDefault()} onclick={() => { if (pickedEntry !== null && canUndo) onUndoTo(pickedEntry.seq); }}>
        <HudIcon name="undo" size={18} />
      </button>
    {/if}
    <button type="button" class="flat icon" tabindex="-1" title="Copy the log as shown" aria-label="Copy the log as shown"
      onmousedown={(e) => e.preventDefault()} onclick={() => void copyAll()}><HudIcon name="copy" size={18} /></button>
  </div>
  <!-- One text: spans only, no controls, nothing hidden, so a selection copies the lines and nothing else. -->
  <pre class="sheet" class:mono={detailed} bind:this={scroller} onscroll={onScroll}>{#each paragraphs as p, i (`${i}:${keyOf(p.entry)}`)}{@const band = p.entry.type === 'block' ? bandOf(p.entry) : 0}<span
    class="p" class:dim={faded(p.entry, pendingUndoTo)} class:struck={p.entry.undone} class:picked={keyOf(p.entry) === picked} class:band={band > 0}
    style:--team={band > 0 ? teamColour(band) : null} data-p={i} title={titleOf(p.entry)} role="presentation"
    onclick={() => pick(p)}>{textOf(p)}</span>{/each}</pre>
  {#if paragraphs.length === 0}<p class="none">No entries yet.</p>{/if}
</section>

<style>
  .log { display: flex; flex-direction: column; gap: var(--space-1); flex: 1; min-height: 0; font-size: .95rem; line-height: 1.35; }
  .bar { display: flex; align-items: center; gap: var(--space-2); }
  .grow { flex: 1; }
  .filter { min-height: 2rem; font-size: .9rem; }
  .detailed { display: flex; align-items: center; gap: var(--space-1); font-size: .9rem; }
  .flat { display: inline-flex; align-items: center; justify-content: center; min-width: 28px; min-height: 28px; padding: var(--space-1); border: 0; border-radius: 0;
    clip-path: none; border-image: none; box-shadow: none; background: none; color: var(--text); cursor: pointer; }
  .flat:hover:not(:disabled) { background: var(--rule); color: var(--accent); }
  .flat:disabled { opacity: .4; cursor: default; }
  .sheet { flex: 1; min-height: 0; margin: 0; padding-right: var(--space-1); overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; }
  .sheet.mono { font-family: ui-monospace, 'JetBrains Mono', Menlo, Consolas, monospace; font-size: .78rem; line-height: 1.3; }
  .p { cursor: pointer; }
  .p:hover { background: var(--rule); }
  .p.band { box-shadow: inset 3px 0 0 var(--team); padding-left: var(--space-2); }
  .p.picked { background: color-mix(in srgb, var(--accent) 22%, transparent); }
  .struck { text-decoration: line-through; }
  .dim { color: var(--dim); }
  .none { margin: var(--space-2) 0; color: var(--dim); }
</style>
