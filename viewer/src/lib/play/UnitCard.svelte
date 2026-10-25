<!-- viewer/src/lib/play/UnitCard.svelte -->
<!-- UnitCard.cs: the headline, the badges, the chips, the decision note, one row per group of like models, the weapons grid and the
     unit's rules with their texts as tooltips. Every word is cardText.ts's or the sheet's. -->
<script lang="ts">
  import { teamColour } from '../board/palette';
  import { WEAPON_HEADINGS, badges, ruleChip, ruleTooltip, weaponName } from './cardText';
  import { groupLine, groupTitle, groupWounds, nextModel, type UnitCardContent } from './unitCard';
  import { HERO_LABEL } from './unitSheet';

  let { card, selectedModel = null, onPick }: { card: UnitCardContent; selectedModel?: string | null; onPick: (modelId: string) => void } = $props();

  const sheet = $derived(card.sheet);
  const noFocus = (e: Event) => e.preventDefault();
</script>

<section class="panel card" aria-label="Unit card" style:--team={teamColour(card.team)}>
  <div class="top">
    <div class="who">
      <h2 class="head">{sheet.name}</h2>
      <p class="meta">{sheet.alive}/{sheet.size} models · Wounds {card.woundsLeft}/{card.woundsMax}</p>
    </div>
    <div class="stats">
      {#each badges(sheet) as b (b)}{@const [label, value] = [b.slice(0, b.lastIndexOf(' ')), b.slice(b.lastIndexOf(' ') + 1)]}
        <span class="stat"><span class="v num">{value}</span><span class="k">{label}</span></span>
      {/each}
    </div>
  </div>
  {#if sheet.chips.length > 0}
    <div class="flow">
      {#each sheet.chips as chip, i (i)}
        <span class="chip" class:warn={chip.kind === 'condition'} class:dim={chip.kind === 'activated'}>{chip.text}</span>
      {/each}
    </div>
  {/if}
  {#if card.note !== null}<p class="warn">{card.note}</p>{/if}
  {#each card.groups as group, i (i)}
    <div class="group" class:gone={group.standing.length === 0}>
      {#if group.hero}<span class="badge">{HERO_LABEL}</span>{/if}
      <button
        type="button" class="flat" tabindex="-1" title={groupLine(group)} disabled={group.standing.length === 0}
        onmousedown={noFocus}
        onclick={() => { const id = nextModel(group, selectedModel); if (id !== null) onPick(id); }}
      >{groupTitle(group)}</button>
      {#if groupWounds(group) !== null}<span class="wounds">{groupWounds(group)}</span>{/if}
    </div>
  {/each}
  {#if sheet.weapons.length > 0}
    <div class="weapons">
      {#each WEAPON_HEADINGS as h (h)}<span class="dim">{h}</span>{/each}
      {#each sheet.weapons as w (w.name)}
        <span>{weaponName(w)}</span><span>{w.range}</span><span>{w.attacks}</span>
        <span title={w.apRule === null ? '' : ruleTooltip(w.apRule)}>{w.ap}</span>
        <span class="flow">
          {#each w.rules as r (r.label)}<span class="chip" class:dim={r.notAutomated} title={ruleTooltip(r)}>{ruleChip(r)}</span>{/each}
        </span>
      {/each}
    </div>
  {/if}
  {#if sheet.rules.length > 0}
    <div class="flow">
      {#each sheet.rules as r (r.label)}<span class="chip" class:dim={r.notAutomated} title={ruleTooltip(r)}>{ruleChip(r)}</span>{/each}
    </div>
  {/if}
</section>

<style>
  .card { display: flex; flex-direction: column; gap: var(--s2); margin: 0; padding: var(--s3) var(--s4); border-top: 4px solid var(--team); overflow-y: auto; flex: 0 1 auto; max-height: 55%;
    min-height: 6rem; font-size: .95rem; }
  :global([data-theme='fantasy']) .card { padding: calc(var(--s3) + 4px) calc(var(--s4) + 4px); border-top: 6px solid var(--team); }
  p { margin: 0; }
  .top { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--s3); }
  .who { min-width: 0; }
  .head { margin: 0; font-family: var(--font-body); font-weight: 700; text-transform: none; letter-spacing: 0; font-size: 1.15rem; line-height: 1.2; color: var(--text); overflow-wrap: anywhere; }
  .meta { color: var(--dim); font-size: .9rem; }
  .stats { display: flex; gap: var(--s2); flex: none; }
  .stat { display: flex; flex-direction: column; align-items: center; min-width: 3.25rem; padding: 2px var(--s1); border: 1px solid var(--rule); line-height: 1.15; }
  .stat .v { font-size: 1.1rem; color: var(--accent); }
  :global([data-theme='fantasy']) .stat .v { color: var(--wood); }
  .stat .k { font-size: .7rem; color: var(--dim); text-transform: uppercase; letter-spacing: .05em; }
  .flow, .group { display: flex; flex-wrap: wrap; gap: var(--s1); align-items: center; }
  .group { flex-wrap: nowrap; }
  .badge, .chip { padding: 0 var(--s1); border: 1px solid var(--rule); font-size: .8rem; line-height: 1.5; color: var(--text); white-space: nowrap; }
  .chip.warn { color: var(--warning); border-color: color-mix(in srgb, var(--warning) 45%, transparent); }
  .warn { color: var(--warning); }
  .dim { color: var(--dim); }
  .flat { flex: 1; min-width: 0; min-height: 0; justify-content: flex-start; padding: 2px var(--s1); border: 0; border-radius: 0; clip-path: none; box-shadow: none;
    background: none; color: var(--text); text-align: left; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .flat:hover:not(:disabled) { background: var(--rule); color: var(--text); }
  .gone { opacity: 0.45; }
  .wounds { white-space: nowrap; font-size: .8rem; letter-spacing: 1px; color: var(--dim); }
  .weapons { display: grid; grid-template-columns: minmax(6rem, auto) auto auto auto 1fr; gap: 2px var(--s3); align-items: baseline; font-size: .9rem;
    padding-top: var(--s2); border-top: 1px solid var(--rule); }
  .weapons > .dim { font-size: .75rem; text-transform: uppercase; letter-spacing: .05em; }
</style>
