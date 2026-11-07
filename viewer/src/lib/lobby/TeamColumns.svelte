<!-- viewer/src/lib/lobby/TeamColumns.svelte -->
<!-- TeamColumnsPanel.cs over columnsOf (spec 2026-11-07-lobby-and-setup-redesign-design.md): one column per team in its colour, then Watching;
     each player with their army, my row with my army picker; Join at a column's foot, a row's ▸ menu and a row dragged onto another column
     each send one PickTeam. The lobby's are wide; setup's sidebar holds them `compact`. -->
<script lang="ts">
  import type { ArmySummary } from '../api/types';
  import type { ConnectionState } from '../connection/gameConnection';
  import { teamColour } from '../board/palette';
  import type { Edit } from '../protocol/wire.generated';
  import { createTeam } from './lobbyEdits';
  import { armyOptions, pickArmy } from './lobbyView';
  import { ADD_TEAM, ADD_TEAM_COMPACT, ALSO_PLAY, NO_ARMY, YOU, columnsOf, moveTo, type TeamColumn } from './teamColumns';
  import { isCreator } from './seats';
  import TeamIcon from './TeamIcon.svelte';

  let { snap, armies, send, compact = false }: {
    snap: ConnectionState; armies: readonly ArmySummary[]; send: (edit: Edit) => Promise<void>; compact?: boolean;
  } = $props();

  const game = $derived(snap.game!);
  const creator = $derived(isCreator(game, snap.participantId));
  const columns = $derived(columnsOf(game, snap.participantId, creator, compact));
  const options = $derived(armyOptions(game.gameRules, armies));

  let dragging: string | null = $state(null);
  let over: string | null = $state(null);           // the column key a drop would land on
  const keyOf = (c: TeamColumn) => c.teamId ?? 'watching';
  const colourOf = (c: TeamColumn) => (c.ordinal > 0 ? teamColour(c.ordinal) : 'var(--dim)');
  const dropFor = (c: TeamColumn) => (dragging === null ? null : moveTo(game, snap.participantId, creator, dragging, c.teamId));

  function drop(c: TeamColumn, e: DragEvent) {
    e.preventDefault();
    const edit = dropFor(c);
    dragging = null; over = null;
    if (edit !== null) void send(edit);
  }
</script>

