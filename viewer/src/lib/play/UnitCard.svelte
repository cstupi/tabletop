<!-- viewer/src/lib/play/UnitCard.svelte -->
<!-- UnitCard.cs: the headline, the badges, the chips, the decision note, one row per group of like models, the weapons grid and the
     unit's rules with their texts as tooltips. Every word is cardText.ts's or the sheet's. -->
<script lang="ts">
  import { teamColour } from '../board/palette';
  import { WEAPON_HEADINGS, badges, cardHeadline, ruleChip, ruleTooltip, weaponName } from './cardText';
  import { groupLine, groupTitle, groupWounds, nextModel, type UnitCardContent } from './unitCard';
  import { HERO_LABEL } from './unitSheet';

  let { card, selectedModel = null, onPick }: { card: UnitCardContent; selectedModel?: string | null; onPick: (modelId: string) => void } = $props();

  const sheet = $derived(card.sheet);
  const noFocus = (e: Event) => e.preventDefault();
</script>

<section class="card" aria-label="Unit card">
  <div class="bar" style:background={teamColour(card.team)}></div>
  <p class="head">{cardHeadline(card)}</p>
  <div class="row">
    {#each badges(sheet) as b (b)}<span class="badge">{b}</span>{/each}
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
  .card { display: flex; flex-direction: column; gap: 0.35rem; padding: 0.4rem 0.5rem; border: 1px solid var(--line); border-radius: 4px; background: var(--panel); overflow: auto; max-height: 55vh; }
  .bar { height: 4px; margin: -0.4rem -0.5rem 0; }
  p { margin: 0; }
  .head { font-weight: 600; overflow-wrap: anywhere; }
  .row, .flow, .group { display: flex; flex-wrap: wrap; gap: 0.25rem; align-items: center; }
  .group { flex-wrap: nowrap; }
  .badge, .chip { padding: 0 0.4rem; border: 1px solid var(--line); border-radius: 3px; }
  .warn { color: var(--warning, #ffc44e); }
  .dim { color: var(--muted); }
  .flat { flex: 1; min-width: 0; text-align: left; background: none; border: 0; padding: 0.1rem 0.2rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .gone { opacity: 0.45; }
  .wounds { white-space: nowrap; }
  .weapons { display: grid; grid-template-columns: auto auto auto auto 1fr; gap: 0.1rem 0.6rem; align-items: start; }
</style>
