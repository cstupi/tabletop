<!-- viewer/src/lib/play/Roster.svelte -->
<!-- Roster.cs as the play page's left column (UX redesign part 2 → Roster): a band per team (its icon, name, ★ you on a team I hold) and a
     row per unit, sorted (may act, activated, off the table): ▶ on the acting unit, ○ / ✓ / —, the wound pips, the conditions, a ▸ on hover
     for the unit's menu. A click selects the unit, a right-click or ▸ opens the menu. The column collapses to a narrow rail (its button, or R). -->
<script lang="ts">
  import type { ConnectionState } from '../connection/gameConnection';
  import { teamColour } from '../board/palette';
  import type { Projection } from '../live/apply';
  import { intentOf, textFocused } from './keys';
  import { Names } from './names';
  import { markOf, rosterOf, woundsText, words } from './rosterView';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';
  import HudIcon from './HudIcon.svelte';

  let { snap, shown, myTeam, myTeams, selected = null, onPick, onMenu }: {
    snap: ConnectionState; shown: Projection; myTeam: string | null;
    /** Every team the account holds, each marked ★ you. */
    myTeams?: readonly string[];
    /** The selected unit, lit in its row. */
    selected?: string | null;
    onPick: (unitId: string) => void; onMenu: (unitId: string, at: { x: number; y: number }) => void;
  } = $props();

  let collapsed = $state(false);
  const teams = $derived(snap.game === null ? [] : rosterOf(shown, snap.game, new Names(snap.game, shown), myTeam, snap.maxWounds, myTeams));

  function onKeydown(e: KeyboardEvent): void {
    if (e.defaultPrevented || textFocused(e.target)) return;
    if (intentOf(e.key, { shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey, alt: e.altKey })?.kind === 'toggleRoster') { collapsed = !collapsed; e.preventDefault(); }
  }
</script>

<svelte:window onkeydown={onKeydown} />

