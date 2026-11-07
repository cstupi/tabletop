// viewer/src/lib/lobby/lobbyPage.ts
// What the lobby page's Game panel shows: the game's settings in words for a player who may not change them, and whether the creator's form
// differs from the game. The team columns are teamColumns.ts.

import type { Enforcement, GameInfo, RulesOptionCatalog, SchedulerShape } from '../protocol/wire.generated';
import { valueOf } from './rulesOptions';

export const SHAPE_LABEL: Record<SchedulerShape, string> = { 'alternating-activation': 'Alternating activation', 'player-turn': 'Player turn' };
export const ENFORCEMENT_LABEL: Record<Enforcement, string> = { silent: 'Silent', warn: 'Warn', strict: 'Strict' };
/** What each enforcement does, for the control's hint. */
export const ENFORCEMENT_HINT: Record<Enforcement, string> = {
  silent: 'Moves are never checked.',
  warn: 'A move that breaks a rule is shown and asked about first.',
  strict: 'A move that breaks a rule is refused.',
};

/** The settings as label and value, for a player who may not change them. */
export function settingsSummary(game: GameInfo, catalog: RulesOptionCatalog): { label: string; value: string }[] {
  return [
    { label: 'Activation', value: SHAPE_LABEL[game.schedulerShape] },
    { label: 'Enforcement', value: ENFORCEMENT_LABEL[game.enforcement] },
    ...catalog.options.map((o) => {
      const v = valueOf(o, game.optionalRules);
      return { label: o.label, value: o.values.find((x) => x.value === v)?.label ?? v };
    }),
  ];
}

/** True when the form differs from the game's configuration: Apply is then worth pressing. */
export function configChanged(
  game: GameInfo, catalog: RulesOptionCatalog, name: string, shape: SchedulerShape, enforcement: Enforcement,
  chosen: Readonly<Record<string, string>>, propertiesHash: string = game.propertiesHash,
): boolean {
  return name.trim() !== game.name || propertiesHash !== game.propertiesHash || shape !== game.schedulerShape || enforcement !== game.enforcement
    || catalog.options.some((o) => valueOf(o, chosen) !== valueOf(o, game.optionalRules));
}
