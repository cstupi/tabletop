<!-- viewer/src/lib/play/PieceMenu.svelte -->
<!-- PieceMenu.cs at the pointer: the items; Esc or a click outside closes it. An integer or text item asks in a small dialog (focused,
     Enter confirms, text stops at 64 characters); a choice item lists its choices behind a filter. What a pick (piece or table click)
     or a send does is the page's: this hands back the chosen item and the step to take. -->
<script lang="ts">
  import { choose, type MenuStep } from './menus';
  import type { MenuItem } from './menuModel';

  let { items, at, onStep, onClose }: {
    items: readonly MenuItem[]; at: { x: number; y: number };
    onStep: (item: MenuItem, step: MenuStep) => void; onClose: () => void;
  } = $props();

  let asking = $state.raw<{ item: MenuItem; input: 'integer' | 'text' | 'choice'; choices: readonly string[] } | null>(null);
  let value = $state('');
  let filter = $state('');
  let picked = $state<string | null>(null);
  const shown = $derived(asking === null ? [] : asking.choices.filter((c) => filter === '' || c.toLowerCase().includes(filter.toLowerCase())));

  function pick(item: MenuItem): void {
    const step = choose(item, null);
    if (step.kind === 'dialog') { asking = { item, input: step.input, choices: step.choices }; value = step.input === 'integer' ? '0' : ''; filter = ''; picked = null; }
    else { onStep(item, step); onClose(); }
  }

  function confirm(): void {
    const a = asking;
    if (a === null) return;
    const input = a.input === 'choice' ? (picked ?? shown[0] ?? null) : value;
    if (input === null) return;
    onStep(a.item, choose(a.item, input));
    onClose();
  }

  const born = performance.now();            // the pointerdown that opened the menu is not a click outside it
  const focusOnMount = (node: HTMLElement) => { node.focus(); };
</script>

<svelte:window
  onkeydown={(e) => { if (e.key === 'Escape') { e.stopPropagation(); onClose(); } }}
  onpointerdown={(e) => { if (e.timeStamp <= born) return; if (!(e.target as Element).closest('.piece-menu, .menu-dialog')) onClose(); }}
/>

{#if asking === null}
  <ul class="piece-menu" role="menu" style:left={`${at.x}px`} style:top={`${at.y}px`}>
    {#each items as item (item.label)}
      <li role="none"><button role="menuitem" onclick={() => pick(item)}>{item.label}</button></li>
    {/each}
  </ul>
{:else}
  <form class="menu-dialog" style:left={`${at.x}px`} style:top={`${at.y}px`} onsubmit={(e) => { e.preventDefault(); confirm(); }}>
    <label>{asking.item.label}
      {#if asking.input === 'integer'}
        <input use:focusOnMount type="number" min="-99" max="99" step="1" bind:value />
      {:else if asking.input === 'text'}
        <input use:focusOnMount type="text" maxlength="64" placeholder="at most 64 characters" bind:value />
      {:else}
        <input use:focusOnMount type="text" placeholder="filter" bind:value={filter} />
      {/if}
    </label>
    {#if asking.input === 'choice'}
      <ul class="choices" role="listbox">
        {#each shown as c (c)}
          <li role="option" aria-selected={(picked ?? shown[0]) === c}>
            <button type="button" class:on={(picked ?? shown[0]) === c} onclick={() => (picked = c)} ondblclick={() => { picked = c; confirm(); }}>{c}</button>
          </li>
        {/each}
      </ul>
    {/if}
    <div class="actions"><button type="submit">OK</button><button type="button" onclick={onClose}>Cancel</button></div>
  </form>
{/if}

<style>
  .piece-menu, .menu-dialog { position: fixed; z-index: 20; margin: 0; padding: 0.2rem; background: var(--panel); color: var(--ink);
    border: 1px solid var(--line); border-radius: 3px; min-width: 9rem; }
  .piece-menu { list-style: none; }
  .piece-menu button { display: block; width: 100%; text-align: left; border: 0; }
  .menu-dialog { padding: 0.5rem; display: grid; gap: 0.4rem; min-width: 15rem; }
  .menu-dialog label { display: grid; gap: 0.2rem; }
  .choices { list-style: none; margin: 0; padding: 0; max-height: 14rem; overflow: auto; border: 1px solid var(--line); }
  .choices button { display: block; width: 100%; text-align: left; border: 0; }
  .choices button.on { background: color-mix(in srgb, var(--ink) 18%, transparent); }
  .actions { display: flex; gap: 0.4rem; justify-content: flex-end; }
</style>
