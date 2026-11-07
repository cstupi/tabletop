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
  } as const;
  export type HudIconName = keyof typeof HUD_ICONS;
</script>

<script lang="ts">
  let { name, size = 18, label }: { name: HudIconName; size?: number; label?: string } = $props();
</script>

<svg class="hud-icon" viewBox="0 0 32 32" width={size} height={size} role={label ? 'img' : undefined} aria-label={label} aria-hidden={label ? undefined : 'true'} focusable="false">
  <g class="glow">{#each HUD_ICONS[name] as d}<path {d} />{/each}</g>
  <g class="line">{#each HUD_ICONS[name] as d}<path {d} />{/each}</g>
</svg>

<style>
  .hud-icon { flex: none; display: block; fill: none; stroke: currentColor; stroke-linecap: round; stroke-linejoin: round; }
  .line { stroke-width: var(--icon-stroke, 2.5); }
  .glow { stroke-width: var(--icon-glow, 0); stroke-opacity: var(--icon-glow-opacity, 0); }
</style>
