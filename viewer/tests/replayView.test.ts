import { describe, expect, it } from 'vitest';
import { RuleTexts } from '../src/lib/play/ruleTexts';
import { ReplayTimeline } from '../src/lib/replay/replayTimeline';
import { replayCardOf, replayHeaderOf, terrainNamer, unitSelection } from '../src/lib/replay/replayView';
import { parseCatalog } from '../src/lib/setup/catalog';
import packed from '../../client/content/builtin/catalog.json';
import { loadFixture } from './fixture';

describe('the replay page at a position', () => {
  const { board, game, events } = loadFixture('skirmish');

  it('the top bar reads "Before round 1" at the start, then the round and the turn, each team with its score', () => {
    const t = new ReplayTimeline(board, game, () => events);
    const start = replayHeaderOf(t.projection, game);
    expect(start.topLine).toBe('Before round 1');
    expect(start.teams.map((x) => [x.name, x.score])).toEqual([['Team 1', 0], ['Team 2', 0]]);
    expect(start.result).toBeNull();
    t.seek(t.next('round'));
    t.seek(t.next('round'));
    t.seek(t.next('turn'));
    expect(replayHeaderOf(t.projection, game).topLine).toMatch(/^Round 1 · Team \d's turn$/);
  });

  it('a model click selects its unit, and the card shows that unit by name', () => {
    const t = new ReplayTimeline(board, game, () => events);
    t.seek(events.length);
    const model = [...t.projection.models.values()][0]!;
    const picked = unitSelection(t.projection, model.id)!;
    expect(picked.unitId).toBe(model.unitId);
    expect(picked.modelIds.has(model.id)).toBe(true);
    const card = replayCardOf(t.projection, game, picked.unitId, RuleTexts.empty, new Map())!;
    expect(card.sheet.name).toBe(t.projection.units.get(model.unitId)!.definition.name);
    expect(card.team).toBeGreaterThan(0);
    expect(replayCardOf(t.projection, game, null, RuleTexts.empty, new Map())).toBeNull();
    expect(unitSelection(t.projection, 'nobody')).toBeNull();
  });

  it('names terrain from the catalog, never by id', () => {
    const name = terrainNamer(parseCatalog(packed));
    const piece = { id: 'x', mesh: { builtin: 'ruined-wall' }, position: { x: 0, y: 0, z: 0 }, yaw: { degrees: 0 }, scale: 1, volumes: [] };
    expect(name(piece)).toBe('Ruined Wall');
    expect(name({ ...piece, mesh: { assetId: '0198b7ac-0001-7000-8000-000000000001' } })).toBe('Terrain');
    expect(terrainNamer(null)(piece)).toBe('Terrain');
  });
});
