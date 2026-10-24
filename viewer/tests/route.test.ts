import { describe, expect, it } from 'vitest';
import { hrefOf, parseRoute, tabsOf, watchUrl, type Route } from '../src/lib/shell/route';

const id = '0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b';
const id2 = '0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c';
const home: Route = { page: 'games' };

describe('routes', () => {
  it('parses each page and falls back to home', () => {
    expect(parseRoute('#/games', { page: 'watch' })).toEqual({ page: 'games' });
    expect(parseRoute(`#/game/${id}`, home)).toEqual({ page: 'game', gameId: id });
    expect(parseRoute(`#/replay/${id}`, home)).toEqual({ page: 'replay', gameId: id });
    expect(parseRoute('#/watch', home)).toEqual({ page: 'watch' });
    expect(parseRoute('#/tests', home)).toEqual({ page: 'tests' });
    expect(parseRoute('#/game/notauuid', { page: 'watch' })).toEqual({ page: 'watch' });
    expect(parseRoute('', { page: 'watch' })).toEqual({ page: 'watch' });
    expect(parseRoute('#/nowhere', home)).toEqual(home);
  });

  it('a game route carries an optional army', () => {
    expect(parseRoute(`#/game/${id}?army=${id2}`, home)).toEqual({ page: 'game', gameId: id, army: id2 });
    expect(parseRoute(`#/game/${id}?army=nope`, home)).toEqual({ page: 'game', gameId: id });
    expect(hrefOf({ page: 'game', gameId: id, army: id2 })).toBe(`#/game/${id}?army=${id2}`);
  });

  it('hrefOf round-trips through parseRoute', () => {
    const routes: Route[] = [
      { page: 'games' }, { page: 'game', gameId: id }, { page: 'game', gameId: id, army: id2 },
      { page: 'replay', gameId: id }, { page: 'watch' }, { page: 'tests' },
    ];
    for (const r of routes) expect(parseRoute(hrefOf(r), { page: 'watch' })).toEqual(r);
    expect(hrefOf({ page: 'game', gameId: id })).toBe(`#/game/${id}`);
  });

  it('players see games, admins games and watch, the developer build adds tests', () => {
    expect(tabsOf('player', false)).toEqual(['games']);
    expect(tabsOf('admin', false)).toEqual(['games', 'watch']);
    expect(tabsOf('admin', true)).toEqual(['games', 'watch', 'tests']);
    expect(tabsOf('signed-out', true)).toEqual(['games', 'watch', 'tests']);
  });

  it('watchUrl adds watch=1', () => {
    expect(watchUrl('wss://h/ws')).toBe('wss://h/ws?watch=1');
    expect(watchUrl('ws://h:5200/ws?x=1')).toBe('ws://h:5200/ws?x=1&watch=1');
  });
});
