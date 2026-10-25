<!-- viewer/src/lib/replay/ReplayPage.svelte -->
<!-- A game's log replayed from the start (client design → Replay), the play page read-only: the top bar, the board with circles and
     names, the selected unit's card, the combat log, and the replay bar at the foot. It opens a watch socket (`?watch=1`) that sends
     nothing but the connect and reauths, and never replaces the account's play socket. Like the play page it loads the geometry for the
     deployment zones, so it lives outside the playback modules. -->
<script lang="ts">
  import packedCatalog from '@builtin-catalog';
  import { untrack } from 'svelte';
  import { apiOf } from '../api/client';
  import { extentOf } from '../board/table';
  import { teamColour } from '../board/palette';
  import type { View } from '../board/transform';
  import { connectGame, type ConnectionState, type GameConnection } from '../connection/gameConnection';
  import { geometryDownload, loadGeometry, type Geometry } from '../geometry';
  import Loading from '../components/Loading.svelte';
  import { tableToolsText } from '../components/loading';
  import { waitText } from '../game/header';
  import { filter as filterEntries } from '../play/combatLog';
  import CombatLogPanel from '../play/CombatLogPanel.svelte';
  import { CombatLogCache } from '../play/combatLogState';
  import { fetchRuleTexts, RuleTexts } from '../play/ruleTexts';
  import { sceneOfPlay } from '../play/scene';
  import type { LogFilter } from '../play/settings';
  import UnitCard from '../play/UnitCard.svelte';
  import ReplayBar from './ReplayBar.svelte';
  import { ReplayPlayer } from './replayPlayer';
  import { ReplayTimeline } from './replayTimeline';
  import { replayCardOf, replayHeaderOf, terrainNamer, unitSelection } from './replayView';
  import { parseCatalog } from '../setup/catalog';
  import type { Auth } from '../shell/auth';
  import { watchUrl } from '../shell/route';
  import Board from '../components/Board.svelte';

  let { gameId, socketUrl, auth }: { gameId: string; socketUrl: string; auth: Auth } = $props();

  const namer = terrainNamer((() => { try { return parseCatalog(packedCatalog); } catch { return null; } })());
  const api = apiOf(() => auth.jwt());
  const cache = new CombatLogCache();

  let snap = $state.raw<ConnectionState | null>(null);
  let player = $state.raw<ReplayPlayer | null>(null);
  /** Bumped on every change of the player (position, playing, speed) and of the log; everything below reads through it. */
  let version = $state(0);
  let geometry = $state.raw<Geometry | null>(null);
  const tools = geometryDownload();
  let rules = $state.raw(RuleTexts.empty);
  let view: View | null = $state(null);
  let selectedUnit = $state<string | null>(null);
  /** A clicked terrain piece or marker: drawn selected, which shows its name. */
  let selectedPiece = $state<string | null>(null);
  let touchedIds = $state.raw<ReadonlySet<string>>(new Set());
  let logFilter = $state<LogFilter>('all');

  $effect(() => {
    const url = watchUrl(socketUrl);
    const id = gameId;
    return untrack(() => open(url, id));
  });

  /** The watch socket and, from its first board, the timeline over its log. Returns the teardown. */
  function open(url: string, id: string): () => void {
    const connection: GameConnection = connectGame({ url, gameId: id, jwt: () => auth.jwt() });
    let built: { board: object; player: ReplayPlayer; stop: () => void } | null = null;
    const stopState = connection.subscribe((s) => {
      snap = s;
      if (s.board !== null && s.game !== null && built?.board !== s.board) {
        // A first board, or a reconnect's: rebuild and return to the position it left.
        const at = built?.player.timeline.position ?? 0;
        built?.player.pause();
        built?.stop();
        const timeline = new ReplayTimeline(s.board, s.game, () => connection.state.log);
        const next = new ReplayPlayer(timeline, () => connection.state.game?.status === 'ended');
        timeline.seek(at);
        built = { board: s.board, player: next, stop: next.subscribe(() => { version++; }) };
        player = next;
      }
      version++;
    });
    return () => { stopState(); built?.player.pause(); built?.stop(); connection.close(); player = null; };
  }

  $effect(() => {
    loadGeometry().then((g) => { geometry = g; }).catch(() => { /* zones stay undrawn */ });
  });

  const fetched = new Set<string>();
  $effect(() => {
    const hash = snap?.game?.propertiesHash;
    if (hash === undefined || fetched.has(hash)) return;
    fetched.add(hash);
    void fetchRuleTexts(api, hash).then((got) => { if (got === null) fetched.delete(hash); else rules = got.rules; });
  });

  const bar = $derived.by(() => { void version; return player?.bar ?? null; });

  // The frame loop while playing: ReplayBar._Process.
  $effect(() => {
    const p = player;
    if (p === null || bar?.playing !== true) return;
    let last = performance.now();
    let id = requestAnimationFrame(function loop(now: number) {
      p.advance(Math.min(0.5, (now - last) / 1000));
      last = now;
      id = requestAnimationFrame(loop);
    });
    return () => cancelAnimationFrame(id);
  });

  const game = $derived(snap?.game ?? null);
  const projection = $derived.by(() => { void version; return player?.timeline.projection ?? null; });
  const scene = $derived.by(() => {
    void version;
    return projection === null || game === null ? null : sceneOfPlay(projection, game, geometry?.zonePolygons ?? (() => []), namer);
  });
  const extent = $derived(scene === null ? undefined : extentOf(scene));
  const header = $derived.by(() => { void version; return projection === null || game === null ? null : replayHeaderOf(projection, game); });
  const card = $derived.by(() => {
    void version;
    return projection === null || game === null ? null : replayCardOf(projection, game, selectedUnit, rules, snap?.maxWounds ?? new Map());
  });
  const selectedModels = $derived.by((): ReadonlySet<string> => {
    void version;
    if (projection === null || selectedUnit === null) return new Set();
    return new Set([...projection.models.values()].filter((m) => m.unitId === selectedUnit).map((m) => m.id));
  });
  const drawnSelection = $derived(new Set([...selectedModels, ...touchedIds, ...(selectedPiece === null ? [] : [selectedPiece])]));
  const entries = $derived.by(() => {
    void version;
    if (player === null || game === null || projection === null) return [];
    const log = cache.of(player.timeline.shown, null, game, projection);
    return filterEntries(log.entries, logFilter, () => false, selectedUnit);
  });
  const loading = $derived(snap === null || (snap.board === null && snap.closeReason === null));

  function select(hit: { kind: string; id: string } | null): void {
    touchedIds = new Set();
    selectedPiece = hit !== null && hit.kind !== 'model' ? hit.id : null;
    if (hit?.kind !== 'model' || projection === null) { selectedUnit = null; return; }
    selectedUnit = unitSelection(projection, hit.id)?.unitId ?? null;
  }
  function pickModel(id: string): void {
    const unit = projection?.models.get(id)?.unitId;
    if (unit !== undefined) selectedUnit = unit;
  }
