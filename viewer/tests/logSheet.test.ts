// logSheet.ts beyond the agreement fixture: the table, the modifiers' signs, and what the panel's text holds for a copy.
import { readFileSync } from 'node:fs';
import { render } from 'svelte/server';
import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import CombatLogPanel from '../src/lib/play/CombatLogPanel.svelte';
import { build } from '../src/lib/play/combatLog';
import { sheetOf, sheetText, modifierText, table } from '../src/lib/play/logSheet';
import { Names } from '../src/lib/play/names';
import { RuleTexts } from '../src/lib/play/ruleTexts';
import type { Board, GameEvent, GameInfo } from '../src/lib/protocol/wire.generated';

interface Case { name: string; game: GameInfo; board: Board | null; events: GameEvent[] }
const cases = JSON.parse(readFileSync('tests/fixtures/combatlog.json', 'utf8')) as Case[];

function entriesOf(name: string) {
  const c = cases.find((x) => x.name === name)!;
  const a = new Applier(c.board!, c.game);
  a.applyAll(c.events);
  return build(a.log, new Names(c.game, a.projection));
}

/** The text a selection of the whole <pre> copies: its text content, tags dropped and entities read. */
function preText(html: string): string {
  const pre = /<pre[^>]*>([\s\S]*)<\/pre>/.exec(html)![1]!;
  return pre.replace(/<!--[\s\S]*?-->/g, '').replace(/<[^>]+>/g, '').replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"').replaceAll('&#39;', "'").replaceAll('&amp;', '&');
}

describe('logSheet', () => {
  it('a table is the faces 1–6 over their counts, two wide, wider past 99', () => {
    expect(table([1, 6, 6, 3])).toEqual([' 1  2  3  4  5  6', ' 1  0  1  0  0  2']);
    expect(table([...Array<number>(100).fill(1), 6])).toEqual(['  1   2   3   4   5   6', '100   0   0   0   0   1']);
  });

  it('modifiers read with the sign the roll applies', () => {
    expect(modifierText({ source: 'AP', amount: -1 })).toBe('AP(1) -1');
    expect(modifierText({ source: 'AP', amount: -4, via: 'Rending' })).toBe('Rending AP(4) -4');
    expect(modifierText({ source: 'cover', amount: 1 })).toBe('Cover +1');
  });

  it('an attack in the summary is one line of hits, wounds and cover', () => {
    const lines = sheetOf(entriesOf('worked'), false).flatMap((p) => p.lines);
    expect(lines).toContain('Marksmen → Blue unit 1  H: 2  W: 2  Cover: Y');
  });

  for (const detailed of [false, true])
    it(`the panel's text is the log's text, a line each, and nothing else (${detailed ? 'detailed' : 'summary'})`, () => {
      const entries = entriesOf('spears');
      const { body } = render(CombatLogPanel, {
        props: { entries, filter: 'all', detailed, pendingUndoTo: null, watching: false, rules: RuleTexts.empty, onFilter: () => {}, onUndoTo: () => {}, onTouch: () => {} },
      });
      expect(preText(body)).toBe(`${sheetText(sheetOf(entries, detailed))}\n`);
    });
});
