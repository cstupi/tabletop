// viewer/src/lib/lobby/lobbyPage.ts
// What the lobby page's Game panel shows: the game's settings in words for a player who may not change them, the rule set fold's heading,
// whether the creator's form differs from the game (Start setup then sends it first) and the rule set's own send (Godot's LobbySettings).
// The team columns are teamColumns.ts.

import type { Enforcement, GameInfo, RulesOptionCatalog, SchedulerShape } from '../protocol/wire.generated';
import { configurationOf, valueOf } from './rulesOptions';
import type { SetConfiguration } from '../protocol/wire.generated';

export const SHAPE_LABEL: Record<SchedulerShape, string> = { 'alternating-activation': 'Alternating activation', 'player-turn': 'Player turn' };
export const ENFORCEMENT_LABEL: Record<Enforcement, string> = { silent: 'Silent', warn: 'Warn', strict: 'Strict' };
/** What each enforcement does, for the control's hint. */
export const ENFORCEMENT_HINT: Record<Enforcement, string> = {
  silent: 'Moves are never checked.',
  warn: 'A move that breaks a rule is shown and asked about first.',
  strict: 'A move that breaks a rule is refused.',
};

/** The rule set fold's heading: the rule set, activation and enforcement on one line (LobbySettings.FoldTitle). */
export function foldTitle(ruleSet: string, shape: SchedulerShape, enforcement: Enforcement): string {
  return `${ruleSet} · ${shape === 'player-turn' ? 'Player turn' : 'Alternating'} · ${ENFORCEMENT_LABEL[enforcement]}`;
}

/** The game's rule set as the header names it. */
export const ruleSetOf = (game: GameInfo): string => game.ruleSet ?? game.gameRules;

/** A picked rule set, sent at once with the game's other values (the edition decides which armies every picker offers); the form's other
 *  edits stay local (LobbySettings.RuleSet). */
export function ruleSetEdit(game: GameInfo, catalog: RulesOptionCatalog, propertiesHash: string): SetConfiguration {
  const chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, game.optionalRules)]));
  return configurationOf(game, game.name, game.schedulerShape, game.enforcement, catalog, chosen, propertiesHash);
}

/** The settings fold's three as label and value, for a player who may not change them. */
export function rulesSummary(game: GameInfo): { label: string; value: string }[] {
  return [
    { label: 'Rule set', value: ruleSetOf(game) },
    { label: 'Activation', value: SHAPE_LABEL[game.schedulerShape] },
    { label: 'Enforcement', value: ENFORCEMENT_LABEL[game.enforcement] },
  ];
}

/** The optional rules as label and value, for a player who may not change them; each carries its and its value's descriptions as titles. */
export function settingsSummary(game: GameInfo, catalog: RulesOptionCatalog): { label: string; value: string; labelTitle?: string; valueTitle?: string }[] {
  return [
    ...catalog.options.map((o) => {
      const v = valueOf(o, game.optionalRules);
      const value = o.values.find((x) => x.value === v);
      return { label: o.label, value: value?.label ?? v, labelTitle: o.description, valueTitle: value?.description };
    }),
  ];
}

/** True when the form differs from the game's configuration: Start setup then sends it before starting. */
export function configChanged(
  game: GameInfo, catalog: RulesOptionCatalog, name: string, shape: SchedulerShape, enforcement: Enforcement,
  chosen: Readonly<Record<string, string>>, propertiesHash: string = game.propertiesHash,
): boolean {
  return name.trim() !== game.name || propertiesHash !== game.propertiesHash || shape !== game.schedulerShape || enforcement !== game.enforcement
    || catalog.options.some((o) => valueOf(o, chosen) !== valueOf(o, game.optionalRules));
}
