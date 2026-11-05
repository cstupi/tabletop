// viewer/src/lib/lobby/rulesOptions.ts
// RulesOptions (client/src/Core/RulesOptions.cs): the lobby's optional-rules controls over the ruleset's catalog. Keys and values are
// the catalog's data: nothing here names a rule.

import type { Enforcement, GameInfo, RulesOption, RulesOptionCatalog, SchedulerShape, SetConfiguration } from '../protocol/wire.generated';

/** What the preset control reads when no preset's map matches the game's. */
export const CUSTOM = 'Custom';

/** The option's value in the map, its default when the map has none. */
export const valueOf = (option: RulesOption, map: Readonly<Record<string, string>>): string => map[option.key] ?? option.default;

/** The first preset whose map reads every option the same as this one, else Custom. */
export function presetOf(catalog: RulesOptionCatalog, map: Readonly<Record<string, string>>): string {
  return catalog.presets.find((p) => catalog.options.every((o) => valueOf(o, p.optionalRules) === valueOf(o, map)))?.name ?? CUSTOM;
}

/** The map to send for these choices: every option not at its default, by key. */
export function mapOf(catalog: RulesOptionCatalog, chosen: Readonly<Record<string, string>>): Record<string, string> {
  const map: Record<string, string> = {};
  for (const o of catalog.options) if (valueOf(o, chosen) !== o.default) map[o.key] = valueOf(o, chosen);
  return map;
}

/** The SetConfiguration Apply sends (WaitsBetweenSteps false; the picked properties hash, the game's by default). */
export function configurationOf(
  game: GameInfo, name: string, shape: SchedulerShape, enforcement: Enforcement, catalog: RulesOptionCatalog,
  chosen: Readonly<Record<string, string>>, propertiesHash: string = game.propertiesHash,
): SetConfiguration {
  return {
    kind: 'set_configuration', name: name.trim(), propertiesHash, schedulerShape: shape, enforcement,
    waitsBetweenSteps: false, optionalRules: mapOf(catalog, chosen),
  };
}
