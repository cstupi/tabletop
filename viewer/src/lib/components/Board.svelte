<script lang="ts">
  import type { Scene } from '../board/frame';
  import { draw } from '../board/draw';
  import type { BoardHit, BoardInteraction, Modifiers, Overlay } from '../board/interaction';
  import { pickAt } from '../board/pick';
  import { IDLE_POINTER, step, type PointerEffect, type PointerInput, type PointerState } from '../board/pointer';
  import { type Point, type View, fit, fitBox, panBy, toTable, zoomAt } from '../board/transform';

  let {
    scene, view = $bindable(null), extent, interaction, overlays = [], selected = new Set<string>(), models = 'bases',
    showGrid = true, showZones = true, showLabels = true,
  }: {
    scene: Scene;
    /** The parent holds the view, so setup and play share one pan and zoom; null fits the extent. */
    view?: View | null;
    /** A box in table inches for fitBox (the table, widened to hold terrain beside it); default the table. */
    extent?: { minX: number; minY: number; maxX: number; maxY: number };
    interaction?: BoardInteraction;
    overlays?: readonly Overlay[];
    selected?: ReadonlySet<string>;
    models?: 'bases' | 'circles';
    showGrid?: boolean; showZones?: boolean; showLabels?: boolean;
  } = $props();

  let canvas: HTMLCanvasElement | undefined = $state();
  let box = $state({ width: 800, height: 600 });
  let hovered: string | undefined = $state(undefined);
  let pointer: PointerState = IDLE_POINTER;

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
    draw(ctx, scene, current, box, { selected, hovered, models, overlays, showGrid, showZones, showLabels });
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
  const pick = (at: Point): BoardHit | null => interaction?.pick?.(at) ?? pickAt(scene, at);

  function apply(effects: readonly PointerEffect[]): void {
    for (const effect of effects) {
      switch (effect.type) {
        case 'hover':
          hovered = effect.hit?.kind === 'model' ? effect.hit.id : undefined;
          interaction?.onHover?.(effect.hit, effect.at);
          break;
        case 'select': interaction?.onSelect?.(effect.hit, effect.mods); break;
        case 'dragStart': break;                                  // taken in `takesDrag`, which is onDragStart itself
        case 'dragMove': interaction?.onDragMove?.(effect.at, effect.mods); break;
        case 'dragEnd': interaction?.onDragEnd?.(effect.at, effect.mods); break;
        case 'dragCancel': interaction?.onDragCancel?.(); break;
        case 'pan': view = panBy(current, effect.dx, effect.dy); break;
        case 'context': interaction?.onContextMenu?.(effect.hit, effect.at, effect.screen); break;
      }
    }
  }

  function feed(input: PointerInput): void {
    const result = step(pointer, input, pick, (hit, at, mods) => interaction?.onDragStart?.(hit, at, mods) ?? false);
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
      canvas!.setPointerCapture(e.pointerId);
      feed({ type: 'down', button: e.button, ...inputAt(e) });
    }}
    onpointermove={(e) => feed({ type: 'move', ...inputAt(e) })}
    onpointerup={(e) => {
      if (canvas!.hasPointerCapture(e.pointerId)) canvas!.releasePointerCapture(e.pointerId);
      feed({ type: 'up', ...inputAt(e) });
    }}
    onpointercancel={() => feed({ type: 'cancel' })}
    onlostpointercapture={() => { if (pointer.kind === 'dragging') feed({ type: 'cancel' }); }}
    onpointerleave={() => { hovered = undefined; }}
    oncontextmenu={(e) => e.preventDefault()}
    onkeydown={(e) => {
      if (e.key === 'Escape' && pointer.kind === 'dragging') { e.preventDefault(); feed({ type: 'cancel' }); return; }
      if (interaction?.onKey?.(e.key, modsOf(e)) === true) e.preventDefault();
    }}
  ></canvas>
  <button onclick={() => (view = null)}>Reset view</button>
</div>

<style>
  .board { position: relative; width: 100%; height: 100%; }
  canvas { display: block; width: 100%; height: 100%; touch-action: none; cursor: crosshair; outline: none; }
  canvas:focus-visible { box-shadow: inset 0 0 0 1px var(--muted); }
  button { position: absolute; top: 0.5rem; right: 0.5rem; }
</style>
