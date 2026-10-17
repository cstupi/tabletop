<script lang="ts">
  import type { FolderDto, Guid } from '$lib/api/types';
  import { buildTree, moveTargets, type FolderNode } from '$lib/library/folders';
  import { nameProblem } from '$lib/validation';
  import Icon from './Icon.svelte';
  let { folders, selected, onselect, oncreate, onrename, onmove, ondelete, oninvalid }: {
    folders: FolderDto[]; selected: Guid | null; onselect: (id: Guid | null) => void; oncreate: (parentId: Guid | null, name: string) => void;
    onrename: (id: Guid, name: string) => void; onmove: (id: Guid, parentId: Guid | null) => void; ondelete: (id: Guid) => void;
    oninvalid: (message: string) => void;
  } = $props();
  const tree = $derived(buildTree(folders));
  let dragging = $state<Guid | null>(null);
  let moving = $state<Guid | null>(null);   // the folder whose "Move to" list is open: the keyboard way to do what dragging does
  function ask(message: string, initial = ''): string | null {
    const name = prompt(message, initial);
    if (name === null || !name.trim()) return null;
    const problem = nameProblem(name, 'Folder name');
    if (problem) { oninvalid(problem); return null; }
    return name.trim();
  }
  const create = (parentId: Guid | null) => { const name = ask(parentId ? 'Name of the new sub-folder' : 'Name of the new folder'); if (name) oncreate(parentId, name); };
  const rename = (f: FolderDto) => { const name = ask('Rename folder', f.name); if (name && name !== f.name) onrename(f.id, name); };
  function move(f: FolderDto, value: string) {
    moving = null;
    const parentId = value === '' ? null : value;
    if (parentId !== (f.parentId ?? null)) onmove(f.id, parentId);
  }
</script>

{#snippet node(n: FolderNode)}
  <li draggable="true" ondragstart={(e) => { e.stopPropagation(); dragging = n.folder.id; }} ondragover={(e) => e.preventDefault()} ondrop={(e) => { e.stopPropagation(); if (dragging && dragging !== n.folder.id) onmove(dragging, n.folder.id); dragging = null; }}>
    <button class="folder quiet" class:selected={selected === n.folder.id} aria-current={selected === n.folder.id ? 'true' : undefined} onclick={() => onselect(n.folder.id)}><Icon name="folder" />{n.folder.name} <small class="num">({n.folder.assetIds.length})</small></button>
    <span class="tools">
      <button class="icon-btn" title="New sub-folder" aria-label="New sub-folder in {n.folder.name}" onclick={() => create(n.folder.id)}><Icon name="plus" size="1.1em" /></button>
      <button class="icon-btn" title="Rename" aria-label="Rename {n.folder.name}" onclick={() => rename(n.folder)}><Icon name="edit" size="1.1em" /></button>
      <button class="icon-btn" title="Move" aria-label="Move {n.folder.name}" aria-expanded={moving === n.folder.id} onclick={() => (moving = moving === n.folder.id ? null : n.folder.id)}><Icon name="move" size="1.1em" /></button>
      <button class="icon-btn" title="Delete folder and sub-folders (assets stay)" aria-label="Delete {n.folder.name}" onclick={() => confirm(`Delete "${n.folder.name}" and its sub-folders? Assets are kept.`) && ondelete(n.folder.id)}><Icon name="close" size="1.1em" /></button>
    </span>
    {#if moving === n.folder.id}
      <label class="move">Move into
        <select value={n.folder.parentId ?? ''} onchange={(e) => move(n.folder, e.currentTarget.value)} onkeydown={(e) => e.key === 'Escape' && (moving = null)}>
          <option value="">Top level</option>
          {#each moveTargets(folders, n.folder.id) as t (t.id)}<option value={t.id}>{t.name}</option>{/each}
        </select>
      </label>
    {/if}
    {#if n.children.length}<ul>{#each n.children as c (c.folder.id)}{@render node(c)}{/each}</ul>{/if}
  </li>
{/snippet}

<nav aria-label="Folders" ondragover={(e) => e.preventDefault()} ondrop={() => { if (dragging) onmove(dragging, null); dragging = null; }}>
  <div class="actions">
    <button class="folder quiet" class:selected={selected === null} aria-current={selected === null ? 'true' : undefined} onclick={() => onselect(null)}><Icon name="library" />All assets</button>
    <button onclick={() => create(null)}><Icon name="plus" />New folder</button>
  </div>
  {#if tree.length === 0}<p class="none"><small>No folders yet. Folders group assets for you; they change nothing in a game.</small></p>{/if}
  <ul>{#each tree as n (n.folder.id)}{@render node(n)}{/each}</ul>
</nav>
<style>
  nav > .actions { justify-content: space-between; margin-top: 0 }
  ul { list-style: none; padding-left: var(--s3); margin: 0 } nav > ul { padding-left: 0 }
  ul ul { border-left: 1px solid var(--rule); margin-left: var(--s2) }
  li { margin: var(--s1) 0 }
  .folder { color: var(--text); text-decoration: none; justify-content: flex-start; gap: var(--s2); min-height: 2rem; padding: 0 var(--s2); max-width: 100%; white-space: normal; text-align: left }
  .folder:hover:not(:disabled) { color: var(--accent) }
  .selected { color: var(--accent); background: var(--rule); font-weight: 700 }
  :global([data-theme='fantasy']) .selected { color: var(--wood) }
  .tools { display: inline-flex; gap: var(--s1); white-space: nowrap; margin-left: var(--s2) }
  .tools button { min-height: 1.75rem; min-width: 1.75rem }
  .move { display: block; margin: var(--s1) 0 var(--s2) var(--s2) }
  .none { margin: var(--s2) 0 }
</style>
