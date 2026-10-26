<!-- viewer/src/lib/play/CombatLogPanel.svelte -->
<!-- CombatLogPanel.cs: the play log, a filter over the combat log's blocks, rolls and lines. A block shows its lines beneath its header
     until the header is clicked; a roll shows its raw faces bright or faded and expands on click. Detailed draws each roll and named
     change as its worked lines in monospace (detailed-log spec, workedLines.ts). Renders only; every word is the
     combat log's (logRows.ts, rollEntry.ts). Rows are keyed by seq, which is what CombatLogRows' reuse does by hand in Godot. -->
<script lang="ts">
  import { tick } from 'svelte';
  import { SvelteSet } from 'svelte/reactivity';
  import { pip } from './combatLog';
  import { FILTER_LABELS, faded, rowText, strike, undoHere } from './logRows';
  import { touched } from './logText';
  import { amendmentLine, dieText, outcome, title, type LogEntry, type RollEntry } from './rollEntry';
  import type { RuleTexts } from './ruleTexts';
  import type { LogFilter } from './settings';
  import { tooltip as spellTooltip } from './spellText';
  import { worked } from './workedLines';

  const FILTERS: readonly LogFilter[] = ['all', 'rolls', 'mine', 'unit'];

  let { entries, filter, detailed = false, pendingUndoTo, watching, rules, reveal = null, showFilter = true, onFilter, onDetailed, onUndoTo, onTouch }: {
    entries: readonly LogEntry[]; filter: LogFilter; pendingUndoTo: number | null; watching: boolean; rules: RuleTexts;
    /** Each roll and named change as its worked lines (ClientSettings.LogDetailed). */
    detailed?: boolean;
    onDetailed?: (on: boolean) => void;
    /** False when the page draws the filter in its own header (FilterSelect). */
    showFilter?: boolean;
    /** Expand the block and the roll holding this RollRecord and scroll to it; `n` makes a repeat request new. */
    reveal?: { rollId: string; n: number } | null;
    onFilter: (f: LogFilter) => void; onUndoTo: (seq: number) => void; onTouch: (pieceIds: readonly string[]) => void;
  } = $props();

  const openRolls = new SvelteSet<string>();      // rolls opened
  const foldedBlocks = new SvelteSet<string>();   // blocks folded: a block starts open, a roll closed
  let scroller: HTMLDivElement | undefined = $state();
  let toEnd = true;

  // One event can write several lines ("Moved", "Moved this round"): a line's key carries its text.
  const keyOf = (e: LogEntry): string => (e.type === 'roll' ? `r${e.record.id}` : e.type === 'line' ? `l${e.seq}:${e.text}` : `s${e.seq}`);
  const isOpen = (e: LogEntry): boolean => (e.type === 'block' ? !foldedBlocks.has(keyOf(e)) : openRolls.has(keyOf(e)));

  function toggle(e: LogEntry): void {
    onTouch(touched(e.event));
    if (e.type === 'line') return;
    const set = e.type === 'block' ? foldedBlocks : openRolls;
    if (!set.delete(keyOf(e))) set.add(keyOf(e));
  }

  function onScroll(): void {
    if (scroller !== undefined) toEnd = scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2;
  }

  // The newest entry stays in view; scrolling away from the end lets go, scrolling back to it follows again.
  $effect(() => {
    void entries;
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
    const path = pathTo(entries, r.rollId);
    if (path === null) return;
    for (const e of path) if (e.type === 'block') foldedBlocks.delete(keyOf(e));
    openRolls.add(`r${r.rollId}`);
    toEnd = false;
    void tick().then(() => scroller?.querySelector(`[data-roll="${r.rollId}"]`)?.scrollIntoView({ block: 'nearest' }));
  });

  /** The blocks holding the roll, outermost first, then the roll. */
  function pathTo(list: readonly LogEntry[], rollId: string): LogEntry[] | null {
    for (const e of list) {
      if (e.type === 'roll' && e.record.id === rollId) return [e];
      if (e.type === 'block') {
        const inner = pathTo(e.entries, rollId);
        if (inner !== null) return [e, ...inner];
      }
    }
    return null;
  }

  const textOf = (e: LogEntry, open: boolean): string => (e.undone ? strike(rowText(e, open)) : rowText(e, open));
  const tail = (r: RollEntry): string =>
    [outcome(r), ...r.amendments.filter((a) => !a.undone).map((a) => amendmentLine(a, pip))].filter((t) => t.length > 0).join(' · ');
  const labelled = (r: RollEntry): boolean => r.dieLabels !== null && r.dieLabels.length > 0;
  const noFocus = (e: Event) => e.preventDefault();
</script>

