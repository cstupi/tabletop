import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { TEAM_COLOURS } from '../src/lib/board/palette';

function hue(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255) as [number, number, number];
  const max = Math.max(r, g, b), d = max - Math.min(r, g, b);
  const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return (h * 60 + 360) % 360;
}

describe('the team colours', () => {
  it('none reads as the activated ring green in either theme', () => {
    const css = readFileSync(new URL('../src/app.css', import.meta.url), 'utf8');
    const activated = [...css.matchAll(/--board-activated:\s*(#[0-9A-Fa-f]{6})/g)].map((m) => m[1]!);
    expect(activated).toHaveLength(2);
    for (const green of activated) for (const team of TEAM_COLOURS) {
      const apart = Math.abs(hue(team) - hue(green));
      expect(Math.min(apart, 360 - apart), `${team} against ${green}`).toBeGreaterThanOrEqual(40);
    }
  });
});
