<!-- viewer/src/lib/lobby/LobbyPanel.svelte -->
<!-- LobbyPanel.cs: lobby edits, shown at lobby and (collapsed) in setup. Every control is one edit Godot sends. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import { nameProblem } from '../api/names';
  import type { ArmySummary } from '../api/types';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import Confirm, { type Question } from '../game/Confirm.svelte';
  import { sendEdit } from '../game/send';
  import type { Enforcement, SchedulerShape } from '../protocol/wire.generated';
  import {
    CREATOR_ONLY, armySeats, endSetupText, pickArmy, place, players, seatChoices, seatLine, teamChoices, waitingForCreator,
  } from './lobbyView';
  import { CUSTOM, configurationOf, presetOf, valueOf } from './rulesOptions';
  import { isCreator } from './seats';

  let { connection, snap, api, onLeave }: {
    connection: GameConnection; snap: ConnectionState; api: Api; onLeave: () => void;
  } = $props();

  const game = $derived(snap.game!);
  const creator = $derived(isCreator(game, snap.participantId));
  const catalog = $derived(snap.options);
  const watcher = $derived(snap.participantId === null || snap.participantId === '00000000-0000-0000-0000-000000000000');

  let problem: string | null = $state(null);
  let armies: ArmySummary[] = $state([]);
  let asking: Question | null = $state(null);

  let name = $state('');
  let shape: SchedulerShape = $state('alternating-activation');
  let enforcement: Enforcement = $state('warn');
  let chosen: Record<string, string> = $state({});

  // The form follows the game's configuration whenever another edit changes it.
  const configKey = $derived(JSON.stringify([game.name, game.schedulerShape, game.enforcement, game.optionalRules]));
  $effect(() => {
    void configKey;
    name = game.name;
    shape = game.schedulerShape;
    enforcement = game.enforcement;
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, game.optionalRules)]));
  });

  $effect(() => {
    api.listArmies().then((list) => { armies = list; }).catch((e: Error) => { problem = e.message; });
  });

  const preset = $derived(presetOf(catalog, chosen));

  async function send(edit: Parameters<typeof sendEdit>[1]) { problem = await sendEdit(connection, edit); }

  function pickPreset(value: string) {
    const picked = catalog.presets.find((p) => p.name === value);
    if (picked === undefined) return;                                 // Custom: the selects stay as they are
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, picked.optionalRules)]));
    if (picked.enforcement !== undefined) enforcement = picked.enforcement;
  }

  function apply() {
    const bad = nameProblem(name, 'A game name');
    if (bad !== null) { problem = bad; return; }
    void send(configurationOf(game, name, shape, enforcement, catalog, chosen));
  }

  const leave = () => {
    asking = { title: 'Leave this game?', text: `Leave ${game.name}? Your seat and the army you brought go.`, ok: 'Leave', run: onLeave };
  };
</script>

<section class="lobby pad" aria-label="Lobby">
  <h2>Lobby</h2>
  <p>{seatLine(game, snap.participantId)}</p>
  {#if waitingForCreator(game, creator) !== null}<p class="muted">{waitingForCreator(game, creator)}</p>{/if}

  <div class="flow">
    {#each teamChoices(game, snap.participantId, creator) as choice (choice.teamId)}
      <button disabled={!choice.enabled} title={choice.why} onclick={() => send(choice.edit)}>{choice.text}</button>
    {/each}
    <button onclick={() => send({ kind: 'pick_team' })}>Watch</button>
    <button onclick={() => send({ kind: 'create_team' })}>Create team</button>
  </div>

  <h3>Players</h3>
  {#each players(game) as seat (seat.participantId)}
    <div class="row">
      <span class="grow" title={seat.army === null ? seat.name : `${seat.name} ⚔ ${seat.army}`}>{seat.army === null ? seat.name : `${seat.name} ⚔ ${seat.army}`}</span>
      {#if creator}
        <select aria-label={`Team for ${seat.name}`} value={String(seat.selected)} onchange={(e) => send(place(game, seat.participantId, Number(e.currentTarget.value)))}>
          {#each seatChoices(game) as choice, i (i)}<option value={String(i)}>{choice.label}</option>{/each}
        </select>
      {:else}
        <span>{seat.seat}</span>
      {/if}
    </div>
  {/each}

  {#each armySeats(game, snap.participantId) as seat (seat.teamId ?? 'none')}
    <label class="field">{seat.label}
      <select value={seat.armyId ?? ''} onchange={(e) => send(pickArmy(seat, e.currentTarget.value === '' ? null : e.currentTarget.value))}>
        <option value="">No army</option>
        {#each armies as army (army.id)}<option value={army.id}>{army.name}</option>{/each}
      </select>
    </label>
  {/each}

  {#if problem !== null}<p class="error" role="alert">{problem}</p>{/if}

  <h3>Configuration</h3>
  {#if !creator}<p class="muted">{CREATOR_ONLY}</p>{/if}
  <fieldset disabled={!creator} title={creator ? '' : CREATOR_ONLY}>
    <label class="field">Game name <input bind:value={name} maxlength="64" placeholder="game name" /></label>
    <label class="field">Activation
      <select bind:value={shape}>
        <option value="alternating-activation">Alternating activation</option>
        <option value="player-turn">Player turn</option>
      </select>
    </label>
    <label class="field">Enforcement
      <select bind:value={enforcement}>
        <option value="silent">silent</option><option value="warn">warn</option><option value="strict">strict</option>
      </select>
    </label>
    {#if catalog.options.length > 0}
      <h4>Optional rules</h4>
      <label class="field">Preset
        <select value={preset} onchange={(e) => pickPreset(e.currentTarget.value)}>
          {#each catalog.presets as p (p.name)}<option value={p.name}>{p.name}</option>{/each}
          <option value={CUSTOM}>{CUSTOM}</option>
        </select>
      </label>
      {#each catalog.options as option (option.key)}
        <label class="field">{option.label} ({option.page})
          <select value={chosen[option.key] ?? option.default} onchange={(e) => { chosen = { ...chosen, [option.key]: e.currentTarget.value }; }}>
            {#each option.values as v (v.value)}<option value={v.value}>{v.label}</option>{/each}
          </select>
        </label>
      {/each}
    {/if}
    <button onclick={apply}>Apply configuration</button>
  </fieldset>

  <div class="flow">
    {#if !watcher}<button onclick={leave}>Leave game</button>{/if}
    {#if creator && game.status === 'lobby'}<button onclick={() => send({ kind: 'start_setup' })}>Start setup</button>{/if}
    {#if creator && game.status === 'setup'}<button onclick={() => send({ kind: 'end_setup' })}>{endSetupText(snap.draft)}</button>{/if}
  </div>
</section>

<Confirm bind:ask={asking} />

<style>
  h2 { font-size: 1rem; margin: 0 0 0.3rem; }
  h3 { font-size: 0.9rem; margin: 0.8rem 0 0.2rem; }
  h4 { font-size: 0.85rem; margin: 0.6rem 0 0.2rem; }
  .flow { display: flex; flex-wrap: wrap; gap: 0.3rem; margin: 0.4rem 0; }
  .row { display: flex; gap: 0.4rem; align-items: center; }
  .grow { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .field { display: flex; flex-direction: column; gap: 0.1rem; margin: 0.3rem 0; color: var(--muted); }
  .field :global(select), .field :global(input) { color: var(--ink); }
  fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
</style>