</script>

<div class="replay">
  <header class="top">
    <a class="back" href="#/games">← Games</a>
    <div class="title">
      <h1>{game?.name ?? 'Replay'}</h1>
      <span class="chip">Replay</span>
    </div>
    {#if header !== null}
      <div class="where">
        <span class="round">{header.topLine}</span>
        {#if header.activation !== null}<span class="activation">{header.activation}</span>{/if}
      </div>
      <div class="scores" aria-label="Scores">
        {#each header.teams as team (team.id)}
          <span class="team"><span class="swatch" style:background={teamColour(team.ordinal)}></span>{team.name}<strong>{team.score}</strong></span>
        {/each}
        {#if header.result !== null}<span class="chip ended result">{header.result}</span>{/if}
      </div>
    {/if}
  </header>

  <div class="body">
    <div class="stage">
      {#if scene !== null && !$tools.finished}<Loading strip text={tableToolsText($tools)} done={$tools.done} total={$tools.total} />{/if}
      {#if scene !== null}
        <Board {scene} bind:view {extent} models="circles" selected={drawnSelection} interaction={{ onSelect: (hit) => select(hit) }} />
      {:else if snap?.closeReason != null}
        <p class="pad" role="alert">{snap.closeReason}</p>
      {:else if loading}
        <Loading text={waitText(snap)} />
      {/if}
    </div>
    <aside class="side" aria-label="Unit and log">
      {#if card !== null}
        <div class="card-slot">
          <UnitCard {card} onPick={pickModel} />
        </div>
      {:else if scene !== null}
        <p class="hint">Click a model to see its unit.</p>
      {/if}
      <div class="log-slot">
        <h2>Combat log</h2>
        <CombatLogPanel
          {entries} filter={logFilter} pendingUndoTo={null} watching={true} {rules}
          onFilter={(f) => (logFilter = f)} onUndoTo={() => {}} onTouch={(ids) => { touchedIds = new Set(ids); }}
        />
      </div>
    </aside>
  </div>

  {#if player !== null && bar !== null}
    <ReplayBar {player} {bar} />
  {/if}
</div>

<style>
  .replay { display: grid; grid-template-rows: auto minmax(0, 1fr) auto; height: 100%; min-height: 0; }
  .top {
    display: flex; align-items: center; gap: var(--s2) var(--s5); flex-wrap: wrap;
    padding: var(--s2) var(--s4); background: var(--panel-solid); border-bottom: 1px solid var(--rule);
  }
  .back { color: var(--dim); text-decoration: none; white-space: nowrap; }
  .back:hover { color: var(--text); }
  .title { display: flex; align-items: center; gap: var(--s2); min-width: 0; }
  h1 { margin: 0; font-size: 1.15rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 22rem; }
  .where { display: flex; align-items: baseline; gap: var(--s3); flex: 1 1 auto; min-width: 0; }
  .round { font-weight: 600; white-space: nowrap; }
  .activation { color: var(--dim); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; }
  .scores { display: flex; align-items: center; gap: var(--s4); flex: none; }
  .team { display: inline-flex; align-items: center; gap: var(--s2); white-space: nowrap; }
  .team strong { font-family: var(--font-num, inherit); font-size: 1.1rem; }
  .swatch { width: 0.75rem; height: 0.75rem; border-radius: 50%; box-shadow: 0 0 0 1px var(--rule); }
  .body { display: grid; grid-template-columns: minmax(0, 1fr) 24rem; min-height: 0; }
  .stage { position: relative; min-width: 0; min-height: 0; }
  .pad { padding: var(--s4); margin: 0; }
  .side {
    display: flex; flex-direction: column; gap: var(--s3); min-height: 0; overflow: hidden;
    padding: var(--s3); border-left: 1px solid var(--rule); background: var(--panel-solid);
  }
  .card-slot { flex: 0 1 auto; min-height: 0; max-height: 55%; display: flex; flex-direction: column; }
  .card-slot :global(.card) { max-height: none; min-height: 0; }
  .hint { margin: 0; color: var(--dim); }
  .log-slot { flex: 1 1 0; min-height: 8rem; display: flex; flex-direction: column; gap: var(--s2); }
  h2 { margin: 0; font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--dim); }
  .log-slot :global(.log) { flex: 1; min-height: 0; }
  .log-slot :global(.log .rows) { flex: 1; max-height: none; min-height: 0; }
  /* The card's and the log's text buttons read as text, not as the theme's button face. */
  .side :global(button.flat) {
    justify-content: flex-start; min-height: 0; padding: 0.1rem 0.2rem; background: none; box-shadow: none; border: 0; clip-path: none;
    color: inherit; transform: none; display: block; text-align: left; overflow: hidden; text-overflow: ellipsis;
  }
  .side :global(button.flat:hover:not(:disabled)) { background: none; color: var(--accent); }
  @media (max-width: 60rem) {
    .body { grid-template-columns: minmax(0, 1fr); grid-template-rows: minmax(0, 1fr) minmax(0, 40%); }
    .side { border-left: 0; border-top: 1px solid var(--rule); }
  }
</style>
