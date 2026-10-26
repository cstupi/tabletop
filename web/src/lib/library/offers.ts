// web/src/lib/library/offers.ts — offering assets to players and answering offers (shared-models spec).
import type { AssetDto, OfferDto, OfferState, PlayerDto } from '$lib/api/types';
import { formatBytes } from '$lib/validation';

const DAY = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
export const day = (iso: string) => DAY.format(new Date(iso));

/** What tells a player apart from another of the same name: the games played together and the last one's date; when two
 *  same-named players share even that, the end of their account id. */
export function playerContext(p: PlayerDto, all: PlayerDto[]): string {
  const context = `${p.games.join(', ')} · ${day(p.lastPlayed)}`;
  const twins = all.filter((o) => o.accountId !== p.accountId && o.name === p.name && `${o.games.join(', ')} · ${day(o.lastPlayed)}` === context);
  return twins.length ? `${context} · #${p.accountId.slice(-4)}` : context;
}

/** Players whose name contains `q`, case-insensitive. */
export function filterPlayers(players: PlayerDto[], q: string): PlayerDto[] {
  const needle = q.trim().toLowerCase();
  return needle ? players.filter((p) => p.name.toLowerCase().includes(needle)) : players;
}

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/** "Folder Orks · 3 models · 12 MB", or "Dread · 4 MB" for one model. */
export function offerSummary(o: OfferDto): string {
  const bytes = formatBytes(o.assets.reduce((s, a) => s + a.byteSize, 0));
  const what = o.folder ? `Folder ${o.folder} · ${plural(o.assets.length, 'model')}` : o.assets.length === 1 ? o.assets[0]!.name : plural(o.assets.length, 'model');
  return o.assets.length === 0 ? `${o.folder ? `Folder ${o.folder} · ` : ''}deleted` : `${what} · ${bytes}`;
}

export const STATE_LABELS: Record<OfferState, string> = { open: 'Waiting', accepted: 'Accepted', declined: 'Declined', withdrawn: 'Withdrawn' };

/** The offered assets whose bytes the recipient already has (same hash). */
export function alreadyHeld(o: OfferDto, mine: AssetDto[]): Set<string> {
  const hashes = new Set(mine.map((a) => a.hash));
  return new Set(o.assets.filter((a) => hashes.has(a.hash)).map((a) => a.id));
}

/** Appends a page, skipping offers already listed. */
export function appendPage(items: OfferDto[], page: OfferDto[]): OfferDto[] {
  const seen = new Set(items.map((i) => i.id));
  return [...items, ...page.filter((i) => !seen.has(i.id))];
}
