<!-- viewer/src/lib/play/Roster.svelte -->
<!-- Roster.cs as the play page's left column: team headers and unit rows; a click selects the unit, a right-click asks for the unit's
     menu. The column collapses to a narrow rail (its button, or R). -->
<script lang="ts">
  import type { ConnectionState } from '../connection/gameConnection';
  import { teamColour } from '../board/palette';
  import type { Projection } from '../live/apply';
  import { intentOf, textFocused } from './keys';
  import { Names } from './names';
  import { rosterOf, woundsText, words } from './rosterView';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';

  let { snap, shown, myTeam, selected = null, onPick, onMenu }: {
    snap: ConnectionState; shown: Projection; myTeam: string | null;
    /** The selected unit, lit in its row. */
    selected?: string | null;
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
          <h3 class="team" style:--team={teamColour(team.ordinal)}>{team.name}{#if team.teamId === myTeam}<span class="you">You</span>{/if}</h3>
          <ul>
            {#each team.rows as row (row.unitId)}
              <li>
                <button type="button" class="row" class:off={row.offTable} class:on={row.unitId === selected} tabindex="-1"
                  onmousedown={(e) => e.preventDefault()} onclick={() => onPick(row.unitId)}
                  oncontextmenu={(e) => { e.preventDefault(); onMenu(row.unitId, { x: e.clientX, y: e.clientY }); }}>
                  <span class="line">
                    <span class="name">{row.name}</span>
                    {#if row.activated}<span class="done" title="Activated this round"><Icon name="activated" size="1rem" label="Activated" /></span>{/if}
                    <span class="wounds" title="Wounds left">{woundsText(row)}</span>
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
  .head { display: flex; align-items: center; justify-content: space-between; gap: var(--s2); padding: var(--s2) var(--s2) var(--s2) var(--s3); border-bottom: 1px solid var(--rule); }
  .collapsed .head { padding: var(--s2) 0; border-bottom: 0; }
  .head h2 { margin: 0; font-size: .85rem; }
  .chev { display: inline-flex; transition: transform .12s; }
  .chev.open { transform: rotate(180deg); }
  .rail { display: flex; flex-direction: column; gap: var(--s2); align-items: center; padding-top: var(--s2); }
  .dot { width: .75rem; height: .75rem; border-radius: 50%; }
  .body { flex: 1; min-height: 0; overflow-y: auto; padding: var(--s1) 0 var(--s3); }
  .team { display: flex; align-items: center; gap: var(--s2); margin: var(--s3) var(--s3) var(--s1); padding-left: var(--s2); border-left: 4px solid var(--team);
    font-size: .8rem; color: var(--text); }
  .you { font-family: var(--font-body); text-transform: none; letter-spacing: 0; font-size: .8rem; color: var(--dim); font-weight: inherit; }
  ul { list-style: none; margin: 0; padding: 0; }
  /* A row is a plain list entry, not a themed button. */
  .row { display: flex; flex-direction: column; align-items: stretch; gap: 2px; width: 100%; min-height: 0; padding: var(--s1) var(--s3) var(--s1) calc(var(--s3) - 3px);
    border: 0; border-left: 3px solid transparent; border-radius: 0; clip-path: none; box-shadow: none; background: none; color: var(--text);
    text-align: left; white-space: normal; font-size: .95rem; cursor: pointer; }
  .row:hover:not(:disabled) { background: var(--rule); color: var(--text); }
  .row.on, .row.on:hover:not(:disabled) { border-left-color: var(--accent); background: color-mix(in srgb, var(--accent) 14%, transparent); }
  .row.off { color: var(--dim); }
  .line { display: flex; align-items: center; gap: var(--s2); }
  .name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .done { display: inline-flex; color: var(--success); }
  .wounds { font-size: .7rem; letter-spacing: 1px; color: var(--dim); white-space: nowrap; }
  .tags { display: flex; flex-wrap: wrap; gap: var(--s1); }
  .tag { font-size: .75rem; padding: 0 var(--s1); border: 1px solid var(--rule); color: var(--dim); line-height: 1.4; }
  .tag.warn { color: var(--warning); border-color: color-mix(in srgb, var(--warning) 45%, transparent); }
</style>
