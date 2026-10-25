<!-- viewer/src/lib/play/CombatLogPanel.svelte -->
<!-- CombatLogPanel.cs: the play log, a filter over the combat log's blocks, rolls and lines. A block shows its lines beneath its header
     until the header is clicked; a roll shows its raw faces bright or faded and expands on click. Renders only; every word is the
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

  const FILTERS: readonly LogFilter[] = ['all', 'rolls', 'mine', 'unit'];

  let { entries, filter, pendingUndoTo, watching, rules, reveal = null, onFilter, onUndoTo, onTouch }: {
    entries: readonly LogEntry[]; filter: LogFilter; pendingUndoTo: number | null; watching: boolean; rules: RuleTexts;
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
    {#each r.details as line, i (i)}<p>{line}</p>{/each}
    {#each r.dice as d, i (i)}
      <p class="die-line">
        <span class="die" class:fail={d.judged && !d.success}>{pip(d.raw)}</span>
        <span class:ok={d.judged && d.success} class:bad={d.judged && !d.success}>{dieText(d)}</span>
      </p>
    {/each}
    {#each r.amendments as a (a.seq)}
      <p class:dim={a.undone}>{a.undone ? strike(amendmentLine(a, String)) : amendmentLine(a, String)}</p>
    {/each}
    {#if r.applied.length > 0}
      <p class="flow">{#each r.applied as name (name)}<button type="button" class="flat" tabindex="-1" title={rules.textOf(name) ?? name} onmousedown={noFocus}>{name}</button>{/each}</p>
    {/if}
    {#if !r.undone && !watching}
      <button type="button" class="flat" tabindex="-1" onmousedown={noFocus} onclick={() => onUndoTo(r.seq)}>Undo to here</button>
    {/if}
  </div>
{/snippet}

{#snippet node(e: LogEntry)}
  {@const open = isOpen(e)}
  {@const dim = faded(e, pendingUndoTo)}
  {#if e.type === 'roll' && !labelled(e)}
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
  <select aria-label="Log filter" value={filter} onchange={(ev) => onFilter(ev.currentTarget.value as LogFilter)}>
    {#each FILTERS as f, i (f)}<option value={f}>{FILTER_LABELS[i]}</option>{/each}
  </select>
  <div class="rows" bind:this={scroller} onscroll={onScroll}>
    {#each entries as e (keyOf(e))}{@render node(e)}{/each}
  </div>
</section>

<style>
  .log { display: flex; flex-direction: column; gap: 0.3rem; min-height: 0; }
  select { align-self: flex-start; }
  .rows { overflow-y: auto; max-height: 40vh; min-height: 6rem; }
  .line { display: flex; gap: 0.25rem; align-items: center; }
  .flat { background: none; border: 0; padding: 0.1rem 0.2rem; text-align: left; }
  .grow { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .roll { cursor: pointer; padding: 0.1rem 0.2rem; }
  .faces { display: flex; flex-wrap: wrap; gap: 0.25rem; align-items: center; }
  .words { overflow-wrap: anywhere; }
  .die { font-size: 1.3rem; line-height: 1; }
  .die.fail { opacity: 0.45; }
  .dim { color: var(--muted); opacity: 0.7; }
  .indent { margin-left: 1rem; }
  .detail p { margin: 0.1rem 0; }
  .die-line { display: flex; gap: 0.3rem; align-items: center; }
  .ok { color: var(--success, #6ddf7c); }
  .bad { color: var(--failure, #ff6b5c); }
  .flow { display: flex; flex-wrap: wrap; gap: 0.2rem; }
</style>
