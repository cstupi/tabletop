// viewer/src/lib/api/client.ts
// One function per management endpoint the browser plays through (client/src/Core/ManagementClient.cs). Every request
// sends the protocol version header and a fresh bearer; a problem+json answer becomes a ManagementError.

import { ManagementError } from '../live/management';
import { PROTOCOL_VERSION } from '../protocol/wire.generated';
import type { TableState, TerrainVolume } from '../protocol/wire.generated';
import type {
  ArmySummary, AssetDto, CreateGameRequest, GameInfo, GrantableEffectDto, LayoutSummary, Page, PropertiesSummary, RandomTable, RandomTableRequest, RuleTextDto,
} from './types';

export interface Api {
  createGame(request: CreateGameRequest): Promise<GameInfo>;
  joinGame(id: string): Promise<GameInfo>;
  leaveGame(id: string): Promise<void>;
  endGame(id: string): Promise<void>;
  deleteGame(id: string): Promise<void>;
  listProperties(): Promise<PropertiesSummary[]>;
  /** Every page of /v1/armies?limit=200. */
  listArmies(): Promise<ArmySummary[]>;
  /** GET /v1/builtin; null on 404 (nothing published: the packed catalog applies). */
  builtinCatalog(): Promise<unknown | null>;
  /** Every page of /v1/assets?kind=terrain&limit=200, uploaded assets only. */
  terrainAssets(): Promise<AssetDto[]>;
  /** GET /v1/assets/{id}/volumes. */
  assetVolumes(id: string): Promise<TerrainVolume[]>;
  listLayouts(): Promise<LayoutSummary[]>;
  getLayout(id: string): Promise<TableState>;
  saveLayout(table: TableState): Promise<TableState>;
  deleteLayout(id: string): Promise<void>;
  /** GET /v1/layouts/builtin: the predefined tables. */
  builtinLayouts(): Promise<TableState[]>;
  /** GET /v1/properties/{hash}/rules: the pinned set's rule texts. */
  ruleTexts(hash: string): Promise<RuleTextDto[]>;
  /** GET /v1/properties/{hash}/grants: the pinned set's grantable effects. */
  grantableEffects(hash: string): Promise<GrantableEffectDto[]>;
  /** POST /v1/layouts/random. */
  randomTable(request: RandomTableRequest): Promise<RandomTable>;
}

const defaultBase = (): string => import.meta.env.VITE_API_BASE ?? '';

export function apiOf(jwt: () => Promise<string>, base: string = defaultBase(), fetchFn: typeof fetch = fetch): Api {
  async function send(method: string, path: string, body?: unknown): Promise<Response> {
    const headers: Record<string, string> = {
      authorization: `Bearer ${await jwt()}`,
      'Tabletop-Client-Version': String(PROTOCOL_VERSION),
    };
    if (body !== undefined) headers['content-type'] = 'application/json';
    const response = await fetchFn(`${base}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.ok) {
      const problem = (await response.json().catch(() => ({}))) as { title?: string; detail?: string };
      throw new ManagementError(problem.detail ?? problem.title ?? `${path} answered ${response.status}`, response.status);
    }
    return response;
  }
  const maybe = async <T>(path: string): Promise<T | null> => {
    try { return await read<T>('GET', path); } catch (e) { if (e instanceof ManagementError && e.status === 404) return null; throw e; }
  };
  const read = async <T>(method: string, path: string, body?: unknown): Promise<T> => (await send(method, path, body)).json() as Promise<T>;
  const none = async (method: string, path: string): Promise<void> => { await send(method, path); };

  return {
    createGame: (request) => read('POST', '/v1/games', request),
    joinGame: (id) => read('POST', `/v1/games/${id}/join`),
    leaveGame: (id) => none('POST', `/v1/games/${id}/leave`),
    endGame: (id) => none('POST', `/v1/games/${id}/end`),
    deleteGame: (id) => none('DELETE', `/v1/games/${id}`),
    listProperties: () => read('GET', '/v1/properties'),
    ruleTexts: (hash) => read('GET', `/v1/properties/${hash}/rules`),
    grantableEffects: (hash) => read('GET', `/v1/properties/${hash}/grants`),
    builtinCatalog: () => maybe('/v1/builtin'),
    async terrainAssets() {
      const assets: AssetDto[] = [];
      let after: string | null = null;
      do {
        const page: Page<AssetDto> = await read('GET', `/v1/assets?kind=terrain&limit=200${after === null ? '' : `&after=${after}`}`);
        assets.push(...page.items.filter((a) => a.uploaded));
        after = page.next ?? null;
      } while (after !== null);
      return assets;
    },
    assetVolumes: (id) => read('GET', `/v1/assets/${id}/volumes`),
    listLayouts: () => read('GET', '/v1/layouts'),
    getLayout: (id) => read('GET', `/v1/layouts/${id}`),
    saveLayout: (table) => read('POST', '/v1/layouts', table),
    deleteLayout: (id) => none('DELETE', `/v1/layouts/${id}`),
    builtinLayouts: () => read('GET', '/v1/layouts/builtin'),
    randomTable: (request) => read('POST', '/v1/layouts/random', request),
    async listArmies() {
      const armies: ArmySummary[] = [];
      let after: string | null = null;
      do {
        const page: Page<ArmySummary> = await read('GET', `/v1/armies?limit=200${after === null ? '' : `&after=${after}`}`);
        armies.push(...page.items);
        after = page.next ?? null;
      } while (after !== null);
      return armies;
    },
  };
}
