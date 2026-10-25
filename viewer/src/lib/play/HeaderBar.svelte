<!-- viewer/src/lib/play/HeaderBar.svelte -->
<!-- HeaderBar.cs: top centre, the round and turn; each team's score (right-click: the team menu) with its not-activated count and the
     objective chips; the activation and its step strip; the waiting line. After the end the first line carries the result, a button
     that reopens the Game over modal. -->
<script lang="ts">
  import type { ConnectionState } from '../connection/gameConnection';
  import { teamColour } from '../board/palette';
  import { playingAs } from '../lobby/seats';
  import type { Projection } from '../live/apply';
  import { notActivatedText, outlineText, scoreText, waitingLine } from './headerText';
  import { activationLine, headerOf, notActivated, objectives } from './headerView';
  import { Names } from './names';
  import { isWatching, waitHolds, waitingTeams } from './waiting';

  let { snap, shown, myTeams, actingTeam, onGameOver, onTeamMenu }: {
    snap: ConnectionState; shown: Projection; myTeams: readonly string[]; actingTeam: string | null;
    onGameOver: () => void; onTeamMenu: (teamId: string, at: { x: number; y: number }) => void;
  } = $props();

  const game = $derived(snap.game);
  const names = $derived(game === null ? null : new Names(game, shown));
  const model = $derived(game === null || names === null ? null : headerOf(shown, game, names));
  const counts = $derived(game === null ? new Map<string, number>() : notActivated(shown, game));
  const chips = $derived(objectives(shown));
  const activation = $derived(game === null || names === null ? null : activationLine(shown, game, names, actingTeam));
  const wait = $derived(game === null || names === null ? null : waitingLine(
    shown, names, game.status === 'playing' && waitHolds(game, shown.board, shown.lastSeq) ? waitingTeams(game, shown.board) : [],
    isWatching(snap), myTeams));
  const as = $derived(game === null ? null : playingAs(game, myTeams, actingTeam));
  const ordinalOf = (teamId: string): number => game?.teams.find((t) => t.id === teamId)?.ordinal ?? 1;
</script>

{#if game !== null && model !== null && (game.status === 'playing' || game.status === 'ended')}
  <header class="bar" aria-label="Game status">
    <div class="line top">
      <span>{model.topLine}</span>
      {#if as !== null}<span class="dim">· {as}</span>{/if}
      {#if model.result !== null}<button type="button" class="flat" onclick={onGameOver}>{`  ${model.result}`}</button>{/if}
    </div>
    <div class="line teams">
      {#each [...game.teams].sort((a, b) => a.ordinal - b.ordinal) as team (team.id)}
        {@const score = model.scores[team.ordinal - 1]?.[1] ?? 0}
        <span class="team">
          <!-- svelte-ignore a11y_no_static_element_interactions -->
          <span class="name" style:color={teamColour(team.ordinal)} title="Right-click: team menu"
            oncontextmenu={(e) => { e.preventDefault(); onTeamMenu(team.id, { x: e.clientX, y: e.clientY }); }}
          >{scoreText(names!.team(team.id), score)}</span><span class="dim">{notActivatedText(counts.get(team.id) ?? 0)}</span>
        </span>
      {/each}
      {#each chips as chip (chip.markerId)}
        <span class="chip" title="Objective"
          style:border-color={chip.teamId === null ? null : teamColour(ordinalOf(chip.teamId))}
          style:background={chip.teamId === null ? null : `color-mix(in srgb, ${teamColour(ordinalOf(chip.teamId))} 35%, transparent)`}>◎</span>
      {/each}
    </div>
    {#if model.activeUnit !== null}
      <div class="line activation">
        <span>{activation ?? model.activeUnit}</span>
        {#each model.outline as item, i (i)}
          <span class={item.state === 'done' ? 'done' : item.state === 'current' ? 'current' : 'dim'}>{outlineText(item)}</span>
        {/each}
      </div>
    {/if}
    {#if wait !== null}<div class="line warn">{wait}</div>{/if}
  </header>
{/if}

<style>
  .bar { background: color-mix(in srgb, var(--panel) 92%, transparent); border: 1px solid var(--line); border-radius: 4px; padding: 0.25rem 0.6rem;
    max-width: min(46rem, 100%); font-size: 0.9rem; pointer-events: auto; }
  .line { display: flex; flex-wrap: wrap; gap: 0 0.25rem; align-items: baseline; }
  .name { cursor: context-menu; white-space: pre; }
  .dim { opacity: 0.6; white-space: pre; }
  .done { color: #6ddf7c; white-space: pre; }
  .current { color: #ffc44e; white-space: pre; }
  .warn { color: #ffc44e; }
  .chip { border: 2px solid var(--line); border-radius: 4px; padding: 0 0.3rem; margin-left: 0.3rem; }
  .flat { background: none; border: none; color: inherit; cursor: pointer; font: inherit; padding: 0; white-space: pre; }
</style>
