<script lang="ts">
  import { ICONS, type IconName } from './icons';
  // Decorative unless given a label; a control that shows one still carries its own text or aria-label.
  let { name, label, size = '1.25em' }: { name: IconName; label?: string; size?: string } = $props();
</script>

<svg class="icon" viewBox="0 0 32 32" width={size} height={size} role={label ? 'img' : undefined} aria-label={label} aria-hidden={label ? undefined : 'true'} focusable="false">
  <g class="glow">{#each ICONS[name] as d}<path {d} />{/each}</g>
  <g class="line">{#each ICONS[name] as d}<path {d} />{/each}</g>
</svg>
