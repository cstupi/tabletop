<!-- viewer/src/lib/setup/PieceMenu.svelte -->
<!-- Setup's right-click menu over a terrain piece or marker (MenuModel.ForSetupPiece), at canvas pixels. -->
<script lang="ts">
  import type { Point } from '../board/transform';
  import type { MenuItem } from './edits';

  let { items, at, onPick, onClose }: { items: MenuItem[]; at: Point; onPick: (item: MenuItem) => void; onClose: () => void } = $props();
</script>

<svelte:window onkeydown={(e) => { if (e.key === 'Escape') onClose(); }} onpointerdown={(e) => { if (!(e.target as Element).closest('.piece-menu')) onClose(); }} />

<ul class="piece-menu" role="menu" style:left={`${at.x}px`} style:top={`${at.y}px`}>
  {#each items as item (item.label)}
    <li role="none"><button role="menuitem" onclick={() => onPick(item)}>{item.label}</button></li>
  {/each}
</ul>

<style>
  .piece-menu { position: absolute; z-index: 5; margin: 0; padding: var(--space-1); list-style: none; background: var(--panel);
    border: 1px solid var(--line); border-radius: 3px; min-width: 9rem; }
  button { display: block; width: 100%; text-align: left; border: 0; }
</style>
