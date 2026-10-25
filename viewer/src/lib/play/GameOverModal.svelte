<!-- viewer/src/lib/play/GameOverModal.svelte -->
<!-- EndMessage.cs: the Game over modal. While warn's game_over Ask is open: its prompt and each team's score, End and Continue answering the
     Ask until my team has answered, then "Waiting for the other player". Once the game has ended: the result, its reason and the scores;
     Stay (or Esc) closes it and keeps the table, Leave goes to the games list. It opens once for each game_over Ask and once when the game
     ends; an undo past the end closes it and arms it again. -->
<script lang="ts">
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { playingAs } from '../lobby/seats';
  import type { Refused } from '../protocol/wire.generated';
  import { gameOverOf } from './gameOverView';
  import { headerOf } from './headerView';
  import { Names } from './names';

  let { connection, snap, myTeams, actingTeam }: {
    connection: GameConnection; snap: ConnectionState; myTeams: readonly string[]; actingTeam: string | null;
  } = $props();

  let dialog: HTMLDialogElement | undefined = $state();
  let shownFor: string | null = null;
  let endedShown = false;
  let problem: string | null = $state(null);
  let answered = $state(false);

  const model = $derived(snap.projection !== null && snap.game !== null ? gameOverOf(snap.projection, snap.game, actingTeam) : null);
  const scores = $derived(snap.projection !== null && snap.game !== null
    ? headerOf(snap.projection, snap.game, new Names(snap.game, snap.projection)).scores : []);
  const note = $derived(model === null ? null
    : model.stage === 'choose' ? (snap.game === null ? null : playingAs(snap.game, myTeams, actingTeam)) : model.note);

  /** The header's Game over button reopens it. */
  export function open(): void { if (model !== null && model.stage !== 'none') dialog?.showModal(); }

  $effect(() => {
    const m = model;
    const d = dialog;
    if (d === undefined) return;
    if (m === null || m.stage === 'none') { shownFor = null; endedShown = false; if (d.open) d.close(); return; }
    if (m.stage === 'ended' ? !endedShown : shownFor !== m.askId) {
      endedShown = m.stage === 'ended';
      shownFor = m.askId;
      answered = false;
      problem = null;
      if (!d.open) d.showModal();
    }
  });

  async function choose(optionId: string) {
    const m = model;
    if (m === null || m.askId === null) return;
    const team = actingTeam;
    answered = true;                                          // one answer per Ask: the buttons go at once
    // A Refused is matched by message id, even one that arrives after another player's frame settled the send as accepted.
    const seen: Refused[] = [];
    let id: string | null = null;
    const fail = (r: Refused) => { problem = `The server refused: ${r.detail}`; answered = false; };
    const stop = connection.onRefused((r) => { seen.push(r); if (id !== null && r.messageId === id) fail(r); });
    setTimeout(stop, 10_000);
    try {
      const outcome = await connection.sendCommand({ kind: 'answer_ask', askId: m.askId, answer: { kind: 'chosen', optionId }, ...(team === null ? {} : { teamId: team }) });
      id = outcome.id;
      const refused = outcome.refused ?? seen.find((r) => r.messageId === outcome.id);
      if (refused !== undefined) fail(refused);
    } catch (e) {
      problem = `Could not send the answer: ${(e as Error).message}`;
      answered = false;
      stop();
    }
  }

  const leave = () => { window.location.hash = '#/games'; };
</script>

<dialog
  bind:this={dialog}
  onkeydown={(e) => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); dialog?.close(); } }}
  oncancel={(e) => { e.preventDefault(); dialog?.close(); }}
  aria-label="Game over"
>
  {#if model !== null && model.stage !== 'none'}
    <h2>Game over</h2>
    <p class="result">{model.result ?? 'End or continue?'}</p>
    <p class="reason">{model.reason}</p>
    <ul class="scores">
      {#each scores as [team, score] (team)}<li>{team}  {score}</li>{/each}
    </ul>
    {#if note !== null && note !== ''}<p class="note">{note}</p>{/if}
    {#if problem !== null}<p class="warn" role="alert">{problem}</p>{/if}
    <div class="buttons">
      {#if model.stage === 'ended'}
        <button type="button" onclick={() => dialog?.close()}>Stay</button>
        <button type="button" onclick={leave}>Leave</button>
      {/if}
      {#each model.choices as choice (choice.id)}
        <button type="button" disabled={answered} onclick={() => choose(choice.id)}>{choice.label}</button>
      {/each}
    </div>
  {/if}
</dialog>

<style>
  dialog { min-width: 22rem; text-align: center; }
  .scores { list-style: none; padding: 0; }
  .buttons { display: flex; gap: 1rem; justify-content: center; }
  .warn { color: var(--warning, #ffc44e); }
</style>