{#snippet detail(r: RollEntry)}
  <div class="detail">
    {#each detailed ? [] : r.details as line, i (i)}<p>{line}</p>{/each}
    {#each detailed ? [] : r.dice as d, i (i)}
      <p class="die-line">
        <span class="die" class:fail={d.judged && !d.success}>{pip(d.raw)}</span>
        <span class:ok={d.judged && d.success} class:bad={d.judged && !d.success}>{dieText(d)}</span>
      </p>
    {/each}
    {#each detailed ? [] : r.amendments as a (a.seq)}
      <p class:dim={a.undone}>{a.undone ? strike(amendmentLine(a, String)) : amendmentLine(a, String)}</p>
    {/each}
    {#if r.applied.length > 0}
      <p class="flow">{#each r.applied as name (name)}<button type="button" class="flat" tabindex="-1" title={rules.textOf(name) ?? name} onmousedown={noFocus}>{name}</button>{/each}</p>
    {/if}
    {#if undoHere(r, watching)}
      <button type="button" class="flat" tabindex="-1" onmousedown={noFocus} onclick={() => onUndoTo(r.seq)}>Undo to here</button>
    {/if}
  </div>
{/snippet}

{#snippet node(e: LogEntry)}
  {@const open = isOpen(e)}
  {@const dim = faded(e, pendingUndoTo)}
  {#if detailed && (e.type === 'roll' || (e.type === 'line' && e.worked !== null))}
    {@const lines = e.type === 'roll' ? worked(e) : e.type === 'line' ? e.worked ?? [] : []}
    <div class="worked" class:dim data-roll={e.type === 'roll' ? e.record.id : undefined} role="button" tabindex="-1" onmousedown={noFocus}
      onclick={() => toggle(e)} onkeydown={() => {}}>{e.undone ? strike(lines.join('\n')) : lines.join('\n')}</div>
  {:else if e.type === 'roll' && !labelled(e)}
    <div class="roll" class:dim data-roll={e.record.id} role="button" tabindex="-1" onmousedown={noFocus} onclick={() => toggle(e)} onkeydown={() => {}}>
      <span class="words">{e.undone ? strike(`${open ? '▾' : '▸'} ${title(e)}`) : `${open ? '▾' : '▸'} ${title(e)}`}</span>
      <span class="faces">
        {#each e.dice as d, i (i)}<span class="die" class:fail={d.judged && !d.success}>{pip(d.raw)}</span>{/each}
        {#if tail(e).length > 0}<span class="words" class:dim={dim}>{e.undone ? strike(tail(e)) : tail(e)}</span>{/if}
      </span>
    </div>
  {:else}
    <div class="line" class:dim>
      <button
        type="button" class="flat grow" tabindex="-1" data-roll={e.type === 'roll' ? e.record.id : undefined}
        title={e.type === 'block' && e.spell !== null ? spellTooltip(rules, e.spell, e.spellCost) ?? '' : ''}
        onmousedown={noFocus} onclick={() => toggle(e)}
      >{textOf(e, open)}</button>
      {#if e.type !== 'roll' && undoHere(e, watching)}
        <button type="button" class="flat" tabindex="-1" onmousedown={noFocus} onclick={() => onUndoTo(e.seq)}>Undo to here</button>
      {/if}
    </div>
  {/if}
  {#if open && e.type === 'block'}
    <div class="indent">{#each e.entries as child (keyOf(child))}{@render node(child)}{/each}</div>
  {:else if open && e.type === 'roll'}
    <div class="indent">{@render detail(e)}</div>
  {/if}
{/snippet}

<section class="log" aria-label="Combat log">
  {#if showFilter}
    <select class="filter" aria-label="Log filter" value={filter} onchange={(ev) => onFilter(ev.currentTarget.value as LogFilter)}>
      {#each FILTERS as f, i (f)}<option value={f}>{FILTER_LABELS[i]}</option>{/each}
    </select>
    {#if onDetailed !== undefined}
      <label class="detailed"><input type="checkbox" checked={detailed} onchange={(ev) => onDetailed(ev.currentTarget.checked)} /> Detailed</label>
    {/if}
  {/if}
  <div class="rows" bind:this={scroller} onscroll={onScroll}>
    {#each entries as e (keyOf(e))}{@render node(e)}{/each}
    {#if entries.length === 0}<p class="none">No entries yet.</p>{/if}
  </div>
</section>

<style>
  .log { display: flex; flex-direction: column; gap: var(--s2); flex: 1; min-height: 0; font-size: .95rem; line-height: 1.35; }
  .filter { align-self: flex-start; min-height: 2rem; font-size: .9rem; }
  .rows { flex: 1; min-height: 0; overflow-y: auto; padding-right: var(--s1); }
  .none { margin: var(--s2) 0; color: var(--dim); }
  /* Rows are text, not themed buttons. */
  .flat { display: inline; min-height: 0; padding: 1px var(--s1); border: 0; border-radius: 0; clip-path: none; box-shadow: none; background: none;
    color: inherit; text-align: left; white-space: normal; font: inherit; cursor: pointer; }
  .flat:hover:not(:disabled) { background: var(--rule); color: inherit; }
  .line { display: flex; gap: var(--s1); align-items: baseline; }
  .line > .flat:not(.grow) { opacity: 0; }
  .line:hover > .flat:not(.grow), .line > .flat:not(.grow):focus-visible { opacity: 1; }
  .line > .flat:not(.grow) { flex: none; color: var(--link); font-size: .85rem; text-decoration: underline; text-underline-offset: .2em; }
  .grow { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .roll { cursor: pointer; padding: 1px var(--s1); }
  .worked { margin: 2px 0; padding: 1px var(--s1); cursor: pointer; font-family: ui-monospace, 'JetBrains Mono', Menlo, Consolas, monospace;
    font-size: .75rem; line-height: 1.3; white-space: pre; overflow-x: auto; }
  .worked:hover { background: var(--rule); }
  .detailed { display: flex; align-items: center; gap: var(--s1); font-size: .9rem; }
  .roll:hover { background: var(--rule); }
  .faces { display: flex; flex-wrap: wrap; gap: var(--s1); align-items: center; }
  .words { overflow-wrap: anywhere; }
  .die { font-size: 1.35rem; line-height: 1; }
  .die.fail { opacity: 0.4; }
  .dim { color: var(--dim); }
  .indent { margin-left: var(--s3); padding-left: var(--s2); border-left: 1px solid var(--rule); }
  .detail p { margin: 2px 0; }
  .detail > .flat { color: var(--link); font-size: .85rem; text-decoration: underline; text-underline-offset: .2em; }
  .die-line { display: flex; gap: var(--s2); align-items: center; }
  .ok { color: var(--success); }
  .bad { color: var(--failure); }
  .flow { display: flex; flex-wrap: wrap; gap: var(--s1); }
</style>
