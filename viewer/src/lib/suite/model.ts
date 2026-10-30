import type { CoverageEntry, DriverName, RunEvent, RunRequest, SuiteGame, SuiteGameResult, SuiteReport } from './client';

export type Group = { readonly name: string; readonly games: readonly SuiteGame[] };

export type Page = {
  readonly games: readonly SuiteGame[];
  readonly selected: ReadonlySet<string>;
  readonly driver: DriverName;
  readonly keep: boolean;
  readonly status: 'idle' | 'running' | 'done' | 'error';
  readonly runId: string | null;
  readonly results: ReadonlyMap<string, SuiteGameResult>;
  readonly coverage: readonly CoverageEntry[];
  readonly passed: boolean | null;
  readonly message: string | null;
};

export const groupsOf = (games: readonly SuiteGame[]): Group[] => {
  const byName = new Map<string, SuiteGame[]>();
  for (const g of games) byName.set(g.group, [...(byName.get(g.group) ?? []), g]);
  return [...byName].map(([name, list]) => ({ name, games: list }));
};

export const initial = (games: readonly SuiteGame[]): Page => ({
  games, selected: new Set(games.map((g) => g.name)), driver: 'in-process', keep: false,
  status: 'idle', runId: null, results: new Map(), coverage: [], passed: null, message: null,
});

export function toggleGame(page: Page, name: string): Page {
  const selected = new Set(page.selected);
  if (!selected.delete(name)) selected.add(name);
  return { ...page, selected };
}

export function toggleGroup(page: Page, group: string): Page {
  const names = page.games.filter((g) => g.group === group).map((g) => g.name);
  const all = names.every((n) => page.selected.has(n));
  const selected = new Set(page.selected);
  for (const n of names) all ? selected.delete(n) : selected.add(n);
  return { ...page, selected };
}

export const setDriver = (page: Page, driver: DriverName): Page => ({ ...page, driver });
export const setKeep = (page: Page, keep: boolean): Page => ({ ...page, keep });

/** Every game selected sends an empty `only`, which the runner reads as the full run (coverage is judged on a full run). */
export const requestOf = (page: Page): RunRequest => ({
  only: page.selected.size === page.games.length ? [] : page.games.filter((g) => page.selected.has(g.name)).map((g) => g.name),
  driver: page.driver, keep: page.keep,
});

export const canRun = (page: Page): boolean => page.status !== 'running' && page.selected.size > 0;

export const started = (page: Page, runId: string): Page =>
  ({ ...page, status: 'running', runId, results: new Map(), coverage: [], passed: null, message: null });

export const failed = (page: Page, message: string): Page => ({ ...page, status: 'error', message });

export function applyEvent(page: Page, event: RunEvent): Page {
  switch (event.type) {
    case 'game': return { ...page, results: new Map(page.results).set(event.result.name, event.result) };
    case 'done': return finish(page, event.report);
    case 'error': return { ...page, status: 'error', message: event.message };
  }
}

function finish(page: Page, report: SuiteReport): Page {
  const results = new Map(page.results);
  for (const g of report.games) results.set(g.name, g);
  return { ...page, status: 'done', results, coverage: report.coverage, passed: report.passed };
}

export const counts = (page: Page) => {
  const all = [...page.results.values()];
  return { passed: all.filter((r) => r.passed).length, failed: all.filter((r) => !r.passed).length };
};

export type CoverageCell = { readonly text: string; readonly tone: 'ok' | 'error' | 'muted' };

/** The coverage table's status cell: a text-only entry is not an error, a wrapper is covered by its pattern's games. */
export function coverageCell(c: CoverageEntry): CoverageCell {
  if (c.state === 'not_automated') return { text: 'not automated', tone: 'muted' };
  if (c.state === 'pattern') return { text: c.fired ? 'by pattern' : 'by pattern: not fired', tone: c.fired ? 'ok' : 'error' };
  return { text: c.fired ? 'yes' : 'no', tone: c.fired ? 'ok' : 'error' };
}

export type Edition = { readonly edition: string; readonly name: string };

/** The editions the suite runs under, in catalog order, each with its rule set's name. */
export function editionsOf(games: readonly SuiteGame[]): Edition[] {
  const seen = new Map<string, string>();
  for (const g of games) if (!seen.has(g.edition)) seen.set(g.edition, g.ruleSet);
  return [...seen].map(([edition, name]) => ({ edition, name }));
}

/** The set's name for an edition id (a coverage entry carries only the id); the id itself when no game of it is listed. */
export const editionName = (games: readonly SuiteGame[], edition: string): string =>
  games.find((g) => g.edition === edition)?.ruleSet ?? edition;

/** Selects every game of the edition, or deselects them all when they are all selected. */
export function toggleEdition(page: Page, edition: string): Page {
  const names = page.games.filter((g) => g.edition === edition).map((g) => g.name);
  const all = names.every((n) => page.selected.has(n));
  const selected = new Set(page.selected);
  for (const n of names) all ? selected.delete(n) : selected.add(n);
  return { ...page, selected };
}
