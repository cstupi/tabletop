<!-- viewer/src/lib/play/HudIcon.svelte -->
<!-- The play Hud's icons, the paths of Godot's client/content/themes/*/icons/<name>.svg, drawn in currentColor: Fantasy one heavier line,
     Sci-fi a translucent glow under a thinner one (the website's --icon-* tokens). -->
<script lang="ts" module>
  export const HUD_ICONS = {
    round: ['M25.5 12 A10.5 10.5 0 1 0 26.5 19', 'M26.5 5 V12 H19.5'],
    turn: ['M10 6 L25 16 L10 26 Z'],
    wait: ['M9 4 H23 M9 28 H23', 'M11 4 C11 11 21 12 21 16 C21 20 11 21 11 28 M21 4 C21 11 11 12 11 16 C11 20 21 21 21 28'],
    activation: ['M18 3 L8 18 H16 L14 29 L24 13 H16 Z'],
    hold: ['M16 4 L26 8 V15 C26 22 21 26 16 28 C11 26 6 22 6 15 V8 Z', 'M13 11 V21 M19 11 V21'],
    advance: ['M5 16 H25 M18 9 L25 16 L18 23'],
    rush: ['M5 8 L13 16 L5 24 M14 8 L22 16 L14 24', 'M24 10 L28 16 L24 22'],
    charge: ['M5 5 L21 21 M20 16 L16 20 M22 22 L27 27', 'M27 5 L11 21 M12 16 L16 20 M10 22 L5 27'],
    quality: ['M6 16 a10 10 0 1 0 20 0 a10 10 0 1 0 -20 0', 'M12 16 a4 4 0 1 0 8 0 a4 4 0 1 0 -8 0', 'M16 2 V8 M16 24 V30 M2 16 H8 M24 16 H30'],
    defense: ['M16 4 L26 8 V15 C26 22 21 26 16 28 C11 26 6 22 6 15 V8 Z', 'M16 9 V23'],
    dice: ['M9 5 H23 A4 4 0 0 1 27 9 V23 A4 4 0 0 1 23 27 H9 A4 4 0 0 1 5 23 V9 A4 4 0 0 1 9 5 Z', 'M11 11 h0.01 M16 16 h0.01 M21 21 h0.01'],
    log: ['M7 8 H25 M7 14 H25 M7 20 H25 M7 26 H18'],
    undo: ['M9 13 H20 A6 6 0 0 1 20 25 H12', 'M14 8 L9 13 L14 18'],
    copy: ['M13 10 H24 A2 2 0 0 1 26 12 V26 A2 2 0 0 1 24 28 H13 A2 2 0 0 1 11 26 V12 A2 2 0 0 1 13 10 Z', 'M7 22 V7 A2 2 0 0 1 9 5 H20'],
    home: ['M5 15 L16 5 L27 15', 'M8 13 V27 H24 V13', 'M13 27 V19 H19 V27'],
    team: ['M8 29V4', 'M8 5h17l-4 6 4 6H8'],
    player: ['M10.5 10 a5.5 5.5 0 1 0 11 0 a5.5 5.5 0 1 0 -11 0', 'M5.5 28c0-6 4.7-10 10.5-10s10.5 4 10.5 10'],
    watch: ['M2.5 16S7.5 7 16 7s13.5 9 13.5 9-5 9-13.5 9S2.5 16 2.5 16z', 'M11.5 16 a4.5 4.5 0 1 0 9 0 a4.5 4.5 0 1 0 -9 0'],
    activated: ['M3.5 16 a12.5 12.5 0 1 0 25 0 a12.5 12.5 0 1 0 -25 0', 'M10 16.5l4 4 8-9'],
    'skip-back': ['M8 7 V25'],
    'step-back': [],
    step: [],
    skip: ['M24 7 V25'],
    play: [],
    pause: ['M11 7 V25 M21 7 V25'],
    replay: ['M6 16a10 10 0 1 0 3-7.1', 'M6 4v6h6'],
    key: ['M6 9 H26 A2 2 0 0 1 28 11 V21 A2 2 0 0 1 26 23 H6 A2 2 0 0 1 4 21 V11 A2 2 0 0 1 6 9 Z', 'M8.5 13.5 H9.5 M13.5 13.5 H14.5 M18.5 13.5 H19.5 M23 13.5 H23.5 M10 18.5 H22'],
    camera: ['M5 11 H10 L12 7 H20 L22 11 H27 A1 1 0 0 1 28 12 V25 A1 1 0 0 1 27 26 H5 A1 1 0 0 1 4 25 V12 A1 1 0 0 1 5 11 Z', 'M16 13 A5 5 0 1 1 15.99 13 Z'],
    measure: ['M3 22 L22 3 L29 10 L10 29 Z', 'M8 17 L11 20 M12 13 L14 15 M16 9 L19 12 M20 5 L22 7'],
    table: ['M3 11 H29 V15 H3 Z', 'M7 15 V27 M25 15 V27'],
    cover: ['M16 4 L26 8 V16 C26 22 21 26 16 28 C11 26 6 22 6 16 V8 Z', 'M16 9 V23'],
    difficult: ['M3 11 Q7 7 11 11 T19 11 T27 11', 'M3 18 Q7 14 11 18 T19 18 T27 18', 'M3 25 Q7 21 11 25 T19 25 T27 25'],
    dangerous: ['M3 27 H29', 'M5 27 L9 11 L13 27', 'M12 27 L16 5 L20 27', 'M19 27 L23 13 L27 27'],
    unstable: ['M3 27 H29', 'M5 27 V20 H13 V27', 'M15 27 V17 H25 V27', 'M19 4 L16 9 L20 11 L17 15'],
    steep: ['M3 27 H29 V7 Z', 'M8 15 L15 8 M15 8 H10 M15 8 V13'],
    impassable: ['M16 4 A12 12 0 1 1 15.99 4 Z', 'M7.5 7.5 L24.5 24.5'],
    'blocks-sight': ['M3 16 C8 8 24 8 29 16 C24 24 8 24 3 16 Z', 'M16 12 A4 4 0 1 1 15.99 12 Z', 'M5 28 L27 4'],
    'see-in-out': ['M3 16 C8 8 24 8 29 16 C24 24 8 24 3 16 Z', 'M16 12 A4 4 0 1 1 15.99 12 Z'],
    'open-ground': ['M3 23 H29', 'M7 23 L9 18 M13 23 L14 17 M19 23 L21 18 M25 23 L26 19'],
    lock: ['M8 14 H24 V27 H8 Z', 'M11 14 V10 A5 5 0 0 1 21 10 V14', 'M16 19 V22'],
    unlock: ['M8 14 H24 V27 H8 Z', 'M11 14 V9 A5 5 0 0 1 21 7', 'M16 19 V22'],
    remove: ['M6 9 H26', 'M13 9 V5 H19 V9', 'M9 9 L10 27 H22 L23 9', 'M14 13 V23 M18 13 V23'],
  } as const;
  export type HudIconName = keyof typeof HUD_ICONS;
  /** The filled shapes of the icons that have them, drawn in currentColor over the strokes. */
  export const HUD_FILLS: Partial<Record<HudIconName, readonly string[]>> = {
    'skip-back': ['M25 7 L12 16 L25 25 Z'],
    'step-back': ['M22 7 L10 16 L22 25 Z'],
    step: ['M10 7 L22 16 L10 25 Z'],
    skip: ['M7 7 L20 16 L7 25 Z'],
    play: ['M10 6 L26 16 L10 26 Z'],
    replay: ['M14 11.5v9l7-4.5z'],
  };
</script>

<script lang="ts">
  let { name, size = 18, label }: { name: HudIconName | (string & {}); size?: number; label?: string } = $props();
  const strokes = $derived<readonly string[]>(HUD_ICONS[name as HudIconName] ?? []);
  const fills = $derived<readonly string[]>(HUD_FILLS[name as HudIconName] ?? []);
</script>

<svg class="hud-icon" viewBox="0 0 32 32" width={size} height={size} role={label ? 'img' : undefined} aria-label={label} aria-hidden={label ? undefined : 'true'} focusable="false">
  <g class="glow">{#each [...strokes, ...fills] as d}<path {d} />{/each}</g>
  <g class="line">{#each strokes as d}<path {d} />{/each}{#each fills as d}<path {d} class="fill" />{/each}</g>
</svg>

<style>
  .hud-icon { flex: none; display: block; fill: none; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; }
  .line { stroke-width: var(--icon-stroke, 2.5); }
  .line .fill { fill: currentColor; stroke-width: 1.5; }
  .glow { stroke-width: var(--icon-glow, 0); stroke-opacity: var(--icon-glow-opacity, 0); }
</style>
