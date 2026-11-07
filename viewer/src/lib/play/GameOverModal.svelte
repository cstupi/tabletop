<!-- viewer/src/lib/play/GameOverModal.svelte -->
<!-- EndMessage.cs (UX redesign part 2 → Game over): the Game over modal. While warn's game_over Ask is open: END OR CONTINUE?, its prompt and
     each team's tile, End and Continue answering the Ask until my team has answered, then "Waiting for" the teams still to answer with their
     bands. Once the game has ended: ✓ GAME OVER, the result with the rounds played, each team's tile as the header draws it, the reason;
     Stay (or Esc) closes it and keeps the table, Leave goes to the games list. It opens once for each game_over Ask and once when the game
     ends; an undo past the end closes it and arms it again. -->
<script lang="ts">
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { playingAs } from '../lobby/seats';
  import type { Refused } from '../protocol/wire.generated';
  import { teamColour } from '../board/palette';
  import { answerOf, gameOverOf, headline } from './gameOverView';
  import { tiles } from './headerView';
  import { Names } from './names';
  import HudIcon from './HudIcon.svelte';

  let { connection, snap, myTeams, actingTeam }: {
    connection: GameConnection; snap: ConnectionState; myTeams: readonly string[]; actingTeam: string | null;
  } = $props();

  let dialog: HTMLDialogElement | undefined = $state();
  let shownFor: string | null = null;
  let endedShown = false;
  let problem: string | null = $state(null);
  let answered = $state(false);

  const model = $derived(snap.projection !== null && snap.game !== null ? gameOverOf(snap.projection, snap.game, actingTeam) : null);
  const names = $derived(snap.projection !== null && snap.game !== null ? new Names(snap.game, snap.projection) : null);
  const teamTiles = $derived(snap.projection !== null && snap.game !== null && names !== null ? tiles(snap.projection, snap.game, names, myTeams) : []);
  const note = $derived(model === null ? null
    : model.stage === 'choose' ? (snap.game === null ? null : playingAs(snap.game, myTeams, actingTeam)) : model.stage === 'waiting' ? null : model.note);
  const ordinalOf = (team: string): number => snap.game?.teams.find((t) => t.id === team)?.ordinal ?? 0;

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
      const outcome = await connection.sendCommand(answerOf(m, optionId, team));
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
    <h2>{model.stage === 'ended' ? '✓ Game over' : 'End or continue?'}</h2>
    {#if headline(model) !== null}<p class="result">{headline(model)}</p>{/if}
    <div class="tiles">
      {#each teamTiles as t (t.teamId)}
        <span class="tile" style:--team={teamColour(t.ordinal)}>
          {#if t.mine}<HudIcon name="player" size={18} />{/if}<span class="name">{t.name}</span><span class="num points">{t.score}</span><span class="vp">VP</span>
        </span>
      {/each}
    </div>
    {#if model.reason !== ''}<p class="reason">{model.reason}</p>{/if}
    {#if note !== null && note !== ''}<p class="note">{note}</p>{/if}
    {#if model.stage === 'waiting' && names !== null}
      <p class="waiting"><HudIcon name="wait" size={16} />{model.waitingFor.length > 0 ? 'Waiting for' : model.note}
        {#each model.waitingFor as team (team)}<span class="band" style:--team={teamColour(ordinalOf(team))}></span><span style:color={teamColour(ordinalOf(team))}>{names.team(team)}</span>{/each}
      </p>
    {/if}
    {#if problem !== null}<p role="alert">{problem}</p>{/if}
    <div class="actions">
      {#if model.stage === 'ended'}
        <button type="button" onclick={() => dialog?.close()}>Stay</button>
        <button type="button" class="primary leave" onclick={leave}><HudIcon name="home" size={16} />Leave</button>
      {/if}
      {#each model.choices as choice, i (choice.id)}
        <button type="button" class:primary={i === 0} disabled={answered} onclick={() => choose(choice.id)}>{choice.label}</button>
      {/each}
    </div>
  {/if}
</dialog>

<style>
  dialog { width: min(30rem, calc(100vw - 2rem)); text-align: center; }
  h2 { margin: 0 0 var(--s2); text-transform: uppercase; }
  .result { margin: 0; font-family: var(--font-display); font-weight: var(--display-weight); text-transform: var(--display-case);
    letter-spacing: var(--display-tracking); font-size: 1.35rem; color: var(--text); }
  .reason { color: var(--dim); }
  .tiles { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--s3); margin: var(--s3) 0; }
  .tile { display: inline-flex; align-items: center; gap: var(--s2); padding: var(--s1) var(--s3); color: var(--team);
    border: 1px solid color-mix(in srgb, var(--team) 60%, transparent); border-left: 8px solid var(--team); background: color-mix(in srgb, var(--team) 12%, transparent); }
  .name { font-weight: 700; text-transform: uppercase; }
  .points { font-size: 1.9rem; color: var(--text); }
  .vp { color: var(--dim); font-size: .85rem; }
  .note { color: var(--dim); }
  .waiting { display: flex; justify-content: center; align-items: center; gap: var(--s2); color: var(--dim); }
  .band { width: 4px; height: 1.1rem; background: var(--team); }
  .leave { display: inline-flex; align-items: center; gap: var(--s1); }
  .actions { justify-content: space-between; margin-bottom: 0; }
</style>