<div class="columns" class:compact>
  {#each columns as c (keyOf(c))}
    <section class="column" class:over={over === keyOf(c)} class:watching={c.teamId === null} style:--team={colourOf(c)} aria-label={c.title}
      ondragover={(e) => { if (dropFor(c) !== null) { e.preventDefault(); over = keyOf(c); } }}
      ondragleave={() => { if (over === keyOf(c)) over = null; }}
      ondrop={(e) => drop(c, e)}>
      <header class="band" title={c.stateText}>
        <TeamIcon name={c.teamId === null ? 'watch' : 'team'} size={compact ? 18 : 28} />
        <div class="words">
          <h3>{c.title}</h3>
          {#if !compact}
            <span class="state">
              {#if c.state !== 'watching'}<TeamIcon name={c.state === 'ready' ? 'ready' : 'warning'} size={14} />{/if}
              {c.stateText}
            </span>
          {/if}
        </div>
        <!-- Compact: the state is an icon after the title, its words the band's title. -->
        {#if compact && c.state !== 'watching'}<TeamIcon name={c.state === 'ready' ? 'ready' : 'warning'} size={14} />{/if}
      </header>
      <ul class="members">
        {#each c.members as m (m.participantId)}
          <li class="member" class:you={m.you} draggable={m.moves.length > 0}
            title={m.moves.length > 0 ? 'Drag to another column to move' : undefined}
            ondragstart={(e) => { dragging = m.participantId; e.dataTransfer?.setData('text/plain', m.participantId); }}
            ondragend={() => { dragging = null; over = null; }}>
            <div class="name">
              <span class="player"><TeamIcon name="player" size={compact ? 16 : 22} /></span>
              <span class="text">{m.name}</span>
              {#if m.you}<span class="you-tag">{YOU}</span>{/if}
              {#if m.moves.length > 0}
                <select class="move" aria-label={`Move ${m.name}`} value=""
                  onchange={(e) => { const i = Number(e.currentTarget.value); e.currentTarget.value = ''; if (!Number.isNaN(i)) void send(m.moves[i]!.edit); }}>
                  <option value="">▸</option>
                  {#each m.moves as move, i (i)}<option value={String(i)}>{move.text}</option>{/each}
                </select>
              {/if}
            </div>
            {#if c.teamId !== null || m.army !== null || m.myArmy !== null}
            <div class="army" class:warn={m.army === null && m.noArmyWarns} class:none={m.army === null && !m.noArmyWarns}>
              <TeamIcon name={m.army === null && m.noArmyWarns ? 'warning' : 'army'} size={compact ? 14 : 18} />
              {#if m.myArmy !== null && (!compact || m.myArmy.armyId === null)}
                {@const seat = m.myArmy}
                <select aria-label={seat.label} value={seat.armyId ?? ''} onchange={(e) => send(pickArmy(seat, e.currentTarget.value === '' ? null : e.currentTarget.value))}>
                  <option value="">{NO_ARMY}</option>
                  {#each options as o (o.army.id)}<option value={o.army.id} disabled={o.disabledReason !== null} title={o.disabledReason}>{o.text}</option>{/each}
                </select>
              {:else}
                <span class="text">{m.army ?? NO_ARMY}</span>
              {/if}
            </div>
            {/if}
          </li>
        {:else}
          <li class="empty">{c.teamId === null ? 'No one' : 'No players yet'}</li>
        {/each}
      </ul>
      <!-- Compact (setup): no buttons; a row's ▸ menu and drag make every move a Join would. -->
      {#if c.join !== null && !compact}
        <footer>
          <button class="join" disabled={c.join.edit === null} title={c.join.why ?? undefined} onclick={() => c.join?.edit && send(c.join.edit)}>{c.join.text}</button>
          {#if c.join.why !== null}<p class="why">{c.join.why}</p>{/if}
          {#if c.alsoPlay !== null}
            {@const also = c.alsoPlay}
            <button class="quiet" title="Keep your team and play this side too" onclick={() => send(also)}>{ALSO_PLAY}</button>
          {/if}
        </footer>
      {/if}
    </section>
  {/each}
  {#if creator}
    <button class="add" onclick={() => send(createTeam())}>{compact ? ADD_TEAM_COMPACT : ADD_TEAM}</button>
  {/if}
</div>

<style>
  .columns { display: grid; grid-template-columns: repeat(auto-fill, minmax(15.5rem, 1fr)); gap: var(--s5); align-items: start; }
  .columns.compact { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--s2); }
  .column { display: flex; flex-direction: column; background: var(--panel-solid); border: 2px solid color-mix(in srgb, var(--team) 85%, transparent);
    border-left-width: 7px; min-width: 0; }
  .column.watching { border-color: color-mix(in srgb, var(--dim) 50%, transparent); }
  .column.over { background: color-mix(in srgb, var(--team) 22%, var(--panel-solid)); }
  .band { display: flex; align-items: center; gap: var(--s3); padding: var(--s3) var(--s4); background: var(--team); color: var(--bg); }
  .watching .band { background: color-mix(in srgb, var(--dim) 25%, transparent); color: var(--text); }
  .compact .band { padding: var(--s1) var(--s2); gap: var(--s2); }
  .words { display: flex; flex-direction: column; min-width: 0; }
  h3 { margin: 0; font-size: 1.15rem; letter-spacing: .06em; text-transform: uppercase; color: inherit; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .compact h3 { font-size: .95rem; }
  .state { display: inline-flex; align-items: center; gap: var(--s1); font-size: .9rem; }
  .members { list-style: none; margin: 0; padding: var(--s4); display: flex; flex-direction: column; gap: var(--s3); }
  .compact .members { padding: var(--s2); gap: var(--s1); }
  .member { display: flex; flex-direction: column; gap: var(--s1); padding: var(--s2); }
  .member[draggable='true'] { cursor: grab; }
  .member.you { background: color-mix(in srgb, var(--accent) 10%, transparent); }
  .name, .army { display: flex; align-items: center; gap: var(--s2); min-width: 0; }
  .player { color: var(--team); display: inline-flex; }
  .text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; flex: 1 1 auto; }
  .army { padding-left: var(--s3); font-size: .95rem; }
  .compact .army { padding-left: var(--s1); font-size: .85rem; }
  .army.warn { color: var(--warning); }
  .army.none { color: var(--dim); }
  .army select { flex: 1 1 auto; min-width: 0; color: var(--text); }
  .you-tag { color: var(--accent); flex: none; }
  .move { flex: none; width: 2.4rem; min-height: 1.8rem; padding: 0 var(--s1); font-size: .9rem; }
  .empty { color: var(--dim); }
  footer { display: flex; flex-direction: column; align-items: center; gap: var(--s2); padding: 0 var(--s4) var(--s4); }
  .join { min-width: 8rem; }
  .why { margin: 0; color: var(--dim); font-size: .85rem; text-align: center; }
  .add { display: flex; align-items: center; justify-content: center; gap: var(--s2); min-height: 10rem; border-style: dashed; }
  .compact .add { min-height: 0; }
</style>
