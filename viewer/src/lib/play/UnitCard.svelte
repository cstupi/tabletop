<!-- viewer/src/lib/play/UnitCard.svelte -->
<!-- UnitCard.cs (UX redesign part 2 → Unit card): the band and the name alone; a pip per model and the count note; Quality and Defense
     badges with their icons, a cover shield after them while the server's facts put the unit in cover, and the state chips with theirs; the decision note; one row per group of like models (its pips, its name, its
     wound pips; a group holding a picked model framed); the weapons grid and the unit's rules with their texts as tooltips. Every word is
     cardText.ts's, unitCard.ts's or the sheet's. -->
<script lang="ts">
  import { teamColour } from '../board/palette';
  import { WEAPON_HEADINGS, ruleChip, ruleTooltip, weaponName } from './cardText';
  import { cardPips, chipText, coverTip, countNote, groupLine, groupMarked, groupName, groupPips, groupWoundCount, groupWoundPips, nextModel, type UnitCardContent } from './unitCard';
  import { HERO_LABEL } from './unitSheet';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';
  import HudIcon from './HudIcon.svelte';

  let { card, selectedModel = null, picked = [], onPick }: {
    card: UnitCardContent; selectedModel?: string | null; picked?: readonly string[]; onPick: (modelId: string) => void;
  } = $props();

  const sheet = $derived(card.sheet);
  const noFocus = (e: Event) => e.preventDefault();
  const heading = (h: string) => (h === 'Attacks' ? 'A' : h);
</script>

<section class="panel card" aria-label="Unit card" style:--team={teamColour(card.team)}>
  <h2 class="head">{sheet.name}</h2>
  <p class="count"><span class="pips">{cardPips(card)}</span><span class="dim">{countNote(card)}</span></p>
  <div class="flow">
    <span class="stat" title={`Quality ${sheet.quality ?? '-'}`}><HudIcon name="quality" size={15} />Q {sheet.quality ?? '-'}</span>
    <span class="stat" title={`Defense ${sheet.defense ?? '-'}`}><HudIcon name="defense" size={15} />D {sheet.defense ?? '-'}</span>
    {#if coverTip(card) !== null}<span class="stat" title={coverTip(card)}><HudIcon name="cover" size={15} /></span>{/if}
    {#each sheet.chips as chip, i (i)}
      <span class="chip" class:warn={chip.kind === 'condition'} class:dim={chip.kind === 'activated'} title={chip.text}>
        {#if chip.kind === 'activated'}<Icon name="activated" size=".95rem" />{/if}{chipText(chip)}
      </span>
    {/each}
  </div>
  {#if card.groups.length > 0}
    <p class="caption">Models</p>
    {#each card.groups as group, i (i)}
      <div class="group" class:gone={group.standing.length === 0} class:marked={groupMarked(group, picked)}>
        <span class="pips gp">{groupPips(group)}</span>
        {#if group.hero}<span class="badge">{HERO_LABEL}</span>{/if}
        <button
          type="button" class="flat" tabindex="-1" title={groupLine(group)} disabled={group.standing.length === 0}
          onmousedown={noFocus}
          onclick={() => { const id = nextModel(group, selectedModel); if (id !== null) onPick(id); }}
        >{groupName(group)}</button>
        {#if groupWoundPips(group) !== null}<span class="wounds" title="Wounds left">{groupWoundPips(group)}</span><span class="dim">{groupWoundCount(group)}</span>{/if}
      </div>
    {/each}
  {/if}
  {#if sheet.weapons.length > 0}
    <div class="weapons">
      {#each WEAPON_HEADINGS as h (h)}<span class="dim" title={h === 'Attacks' ? h : undefined}>{heading(h)}</span>{/each}
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
    <p class="caption">Rules</p>
    <div class="flow">
      {#each sheet.rules as r (r.label)}<span class="chip" class:dim={r.notAutomated} title={ruleTooltip(r)}>{ruleChip(r)}</span>{/each}
    </div>
  {/if}
</section>

<style>
  .card { display: flex; flex-direction: column; gap: var(--space-2); margin: 0; padding: var(--space-2); border-left: 6px solid var(--team); overflow-y: auto; flex: 0 1 auto; max-height: 45%;
    min-height: 6rem; font-size: .95rem; }
  :global([data-theme='fantasy']) .card { padding: calc(var(--space-2) + var(--frame-inset)); border-left: 8px solid var(--team); }
  p { margin: 0; }
  .head { margin: 0; font-family: var(--font-display); font-weight: var(--display-weight); letter-spacing: var(--display-tracking); font-size: 1.15rem;
    line-height: 1.2; color: var(--team); overflow-wrap: anywhere; }
  .count { display: flex; align-items: baseline; gap: var(--space-2); }
  .pips { color: var(--team); letter-spacing: 1px; white-space: nowrap; }
  .gp { flex: none; min-width: 3.5rem; font-size: .85rem; }
  .caption { font-size: .75rem; color: var(--dim); text-transform: uppercase; letter-spacing: .05em; margin-top: var(--space-1); }
  .stat { display: inline-flex; align-items: center; gap: var(--space-1); padding: var(--space-2); border: 1px solid var(--accent); line-height: 1.5; white-space: nowrap; }
  .stat :global(.hud-icon) { color: var(--accent); }
  .flow, .group { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-2); align-items: center; }
  .group { flex-wrap: nowrap; border: 1px solid transparent; }
  .group.marked { border-color: var(--accent); }
  .badge, .chip { display: inline-flex; align-items: center; gap: var(--space-1); padding: var(--space-2); border: 1px solid var(--rule); font-size: .8rem; line-height: 1.5; color: var(--text); white-space: nowrap; }
  .chip.warn { color: var(--warning); border-color: color-mix(in srgb, var(--warning) 45%, transparent); }
  .warn { color: var(--warning); }
  .dim { color: var(--dim); }
  .flat { flex: 1; min-width: 0; min-height: 0; justify-content: flex-start; padding: var(--space-1) var(--space-1); border: 0; border-radius: 0; clip-path: none; border-image: none; box-shadow: none;
    background: none; color: var(--text); text-align: left; white-space: normal; }
  .flat:hover:not(:disabled) { background: var(--rule); color: var(--text); }
  .gone { opacity: 0.45; }
  .wounds { white-space: nowrap; font-size: .8rem; letter-spacing: 1px; color: var(--text); }
  .weapons { display: grid; grid-template-columns: minmax(6rem, auto) auto auto auto 1fr; gap: var(--space-1) var(--space-2); align-items: baseline; font-size: .9rem;
    padding-top: var(--space-2); border-top: 1px solid var(--rule); }
  .weapons > .dim { font-size: .75rem; text-transform: uppercase; letter-spacing: .05em; }
</style>
