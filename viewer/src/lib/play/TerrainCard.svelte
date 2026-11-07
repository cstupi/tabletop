<!-- viewer/src/lib/play/TerrainCard.svelte -->
<!-- The selected terrain piece in play (client/src/Hud/TerrainCard.cs RenderEffects), in the unit card's place: a band in its strongest
     effect's colour with its name and height, then each effect as a large icon in its colour, its word and the server's sentence of what it
     does to a unit; a piece of several volumes lists each with its tint; facing and scale, dim. No edits. The board fills the piece's
     volumes in the same colours while it is selected. -->
<script lang="ts">
  import type { TerrainCard } from '../setup/terrainCard';
  import { inches } from '../setup/terrainCard';
  import HudIcon from './HudIcon.svelte';
  import { primaryEffect } from './terrainEffects';

  let { card }: { card: TerrainCard } = $props();
  const band = $derived(primaryEffect(card.effects.map((e) => e.name)).colour);
</script>

<section class="panel card" aria-label="Terrain card">
  <div class="band" style:--effect={band}>
    <h2 class="head">{card.name}</h2>
    {#if card.height !== ''}<span class="meta">{card.height} high</span>{/if}
  </div>
  <ul class="effects">
    {#each card.effects as e (e.name)}
      <li style:--effect={e.colour}>
        <span class="glyph"><HudIcon name={e.icon} size={34} /></span>
        <div><p class="word">{e.word}</p>{#if e.rule !== null}<p>{e.rule}</p>{/if}</div>
      </li>
    {/each}
  </ul>
  {#if card.volumes.length > 1}
    {#each card.volumes as v (v.id)}
      <p class="meta volume"><span class="swatch" style:--effect={v.colour}></span>{v.heights} · {v.declarations.join(', ')}</p>
    {/each}
  {/if}
  {#each card.volumes as v (v.id)}{#if v.notes !== null}<p class="meta">{v.notes}</p>{/if}{/each}
  <p class="meta">Facing {card.facing}°{#if card.scale !== 1} · Scale ×{inches(card.scale)}{/if}</p>
</section>

<style>
  .card { display: flex; flex-direction: column; gap: var(--s3); margin: 0; padding: var(--s3) var(--s4); overflow-y: auto;
    flex: 0 1 auto; max-height: 45%; min-height: 6rem; font-size: .95rem; }
  p { margin: 0; }
  .band { display: flex; align-items: baseline; gap: var(--s3); padding: var(--s1) var(--s3); border-left: 6px solid var(--effect);
    background: color-mix(in srgb, var(--effect) 12%, transparent); }
  .head { margin: 0; flex: 1; min-width: 0; font-family: var(--font-display, var(--font-body)); font-weight: 700; text-transform: none; letter-spacing: 0;
    font-size: 1.15rem; line-height: 1.2; color: var(--text); overflow-wrap: anywhere; }
  .meta { color: var(--dim); font-size: .9rem; }
  .effects { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--s3); }
  .effects li { display: flex; gap: var(--s3); align-items: flex-start; }
  .glyph { color: var(--effect); }
  .word { font-size: 1.1rem; font-weight: 600; color: color-mix(in srgb, var(--effect) 65%, var(--text)); }
  .volume { display: flex; align-items: center; gap: var(--s2); }
  .swatch { width: .8rem; height: .8rem; border-radius: 3px; border: 1px solid var(--effect); background: color-mix(in srgb, var(--effect) 35%, transparent); }
</style>
