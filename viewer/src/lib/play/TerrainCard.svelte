<!-- viewer/src/lib/play/TerrainCard.svelte -->
<!-- The selected terrain piece in play (client/src/Hud/TerrainCard.cs read-only), in the unit card's place: its name, where the mesh comes
     from, height, facing, scale, then each volume's kind, footprint, heights, what it does in words and its notes. No edits. -->
<script lang="ts">
  import type { TerrainCard } from '../setup/terrainCard';
  import { inches } from '../setup/terrainCard';

  let { card }: { card: TerrainCard } = $props();
</script>

<section class="panel card" aria-label="Terrain card">
  <div class="who">
    <h2 class="head">{card.name}</h2>
    <p class="meta">{card.source}</p>
  </div>
  <div class="flow">
    {#if card.height !== ''}<span class="chip">Height {card.height}</span>{/if}
    <span class="chip">Facing {card.facing}°</span>
    {#if card.scale !== 1}<span class="chip">Scale ×{inches(card.scale)}</span>{/if}
  </div>
  {#each card.volumes as v, i (v.id)}
    <div class="volume">
      <p class="meta">{card.volumes.length === 1 ? 'Volume' : `Volume ${i + 1}`} · {v.kind} · {v.footprint} · {v.heights}</p>
      <div class="flow">{#each v.declarations as d (d)}<span class="chip">{d}</span>{/each}</div>
      {#if v.notes !== null}<p class="meta">{v.notes}</p>{/if}
    </div>
  {/each}
</section>

<style>
  .card { display: flex; flex-direction: column; gap: var(--s2); margin: 0; padding: var(--s3) var(--s4); border-top: 4px solid var(--rule); overflow-y: auto;
    flex: 0 1 auto; max-height: 45%; min-height: 6rem; font-size: .95rem; }
  :global([data-theme='fantasy']) .card { padding: calc(var(--s3) + 4px) calc(var(--s4) + 4px); border-top: 6px solid var(--rule); }
  p { margin: 0; }
  .who { min-width: 0; }
  .head { margin: 0; font-family: var(--font-body); font-weight: 700; text-transform: none; letter-spacing: 0; font-size: 1.15rem; line-height: 1.2; color: var(--text); overflow-wrap: anywhere; }
  .meta { color: var(--dim); font-size: .9rem; }
  .flow { display: flex; flex-wrap: wrap; gap: var(--s1); align-items: center; }
  .volume { display: flex; flex-direction: column; gap: var(--s1); padding-top: var(--s2); border-top: 1px solid var(--rule); }
</style>
