// The combat log's agreement with Godot's: tests/fixtures/combatlog.json is written by
// tests/Tabletop.Client.Tests/CombatLogViewerFixtureTests.cs (Godot's CombatLog over three recorded logs). A difference is fixed in the
// TypeScript port, never in the fixture.
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import { build, flatten, latest, pip } from '../src/lib/play/combatLog';
import { Names } from '../src/lib/play/names';
import { chain, collapsed, outcome, result, title, type LogEntry } from '../src/lib/play/rollEntry';
import { undoPlanOf } from '../src/lib/play/undo';
import { sheetOf } from '../src/lib/play/logSheet';
import type { Board, GameEvent, GameInfo } from '../src/lib/protocol/wire.generated';

interface Row {
  depth: number; kind: 'block' | 'roll' | 'line'; seq: number; undone: boolean; text: string;
  title?: string; result?: string; outcome?: string; chain?: string; details?: string[];
}
interface Paragraph { seq: number; depth: number; entries: number; lines: string[] }
interface Case {
  name: string; game: GameInfo; board: Board | null; events: GameEvent[]; rows: Row[]; latest: string | null;
  undo: { toSeq: number; tooltip: string; lostRolls: string[] } | null; sheet: Paragraph[]; sheetDetailed: Paragraph[];
}

const cases = JSON.parse(readFileSync('tests/fixtures/combatlog.json', 'utf8')) as Case[];

function rowsOf(entries: readonly LogEntry[], depth = 0): Row[] {
  return entries.flatMap((e): Row[] => {
    switch (e.type) {
      case 'block': return [{ depth, kind: 'block', seq: e.seq, undone: e.undone, text: e.header, chain: e.chainParts.join(' → ') }, ...rowsOf(e.entries, depth + 1)];
      case 'roll': return [{
        depth, kind: 'roll', seq: e.seq, undone: e.undone, text: collapsed(e, pip), title: title(e), result: result(e), outcome: outcome(e),
        chain: chain(e), details: [...e.details],
      }];
      case 'line': return [{ depth, kind: 'line', seq: e.seq, undone: e.undone, text: e.text, ...(e.worked === null ? {} : { worked: [...e.worked] }) }];
    }
  });
}

describe.each(cases)('combat log agreement', (c) => {
  const projection = (() => {
    if (c.board === null) return null;
    const a = new Applier(c.board, c.game);
    a.applyAll(c.events);
    return a;
  })();
  const names = new Names(c.game, projection?.projection ?? null);
  const entries = build(projection?.log ?? c.events, names);

  it(`combatlog fixture ${c.name} renders as Godot does`, () => {
    expect(c.rows.length).toBeGreaterThan(0);
    expect(rowsOf(entries)).toEqual(c.rows);
    expect(flatten(entries).length).toBe(c.rows.length);
  });

  it(`combatlog fixture ${c.name} text agrees, summary and detailed`, () => {
    for (const [detailed, expected] of [[false, c.sheet], [true, c.sheetDetailed]] as const)
      expect(sheetOf(entries, detailed).map((p) => ({ seq: p.entry.seq, depth: p.depth, entries: p.entries.length, lines: [...p.lines] }))).toEqual(expected);
  });

  it(`combatlog fixture ${c.name} latest roll agrees`, () => {
    const l = latest(entries);
    expect(l === null ? null : collapsed(l, pip)).toBe(c.latest);
  });

  it(`combatlog fixture ${c.name} undo plan agrees`, () => {
    const plan = undoPlanOf(projection?.log ?? c.events, names, entries);
    expect(plan === null ? null : { toSeq: plan.toSeq, tooltip: plan.tooltip, lostRolls: [...plan.lostRolls] }).toEqual(c.undo);
  });
});
