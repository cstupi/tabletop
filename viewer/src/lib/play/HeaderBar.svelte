<!-- viewer/src/lib/play/HeaderBar.svelte -->
<!-- HeaderBar.cs as the play page's top bar (UX redesign part 2 → Header): the round; the acting unit with its action's icon and the step
     strip (marks only but the current step's under 640 px, HeaderFit), or after the end the result as a button that reopens the Game over modal; the waiting line (`Your move` in my team's colour, else
     the hourglass and the waiting team's band). At the right one tile per team (band, name, score, a dot per unit in play filled once it has
     acted, ▶ on the active team, the player icon on mine; right-click: the team menu) and the objectives by who holds them. -->
<script lang="ts">
  import type { ConnectionState } from '../connection/gameConnection';
  import { teamColour } from '../board/palette';
  import { playingAs } from '../lobby/seats';
  import type { Projection } from '../live/apply';
  import { notActivatedText, ruleSetText } from './headerText';
  import { actionIcon, activationLine, headerOf, objectives, roundText, stepText, tileCount, tileDots, tiles, waitLineOf, REPLAYING, WATCHING, waitingForPlayers, type HeaderFit } from './headerView';
  import { Names } from './names';
  import { isWatching, waitHolds, waitingTeams } from './waiting';
  import Icon from '../../../../web/src/lib/components/Icon.svelte';
  import HudIcon from './HudIcon.svelte';

  let { snap, shown, myTeams, actingTeam, onGameOver, onTeamMenu, replaying = false }: {
    snap: ConnectionState; shown: Projection; myTeams: readonly string[]; actingTeam: string | null;
    onGameOver: () => void; onTeamMenu: (teamId: string, at: { x: number; y: number }) => void; replaying?: boolean;
  } = $props();

  // HeaderFit by the status column's width: a narrow bar draws the step strip's marks without their words but for the current step's.
  let stateWidth = $state(0);
  const fit = $derived<HeaderFit>(stateWidth > 0 && stateWidth < 640 ? 'compact' : 'full');

  // The projection is mutated in place, so every line is read again from each new connection state, not from `shown`'s identity.
  const v = $derived.by(() => {
    const game = snap.game;
    if (game === null) return null;
    const names = new Names(game, shown);
    const deploying = game.status === 'playing' && waitHolds(game, shown.board, shown.lastSeq) ? waitingTeams(game, shown.board) : [];
    const quiet = waitingForPlayers(deploying) ?? (replaying ? REPLAYING : isWatching(snap) ? WATCHING : null);
    return {
      game, names, model: headerOf(shown, game, names), tiles: tiles(shown, game, names, myTeams), chips: objectives(shown),
      activation: activationLine(shown, game, names, actingTeam, fit), quiet, wait: quiet === null ? waitLineOf(shown, game, names, myTeams) : null,
      round: roundText(shown.scheduler.round, shown.scheduler.round === 0 ? shown.scheduler.phase : null),
      as: playingAs(game, myTeams, actingTeam),
    };
  });
  const ordinalOf = (teamId: string): number => v?.game.teams.find((t) => t.id === teamId)?.ordinal ?? 1;
</script>

