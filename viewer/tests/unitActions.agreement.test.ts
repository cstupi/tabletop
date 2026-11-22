// The unit actions agreement: tests/fixtures/unitActions.json is written by tests/Tabletop.Client.Tests/UnitActionsViewerFixtureTests.cs from
// Godot's UnitActionsView over recorded logs. Each case is computed again through the browser's port and must agree.
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import { acting, teamsOf } from '../src/lib/lobby/seats';
import { actionTip } from '../src/lib/play/cardActions';
import { tipOf, unitActionsOf, type UnitActions } from '../src/lib/play/unitActionsView';
import type { Board, GameEvent, GameInfo } from '../src/lib/protocol/wire.generated';

interface Case {
  log: string; upTo: number; participantId: string; selected: string; watching: boolean;
  actions: { askId: string; unitId: string; anchor: [number, number, number];
    options: { optionId: string; key: number; word: string; icon: string | null; tip: string }[];
    foot: { id: string; word: string; icon: string | null; primary: boolean; key: number; tip: string }[];
    targets: { unitId: string; anchor: [number, number, number] }[] } | null;
}

const fixture = JSON.parse(readFileSync('tests/fixtures/unitActions.json', 'utf8')) as { cases: Case[] };
const read = <T>(name: string, s: string): T => JSON.parse(readFileSync(`tests/fixtures/${name}.${s}.json`, 'utf8')) as T;

function project(c: Case) {
  const game = read<{ game: GameInfo }>(c.log, 'welcome').game;
  const applier = new Applier(read<{ board: Board }>(c.log, 'board').board, game);
  applier.applyAll(read<{ events: GameEvent[] }>(c.log, 'events').events.filter((e) => e.seq <= c.upTo));
  return { game, p: applier.projection, log: applier.log };
}

const shape = (v: UnitActions | null) => v === null ? null : {
  askId: v.askId, unitId: v.unitId,
  options: v.options.map((o) => ({ optionId: o.optionId, key: o.key, word: o.word, icon: o.icon, tip: tipOf(o) })),
  foot: v.actions.map((a) => ({ id: a.id, word: a.word, icon: a.icon, primary: a.primary, key: a.key, tip: actionTip(a) })),
  targets: v.targets.map((t) => ({ unitId: t.unitId, anchor: [t.anchor.x, t.anchor.y, t.anchor.z].map((n) => Math.round(n * 1e9)) })),
};

describe.each(fixture.cases.map((c, i) => ({ ...c, i })))('unit actions agreement', (c) => {
  it(`${c.log}@${c.upTo} #${c.i}${c.watching ? ' watching' : ''}`, () => {
    const { game, p, log } = project(c);
    const team = acting(p, teamsOf(game, c.participantId), log);
    const v = unitActionsOf(p, game, team, c.selected, c.watching);
    expect(shape(v)).toEqual(c.actions === null ? null : {
      askId: c.actions.askId, unitId: c.actions.unitId, options: c.actions.options, foot: c.actions.foot,
      targets: c.actions.targets.map((t) => ({ unitId: t.unitId, anchor: t.anchor.map((n) => Math.round(n * 1e9)) })),
    });
    if (v !== null && c.actions !== null) {
      const [x, y, z] = c.actions.anchor;
      expect([v.anchor.x, v.anchor.y, v.anchor.z].map((n) => Math.round(n * 1e9))).toEqual([x, y, z].map((n) => Math.round(n * 1e9)));
    }
  });
});

it('the fixture shows buttons and hides them', () => {
  expect(fixture.cases.some((c) => c.actions !== null)).toBe(true);
  expect(fixture.cases.some((c) => c.actions === null && !c.watching)).toBe(true);
});
