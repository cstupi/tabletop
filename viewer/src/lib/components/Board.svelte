<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { Scene } from '../board/frame';
  import { draw } from '../board/draw';
  import { boardColoursOf, type BoardColours } from '../board/style';
  import type { Glow } from '../play/decisionBox';
  import type { BoardHit, BoardInteraction, FloatingLayer, Modifiers, Overlay } from '../board/interaction';
  import { pickAt } from '../board/pick';
  import { SLOP_PIXELS } from '../board/tablePick';
  import { IDLE_POINTER, step, type PointerEffect, type PointerInput, type PointerState } from '../board/pointer';
  import { type Point, type View, fit, fitBox, panBy, toScreen, toTable, zoomAt } from '../board/transform';

  let {
    scene, view = $bindable(null), extent, interaction, overlays = [], selected = new Set<string>(), models = 'bases',
    showGrid = true, showZones = true, showLabels = true, glow, marks, warned, dimmed, floating, above, resetAt = null,
  }: {
    scene: Scene;
    /** Where Reset view stands (its top right, from the board's top right), clear of panels over the board; default its corner. */
    resetAt?: { readonly top: number; readonly right: number } | null;
    /** The parent holds the view, so setup and play share one pan and zoom; null fits the extent. */
    view?: View | null;
    /** A box in table inches for fitBox (the table, widened to hold terrain beside it); default the table. */
    extent?: { minX: number; minY: number; maxX: number; maxY: number };
    interaction?: BoardInteraction;
    overlays?: readonly Overlay[];
    selected?: ReadonlySet<string>;
    models?: 'bases' | 'circles';
    showGrid?: boolean; showZones?: boolean; showLabels?: boolean;
    /** The open Ask's glow per model, and the labels over models (play only). */
    glow?: ((modelId: string, unitId: string) => Glow | null) | undefined;
    marks?: ReadonlyMap<string, string> | undefined;
    /** Models in breach, drawn in the warning colour. */
    warned?: ReadonlySet<string> | undefined;
    /** Models darkened by the sight check. */
    dimmed?: ReadonlySet<string> | undefined;
    /** What moves for a moment (the wound numbers), on a canvas of its own: animated only while it draws something. */
    floating?: FloatingLayer | undefined;
    /** Controls over the board at table points (the action buttons over the selected unit): given the table-to-screen map, the scale in
     *  pixels per inch and the board's size. */
    above?: Snippet<[(p: Point) => Point, number, { width: number; height: number }]> | undefined;
  } = $props();

  let canvas: HTMLCanvasElement | undefined = $state();
  let box = $state({ width: 800, height: 600 });
  let hovered: string | undefined = $state(undefined);
  let pointer: PointerState = IDLE_POINTER;
  // The theme's board colours, read again whenever <html data-theme> changes, so a theme switch redraws in the new one.
  const readColours = (): BoardColours => boardColoursOf((name) => getComputedStyle(document.documentElement).getPropertyValue(name));
  let colours: BoardColours = $state.raw(readColours());
  $effect(() => {
    const observer = new MutationObserver(() => { colours = readColours(); });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'class', 'style'] });
    return () => observer.disconnect();
  });
  let downMods: Modifiers = { shift: false, ctrl: false, alt: false };

  // A new table is a different table: the old pan and zoom mean nothing on it.
  $effect(() => { void scene.table.width; void scene.table.length; view = null; });

  const current = $derived(view ?? (extent === undefined ? fit(scene.table, box) : fitBox(extent, scene.table, box)));

  $effect(() => {
    if (canvas === undefined) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(box.width * dpr);
    canvas.height = Math.round(box.height * dpr);
    const ctx = canvas.getContext('2d')!;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    draw(ctx, scene, current, box, { selected, hovered, models, overlays, showGrid, showZones, showLabels, glow, marks, warned, dimmed, colours });
  });

  // The floating layer: one animation frame after another while it draws something, none while idle; the board below is not redrawn.
  let floatCanvas: HTMLCanvasElement | undefined = $state();
  $effect(() => {
    const layer = floating, top = floatCanvas;
    if (layer === undefined || top === undefined) return;
    let frame = 0;
    const tick = (now: number): void => {
      const dpr = window.devicePixelRatio || 1, w = Math.round(box.width * dpr), h = Math.round(box.height * dpr);
      if (top.width !== w || top.height !== h) { top.width = w; top.height = h; }
      const ctx = top.getContext('2d')!;
      ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.clearRect(0, 0, w, h); ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      const view = current, table = scene.table;
      frame = layer.draw(ctx, (p) => toScreen(view, table, p), view.scale, now) ? requestAnimationFrame(tick) : 0;
    };
    const stop = layer.onStart(() => { if (frame === 0) frame = requestAnimationFrame(tick); });
    return () => { stop(); if (frame !== 0) cancelAnimationFrame(frame); };
  });

  $effect(() => {
    if (canvas === undefined) return;
    const observer = new ResizeObserver(([entry]) => {
      const rect = entry!.contentRect;
      box = { width: Math.max(1, rect.width), height: Math.max(1, rect.height) };
    });
    observer.observe(canvas);
    return () => observer.disconnect();
  });

  function local(event: PointerEvent | WheelEvent | MouseEvent): Point {
    const rect = canvas!.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }
  const modsOf = (event: { shiftKey: boolean; ctrlKey: boolean; altKey: boolean }): Modifiers =>
    ({ shift: event.shiftKey, ctrl: event.ctrlKey || false, alt: event.altKey });
  const pick = (at: Point): BoardHit | null => interaction?.pick?.(at) ?? pickAt(scene, at, models, SLOP_PIXELS / current.scale);   // a click beside a model picks it

  function apply(effects: readonly PointerEffect[]): void {
    for (const effect of effects) {
      switch (effect.type) {
        case 'hover':
          hovered = effect.hit?.kind === 'model' ? effect.hit.id : undefined;
          interaction?.onHover?.(effect.hit, effect.at);
          break;
        case 'select': interaction?.onSelect?.(effect.hit, effect.mods, effect.at); break;
        case 'dragStart': break;                                  // taken in `takesDrag`, which is onDragStart itself
        case 'dragMove': interaction?.onDragMove?.(effect.at, effect.mods); break;
        case 'dragEnd': interaction?.onDragEnd?.(effect.at, effect.mods); break;
        case 'dragCancel': interaction?.onDragCancel?.(); break;
        case 'boxMove': interaction?.onBoxMove?.(effect.from, effect.at); break;
        case 'boxEnd': interaction?.onBoxEnd?.(effect.from, effect.at); break;
        case 'boxCancel': interaction?.onBoxCancel?.(); break;
        case 'pan': view = panBy(current, effect.dx, effect.dy); break;
        case 'context': interaction?.onContextMenu?.(effect.hit, effect.at, effect.screen, downMods); break;
      }
    }
  }

  function feed(input: PointerInput): void {
    const result = step(pointer, input, pick, (hit, at, mods) => interaction?.onDragStart?.(hit, at, mods) ?? false,
      (at, mods) => interaction?.onBoxStart?.(at, mods) ?? false);
    pointer = result.state;
    apply(result.effects);
  }

  const inputAt = (event: PointerEvent) => {
    const screen = local(event);
    return { screen, at: toTable(current, scene.table, screen), mods: modsOf(event) };
  };
