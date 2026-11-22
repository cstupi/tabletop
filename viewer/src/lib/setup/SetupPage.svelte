<!-- viewer/src/lib/setup/SetupPage.svelte -->
<!-- Setup on the 2D board: the tools sidebar on the left, the draft as a scene in the centre (solid drags through Geometry), the
     selected piece's card on the right. Every control sends one edit Godot sends; the server judges. -->
<script lang="ts">
  import { untrack } from 'svelte';
  import type { Api } from '../api/client';
  import type { BoardHit, BoardInteraction, Modifiers, Overlay } from '../board/interaction';
  import { extentOf, ghostOverlay, pieceOutlines, sceneOfTable } from '../board/table';
  import type { Point, View } from '../board/transform';
  import Board from '../components/Board.svelte';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { sendEdit } from '../game/send';
  import { geometryDownload, loadGeometry, type Geometry } from '../geometry';
  import Loading from '../components/Loading.svelte';
  import { tableToolsText } from '../components/loading';
  import type { Edit } from '../protocol/wire.generated';
  import { type Catalog, loadCatalog, nameOf } from './catalog';
  import { SetupDrag, type Held } from './drag';
  import { dropBuiltin, removeFromDraft, setupMenu, turnInDraft, type MenuItem, TERRAIN_TURN } from './edits';
  import { cannotAddTerrain } from './limits';
  import { TILE_TYPE } from './setupPanel';
  import PieceMenu from './PieceMenu.svelte';
  import SetupTools from './SetupTools.svelte';
  import TerrainCard from './TerrainCard.svelte';

  let { connection, snap, api, view = $bindable(null), creator }: {
    connection: GameConnection; snap: ConnectionState; api: Api; view?: View | null; creator: boolean;
  } = $props();

  let geometry = $state.raw<Geometry | null>(null);
  const tools = geometryDownload();
  let catalog = $state.raw<Catalog | null>(null);
  let loadError: string | null = $state(null);
  let problem: string | null = $state(null);
  let selected: string | null = $state(null);
  let held = $state.raw<Held | null>(null);
  let menu = $state.raw<{ items: MenuItem[]; at: Point } | null>(null);

  const draft = $derived(snap.draft);
  const drag = $derived(geometry === null ? null : new SetupDrag(geometry));
  const picked: ReadonlySet<string> = $derived(new Set(selected === null ? [] : [selected]));
  const carded = $derived(selected !== null && draft !== null
    && (draft.terrain.some((t) => t.id === selected) || draft.markers.some((m) => m.id === selected)));

  $effect(() => {
    loadGeometry().then((g) => { geometry = g; }).catch((e: Error) => { loadError = `The table tools did not load: ${e.message}`; });
    loadCatalog(api).then((c) => { catalog = c; }).catch((e: Error) => { loadError = `The terrain catalog did not load: ${e.message}`; });
  });

  async function send(edit: Edit): Promise<void> { problem = await sendEdit(connection, edit); }
  const report = (text: string | null) => { problem = text; };

  const scene = $derived(draft !== null && geometry !== null && catalog !== null
    ? sceneOfTable(draft, geometry.zonePolygons, (p) => nameOf(p, catalog!), snap.zones ?? undefined, snap.markerRange ?? undefined) : null);
  const extent = $derived(scene === null ? undefined : extentOf(scene));

  // A piece or marker another player removed leaves my selection and any drag of it.
  $effect(() => {
    const d = draft;
    untrack(() => {
      if (d !== null && selected !== null && !d.terrain.some((t) => t.id === selected) && !d.markers.some((m) => m.id === selected)) selected = null;
      if (held !== null && drag !== null && drag.held === null) held = null;
    });
  });

  const ring = (at: Point, radius: number, colour: string): Overlay => (ctx, project, ppi) => {
    const p = project(at);
    ctx.strokeStyle = colour;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.x, p.y, Math.max(4, radius * ppi), 0, 2 * Math.PI);
    ctx.stroke();
  };

  const overlays = $derived.by((): Overlay[] => {
    if (held === null || draft === null) return [];
    if (held.kind === 'marker') return [ring(held.at, 0.75, '#ffffff')];
    const piece = draft.terrain.find((t) => t.id === held!.id);
    if (piece === undefined) return [];
    return [
      ghostOverlay(pieceOutlines(piece, { position: held.end.drawn, yaw: held.yaw }), '#8ab4ff'),
      ring(held.end.lands, 0.75, '#6ddf7c'),
    ];
  });

  const interaction: BoardInteraction = {
    onSelect(hit: BoardHit | null) { menu = null; selected = hit !== null && hit.kind !== 'model' ? hit.id : null; },
    onDragStart(hit: BoardHit, at: Point) {
      menu = null;
      if (draft === null || drag === null) return false;
      const taken = drag.start(draft, hit, at);
      if (taken) { selected = hit.kind === 'model' ? null : hit.id; held = drag.held; }
      return taken;
    },
    onDragMove(at: Point) { if (draft !== null && drag !== null) held = drag.move(draft, at); },
    onDragEnd() {
      if (draft === null || drag === null) return;
      const edit = drag.end(draft);
      held = null;
      if (edit !== null) void send(edit);
    },
    onDragCancel() { drag?.cancel(); held = null; },
    onContextMenu(hit: BoardHit | null, _at: Point, screen: Point) {
      if (hit === null || hit.kind === 'model' || draft === null || geometry === null) { menu = null; return; }
      selected = hit.id;
      const items = setupMenu(geometry, draft, hit.id);
      menu = items.length === 0 ? null : { items, at: screen };
    },
    onKey(key: string, mods: Modifiers) {
      if (draft === null || geometry === null || drag === null) return false;
      const k = key.toLowerCase();
      if (k === 'q' || k === 'e') {
        const step = (mods.shift ? 2 : 1) * TERRAIN_TURN * (k === 'q' ? 1 : -1);
        if (held !== null) { held = drag.turn(draft, step); return true; }
        const piece = selected === null ? undefined : draft.terrain.find((t) => t.id === selected);
        if (piece === undefined) return false;
        const turn = turnInDraft(geometry, draft, piece.id, piece.yaw.degrees + step);
        if (turn !== null) void send(turn);
        return true;
      }
      if ((key === 'Delete' || key === 'Backspace') && selected !== null) {
        const remove = removeFromDraft(draft, selected);
        if (remove !== null) void send(remove);
        return true;
      }
      return false;
    },
    // A terrain tile dragged from the sidebar lands where it is dropped (SetupEdits.DropBuiltin).
    acceptsDrop: (types) => types.includes(TILE_TYPE) && geometry !== null && catalog !== null && draft !== null && cannotAddTerrain(draft) === null,
    onDrop(data, at) {
      const entry = catalog?.terrain.get(data.getData(TILE_TYPE));
      if (entry === undefined || draft === null || geometry === null) return;
      void send(dropBuiltin(geometry, entry, draft.terrain, at));
    },
  };

  async function runMenu(item: MenuItem) {
    menu = null;
    let text = '';
    if (item.input === 'integer') {
      const answer = window.prompt(item.label.replace('…', ''), '0');
      if (answer === null) return;
      text = answer;
    }
    const edit = item.edit(text);
    if (edit === null) { problem = 'That is not a whole number of degrees.'; return; }
    await send(edit);
  }