{#if snap.game !== null}
  <section class="roster" class:collapsed aria-label="Roster">
    <div class="head">
      {#if !collapsed}<h2>Roster</h2>{/if}
      <button type="button" class="icon-btn" tabindex="-1" aria-expanded={!collapsed} aria-label={collapsed ? 'Show the roster (R)' : 'Hide the roster (R)'}
        title={collapsed ? 'Show the roster (R)' : 'Hide the roster (R)'} onmousedown={(e) => e.preventDefault()} onclick={() => (collapsed = !collapsed)}>
        <span class="chev" class:open={!collapsed}><Icon name="chevron" size="1rem" /></span>
      </button>
    </div>
    {#if collapsed}
      <div class="rail">
        {#each teams as team (team.teamId)}<span class="dot" style:background={teamColour(team.ordinal)} title={team.name}></span>{/each}
      </div>
    {:else}
      <div class="body">
        {#each teams as team (team.teamId)}
          <h3 class="team" style:--team={teamColour(team.ordinal)}><HudIcon name="team" size={15} /><span class="tname">{team.name}</span>{#if team.mine}<span class="you">★ you</span>{/if}</h3>
          <ul>
            {#each team.rows as row (row.unitId)}
              <li>
                <button type="button" class="row" class:off={row.offTable} class:on={row.unitId === selected} tabindex="-1"
                  onmousedown={(e) => e.preventDefault()} onclick={() => onPick(row.unitId)}
                  oncontextmenu={(e) => { e.preventDefault(); onMenu(row.unitId, { x: e.clientX, y: e.clientY }); }}>
                  <span class="line">
                    <span class="acting" aria-label={row.acting ? 'Acting' : undefined}>{row.acting ? '▶' : ''}</span>
                    <span class="name">{row.name}</span>
                    {#if markOf(row) === 'activated'}<span class="mark done" title="Activated this round"><Icon name="activated" size="1rem" label="Activated" /></span>
                    {:else if markOf(row) === 'mayAct'}<span class="mark" title="Not yet activated">○</span>
                    {:else}<span class="mark off" title="Off the table">—</span>{/if}
                    <span class="wounds" title="Wounds left">{woundsText(row)}</span>
                    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
                    <span class="menu" title="Unit menu" onclick={(e) => { e.stopPropagation(); const r = (e.currentTarget as HTMLElement).getBoundingClientRect(); onMenu(row.unitId, { x: r.right, y: r.top }); }}>▸</span>
                  </span>
                  {#if row.conditions.length > 0 || row.inside !== null || row.reserve !== null}
                    <span class="tags">
                      {#each row.conditions as c (c)}<span class="tag warn">{words(c)}</span>{/each}
                      {#if row.inside !== null}<span class="tag">In {row.inside}</span>{/if}
                      {#if row.reserve !== null}<span class="tag">{row.reserve}</span>{/if}
                    </span>
                  {/if}
                </button>
              </li>
            {/each}
          </ul>
        {/each}
      </div>
    {/if}
  </section>
{/if}

<style>
  .roster { display: flex; flex-direction: column; width: 16rem; height: 100%; min-height: 0; background: var(--panel-solid); border-right: 1px solid var(--rule); }
  .roster.collapsed { width: 2.75rem; align-items: center; }
  .head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); padding: var(--space-2) var(--space-2) var(--space-2) var(--space-2); border-bottom: 1px solid var(--rule); }
  .collapsed .head { padding: var(--space-2) 0; border-bottom: 0; }
  .head h2 { margin: 0; font-size: .85rem; }
  .chev { display: inline-flex; transition: transform .12s; }
  .chev.open { transform: rotate(180deg); }
  .rail { display: flex; flex-direction: column; gap: var(--space-2); align-items: center; padding-top: var(--space-2); }
  .dot { width: .75rem; height: .75rem; border-radius: 50%; }
  .body { flex: 1; min-height: 0; overflow-y: auto; padding: var(--space-1) 0 var(--space-2); }
  /* The team's band: its colour solid, the words in the background colour (Ui.TeamBand, Ui.OnBand). */
  .team { display: flex; align-items: center; gap: var(--space-2); margin: var(--space-2) var(--space-2) var(--space-1); padding: var(--space-1) var(--space-2); background: var(--team);
    font-size: .8rem; color: var(--panel-solid); }
  .tname { flex: 1; text-transform: uppercase; }
  .you { font-family: var(--font-body); text-transform: none; letter-spacing: 0; font-size: .8rem; font-weight: inherit; }
  .acting { flex: none; width: .8rem; color: var(--accent); font-size: .75rem; }
  .mark { flex: none; width: 1rem; display: inline-flex; justify-content: center; color: var(--text); }
  .mark.off { color: var(--dim); }
  .menu { flex: none; visibility: hidden; color: var(--dim); padding: 0 var(--space-1); }
  .row:hover .menu { visibility: visible; }
  .menu:hover { color: var(--accent); }
  @media (hover: none) { .menu { visibility: visible; } }
  ul { list-style: none; margin: 0; padding: 0; }
  /* A row is a plain list entry, not a themed button. */
  .row { display: flex; flex-direction: column; align-items: stretch; gap: var(--space-1); width: 100%; min-height: 0; padding: var(--space-1) var(--space-2);
    border: 0; border-radius: 0; clip-path: none; border-image: none; box-shadow: none; background: none; color: var(--text);
    text-align: left; white-space: normal; font-size: .95rem; cursor: pointer; }
  .row:hover:not(:disabled) { background: var(--rule); color: var(--text); }
  .row.on, .row.on:hover:not(:disabled) { box-shadow: inset 3px 0 0 var(--accent); background: color-mix(in srgb, var(--accent) 14%, transparent); }
  .row.off { color: var(--dim); }
  .line { display: flex; align-items: center; gap: var(--space-2); }
  .name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .done { display: inline-flex; color: var(--success); }
  .wounds { font-size: .7rem; letter-spacing: 1px; color: var(--dim); white-space: nowrap; }
  .tags { display: flex; flex-wrap: wrap; gap: var(--space-1); }
  .tag { font-size: .75rem; padding: 0 var(--space-1); border: 1px solid var(--rule); color: var(--dim); line-height: 1.4; }
  .tag.warn { color: var(--warning); border-color: color-mix(in srgb, var(--warning) 45%, transparent); }
</style>
