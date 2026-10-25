<!-- viewer/src/lib/lobby/TeamCards.svelte -->
<!-- The teams as cards: each team's players and their armies, my army picker on a team I hold, the one team action I have, the
     creator's placing control on every player, and the players without a team. The lobby shows them in a grid; setup's sidebar in
     one column (`compact`). Every control sends one lobby edit. -->
<script lang="ts">
  import type { ArmySummary } from '../api/types';
  import type { ConnectionState } from '../connection/gameConnection';
  import type { Edit } from '../protocol/wire.generated';
  import { armySeats, pickArmy, place } from './lobbyView';
  import { placeLabels, teamCards, unassigned, type CardPlayer } from './lobbyPage';
  import { teamsOf } from './seats';

  let { snap, armies, creator, send, compact = false }: {
    snap: ConnectionState; armies: readonly ArmySummary[]; creator: boolean; send: (edit: Edit) => Promise<void>; compact?: boolean;
  } = $props();

  const game = $derived(snap.game!);
  const cards = $derived(teamCards(game, snap.participantId, creator));
  const loose = $derived(unassigned(game, snap.participantId));
  const labels = $derived(placeLabels(game));
  const seats = $derived(armySeats(game, snap.participantId));
  const holding = $derived(teamsOf(game, snap.participantId).length > 0);
  const participant = $derived(game.participants.some((p) => p.id === snap.participantId));
</script>

{#snippet player(p: CardPlayer)}
  <li class="player">
    <div class="who">
      <span class="name">{p.name}{#if p.you}<span class="you">You</span>{/if}</span>
      {#if p.army !== null}<span class="army">{p.army}</span>{:else}<span class="army none">No army yet</span>{/if}
    </div>
    {#if creator}
      <select class="place" aria-label={`Team for ${p.name}`} value={String(p.selected)}
        onchange={(e) => send(place(game, p.participantId, Number(e.currentTarget.value)))}>
        {#each labels as label, i (i)}<option value={String(i)}>{label}</option>{/each}
      </select>
    {/if}
  </li>
{/snippet}

<div class="cards" class:compact>
  {#each cards as card (card.teamId)}
    <section class="panel team" class:mine={card.mine} aria-label={card.title}>
      <header>
        <h3>{card.title}</h3>
        <span class="count">{card.players.length === 0 ? 'Open' : `${card.players.length} player${card.players.length === 1 ? '' : 's'}`}</span>
      </header>
      {#if card.players.length === 0}
        <p class="dim open">No one has joined this team.</p>
      {:else}
        <ul class="players">{#each card.players as p (p.participantId)}{@render player(p)}{/each}</ul>
      {/if}
      {#each seats.filter((s) => s.teamId === card.teamId) as seat (seat.teamId)}
        <label class="army-pick">Your army
          <select value={seat.armyId ?? ''} onchange={(e) => send(pickArmy(seat, e.currentTarget.value === '' ? null : e.currentTarget.value))}>
            <option value="">Choose an army…</option>
            {#each armies as army (army.id)}<option value={army.id}>{army.name}</option>{/each}
          </select>
        </label>
        {#if armies.length === 0}<p class="dim hint">You have no armies yet: import one on the website's Armies page.</p>{/if}
      {/each}
      {#if participant}
        <footer>
          {#if card.action.leave}
            <button class="small" onclick={() => send(card.action.edit)} title={card.action.why}>Leave team</button>
          {:else}
            <button class="small" class:primary={card.action.enabled && !holding} disabled={!card.action.enabled} title={card.action.why}
              onclick={() => send(card.action.edit)}>{card.action.label}</button>
          {/if}
        </footer>
      {/if}
    </section>
  {/each}

  {#if loose.length > 0 || participant}
    <section class="panel team loose" aria-label="Without a team">
      <header><h3>Without a team</h3></header>
      {#if loose.length === 0}
        <p class="dim open">Everyone has a team.</p>
      {:else}
        <ul class="players">{#each loose as p (p.participantId)}{@render player(p)}{/each}</ul>
        <p class="dim hint">Players without a team watch once play starts.</p>
      {/if}
      {#if participant && holding}
        <footer><button class="small" onclick={() => send({ kind: 'pick_team' })} title="Leave every team you hold and watch once play starts">Watch instead</button></footer>
      {/if}
    </section>
  {/if}
</div>

<style>
  .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(17rem, 1fr)); gap: var(--s4); align-items: start; }
  .cards.compact { grid-template-columns: minmax(0, 1fr); gap: var(--s3); }
  .team { margin: 0; display: flex; flex-direction: column; gap: var(--s3); padding: var(--s4); }
  :global([data-theme='fantasy']) .team { padding: calc(var(--s4) + 4px); }
  .compact .team { padding: var(--s3); gap: var(--s2); }
  :global([data-theme='fantasy']) .compact .team { padding: calc(var(--s3) + 4px); }
  .team.loose { border-style: dashed; }
  header { display: flex; align-items: baseline; justify-content: space-between; gap: var(--s2); }
  h3 { margin: 0; font-size: 1rem; color: var(--text); }
  .mine h3 { color: var(--accent); }
  .count { color: var(--dim); font-size: .9rem; }
  .players { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; }
  .player { display: flex; align-items: center; justify-content: space-between; gap: var(--s2); padding: var(--s2) 0; border-top: 1px solid var(--rule); }
  .player:first-child { border-top: 0; padding-top: 0; }
  .who { display: flex; flex-direction: column; min-width: 0; }
  .name { display: inline-flex; align-items: center; gap: var(--s2); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .you { font-size: .75rem; padding: 0 var(--s1); border: 1px solid var(--accent); color: var(--accent); line-height: 1.3; }
  :global([data-theme='fantasy']) .you { border-radius: 3px; }
  .army { font-size: .9rem; color: var(--text); opacity: .85; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .army.none { color: var(--warning); opacity: 1; }
  .place { min-height: 2rem; font-size: .9rem; padding-block: 0; flex: none; max-width: 9rem; }
  .army-pick { display: flex; flex-direction: column; gap: var(--s1); color: var(--dim); font-size: .9rem; }
  .army-pick select { width: 100%; color: var(--text); font-size: 1rem; }
  .open, .hint { margin: 0; font-size: .9rem; }
  footer { display: flex; justify-content: flex-end; }
  .compact footer { justify-content: flex-start; }
</style>
