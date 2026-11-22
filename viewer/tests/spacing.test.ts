// Spacing rules (docs/superpowers/specs/2026-11-21-spacing-rules-design.md): every padding, margin and gap names a step of the
// scale, var(--space-1) … var(--space-6) (web/src/themes.css, which src/main.ts imports). 0, auto and the CSS keywords stay; anything else is a defect.
// var(--frame-inset) is the Fantasy panel's drawn band, a border a padding adds to, not a step.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const PROPERTY = /(?<![\w-])(?:style:)?(?:padding|margin|gap|row-gap|column-gap|scroll-padding|scroll-margin)(?:-(?:top|right|bottom|left|inline|block)(?:-(?:start|end))?)?\s*(?::|="?)\s*([^;}"\n]*)/g;

function sources(path: string): string[] {
  if (statSync(path).isFile()) return /\.(css|svelte)$/.test(path) ? [path] : [];
  return readdirSync(path).flatMap((n) => sources(join(path, n)));
}

/** A file's style text: comments and Svelte scripts blanked, lines kept. */
function styleText(text: string): string {
  const blank = (s: string) => s.replace(/[^\n]/g, ' ');
  return text.replace(/<script[\s\S]*?<\/script>/g, blank).replace(/<!--[\s\S]*?-->/g, blank).replace(/\/\*[\s\S]*?\*\//g, blank);
}

/** True when a value is only steps, 0, keywords, env() and unitless calc() factors, or a lone -1px border overlap. */
function named(value: string): boolean {
  if (value.trim() === '-1px') return true; // a 1px border laid over its neighbour's, not a gap
  const rest = value
    .replace(/\{[^}]*\}/g, 'N')
    .replace(/var\(--(?:space-[1-6]|frame-inset)\)/g, ' ')
    .replace(/env\([^)]*\)/g, ' ')
    .replace(/[*/]\s*-?(?:\d+(?:\.\d+)?|N)(?![\w%])/g, ' ')
    .replace(/(?<![\w.])-?(?:\d+(?:\.\d+)?|N)\s*\*/g, ' ')
    .replace(/(?<![\w.-])-?0(?![\w.%])/g, ' ');
  return !/\d|var\(|N\w/.test(rest);
}

/** Each spacing declaration in a file that is not named, as `file:line declaration`. */
export function literals(file: string): string[] {
  const text = styleText(readFileSync(file, 'utf8'));
  return [...text.matchAll(PROPERTY)]
    .filter((m) => !named(m[1] ?? ''))
    .map((m) => `${file}:${text.slice(0, m.index).split('\n').length} ${m[0].trim()}`);
}

describe('spacing', () => {
  it('names a step for 0-free values and passes 0, auto and steps', () => {
    expect(named('var(--space-2) var(--space-3)')).toBe(true);
    expect(named('0 auto')).toBe(true);
    expect(named('calc(var(--space-3) * {depth})')).toBe(true);
    expect(named('calc(-1 * var(--space-2))')).toBe(true);
    expect(named('calc(var(--space-4) + var(--frame-inset))')).toBe(true);
    expect(named('-1px')).toBe(true);
    expect(named('-2px')).toBe(false);
    expect(named('12px')).toBe(false);
    expect(named('var(--space-2) .5rem')).toBe(false);
    expect(named('{depth * 12}px')).toBe(false);
    expect(named('var(--s2)')).toBe(false);
    expect(named('calc(var(--space-3) + 4px)')).toBe(false);
  });

  it('every padding, margin and gap in src names a step', () => {
    const files = sources('src');
    expect(files.length).toBeGreaterThan(10);
    expect(files.flatMap(literals).join('\n')).toBe('');
  });
});
