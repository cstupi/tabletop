<script lang="ts">
  import type { FolderDto, Guid } from '$lib/api/types';
  import { buildTree, type FolderNode } from '$lib/library/folders';
  let { folders, selected, onselect, oncreate, onrename, onmove, ondelete }: {
    folders: FolderDto[]; selected: Guid | null; onselect: (id: Guid | null) => void; oncreate: (parentId: Guid | null, name: string) => void;
    onrename: (id: Guid, name: string) => void; onmove: (id: Guid, parentId: Guid | null) => void; ondelete: (id: Guid) => void;
  } = $props();
  const tree = $derived(buildTree(folders));
  let dragging = $state<Guid | null>(null);
  const create = (parentId: Guid | null) => { const name = prompt('Folder name'); if (name?.trim()) oncreate(parentId, name.trim()); };
  const rename = (f: FolderDto) => { const name = prompt('Rename folder', f.name); if (name?.trim() && name !== f.name) onrename(f.id, name.trim()); };
</script>

{#snippet node(n: FolderNode)}
  <li draggable="true" ondragstart={(e) => { e.stopPropagation(); dragging = n.folder.id; }} ondragover={(e) => e.preventDefault()} ondrop={(e) => { e.stopPropagation(); if (dragging && dragging !== n.folder.id) onmove(dragging, n.folder.id); dragging = null; }}>
    <button class:selected={selected === n.folder.id} onclick={() => onselect(n.folder.id)}>{n.folder.name} <small>({n.folder.assetIds.length})</small></button>
    <button title="New sub-folder" onclick={() => create(n.folder.id)}>+</button>
    <button title="Rename" onclick={() => rename(n.folder)}>✎</button>
    <button title="Delete folder and sub-folders (assets stay)" onclick={() => confirm(`Delete "${n.folder.name}" and its sub-folders? Assets are kept.`) && ondelete(n.folder.id)}>×</button>
    {#if n.children.length}<ul>{#each n.children as c (c.folder.id)}{@render node(c)}{/each}</ul>{/if}
  </li>
{/snippet}

<nav ondragover={(e) => e.preventDefault()} ondrop={() => { if (dragging) onmove(dragging, null); dragging = null; }}>
  <button class:selected={selected === null} onclick={() => onselect(null)}>All assets</button>
  <button onclick={() => create(null)}>New folder</button>
  <ul>{#each tree as n (n.folder.id)}{@render node(n)}{/each}</ul>
</nav>
<style>.selected { font-weight: bold } ul { list-style: none; padding-left: 1rem }</style>