{#if v !== null && (v.game.status === 'playing' || v.game.status === 'ended')}
  {@const { game, names, model } = v}
  <header class="bar" aria-label="Game status">
    <div class="state" bind:clientWidth={stateWidth}>
      <p class="turn" title={ruleSetText(game)}>
        <span class="round"><HudIcon name="round" size={18} />{v.round}</span>
        {#if v.as !== null}<span class="chip">{v.as}</span>{/if}
      </p>
      <!-- Every line keeps its row, filled or not, so the bar's height never changes and the board under it never moves. -->
      <p class="activation">
        {#if model.result !== null}
          <button type="button" class="primary small" onclick={onGameOver}>✓ {model.result}</button>
        {:else if model.activeUnit !== null}
          <span class="who"><span class="act"><HudIcon name={actionIcon(model.action)} size={16} /></span>{v.activation ?? model.activeUnit}</span>
          {#each model.outline as item, i (i)}
            <span class="step {item.state}" title={item.label}>{stepText(item, fit)}</span>
          {/each}
        {/if}
      </p>
      {#if v.quiet !== null}
        <p class="wait quiet" title={v.quiet}><HudIcon name={replaying || isWatching(snap) ? 'watch' : 'wait'} size={15} />{v.quiet}</p>
      {:else if v.wait?.mine}
        <p class="wait mine" style:--team={teamColour(v.wait.ordinal)}><HudIcon name="turn" size={15} />{v.wait.text}</p>
      {:else if v.wait}
        <p class="wait" title={v.wait.text}><span class="band" style:--team={teamColour(v.wait.ordinal)}></span><HudIcon name="wait" size={15} />{v.wait.text}</p>
      {:else}
        <p class="wait"></p>
      {/if}
    </div>
    <div class="score">
      {#each v.tiles as t (t.teamId)}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="tile" class:acting={t.acting} style:--team={teamColour(t.ordinal)}
          title={`${t.name}${t.mine ? ' · you' : ''}${t.acting ? ' · their turn' : ''}${model.round > 0 && game.status === 'playing' ? ` · ${notActivatedText(t.inPlay - t.acted)}` : ''} · right-click: team menu`}
          oncontextmenu={(e) => { e.preventDefault(); onTeamMenu(t.teamId, { x: e.clientX, y: e.clientY }); }}>
          {#if t.mine}<span class="ink"><HudIcon name="player" size={16} /></span>{/if}
          <span class="name">{names.team(t.teamId)}</span>
          <span class="points num">{t.score}</span><span class="vp">VP</span>
          {#if model.round > 0 && game.status === 'playing' && t.inPlay > 0}
            <span class="dots ink">{tileDots(t) ? '●'.repeat(t.acted) + '○'.repeat(t.inPlay - t.acted) : tileCount(t)}</span>
          {/if}
          {#if t.acting}<span class="ink"><HudIcon name="turn" size={14} /></span>{/if}
        </div>
      {/each}
      {#if v.chips.length > 0}
        <div class="objectives" aria-label="Objectives">
          {#each v.chips as chip (chip.markerId)}
            {@const held = chip.teamId === null ? null : teamColour(ordinalOf(chip.teamId))}
            <span class="objective" class:held={held !== null} style:--held={held} title={chip.teamId === null ? 'Objective: nobody holds it' : `Objective held by ${names.team(chip.teamId)}`}>
              <Icon name="objective" size="1.1rem" />
            </span>
          {/each}
        </div>
      {/if}
    </div>
  </header>
{/if}

<style>
  /* Three single-line rows (the round, the activation, the wait), each cut short with an ellipsis, beside the score; in a slot too narrow
     for both (the HUD frame's header between the corner and the toasts) the score takes a row above them, as Godot's header does. */
  .bar { --line: 1.6rem; display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--space-1) var(--space-4);
    box-sizing: border-box; min-height: calc(3 * var(--line) + 2 * var(--space-1) + 2 * var(--space-2)); overflow: hidden;
    padding: var(--space-2) var(--space-3); background: var(--panel-solid); border-bottom: 1px solid var(--rule); }
  p { margin: 0; height: var(--line); line-height: var(--line); min-width: 0; overflow: hidden; white-space: nowrap; text-overflow: ellipsis;
    display: flex; flex-wrap: nowrap; align-items: center; gap: var(--space-2); }
  .state { display: flex; flex-direction: column; gap: var(--space-1); min-width: 0; flex: 1 1 18rem; }
  .turn { font-family: var(--font-display); font-weight: var(--display-weight); text-transform: var(--display-case); letter-spacing: var(--display-tracking); font-size: 1rem; }
  .round { display: inline-flex; align-items: center; gap: var(--space-2); color: var(--text); text-transform: uppercase; }
  .round :global(.hud-icon) { color: var(--accent); }
  .turn .chip { font-family: var(--font-body); text-transform: none; letter-spacing: 0; }
  .activation { gap: var(--space-2); font-size: .95rem; }
  .who { display: inline-flex; align-items: center; gap: var(--space-2); font-weight: 700; }
  .act { color: var(--accent); display: inline-flex; }
  .step { color: var(--dim); white-space: nowrap; }
  .step.done { color: var(--success); }
  .step.current { color: var(--accent); }
  .wait { font-size: .95rem; color: var(--text); }
  .wait.quiet { color: var(--dim); }
  .wait.mine { color: var(--team); font-family: var(--font-display); font-weight: var(--display-weight); letter-spacing: var(--display-tracking); }
  .band { flex: none; width: 4px; height: 1.1rem; background: var(--team); }
  .score { display: flex; flex-wrap: nowrap; align-items: center; gap: var(--space-2); flex: none; max-width: 100%; }
  @container (max-width: 52rem) { .score { order: -1; } .state { flex-basis: 100%; } }
  /* web/src/app.css gives every .tile a column, 4.5rem minimum height and centred text; this one is a single row. */
  .tile { display: inline-flex; flex-direction: row; justify-content: flex-start; min-height: 0; text-align: left; white-space: nowrap; align-items: center; gap: var(--space-2); padding: var(--space-1) var(--space-2); cursor: context-menu; line-height: 1.2;
    border: 1px solid color-mix(in srgb, var(--team) 45%, transparent); border-left: 6px solid var(--team); border-radius: 4px;
    background: color-mix(in srgb, var(--team) 10%, transparent); }
  .tile.acting { border-width: 2px; border-left-width: 6px; border-color: var(--team); background: color-mix(in srgb, var(--team) 22%, transparent); }
  .name { font-weight: 700; color: var(--team); text-transform: uppercase; }
  .ink { color: var(--team); display: inline-flex; }
  .points { font-size: 1.4rem; color: var(--text); white-space: nowrap; }
  .vp { color: var(--dim); font-size: .85rem; }
  .dots { letter-spacing: 1px; }
  .objectives { display: flex; gap: var(--space-1); align-items: center; }
  .objective { display: inline-flex; color: var(--dim); }
  .objective.held { color: var(--held); }
</style>
