<!-- viewer/src/lib/components/Scoreboard.svelte -->
<!-- The score and the scheduler's position, read straight off the frame: whole state, no accumulation. -->
<script lang="ts">
  import { teamColour } from '../board/palette';
  import type { RecordedFrame, RecordedTeam } from '../protocol/recording.generated';

  let { teams, frame }: { teams: readonly RecordedTeam[]; frame: RecordedFrame | null } = $props();

  const ordered = $derived([...teams].sort((a, b) => a.ordinal - b.ordinal));
  const passed = $derived(new Set(frame?.passed ?? []));
  const held = (teamId: string): number => (frame?.markers ?? []).filter((m) => m.controlledBy === teamId).length;
</script>

{#if frame !== null && ordered.length > 0}
  <div class="scoreboard">
    <span class="muted">round {frame.round}</span>
    {#each ordered as team (team.id)}
      <span class="team" class:active={frame.activeTeamId === team.id} style="--team: {teamColour(team.ordinal)}">
        <span class="swatch"></span>
        {team.name}
        <strong>{frame.scores[team.id] ?? 0}</strong>
        {#if held(team.id) > 0}<span class="tag">{held(team.id)} held</span>{/if}
        {#if passed.has(team.id)}<span class="tag">passed</span>
        {:else if frame.activeTeamId === team.id}<span class="tag">active</span>{/if}
      </span>
    {/each}
  </div>
{/if}

<style>
  .scoreboard { display: flex; gap: var(--space-2); align-items: center; flex-wrap: wrap; }
  .team { display: inline-flex; gap: var(--space-1); align-items: center;
    padding: var(--space-2); border: 1px solid var(--line); border-radius: 0.3rem; }
  .team.active { border-color: var(--team); }
  .swatch { width: 0.6rem; height: 0.6rem; border-radius: 50%; background: var(--team); }
  .tag { font-size: 0.85em; color: var(--muted); text-transform: uppercase; letter-spacing: 0.04em; }
</style>