</script>

<div class="board">
  <canvas
    bind:this={canvas}
    tabindex="0"
    aria-label="The table"
    onwheel={(e) => { e.preventDefault(); view = zoomAt(current, scene.table, local(e), e.deltaY < 0 ? 1.15 : 1 / 1.15); }}
    onpointerdown={(e) => {
      if (e.button !== 0 && e.button !== 2) return;
      canvas!.focus();
      downMods = modsOf(e);
      canvas!.setPointerCapture(e.pointerId);
      feed({ type: 'down', button: e.button, ...inputAt(e) });
    }}
    onpointermove={(e) => feed({ type: 'move', ...inputAt(e) })}
    onpointerup={(e) => {
      if (canvas!.hasPointerCapture(e.pointerId)) canvas!.releasePointerCapture(e.pointerId);
      feed({ type: 'up', ...inputAt(e) });
    }}
    onpointercancel={() => feed({ type: 'cancel' })}
    onlostpointercapture={() => { if (pointer.kind === 'dragging' || pointer.kind === 'boxing') feed({ type: 'cancel' }); }}
    onpointerleave={() => { hovered = undefined; }}
    oncontextmenu={(e) => e.preventDefault()}
    ondragover={(e) => { if (e.dataTransfer !== null && interaction?.acceptsDrop?.([...e.dataTransfer.types]) === true) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } }}
    ondrop={(e) => {
      if (e.dataTransfer === null || interaction?.acceptsDrop?.([...e.dataTransfer.types]) !== true) return;
      e.preventDefault();
      interaction.onDrop?.(e.dataTransfer, toTable(current, scene.table, local(e)));
    }}
    onkeydown={(e) => {
      if (e.key === 'Escape' && (pointer.kind === 'dragging' || pointer.kind === 'boxing')) { e.preventDefault(); feed({ type: 'cancel' }); return; }
      if (interaction?.onKey?.(e.key, modsOf(e)) === true) e.preventDefault();
    }}
  ></canvas>
  {#if floating !== undefined}<canvas class="floating" bind:this={floatCanvas} aria-hidden="true"></canvas>{/if}
  {@render above?.((p) => toScreen(current, scene.table, p), current.scale, box)}
  <button class="small reset" title="Fit the table to the window" style:top={resetAt === null ? null : `${resetAt.top}px`} style:right={resetAt === null ? null : `${resetAt.right}px`} onclick={() => (view = null)}>Reset view</button>
</div>

<style>
  .board { position: relative; width: 100%; height: 100%; }
  canvas { display: block; width: 100%; height: 100%; touch-action: none; cursor: crosshair; outline: none; }
  canvas:focus-visible { box-shadow: inset 0 0 0 1px var(--muted); }
  canvas.floating { position: absolute; inset: 0; pointer-events: none; cursor: default; }
  .reset { position: absolute; top: var(--space-2); right: var(--space-2); z-index: 1; }
</style>
