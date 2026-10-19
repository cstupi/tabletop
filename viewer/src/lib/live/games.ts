// viewer/src/lib/live/games.ts
// The source picker's game list: GET /v1/games with the session JWT (admin watch spec → The viewer).
// An admin watches any game it lists; a non-admin is refused at connect unless it plays in it.

import { ManagementError } from './management';

/** management's `GameSummary` (server/management/Games/GameDtos.cs), as the browser receives it. */
export type GameSummary = {
  readonly id: string;
  readonly name: string;
  readonly status: string;
  readonly createdBy: string;
  readonly createdAt: string;
  readonly participants: number;
  readonly participantNames: readonly string[];
};

/** Management leaves `next` out of the last page. */
type Page = { readonly items: readonly GameSummary[]; readonly next?: string | null };

/** One row of the list as the picker shows it. */
export type GameRow = { readonly id: string; readonly name: string; readonly status: string; readonly players: string; readonly created: string };

const defaultBase = (): string => import.meta.env.VITE_API_BASE ?? '';

/** Every page of the keyset, newest game first. */
export async function listGames(sessionJwt: string, base: string = defaultBase(), fetchFn: typeof fetch = fetch): Promise<GameSummary[]> {
  const games: GameSummary[] = [];
  let after: string | null = null;
  do {
    const path: string = `/v1/games?limit=200${after === null ? '' : `&after=${after}`}`;
    const response = await fetchFn(`${base}${path}`, { headers: { authorization: `Bearer ${sessionJwt}` } });
    if (!response.ok) {
      const problem = (await response.json().catch(() => ({}))) as { title?: string; detail?: string };
      throw new ManagementError(problem.detail ?? problem.title ?? `${path} answered ${response.status}`, response.status);
    }
    const page = (await response.json()) as Page;
    games.push(...page.items);
    after = page.next ?? null;
  } while (after !== null);
  return games;
}

/** Created time in UTC, to the minute. */
export function rowOf(game: GameSummary): GameRow {
  return {
    id: game.id, name: game.name, status: game.status,
    players: game.participantNames.length === 0 ? 'nobody' : game.participantNames.join(', '),
    created: game.createdAt.slice(0, 16).replace('T', ' '),
  };
}
