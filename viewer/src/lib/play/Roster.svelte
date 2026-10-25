<!-- viewer/src/lib/play/Roster.svelte -->
<!-- Roster.cs: team headers and unit rows; a click selects the unit, a right-click asks for the unit's menu, R collapses to the headers. -->
<script lang="ts">
  import type { ConnectionState } from '../connection/gameConnection';
  import { teamColour } from '../board/palette';
  import type { Projection } from '../live/apply';
  import { intentOf, textFocused } from './keys';
  import { Names } from './names';
  import { rosterOf, woundsText, words } from './rosterView';

  let { snap, shown, myTeam, onPick, onMenu }: {
    snap: ConnectionState; shown: Projection; myTeam: string | null;
    onPick: (unitId: string) => void; onMenu: (unitId: string, at: { x: number; y: number }) => void;
  } = $props();

  let collapsed = $state(false);
  const teams = $derived(snap.game === null ? [] : rosterOf(shown, snap.game, new Names(snap.game, shown), myTeam, snap.maxWounds));

  function onKeydown(e: KeyboardEvent): void {
    if (e.defaultPrevented || textFocused(e.target)) return;
    if (intentOf(e.key, { shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey, alt: e.altKey })?.kind === 'toggleRoster') { collapsed = !collapsed; e.preventDefault(); }
  }
</script>

<svelte:window onkeydown={onKeydown} />

{#if snap.game !== null}
  <section class="roster" aria-label="Roster">
    {#each teams as team (team.teamId)}
      <div class="head" style:color={teamColour(team.ordinal)}>{team.name}</div>
      {#if !collapsed}
        {#each team.rows as row (row.unitId)}
          <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
          <div class="row" class:off={row.offTable} onclick={() => onPick(row.unitId)}
            oncontextmenu={(e) => { e.preventDefault(); onMenu(row.unitId, { x: e.clientX, y: e.clientY }); }}>
            <span class="name">{row.name}</span>
            <span class="mark" title={row.activated ? 'Activated' : ''}>{row.activated ? '✓' : ''}</span>
            <span class="wounds">{woundsText(row)}</span>
            {#each row.conditions as c (c)}<span class="chip">{words(c)}</span>{/each}
            {#if row.inside !== null || row.reserve !== null}
              <span class="where">
                {#if row.inside !== null}<span class="chip">in {row.inside}</span>{/if}
                {#if row.reserve !== null}<span class="chip">{row.reserve}</span>{/if}
              </span>
            {/if}
          </div>
        {/each}
      {/if}
    {/each}
  </section>
{/if}

<style>
  .roster { background: color-mix(in srgb, var(--panel) 92%, transparent); border: 1px solid var(--line); border-radius: 4px; padding: 0.3rem 0.5rem;
    width: 15rem; max-height: 100%; overflow: auto; font-size: 0.85rem; pointer-events: auto; }
  .head { font-weight: 600; }
  .row { display: flex; flex-wrap: wrap; gap: 0 0.4rem; align-items: baseline; cursor: pointer; padding: 1px 0; }
  .row:hover { background: color-mix(in srgb, var(--ink) 10%, transparent); }
  .row.off { opacity: 0.55; }
  .name { flex: 1 1 5rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .mark { width: 1rem; text-align: center; color: #6ddf7c; }
  .wounds { letter-spacing: -1px; }
  .where { flex-basis: 100%; padding-left: 1rem; }
  .chip { border: 1px solid var(--line); border-radius: 3px; padding: 0 0.3rem; font-size: 0.8rem; margin-right: 0.2rem; }
</style>
