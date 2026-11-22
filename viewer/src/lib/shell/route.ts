// viewer/src/lib/shell/route.ts
// The shell's hash routes. A hash route keeps the hosted build a static app under /viewer/ and survives a reload.

import type { Access } from '../hosted/hosted';

export type Route =
  | { readonly page: 'games' }
  | { readonly page: 'game'; readonly gameId: string }
  | { readonly page: 'replay'; readonly gameId: string }
  | { readonly page: 'watch' }
  | { readonly page: 'tests' };

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** `#/games`, `#/game/<uuid>`, `#/replay/<uuid>`, `#/watch`, `#/tests`; anything else is the build's home. */
export function parseRoute(hash: string, home: Route): Route {
  const [path = ''] = hash.replace(/^#\/?/, '').split('?', 2);
  const [page, id] = path.split('/');
  if (id === undefined) {
    if (page === 'games' || page === 'watch' || page === 'tests') return { page };
    return home;
  }
  if (!UUID.test(id)) return home;
  if (page === 'replay' || page === 'game') return { page, gameId: id };
  return home;
}

export function hrefOf(route: Route): string {
  switch (route.page) {
    case 'game': return `#/game/${route.gameId}`;
    case 'replay': return `#/replay/${route.gameId}`;
    default: return `#/${route.page}`;
  }
}

/** The tabs a viewer sees: players games; admins games and watch; the developer build adds tests (and watch, which holds its sign-in). */
export type Tab = 'games' | 'watch' | 'tests';
export function tabsOf(access: Access | 'checking', runner: boolean): readonly Tab[] {
  const pages: Tab[] = access === 'admin' || runner ? ['games', 'watch'] : ['games'];
  if (runner) pages.push('tests');
  return pages;
}

/** The socket a replay opens: the same URL with `watch=1` (ConnectionHandshake: read-only, never replaces a play socket). */
export function watchUrl(socketUrl: string): string {
  return `${socketUrl}${socketUrl.includes('?') ? '&' : '?'}watch=1`;
}
