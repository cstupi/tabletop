<!-- viewer/src/lib/play/HeaderBar.svelte -->
<!-- HeaderBar.cs as the play page's top bar: the round and whose turn; the activation and its step strip, or the waiting line; each team's
     score (right-click: the team menu) with its units still to activate; the objectives by who holds them. After the end the result is a
     button that reopens the Game over modal. -->
<script lang="ts">
  import type { ConnectionState } from '../connection/gameConnection';
  import { teamColour } from '../board/palette';
  import { playingAs } from '../lobby/seats';
  import type { Projection } from '../live/apply';
  import { notActivatedText, ruleSetText, scoreText, waitingLine } from './headerText';
  import { activationLine, headerOf, notActivated, objectives } from './headerView';
  import { Names } from './names';
  import { words } from './rosterView';
  import { isWatching, waitHolds, waitingTeams } from './waiting';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';

  let { snap, shown, myTeams, actingTeam, onGameOver, onTeamMenu }: {
    snap: ConnectionState; shown: Projection; myTeams: readonly string[]; actingTeam: string | null;
    onGameOver: () => void; onTeamMenu: (teamId: string, at: { x: number; y: number }) => void;
  } = $props();

  // The projection is mutated in place, so every line is read again from each new connection state, not from `shown`'s identity.
  const v = $derived.by(() => {
    const game = snap.game;
    if (game === null) return null;
    const names = new Names(game, shown);
    return {
      game, names, model: headerOf(shown, game, names), counts: notActivated(shown, game), chips: objectives(shown),
      activation: activationLine(shown, game, names, actingTeam),
      wait: waitingLine(shown, names, game.status === 'playing' && waitHolds(game, shown.board, shown.lastSeq) ? waitingTeams(game, shown.board) : [],
        isWatching(snap), myTeams),
      as: playingAs(game, myTeams, actingTeam),
      teams: [...game.teams].sort((a, b) => a.ordinal - b.ordinal),
    };
  });
  const game = $derived(v?.game ?? null);
  const names = $derived(v?.names ?? null);
  const model = $derived(v?.model ?? null);
  const counts = $derived(v?.counts ?? new Map<string, number>());
  const chips = $derived(v?.chips ?? []);
  const activation = $derived(v?.activation ?? null);
  const wait = $derived(v?.wait ?? null);
  const as = $derived(v?.as ?? null);
  const teams = $derived(v?.teams ?? []);
  const ordinalOf = (teamId: string): number => game?.teams.find((t) => t.id === teamId)?.ordinal ?? 1;
</script>

{#if game !== null && model !== null && (game.status === 'playing' || game.status === 'ended')}
  <header class="bar" aria-label="Game status">
    <div class="state">
      <p class="turn">
        {#if model.round > 0}<span class="round">Round {model.round}</span>{#if model.turn !== ''}<span class="sep" aria-hidden="true">·</span>{/if}{/if}
        {#if model.turn !== ''}<span class:round={model.round === 0}>{words(model.turn)}</span>{:else if model.round === 0}<span class="round">Deployment</span>{/if}
        {#if as !== null}<span class="chip">{as}</span>{/if}
        <span class="chip">{ruleSetText(game)}</span>
      </p>
      <!-- Every line keeps its row, filled or not, so the bar's height never changes and the board under it never moves. -->
      <p class="activation">
        {#if model.activeUnit !== null}
          <span class="who">{activation ?? model.activeUnit}</span>
          {#each model.outline as item, i (i)}
            <span class="step {item.state}">{#if item.state === 'done'}<span aria-hidden="true">✓ </span>{/if}{item.label}</span>
          {/each}
        {/if}
      </p>
      <p class="wait" title={wait ?? undefined}>{wait ?? ''}</p>
    </div>
    <div class="score">
      {#each teams as team (team.id)}
        {@const score = model.scores[team.ordinal - 1]?.[1] ?? 0}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="team" style:--team={teamColour(team.ordinal)} title="Right-click for the team menu"
          oncontextmenu={(e) => { e.preventDefault(); onTeamMenu(team.id, { x: e.clientX, y: e.clientY }); }}>
          <span class="name">{names!.team(team.id)}</span>
          <span class="points num">{scoreText(score)}</span>
          {#if model.round > 0 && game.status === 'playing'}<span class="left">{notActivatedText(counts.get(team.id) ?? 0)}</span>{/if}
        </div>
      {/each}
      {#if chips.length > 0}
        <div class="objectives" aria-label="Objectives">
          {#each chips as chip (chip.markerId)}
            {@const held = chip.teamId === null ? null : teamColour(ordinalOf(chip.teamId))}
            <span class="objective" class:held={held !== null} style:--held={held} title={chip.teamId === null ? 'Objective: nobody holds it' : `Objective held by ${names!.team(chip.teamId)}`}>
              <Icon name="objective" size="1.1rem" />
            </span>
          {/each}
        </div>
      {/if}
      {#if model.result !== null}<button type="button" class="primary small" onclick={onGameOver}>{model.result}</button>{/if}
    </div>
  </header>
{/if}

<style>
  /* One height whatever it says: three single-line rows (the turn, the activation, the wait), each cut short with an ellipsis. */
  .bar { --line: 1.5rem; display: flex; flex-wrap: nowrap; align-items: center; justify-content: space-between; gap: var(--s5);
    box-sizing: border-box; height: calc(3 * var(--line) + 4px + 2 * var(--s2)); overflow: hidden;
    padding: var(--s2) var(--s4); background: var(--panel-solid); border-bottom: 1px solid var(--rule); }
  p { margin: 0; height: var(--line); line-height: var(--line); min-width: 0; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
  .state { display: flex; flex-direction: column; gap: 2px; min-width: 0; flex: 1 1 18rem; }
  .turn { display: flex; flex-wrap: nowrap; align-items: baseline; gap: var(--s2); font-family: var(--font-display); font-weight: var(--display-weight);
    text-transform: var(--display-case); letter-spacing: var(--display-tracking); font-size: .95rem; }
  .round { color: var(--accent); }
  .sep { color: var(--dim); }
  .turn .chip { font-family: var(--font-body); text-transform: none; letter-spacing: 0; }
  .activation { display: flex; flex-wrap: nowrap; align-items: baseline; gap: var(--s3); font-size: .95rem; }
  .who { font-weight: 700; }
  .step { color: var(--dim); white-space: nowrap; }
  .step.done { color: var(--success); }
  .step.current { color: var(--text); border-bottom: 2px solid var(--accent); }
  .wait { color: var(--warning); font-size: .95rem; }
  .score { display: flex; flex-wrap: nowrap; align-items: center; gap: var(--s4); flex: none; }
  .team { display: grid; grid-template-columns: auto auto; grid-template-areas: 'name points' 'left points'; column-gap: var(--s3);
    align-items: center; padding: 0 0 0 var(--s3); border-left: 4px solid var(--team); cursor: context-menu; line-height: 1.2; }
  .name { grid-area: name; font-weight: 700; color: var(--text); }
  .left { grid-area: left; color: var(--dim); font-size: .85rem; }
  .points { grid-area: points; font-size: 1.25rem; color: var(--text); white-space: nowrap; }
  .objectives { display: flex; gap: var(--s1); align-items: center; }
  .objective { display: inline-flex; color: var(--dim); }
  .objective.held { color: var(--held); }
</style>
