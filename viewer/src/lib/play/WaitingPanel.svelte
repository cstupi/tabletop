<!-- viewer/src/lib/play/WaitingPanel.svelte -->
<!-- WaitingPanel.cs over waitingLines: while deployment waits for players, one line per team it waits on, for everyone; Join on a team no one
     holds, and a list of my armies on a team of mine with no army (the army is final once it joins, so picking sends it). The rows are
     rebuilt only when the lines change (linesChanged), so an open army list survives unrelated frames. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import type { ArmySummary } from '../api/types';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { sendEdit } from '../game/send';
  import { pickArmy } from '../lobby/lobbyView';
  import { WAITING_TITLE, linesChanged, waitHolds, waitingLines, waitingTeams, type WaitingLine } from './waiting';

  let { connection, api, snap }: { connection: GameConnection; api: Api; snap: ConnectionState } = $props();

  let shown = $state.raw<readonly WaitingLine[] | null>(null);
  let armies = $state.raw<readonly ArmySummary[] | null>(null);
  let problem = $state<string | null>(null);

  const now = $derived.by((): WaitingLine[] | null => {
    const game = snap.game, p = snap.projection;
    if (game === null || p === null || game.status !== 'playing' || !waitHolds(game, p.board, p.lastSeq)) return null;
    return waitingLines(game, waitingTeams(game, p.board), snap.participantId);
  });
  // The rows change only when the lines do; an equal frame keeps the old lines (and the open list).
  $effect(() => {
    const next = now;
    if (next === null) { shown = null; return; }
    if (linesChanged(shown, next)) {
      shown = next;
      if (next.some((l) => l.army !== null)) {
        armies = null;
        api.listArmies().then((list) => { armies = list; }).catch((e: Error) => { problem = e.message; });
      }
    }
  });

  async function send(edit: Parameters<typeof sendEdit>[1]): Promise<void> { problem = await sendEdit(connection, edit); }
</script>

{#if shown !== null}
  <section class="panel waiting" aria-label="Waiting for players">
    <h2>Waiting for players</h2>
    <p class="dim">{WAITING_TITLE}</p>
    {#each shown as line, i (i)}
      <p class="warn">{line.text}</p>
      {#if line.join !== null}
        {@const join = line.join}
        <button type="button" class="primary small" title={join.why} onclick={() => send(join.edit)}>{join.text}</button>
      {/if}
      {#if line.army !== null}
        {@const seat = line.army}
        <label>{seat.label}
          <select disabled={armies === null || armies.length === 0} onchange={(e) => { if (e.currentTarget.value !== '') void send(pickArmy(seat, e.currentTarget.value)); }}>
            <option value="">{armies !== null && armies.length === 0 ? 'No armies: import one in the web app' : 'Pick an army'}</option>
            {#each armies ?? [] as army (army.id)}<option value={army.id}>{army.name}</option>{/each}
          </select>
        </label>
      {/if}
    {/each}
    {#if problem !== null}<p role="alert">{problem}</p>{/if}
  </section>
{/if}

<style>
  .waiting { display: flex; flex-direction: column; align-items: flex-start; gap: var(--s2); margin: 0; padding: var(--s4); flex: none; }
  h2 { margin: 0; font-size: .95rem; }
  p { margin: 0; }
  .warn { color: var(--warning); }
  label { display: grid; gap: var(--s1); width: 100%; }
  select { width: 100%; }
</style>