</script>

<div class="setup" class:carded>
  <aside class="tools" aria-label="Setup tools">
    {#if draft !== null && catalog !== null}
      <SetupTools {draft} {catalog} {api} {snap} {creator} {geometry} {send} {report} />
    {/if}
    <p class="help dim">Drag terrain and markers. <kbd>Q</kbd> <kbd>E</kbd> turn, <kbd>Del</kbd> removes, right-click for more.</p>
  </aside>
  <div class="stage">
    {#if loadError !== null}
      <p role="alert" class="pad">{loadError}</p>
    {:else if scene === null}
      <Loading text={tableToolsText($tools.finished ? null : $tools)} done={$tools.done} total={$tools.finished ? undefined : $tools.total} />
    {:else}
      <Board {scene} bind:view {extent} {interaction} {overlays} selected={picked} />
      {#if menu !== null}
        <PieceMenu items={menu.items} at={menu.at} onPick={runMenu} onClose={() => (menu = null)} />
      {/if}
    {/if}
    {#if problem !== null}
      <div class="banner" role="alert">{problem} <button class="quiet" onclick={() => (problem = null)}>Dismiss</button></div>
    {/if}
  </div>
  {#if carded && draft !== null && catalog !== null && geometry !== null}
    <aside class="card-col" aria-label="Selected piece">
      <TerrainCard {draft} {catalog} {geometry} {api} {selected} terrainWords={snap.terrainWords} {send} {report} onClose={() => (selected = null)} />
    </aside>
  {/if}
</div>

<style>
  .setup { display: grid; grid-template-columns: 20rem minmax(0, 1fr); height: 100%; min-height: 0; }
  .setup.carded { grid-template-columns: 20rem minmax(0, 1fr) 20rem; }
  .tools, .card-col { min-height: 0; overflow-y: auto; background: var(--panel-solid); }
  .tools { border-right: 1px solid var(--rule); display: flex; flex-direction: column; }
  .card-col { border-left: 1px solid var(--rule); }
  .help { margin: auto 0 0; padding: var(--space-2) var(--space-3); font-size: .85rem; border-top: 1px solid var(--rule); }
  kbd { font-family: var(--font-num); font-size: .8em; padding: var(--space-2); border: 1px solid var(--rule); color: var(--text); }
  .stage { position: relative; min-height: 0; min-width: 0; background: var(--board-bg); }
  .pad { margin: var(--space-3); }
  .banner { position: absolute; top: var(--space-2); left: 50%; transform: translateX(-50%); max-width: min(36rem, calc(100% - 2 * var(--space-3)));
    display: flex; gap: var(--space-2); align-items: baseline; padding: var(--space-2) var(--space-2); background: var(--panel-solid); color: var(--text);
    border-left: 3px solid var(--failure); box-shadow: 0 2px 8px #0006; z-index: 4; }
  @media (max-width: 70rem) {
    .setup, .setup.carded { grid-template-columns: 20rem minmax(0, 1fr); }
    .card-col { position: absolute; right: 0; top: 0; bottom: 0; width: 20rem; z-index: 5; box-shadow: -4px 0 12px #0005; }
    .setup { position: relative; }
  }
</style>
